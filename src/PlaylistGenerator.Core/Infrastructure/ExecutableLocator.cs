using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Core.Infrastructure;

/// <summary>
/// Resolves executables against the process search path.
/// </summary>
/// <remarks>
/// The Windows executable extensions are read from the environment once per lookup and then
/// passed down. They were previously re-read and re-split for every candidate tested, which
/// repeated the same work for every entry on a long <c>PATH</c>.
/// </remarks>
public sealed class ExecutableLocator : IExecutableLocator
{
    /// <summary>Extensions tried on Windows when <c>PATHEXT</c> is unset or empty.</summary>
    private static readonly string[] DefaultWindowsExtensions = [".COM", ".EXE", ".BAT", ".CMD"];

    private const UnixFileMode ExecutableBits =
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <inheritdoc />
    public string? Find(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        // Empty on every other platform, which is also what makes "append an extension"
        // a no-op there.
        var windowsExtensions = OperatingSystem.IsWindows() ? GetWindowsExtensions() : [];

        if (executableName.Contains(Path.DirectorySeparatorChar)
            || executableName.Contains(Path.AltDirectorySeparatorChar))
        {
            return FindAtExplicitPath(executableName, windowsExtensions);
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return null;
        }

        // Resolved once rather than per search-path entry.
        var candidateNames = GetCandidateNames(executableName, windowsExtensions);

        foreach (var directory in searchPath.Split(Path.PathSeparator))
        {
            // Windows tolerates quoted PATH entries; an empty entry means "current directory",
            // which is deliberately not searched. Anything else malformed is left to
            // TryResolve, which already treats an unusable candidate as simply not a match.
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var candidateName in candidateNames)
            {
                var resolved = TryResolve(Path.Combine(trimmed, candidateName));
                if (resolved is not null)
                {
                    return resolved;
                }
            }
        }

        return null;
    }

    private static string? FindAtExplicitPath(string executableName, string[] windowsExtensions)
    {
        var explicitPath = PathUtility.GetFullPath(executableName);
        if (TryResolve(explicitPath) is { } resolved)
        {
            return resolved;
        }

        if (windowsExtensions.Length == 0)
        {
            return null;
        }

        // A Windows path may legitimately omit the extension, as "C:\tools\ffmpeg" does.
        var directory = Path.GetDirectoryName(explicitPath);
        var fileName = Path.GetFileName(explicitPath);
        if (directory is null
            || fileName.Length == 0
            || HasExecutableExtension(fileName, windowsExtensions))
        {
            return null;
        }

        foreach (var extension in windowsExtensions)
        {
            if (TryResolve(Path.Combine(directory, fileName + extension)) is { } candidate)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string[] GetCandidateNames(string executableName, string[] windowsExtensions)
    {
        // "ffmpeg.exe" must not become "ffmpeg.exe.exe", and no other platform appends
        // anything at all.
        if (windowsExtensions.Length == 0
            || HasExecutableExtension(executableName, windowsExtensions))
        {
            return [executableName];
        }

        return
        [
            executableName,
            .. windowsExtensions.Select(extension => executableName + extension),
        ];
    }

    private static string[] GetWindowsExtensions()
    {
        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExtensions))
        {
            return DefaultWindowsExtensions;
        }

        var parsed = pathExtensions
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => extension.StartsWith('.'))
            .ToArray();
        return parsed.Length > 0 ? parsed : DefaultWindowsExtensions;
    }

    private static bool HasExecutableExtension(string fileName, string[] windowsExtensions)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length > 0
            && windowsExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryResolve(string candidate)
    {
        try
        {
            if (!File.Exists(candidate))
            {
                return null;
            }

            // Windows derives executability from the extension, which the caller already
            // constrained; Unix requires an execute bit for at least one class.
            if (!OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(candidate) & ExecutableBits) == 0)
            {
                return null;
            }

            return Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
        {
            // An unreadable or malformed search-path entry is simply not a match.
            return null;
        }
    }
}
