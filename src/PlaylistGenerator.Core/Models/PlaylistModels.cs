namespace PlaylistGenerator.Core.Models;

public sealed record PlaylistRequest(
    string SourceDirectory,
    string SpecialFile,
    int InsertEvery,
    string OutputPath);

public sealed record PlaylistResult(
    string SourceDirectory,
    string SpecialFile,
    string OutputPath,
    int SourceTrackCount,
    int PlaylistEntryCount,
    int InsertEvery);
