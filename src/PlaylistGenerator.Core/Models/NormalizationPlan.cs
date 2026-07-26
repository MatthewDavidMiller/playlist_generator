namespace PlaylistGenerator.Core.Models;

/// <summary>
/// The work a normalization run will perform, decided before any encoding starts.
/// </summary>
/// <param name="Jobs">Files that still need encoding.</param>
/// <param name="SkippedSourcePaths">
/// Files needing no work, because output already exists or they belong to the output tree.
/// </param>
public sealed record NormalizationPlan(
    IReadOnlyList<NormalizationJob> Jobs,
    IReadOnlyList<string> SkippedSourcePaths)
{
    /// <summary>Gets the number of files the run accounts for.</summary>
    public int TotalFileCount => Jobs.Count + SkippedSourcePaths.Count;
}
