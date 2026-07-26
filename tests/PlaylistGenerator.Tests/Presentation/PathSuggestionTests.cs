using PlaylistGenerator.Presentation.Infrastructure;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Presentation;

public sealed class PathSuggestionTests
{
    [Fact]
    public void UsesTheFolderNameForTheSuggestedPlaylist()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");

        Assert.Equal(
            Path.Combine(music, "music-playlist.m3u8"),
            PathSuggestion.BuildPlaylistPath(music));
    }

    [Fact]
    public void PlacesTheNormalizedFolderBesideTheSourceRatherThanInsideIt()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");

        // Nesting it inside would make a later scan of the source pick up the copies.
        Assert.Equal(
            temporary.GetPath("music-normalized"),
            PathSuggestion.BuildNormalizedOutputPath(music));
    }

    [Fact]
    public void IgnoresTrailingSeparators()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");

        Assert.Equal("music", PathSuggestion.GetDirectoryName(music + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FallsBackToAGenericNameForARootDirectory()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("/"))!;

        Assert.Equal("music", PathSuggestion.GetDirectoryName(root));
    }

    [Fact]
    public void RejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => PathSuggestion.GetDirectoryName(null!));
        Assert.Throws<ArgumentNullException>(() => PathSuggestion.BuildPlaylistPath(null!));
        Assert.Throws<ArgumentNullException>(
            () => PathSuggestion.BuildNormalizedOutputPath(null!));
    }
}
