using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Core.Infrastructure;

/// <summary>
/// Resolves executables against the process search path.
/// </summary>
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

        if (executableName.Contains(Path.DirectorySeparatorChar)
            || executableName.Contains(Path.AltDirectorySeparatorChar))
        {
            return FindAtExplicitPath(executableName);
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return null;
        }

        // Resolved once rather than per search-path entry.
        var candidateNames = GetCandidateNames(executableName);

        foreach (var directory in searchPath.Split(Path.PathSeparator))
        {
            // Windows tolerates quoted PATH entries; an empty entry means "current directory",
            // which is deliberately not searched.
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0 || trimmed.Contains('\0'))
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

    private static string? FindAtExplicitPath(string executableName)
    {
        var explicitPath = PathUtility.GetFullPath(executableName);
        if (TryResolve(explicitPath) is { } resolved)
        {
            return resolved;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        // A Windows path may legitimately omit the extension, as "C:\tools\ffmpeg" does.
        var directory = Path.GetDirectoryName(explicitPath);
        var fileName = Path.GetFileName(explicitPath);
        if (directory is null || fileName.Length == 0 || HasExecutableExtension(fileName))
        {
            return null;
        }

        foreach (var extension in GetWindowsExtensions())
        {
            if (TryResolve(Path.Combine(directory, fileName + extension)) is { } candidate)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string[] GetCandidateNames(string executableName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [executableName];
        }

        // "ffmpeg.exe" must not become "ffmpeg.exe.exe".
        if (HasExecutableExtension(executableName))
        {
            return [executableName];
        }

        return
        [
            executableName,
            .. GetWindowsExtensions().Select(extension => executableName + extension),
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

    private static bool HasExecutableExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length > 0
            && GetWindowsExtensions().Contains(extension, StringComparer.OrdinalIgnoreCase);
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
