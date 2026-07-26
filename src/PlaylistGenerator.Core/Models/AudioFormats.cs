using System.Collections.Frozen;

namespace PlaylistGenerator.Core.Models;

/// <summary>
/// The audio formats this application accepts as input, and the format it always writes.
/// </summary>
public static class AudioFormats
{
    /// <summary>
    /// Extension of every normalized file this application produces.
    /// </summary>
    public const string NormalizedExtension = ".opus";

    /// <summary>
    /// Supported input extensions, matched case-insensitively and including the leading dot.
    /// </summary>
    public static readonly FrozenSet<string> SupportedExtensions =
        new[]
        {
            ".mp3",
            ".flac",
            ".wav",
            ".m4a",
            ".aac",
            ".ogg",
            ".opus",
            ".wma",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Lets a recursive scan test each file's extension without allocating a string for it.
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> SpanLookup =
        SupportedExtensions.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// Returns whether <paramref name="path"/> carries a supported audio extension.
    /// </summary>
    public static bool IsSupported(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return IsSupported(path.AsSpan());
    }

    /// <summary>
    /// Returns whether <paramref name="path"/> carries a supported audio extension, without
    /// allocating a string for the extension.
    /// </summary>
    public static bool IsSupported(ReadOnlySpan<char> path) =>
        SpanLookup.Contains(Path.GetExtension(path));
}
