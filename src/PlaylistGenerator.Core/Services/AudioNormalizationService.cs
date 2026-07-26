using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Produces loudness-normalized Opus copies of an audio library using two FFmpeg passes.
/// </summary>
/// <remarks>
/// Source files are never opened for writing. Each output is encoded to a temporary file and
/// moved into place, so an interrupted run leaves no partial <c>.opus</c> file behind and the
/// next run resumes from what already completed.
/// </remarks>
public sealed class AudioNormalizationService : IAudioNormalizer
{
    /// <summary>Upper bound on FFmpeg diagnostics quoted into an error message.</summary>
    private const int MaximumDiagnosticsLength = 4_000;

    private readonly IAudioFileCatalog _catalog;
    private readonly IExecutableLocator _executableLocator;
    private readonly IProcessRunner _processRunner;

    public AudioNormalizationService(
        IAudioFileCatalog catalog,
        IExecutableLocator executableLocator,
        IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(executableLocator);
        ArgumentNullException.ThrowIfNull(processRunner);
        _catalog = catalog;
        _executableLocator = executableLocator;
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    /// <exception cref="PlaylistValidationException">The request cannot be normalized.</exception>
    /// <exception cref="PlaylistIOException">FFmpeg or the filesystem failed.</exception>
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
        var reporter = new ProgressReporter(progress, plan.TotalFileCount);

        foreach (var skippedPath in plan.SkippedSourcePaths)
        {
            reporter.ReportSkipped(skippedPath);
        }

        var stopped = false;
        foreach (var job in plan.Jobs)
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
                stopped = true;
                reporter.Report(job.SourcePath, NormalizationAction.Stopped);
                break;
            }
        }

        return new NormalizationResult(
            sourceDirectory,
            outputDirectory,
            reporter.NormalizedCount,
            reporter.SkippedCount,
            stopped);
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
        ProgressReporter reporter,
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

            // FFmpeg writes the JSON summary to whichever stream is available.
            var stats = LoudnessJsonParser.Parse(
                $"{analysis.StandardOutput}{Environment.NewLine}{analysis.StandardError}",
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
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static async Task WaitWhilePausedAsync(
        IPauseSignal? pauseSignal,
        ProgressReporter reporter,
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

    /// <summary>
    /// Tracks run counters and publishes progress, keeping the counting rules in one place.
    /// </summary>
    private sealed class ProgressReporter(
        IProgress<NormalizationProgress>? progress,
        int totalFileCount)
    {
        public int NormalizedCount { get; private set; }

        public int SkippedCount { get; private set; }

        private int CompletedCount => NormalizedCount + SkippedCount;

        public void ReportSkipped(string sourcePath)
        {
            SkippedCount++;
            Report(sourcePath, NormalizationAction.Skipped);
        }

        public void ReportCompleted(string sourcePath)
        {
            NormalizedCount++;
            Report(sourcePath, NormalizationAction.Completed);
        }

        public void Report(string sourcePath, NormalizationAction action) =>
            progress?.Report(
                new NormalizationProgress(
                    totalFileCount,
                    CompletedCount,
                    NormalizedCount,
                    SkippedCount,
                    sourcePath,
                    action));
    }
}
