using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests;

public sealed class PlaylistGeneratorServiceTests
{
    [Fact]
    public void ComposerInsertsSpecialFileOnlyAfterCompleteBlocks()
    {
        var entries = PlaylistComposer.Compose(["A", "B", "C", "D", "E"], "ID", 2);

        Assert.Equal(["A", "B", "ID", "C", "D", "ID", "E"], entries);
    }

    [Fact]
    public void ComposerRejectsInvalidInterval()
    {
        var exception = Assert.Throws<PlaylistValidationException>(
            () => PlaylistComposer.Compose(["A"], "ID", 0));

        Assert.Contains("at least 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogFindsSupportedFilesRecursivelyAndSortsThem()
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
    public void CatalogDoesNotFollowDirectorySymbolicLinks()
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

        var files = new AudioFileCatalog().Scan(source);

        Assert.Empty(files);
    }

    [Fact]
    public void GeneratorWritesUtf8PlaylistAndExcludesSpecialFileFromSourcePool()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var first = temporary.CreateFile("music/one.mp3");
        var second = temporary.CreateFile("music/two.ogg");
        var special = temporary.CreateFile("music/id.mp3");
        var output = temporary.GetPath("output/mix.m3u8");
        var service = CreateService();

        var result = service.Generate(new PlaylistRequest(source, special, 2, output));
        var lines = File.ReadAllLines(output);
        var bytes = File.ReadAllBytes(output);

        Assert.Equal("#EXTM3U", lines[0]);
        Assert.Equal((byte)'#', bytes[0]);
        Assert.Equal(1, lines.Count(line => line == special));
        Assert.Contains(first, lines);
        Assert.Contains(second, lines);
        Assert.Equal(2, result.SourceTrackCount);
        Assert.Equal(3, result.PlaylistEntryCount);
    }

    [Fact]
    public void GeneratorRejectsUnsupportedSpecialFile()
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
    public void GeneratorKeepsExistingPlaylistWhenTrackDisappearsBeforeWrite()
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
        Assert.Equal("existing\n", File.ReadAllText(output));
    }

    [Fact]
    public void GeneratorRejectsLibraryContainingOnlyTheSpecialFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var special = temporary.CreateFile("music/id.mp3");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => CreateService().Generate(
                new PlaylistRequest(source, special, 2, temporary.GetPath("mix.m3u8"))));

        Assert.Contains("after excluding", exception.Message, StringComparison.Ordinal);
    }

    private static PlaylistGeneratorService CreateService() =>
        new(new AudioFileCatalog(), new FixedTrackShuffler());

    private sealed class DeletingShuffler(string track) : PlaylistGenerator.Core.Abstractions.ITrackShuffler
    {
        public IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks)
        {
            File.Delete(track);
            return tracks;
        }
    }
}
