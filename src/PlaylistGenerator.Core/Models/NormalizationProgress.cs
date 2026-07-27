namespace PlaylistGenerator.Core.Models;

/// <summary>
/// A single progress report published while a normalization run is in flight.
/// </summary>
/// <param name="CompletedFileCount">
/// Files the run is finished with, whether encoded, skipped, or failed. It never exceeds
/// <paramref name="TotalFileCount"/>.
/// </param>
public sealed record NormalizationProgress(
    int TotalFileCount,
    int CompletedFileCount,
    int NormalizedFileCount,
    int SkippedFileCount,
    int FailedFileCount,
    string CurrentSourcePath,
    NormalizationAction Action);
