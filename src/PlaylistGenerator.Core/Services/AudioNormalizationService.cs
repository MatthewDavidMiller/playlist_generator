using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Produces loudness-normalized Opus copies of an audio library using two FFmpeg passes.
/// </summary>
/// <remarks>
/// <para>
/// Source files are never opened for writing. Each output is encoded to a temporary file and
/// moved into place, so an interrupted run leaves no partial <c>.opus</c> file behind and the
/// next run resumes from what already completed.
/// </para>
/// <para>
/// Several files are encoded at once. Both FFmpeg passes are processor-bound and each one
/// works on a single file, so a sequential run leaves most of a multi-core machine idle. The
/// planner guarantees distinct destinations, which is what makes concurrent workers safe.
/// </para>
/// <para>
/// A file that cannot be normalized is recorded in the result and the run continues. Losing
/// hours of completed work to one unreadable file would make a large library impractical to
/// process.
/// </para>
/// </remarks>
public sealed class AudioNormalizationService : IAudioNormalizer
{
    /// <summary>Upper bound on FFmpeg diagnostics quoted into an error message.</summary>
    private const int MaximumDiagnosticsLength = 4_000;

    /// <summary>
    /// Ceiling applied to the default worker count. FFmpeg uses several threads of its own and
    /// both passes also read from disk, so more workers than this stop paying for themselves
    /// and start competing for the same drive.
    /// </summary>
    private const int DefaultParallelismCeiling = 8;

    private readonly IAudioFileCatalog _catalog;
    private readonly IExecutableLocator _executableLocator;
    private readonly IProcessRunner _processRunner;
    private readonly int _maxDegreeOfParallelism;

    /// <summary>Creates a service that sizes its worker count for the current machine.</summary>
    public AudioNormalizationService(
        IAudioFileCatalog catalog,
        IExecutableLocator executableLocator,
        IProcessRunner processRunner)
        : this(catalog, executableLocator, processRunner, DefaultMaxDegreeOfParallelism)
    {
    }

    /// <summary>
    /// Creates a service that encodes at most <paramref name="maxDegreeOfParallelism"/> files
    /// at once. Pass <c>1</c> for a strictly sequential run.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The worker count is below one.</exception>
    public AudioNormalizationService(
        IAudioFileCatalog catalog,
        IExecutableLocator executableLocator,
        IProcessRunner processRunner,
        int maxDegreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(executableLocator);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        _catalog = catalog;
        _executableLocator = executableLocator;
        _processRunner = processRunner;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    /// <summary>Gets the worker count used when a caller does not choose one.</summary>
    public static int DefaultMaxDegreeOfParallelism { get; } =
        Math.Clamp(Environment.ProcessorCount, 1, DefaultParallelismCeiling);

    /// <inheritdoc />
    /// <exception cref="PlaylistValidationException">The request cannot be normalized.</exception>
    /// <exception cref="PlaylistIOException">The library could not be read.</exception>
    public async Task<NormalizationResult> NormalizeAsync(
        NormalizationRequest request,
        IProgress<NormalizationProgress>? progress = null,
        IPauseSignal? pauseSignal = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ffmpeg = ValidateRequest(request);
        var sourceDirectory = PathUtility.GetFullPath(request.SourceDirectory);
        var outputDirectory = PathUtility.GetFullPath(request.OutputDirectory);

        if (PathUtility.AreSameFull(sourceDirectory, outputDirectory))
        {
            throw new PlaylistValidationException(
                "The normalized output folder must differ from the source folder so that "
                + "source files are never replaced.");
        }

        var audioFiles = _catalog.Scan(sourceDirectory);
        if (audioFiles.Count == 0)
        {
            throw new PlaylistValidationException(
                $"No supported audio files were found in '{sourceDirectory}'.");
        }

        var plan = NormalizationPlanner.Create(audioFiles, sourceDirectory, outputDirectory);
        var reporter = new NormalizationProgressReporter(progress, plan.TotalFileCount);

        reporter.ReportSkipped(plan.SkippedSourcePaths);

        var stopped = await RunJobsAsync(ffmpeg, plan, pauseSignal, reporter, cancellationToken)
            .ConfigureAwait(false);

        return new NormalizationResult(
            sourceDirectory,
            outputDirectory,
            reporter.NormalizedCount,
            reporter.SkippedCount,
            reporter.Failures,
            stopped);
    }

    /// <summary>Runs every planned job and reports whether cancellation ended the run.</summary>
    private async Task<bool> RunJobsAsync(
        string ffmpeg,
        NormalizationPlan plan,
        IPauseSignal? pauseSignal,
        NormalizationProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        if (plan.Jobs.Count == 0)
        {
            // Nothing to schedule, but an already-canceled token still means a stopped run.
            return cancellationToken.IsCancellationRequested;
        }

        var options = new ParallelOptions
        {
            // Never start more workers than there is work for them to do.
            MaxDegreeOfParallelism = Math.Min(_maxDegreeOfParallelism, plan.Jobs.Count),
            CancellationToken = cancellationToken,
        };

        try
        {
            await Parallel
                .ForEachAsync(
                    plan.Jobs,
                    options,
                    (job, token) => NormalizeJobAsync(ffmpeg, job, pauseSignal, reporter, token))
                .ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation reports the partial counts already gathered rather than throwing.
            return true;
        }
    }

    /// <summary>
    /// Normalizes one file, turning an expected failure into a recorded one so the remaining
    /// files still run.
    /// </summary>
    private async ValueTask NormalizeJobAsync(
        string ffmpeg,
        NormalizationJob job,
        IPauseSignal? pauseSignal,
        NormalizationProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(pauseSignal, reporter, job.SourcePath, cancellationToken)
                .ConfigureAwait(false);

            reporter.Report(job.SourcePath, NormalizationAction.Analyzing);
            await NormalizeFileAsync(ffmpeg, job, pauseSignal, reporter, cancellationToken)
                .ConfigureAwait(false);

            reporter.ReportCompleted(job.SourcePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reporter.Report(job.SourcePath, NormalizationAction.Stopped);

            // Rethrowing keeps the remaining files from being scheduled.
            throw;
        }
        catch (PlaylistGeneratorException exception)
        {
            // A file that broke only because the run was being torn down is a stopped file,
            // not an unusable one. Recording it as a failure would invent errors on every
            // stop and leave the summary claiming a library it never actually rejected.
            if (cancellationToken.IsCancellationRequested)
            {
                reporter.Report(job.SourcePath, NormalizationAction.Stopped);
                throw new OperationCanceledException(exception.Message, exception, cancellationToken);
            }

            reporter.ReportFailed(job.SourcePath, exception.Message);
        }
    }

    /// <summary>Validates the request and returns the resolved FFmpeg path.</summary>
    private string ValidateRequest(NormalizationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            throw new PlaylistValidationException("Source directory is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new PlaylistValidationException("Output directory is required.");
        }

        // Checked before scanning so a missing dependency is reported immediately rather
        // than after a long directory walk.
        return _executableLocator.Find(FfmpegInstallAdvisor.FfmpegExecutable)
            ?? throw new PlaylistValidationException(
                "FFmpeg is required for volume normalization. Install FFmpeg and "
                + "make sure it is available on PATH.");
    }

    private async Task NormalizeFileAsync(
        string ffmpeg,
        NormalizationJob job,
        IPauseSignal? pauseSignal,
        NormalizationProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(job.SourcePath))
        {
            throw new PlaylistIOException(
                $"Audio file '{job.SourcePath}' became unavailable while it was being normalized.");
        }

        var destinationDirectory = Path.GetDirectoryName(job.DestinationPath)
            ?? throw new PlaylistValidationException(
                $"Normalized output path '{job.DestinationPath}' has no parent directory.");

        // The temporary file shares the destination directory so the final move stays on one
        // volume and is therefore atomic.
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileNameWithoutExtension(job.DestinationPath)}.{Guid.NewGuid():N}.tmp"
                + AudioFormats.NormalizedExtension);
        var moved = false;

        try
        {
            Directory.CreateDirectory(destinationDirectory);

            var analysis = await _processRunner
                .RunAsync(
                    ffmpeg,
                    FfmpegCommandBuilder.BuildAnalysis(job.SourcePath),
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccess(analysis, $"FFmpeg failed to analyze loudness for '{job.SourcePath}'");

            // FFmpeg prints the JSON summary to standard error, but has used standard output
            // across versions. Trying them in turn avoids joining two large logs into one.
            var stats = LoudnessJsonParser.Parse(
                analysis.StandardError,
                analysis.StandardOutput,
                job.SourcePath);

            // A pause requested during analysis takes effect here, between the two passes,
            // rather than being deferred until the whole file finishes.
            await WaitWhilePausedAsync(pauseSignal, reporter, job.SourcePath, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            reporter.Report(job.SourcePath, NormalizationAction.Encoding);
            var encoding = await _processRunner
                .RunAsync(
                    ffmpeg,
                    FfmpegCommandBuilder.BuildEncode(job.SourcePath, temporaryPath, stats),
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccess(
                encoding,
                $"FFmpeg failed to encode normalized audio for '{job.SourcePath}'");

            if (!File.Exists(temporaryPath))
            {
                throw new PlaylistIOException(
                    "FFmpeg reported success but did not create normalized audio for "
                    + $"'{job.SourcePath}'.");
            }

            // Never overwrite: the planner already established that nothing valid is there.
            File.Move(temporaryPath, job.DestinationPath, overwrite: false);
            moved = true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new PlaylistIOException(
                $"Unable to normalize '{job.SourcePath}' to '{job.DestinationPath}': "
                + exception.Message,
                exception);
        }
        finally
        {
            // After a successful move nothing is left at the temporary path, so the cleanup
            // syscall is skipped rather than issued once per file in the library.
            if (!moved)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private static async Task WaitWhilePausedAsync(
        IPauseSignal? pauseSignal,
        NormalizationProgressReporter reporter,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (pauseSignal is null)
        {
            return;
        }

        if (pauseSignal.IsPaused)
        {
            reporter.Report(sourcePath, NormalizationAction.Paused);
        }

        await pauseSignal.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the normalization result if temporary cleanup is rejected.
        }
    }

    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var detail = result.Diagnostics.Trim();
        if (detail.Length > MaximumDiagnosticsLength)
        {
            // FFmpeg puts the actual cause last, so keep the tail.
            detail = detail[^MaximumDiagnosticsLength..];
        }

        throw new PlaylistIOException(
            detail.Length == 0 ? $"{message}." : $"{message}: {detail}");
    }
}
