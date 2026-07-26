namespace PlaylistGenerator.Core.Infrastructure;

/// <summary>
/// Path helpers that apply the current platform's case rules consistently.
/// </summary>
/// <remarks>
/// Methods named <c>Full</c> take paths that are already absolute and normalized. They skip
/// re-normalization, which matters because normalization planning compares every scanned file
/// against the output tree.
/// </remarks>
public static class PathUtility
{
    /// <summary>Path comparer matching the current platform's filesystem case rules.</summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Path comparison matching the current platform's filesystem case rules.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Expands a leading <c>~</c> and returns the absolute, normalized form of a path.
    /// </summary>
    public static string GetFullPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetFullPath(ExpandHome(path));
    }

    /// <summary>
    /// Returns whether two paths refer to the same location after normalization.
    /// </summary>
    public static bool AreSame(string left, string right) =>
        AreSameFull(GetFullPath(left), GetFullPath(right));

    /// <summary>
    /// Returns whether two already-normalized absolute paths refer to the same location.
    /// </summary>
    public static bool AreSameFull(string left, string right) =>
        string.Equals(left, right, Comparison);

    /// <summary>
    /// Returns whether <paramref name="path"/> is <paramref name="directory"/> itself or sits
    /// beneath it.
    /// </summary>
    public static bool IsWithinDirectory(string path, string directory) =>
        IsWithinFullDirectory(GetFullPath(path), GetFullPath(directory));

    /// <summary>
    /// Returns whether an already-normalized absolute <paramref name="fullPath"/> is
    /// <paramref name="fullDirectory"/> itself or sits beneath it.
    /// </summary>
    public static bool IsWithinFullDirectory(string fullPath, string fullDirectory)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(fullDirectory);

        if (fullDirectory.Length == 0 || fullPath.Length < fullDirectory.Length)
        {
            return false;
        }

        if (!fullPath.AsSpan(0, fullDirectory.Length).Equals(fullDirectory, Comparison))
        {
            return false;
        }

        // Equal paths count as contained; otherwise the directory must end at a boundary so
        // that "/music" is not treated as the parent of "/musicbox".
        return fullPath.Length == fullDirectory.Length
            || IsSeparator(fullDirectory[^1])
            || IsSeparator(fullPath[fullDirectory.Length]);
    }

    private static bool IsSeparator(char value) =>
        value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

    private static string ExpandHome(string path)
    {
        var isHomeRelative = path.Length >= 2 && path[0] == '~' && IsSeparator(path[1]);
        if (path is not "~" && !isHomeRelative)
        {
            return path;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Length == 1
            ? userProfile
            : Path.Combine(userProfile, path[2..]);
    }
}
