using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class ExecutableLocatorTests
{
    [Fact]
    public void AcceptsExplicitExecutablePaths()
    {
        using var temporary = new TemporaryDirectory();
        var executable = temporary.CreateExecutable("tools/test-tool");

        Assert.Equal(Path.GetFullPath(executable), new ExecutableLocator().Find(executable));
    }

    [Fact]
    public void RejectsMissingAndNonExecutableFiles()
    {
        using var temporary = new TemporaryDirectory();
        var locator = new ExecutableLocator();

        Assert.Null(locator.Find(temporary.GetPath("missing-tool")));

        if (!OperatingSystem.IsWindows())
        {
            var nonExecutable = temporary.CreateFile("tools/not-executable");
            File.SetUnixFileMode(
                nonExecutable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Null(locator.Find(nonExecutable));
        }
    }

    [Fact]
    public void FindsABareNameOnTheSearchPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var directory = temporary.CreateDirectory("bin");
        var executable = temporary.CreateExecutable("bin/pg-test-tool");

        using var searchPath = new EnvironmentVariableScope("PATH", directory);

        Assert.Equal(Path.GetFullPath(executable), new ExecutableLocator().Find("pg-test-tool"));
    }

    [Fact]
    public void PrefersTheFirstMatchingSearchPathEntry()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var first = temporary.CreateDirectory("first");
        var second = temporary.CreateDirectory("second");
        var expected = temporary.CreateExecutable("first/pg-test-tool");
        temporary.CreateExecutable("second/pg-test-tool");

        using var searchPath = new EnvironmentVariableScope(
            "PATH",
            string.Join(Path.PathSeparator, first, second));

        Assert.Equal(Path.GetFullPath(expected), new ExecutableLocator().Find("pg-test-tool"));
    }

    [Fact]
    public void IgnoresBlankAndNonExecutableSearchPathEntries()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var empty = temporary.CreateDirectory("empty");
        var real = temporary.CreateDirectory("real");
        var executable = temporary.CreateExecutable("real/pg-test-tool");

        // A non-executable file of the same name must not shadow the real one.
        var shadow = temporary.CreateFile("empty/pg-test-tool");
        File.SetUnixFileMode(shadow, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        using var searchPath = new EnvironmentVariableScope(
            "PATH",
            string.Join(Path.PathSeparator, string.Empty, empty, "   ", real));

        Assert.Equal(Path.GetFullPath(executable), new ExecutableLocator().Find("pg-test-tool"));
    }

    [Fact]
    public void ReturnsNullWhenTheSearchPathIsUnset()
    {
        using var searchPath = new EnvironmentVariableScope("PATH", null);

        Assert.Null(new ExecutableLocator().Find("pg-test-tool"));
    }

    [Fact]
    public void RejectsABlankExecutableName()
    {
        var locator = new ExecutableLocator();

        Assert.Throws<ArgumentNullException>(() => locator.Find(null!));
        Assert.Throws<ArgumentException>(() => locator.Find("   "));
    }
}
