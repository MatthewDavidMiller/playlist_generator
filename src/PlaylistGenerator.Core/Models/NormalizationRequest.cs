namespace PlaylistGenerator.Core.Models;

/// <summary>
/// Describes one non-destructive volume-normalization request. Source files are never modified.
/// </summary>
public sealed record NormalizationRequest(
    string SourceDirectory,
    string OutputDirectory);
