namespace PlaylistGenerator.Core.Models;

/// <summary>
/// Describes one shuffled-playlist generation request.
/// </summary>
/// <param name="SourceDirectory">Music library scanned recursively for supported audio.</param>
/// <param name="SpecialFile">Audio file inserted after each complete block of tracks.</param>
/// <param name="InsertEvery">Block size; the special file follows every complete block.</param>
/// <param name="OutputPath">Destination <c>.m3u8</c> file.</param>
public sealed record PlaylistRequest(
    string SourceDirectory,
    string SpecialFile,
    int InsertEvery,
    string OutputPath);
