namespace PlaylistGenerator.Core.Models;

/// <summary>
/// One source file paired with the normalized file it will produce. Both paths are absolute.
/// </summary>
public sealed record NormalizationJob(string SourcePath, string DestinationPath);
