using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class AudioFileCatalogTests
{
    [Fact]
    public void FindsSupportedFilesRecursivelyAndSortsThem()
    {
        using var temporary = new TemporaryDirectory();
        temporary.CreateFile("music/Z.MP3");
        temporary.CreateFile("music/album/a.flac");
        temporary.CreateFile("music/album/cover.jpg");

        var files = new AudioFileCatalog().Scan(temporary.GetPath("music"));

        Assert.Equal(2, files.Count);
        Assert.EndsWith("a.flac", files[0], StringComparison.Ordinal);
        Assert.EndsWith("Z.MP3", files[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnsAbsolutePathsForARelativeInput()
    {
        using var temporary = new TemporaryDirectory();
        temporary.CreateFile("music/one.mp3");

        var files = new AudioFileCatalog().Scan(temporary.GetPath("music"));

        Assert.All(files, file => Assert.True(Path.IsPathFullyQualified(file)));
    }

    [Fact]
    public void ReturnsAnEmptyListForADirectoryWithNoAudio()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/readme.txt");

        Assert.Empty(new AudioFileCatalog().Scan(source));
    }

    [Fact]
    public void DoesNotFollowDirectorySymbolicLinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var outside = temporary.CreateDirectory("outside");
        temporary.CreateFile("outside/hidden.mp3");
        Directory.CreateSymbolicLink(Path.Combine(source, "linked"), outside);

        Assert.Empty(new AudioFileCatalog().Scan(source));
    }

    [Fact]
    public void SurvivesASymbolicLinkCycle()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");

        // Following this link would make the walk unbounded.
        Directory.CreateSymbolicLink(Path.Combine(source, "loop"), source);

        Assert.Single(new AudioFileCatalog().Scan(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankDirectory(string sourceDirectory)
    {
        var exception = Assert.Throws<PlaylistValidationException>(
            () => new AudioFileCatalog().Scan(sourceDirectory));

        Assert.Contains("required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAMissingDirectory()
    {
        using var temporary = new TemporaryDirectory();

        var exception = Assert.Throws<PlaylistValidationException>(
            () => new AudioFileCatalog().Scan(temporary.GetPath("absent")));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsATreeItCannotRead()
    {
        // Permission bits are a Unix concept, and root ignores them.
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file permissions are required.");
            return;
        }

        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("USER") == "root",
            "Root bypasses the permission bits this test relies on.");

        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/locked/one.mp3");
        var locked = temporary.GetPath("music/locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            var exception = Assert.Throws<PlaylistIOException>(
                () => new AudioFileCatalog().Scan(source));

            // A partial listing would silently drop tracks from the playlist.
            Assert.Contains("Unable to scan", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(
                locked,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
