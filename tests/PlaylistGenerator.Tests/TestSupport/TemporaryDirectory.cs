namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// A scratch directory removed when the test finishes.
/// </summary>
public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"playlist-generator-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>The absolute root of this scratch directory.</summary>
    public string Path { get; }

    public string CreateDirectory(string relativePath)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relativePath, string contents = "")
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    /// <summary>Creates a file the operating system will treat as runnable.</summary>
    public string CreateExecutable(string relativePath)
    {
        var path = CreateFile(relativePath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    /// <summary>Returns the absolute path for a path relative to this directory.</summary>
    public string GetPath(string relativePath) =>
        System.IO.Path.Combine(Path, relativePath);

    /// <summary>Returns every file under this directory, relative to its root.</summary>
    public IReadOnlyList<string> EnumerateRelativeFiles() =>
        Directory
            .EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .Select(file => System.IO.Path.GetRelativePath(Path, file))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or DirectoryNotFoundException)
        {
            // A failed test should not be hidden by best-effort cleanup.
        }
    }
}
