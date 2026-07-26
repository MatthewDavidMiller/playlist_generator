using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Core.Infrastructure;

public sealed class ExecutableLocator : IFfmpegLocator
{
    public string? Find() => Find("ffmpeg");

    public string? Find(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        if (executableName.Contains(Path.DirectorySeparatorChar)
            || executableName.Contains(Path.AltDirectorySeparatorChar))
        {
            var explicitPath = PathUtility.GetFullPath(executableName);
            return IsExecutable(explicitPath) ? explicitPath : null;
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return null;
        }

        var candidateNames = OperatingSystem.IsWindows()
            ? new[] { executableName, $"{executableName}.exe" }
            : new[] { executableName };

        foreach (var directory in searchPath.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var candidateName in candidateNames)
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), candidateName);
                if (IsExecutable(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            const UnixFileMode executableBits =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & executableBits) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
