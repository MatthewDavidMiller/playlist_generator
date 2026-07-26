namespace PlaylistGenerator.Core.Infrastructure;

public static class PathUtility
{
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string GetFullPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetFullPath(ExpandHome(path));
    }

    public static bool AreSame(string left, string right) =>
        Comparer.Equals(GetFullPath(left), GetFullPath(right));

    public static bool IsWithinDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(GetFullPath(directory), GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", Comparison)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", Comparison)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", Comparison);
    }

    private static string ExpandHome(string path)
    {
        var isHomeRelative = path.Length >= 2
            && path[0] == '~'
            && (path[1] == Path.DirectorySeparatorChar
                || path[1] == Path.AltDirectorySeparatorChar);
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
