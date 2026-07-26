namespace PlaylistGenerator.Core.Models;

/// <summary>
/// Summarizes a completed playlist generation. All paths are absolute.
/// </summary>
/// <param name="SourceTrackCount">Library tracks used, excluding the special file.</param>
/// <param name="PlaylistEntryCount">Total written entries, including special-file insertions.</param>
public sealed record PlaylistResult(
    string SourceDirectory,
    string SpecialFile,
    string OutputPath,
    int SourceTrackCount,
    int PlaylistEntryCount,
    int InsertEvery);
