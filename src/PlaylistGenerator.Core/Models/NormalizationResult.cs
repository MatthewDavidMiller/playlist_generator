namespace PlaylistGenerator.Core.Models;

/// <summary>
/// Summarizes a completed or stopped normalization run. All paths are absolute.
/// </summary>
/// <param name="NormalizedFileCount">Files encoded during this run.</param>
/// <param name="SkippedFileCount">Files left alone because output already existed.</param>
/// <param name="Stopped">Whether the run ended early because it was canceled.</param>
public sealed record NormalizationResult(
    string SourceDirectory,
    string OutputDirectory,
    int NormalizedFileCount,
    int SkippedFileCount,
    bool Stopped);
