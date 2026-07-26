namespace PlaylistGenerator.Core.Models;

/// <summary>
/// A single progress report published while a normalization run is in flight.
/// </summary>
/// <param name="CompletedFileCount">Files that no longer need work, whether encoded or skipped.</param>
public sealed record NormalizationProgress(
    int TotalFileCount,
    int CompletedFileCount,
    int NormalizedFileCount,
    int SkippedFileCount,
    string CurrentSourcePath,
    NormalizationAction Action);
