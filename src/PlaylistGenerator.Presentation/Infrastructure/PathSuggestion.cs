namespace PlaylistGenerator.Presentation.Infrastructure;

/// <summary>
/// Derives the default paths offered after a user picks a music folder.
/// </summary>
/// <remarks>
/// These are suggestions only. Every one lands in an editable field the user can override.
/// </remarks>
public static class PathSuggestion
{
    /// <summary>Fallback name when a directory has no usable leaf name, such as a drive root.</summary>
    private const string FallbackName = "music";

    /// <summary>
    /// Returns the leaf name of <paramref name="directory"/>, ignoring trailing separators.
    /// </summary>
    public static string GetDirectoryName(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var trimmed = directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : FallbackName;
    }

    /// <summary>
    /// Suggests a playlist file inside the music folder, named after that folder.
    /// </summary>
    public static string BuildPlaylistPath(string sourceDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        return Path.Combine(
            sourceDirectory,
            $"{GetDirectoryName(sourceDirectory)}-playlist.m3u8");
    }

    /// <summary>
    /// Suggests a normalized-output folder beside the source folder rather than inside it,
    /// so a later scan of the source does not pick up normalized copies.
    /// </summary>
    public static string BuildNormalizedOutputPath(string sourceDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);

        var fullPath = Path.GetFullPath(sourceDirectory);
        var parent = Directory.GetParent(fullPath)?.FullName ?? fullPath;
        return Path.Combine(parent, $"{GetDirectoryName(fullPath)}-normalized");
    }
}
