using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class PlaylistGeneratorServiceTests
{
    [Fact]
    public void WritesUtf8PlaylistAndExcludesTheSpecialFileFromTheSourcePool()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var first = temporary.CreateFile("music/one.mp3");
        var second = temporary.CreateFile("music/two.ogg");
        var special = temporary.CreateFile("music/id.mp3");
        var output = temporary.GetPath("output/mix.m3u8");

        var result = CreateService().Generate(new PlaylistRequest(source, special, 2, output));
        var lines = File.ReadAllLines(output);
        var bytes = File.ReadAllBytes(output);

        Assert.Equal("#EXTM3U", lines[0]);

        // No byte-order mark: VLC expects a plain UTF-8 m3u8.
        Assert.Equal((byte)'#', bytes[0]);
        Assert.Equal(1, lines.Count(line => line == special));
        Assert.Contains(first, lines);
        Assert.Contains(second, lines);
        Assert.Equal(2, result.SourceTrackCount);
        Assert.Equal(3, result.PlaylistEntryCount);
    }

    [Fact]
    public void WritesNonAsciiPathsAsUtf8()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var track = temporary.CreateFile("music/宇多田-café.mp3");
        var special = temporary.CreateFile("id.mp3");
        var output = temporary.GetPath("mix.m3u8");

        CreateService().Generate(new PlaylistRequest(source, special, 2, output));

        var text = File.ReadAllText(output, System.Text.Encoding.UTF8);
        Assert.Contains(track, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAbsolutePathsRegardlessOfInputForm()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var special = temporary.CreateFile("id.mp3");
        var output = temporary.GetPath("nested/../mix.m3u8");

        var result = CreateService().Generate(new PlaylistRequest(source, special, 2, output));

        Assert.Equal(temporary.GetPath("mix.m3u8"), result.OutputPath);
        Assert.True(Path.IsPathFullyQualified(result.SourceDirectory));
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public void LeavesNoTemporaryFileBehind()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var special = temporary.CreateFile("id.mp3");

        CreateService().Generate(
            new PlaylistRequest(source, special, 2, temporary.GetPath("mix.m3u8")));

        Assert.DoesNotContain(
            temporary.EnumerateRelativeFiles(),
            file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsAnExistingPlaylistWhenATrackDisappearsBeforeTheWrite()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var track = temporary.CreateFile("music/song.mp3");
        var special = temporary.CreateFile("id.mp3");
        var output = temporary.CreateFile("mix.m3u8", "existing\n");
        var service = new PlaylistGeneratorService(
            new AudioFileCatalog(),
            new DeletingShuffler(track));

        Assert.Throws<PlaylistIOException>(
            () => service.Generate(new PlaylistRequest(source, special, 2, output)));

        // The previous playlist must survive a failed regeneration intact.
        Assert.Equal("existing\n", File.ReadAllText(output));
    }

    [Fact]
    public void RejectsAnUnsupportedSpecialFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/song.mp3");
        var special = temporary.CreateFile("id.txt");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => CreateService().Generate(
                new PlaylistRequest(source, special, 2, temporary.GetPath("mix.m3u8"))));

        Assert.Contains("supported audio extension", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAMissingSpecialFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/song.mp3");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => CreateService().Generate(
                new PlaylistRequest(
                    source,
                    temporary.GetPath("absent.mp3"),
                    2,
                    temporary.GetPath("mix.m3u8"))));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsALibraryContainingOnlyTheSpecialFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var special = temporary.CreateFile("music/id.mp3");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => CreateService().Generate(
                new PlaylistRequest(source, special, 2, temporary.GetPath("mix.m3u8"))));

        Assert.Contains("after excluding", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "at least 1")]
    [InlineData(-3, "at least 1")]
    public void RejectsAnIntervalBelowOne(int insertEvery, string expected)
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var special = temporary.CreateFile("id.mp3");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => CreateService().Generate(
                new PlaylistRequest(source, special, insertEvery, temporary.GetPath("mix.m3u8"))));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "", "Special file is required.")]
    [InlineData("  ", "  ", "Special file is required.")]
    public void RejectsBlankRequiredPaths(string specialFile, string outputPath, string expected)
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => CreateService().Generate(
                new PlaylistRequest(source, specialFile, 2, outputPath)));

        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public void RejectsABlankOutputPath()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var special = temporary.CreateFile("id.mp3");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => CreateService().Generate(new PlaylistRequest(source, special, 2, "  ")));

        Assert.Equal("Output path is required.", exception.Message);
    }

    [Fact]
    public void RejectsANullRequest() =>
        Assert.Throws<ArgumentNullException>(() => CreateService().Generate(null!));

    [Fact]
    public void RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PlaylistGeneratorService(null!, new FixedTrackShuffler()));
        Assert.Throws<ArgumentNullException>(
            () => new PlaylistGeneratorService(new AudioFileCatalog(), null!));
    }

    private static PlaylistGeneratorService CreateService() =>
        new(new AudioFileCatalog(), new FixedTrackShuffler());

    /// <summary>Deletes a track mid-run, simulating a library that changes during generation.</summary>
    private sealed class DeletingShuffler(string track) : ITrackShuffler
    {
        public IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks)
        {
            File.Delete(track);
            return tracks;
        }
    }
}
