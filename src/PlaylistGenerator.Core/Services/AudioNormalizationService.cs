using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

public sealed class AudioNormalizationService : IAudioNormalizer
{
    private readonly IAudioFileCatalog _catalog;
    private readonly IFfmpegLocator _ffmpegLocator;
    private readonly IProcessRunner _processRunner;

    public AudioNormalizationService(
        IAudioFileCatalog catalog,
        IFfmpegLocator ffmpegLocator,
        IProcessRunner processRunner)
    {
        _catalog = catalog;
        _ffmpegLocator = ffmpegLocator;
        _processRunner = processRunner;
    }

    public async Task<NormalizationResult> NormalizeAsync(
        NormalizationRequest request,
        IProgress<NormalizationProgress>? progress = null,
        PauseController? pauseController = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new PlaylistValidationException("Output directory is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            throw new PlaylistValidationException("Source directory is required.");
        }

        var ffmpeg = _ffmpegLocator.Find();
        if (ffmpeg is null)
        {
            throw new PlaylistValidationException(
                "FFmpeg is required for volume normalization. Install FFmpeg and "
                + "make sure it is available on PATH.");
        }

        var sourceDirectory = PathUtility.GetFullPath(request.SourceDirectory);
        var outputDirectory = PathUtility.GetFullPath(request.OutputDirectory);
        var audioFiles = _catalog.Scan(sourceDirectory);
        if (audioFiles.Count == 0)
        {
            throw new PlaylistValidationException(
                $"No supported audio files were found in '{sourceDirectory}'.");
        }

        var normalizedCount = 0;
        var skippedCount = 0;
        var completedCount = 0;
        var jobs = new List<NormalizationJob>(audioFiles.Count);
        var destinations = new Dictionary<string, string>(PathUtility.Comparer);
        var outputIsInsideSource =
            PathUtility.IsWithinDirectory(outputDirectory, sourceDirectory);

        void Report(string sourcePath, NormalizationAction action) =>
            progress?.Report(
                new NormalizationProgress(
                    audioFiles.Count,
                    completedCount,
                    normalizedCount,
                    skippedCount,
                    sourcePath,
                    action));

        foreach (var sourcePath in audioFiles)
        {
            if (outputIsInsideSource
                && PathUtility.IsWithinDirectory(sourcePath, outputDirectory))
            {
                skippedCount++;
                completedCount++;
                Report(sourcePath, NormalizationAction.Skipped);
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destination = Path.ChangeExtension(
                Path.Combine(outputDirectory, relativePath),
                ".opus");

            if (PathUtility.AreSame(sourcePath, destination) || File.Exists(destination))
            {
                skippedCount++;
                completedCount++;
                Report(sourcePath, NormalizationAction.Skipped);
                continue;
            }

            if (destinations.TryGetValue(destination, out var existingSource))
            {
                throw new PlaylistValidationException(
                    "Multiple source files would write to the same normalized output path "
                    + $"'{destination}': '{existingSource}' and '{sourcePath}'.");
            }

            destinations.Add(destination, sourcePath);
            jobs.Add(new NormalizationJob(sourcePath, destination));
        }

        var stopped = false;
        foreach (var job in jobs)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pauseController?.IsPaused == true)
                {
                    Report(job.SourcePath, NormalizationAction.Paused);
                }

                if (pauseController is not null)
                {
                    await pauseController
                        .WaitIfPausedAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                Report(job.SourcePath, NormalizationAction.Analyzing);
                await NormalizeFileAsync(
                        ffmpeg,
                        job.SourcePath,
                        job.DestinationPath,
                        pauseController,
                        () => Report(job.SourcePath, NormalizationAction.Paused),
                        () => Report(job.SourcePath, NormalizationAction.Encoding),
                        cancellationToken)
                    .ConfigureAwait(false);

                normalizedCount++;
                completedCount++;
                Report(job.SourcePath, NormalizationAction.Completed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopped = true;
                Report(job.SourcePath, NormalizationAction.Stopped);
                break;
            }
        }

        return new NormalizationResult(
            sourceDirectory,
            outputDirectory,
            normalizedCount,
            skippedCount,
            stopped);
    }

    private async Task NormalizeFileAsync(
        string ffmpeg,
        string sourcePath,
        string destinationPath,
        PauseController? pauseController,
        Action onPaused,
        Action onEncoding,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new PlaylistIOException(
                $"Audio file '{sourcePath}' became unavailable while it was being normalized.");
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new PlaylistValidationException(
                $"Normalized output path '{destinationPath}' has no parent directory.");
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileNameWithoutExtension(destinationPath)}.{Guid.NewGuid():N}.tmp.opus");

        try
        {
            Directory.CreateDirectory(destinationDirectory);

            var analysis = await _processRunner
                .RunAsync(
                    ffmpeg,
                    FfmpegCommandBuilder.BuildAnalysis(sourcePath),
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccess(
                analysis,
                $"FFmpeg failed to analyze loudness for '{sourcePath}'");

            var stats = LoudnessJsonParser.Parse(
                $"{analysis.StandardOutput}{Environment.NewLine}{analysis.StandardError}",
                sourcePath);

            if (pauseController?.IsPaused == true)
            {
                onPaused();
            }

            if (pauseController is not null)
            {
                await pauseController
                    .WaitIfPausedAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            onEncoding();
            var encoding = await _processRunner
                .RunAsync(
                    ffmpeg,
                    FfmpegCommandBuilder.BuildEncode(sourcePath, temporaryPath, stats),
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccess(
                encoding,
                $"FFmpeg failed to encode normalized audio for '{sourcePath}'");

            if (!File.Exists(temporaryPath))
            {
                throw new PlaylistIOException(
                    $"FFmpeg reported success but did not create normalized audio for '{sourcePath}'.");
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new PlaylistIOException(
                $"Unable to normalize '{sourcePath}' to '{destinationPath}': {exception.Message}",
                exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Preserve the normalization result if temporary cleanup is rejected.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the normalization result if temporary cleanup is rejected.
            }
        }
    }

    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var processDetail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        processDetail = processDetail.Trim();
        if (processDetail.Length > 4_000)
        {
            processDetail = processDetail[^4_000..];
        }

        throw new PlaylistIOException(
            string.IsNullOrEmpty(processDetail) ? $"{message}." : $"{message}: {processDetail}");
    }

    private sealed record NormalizationJob(string SourcePath, string DestinationPath);
}
