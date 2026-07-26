using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Services;

namespace PlaylistGenerator.Tests.Core;

public sealed class PlaylistComposerTests
{
    [Fact]
    public void InsertsTheSpecialFileOnlyAfterCompleteBlocks()
    {
        var entries = PlaylistComposer.Compose(["A", "B", "C", "D", "E"], "ID", 2);

        Assert.Equal(["A", "B", "ID", "C", "D", "ID", "E"], entries);
    }

    [Fact]
    public void AppendsTheSpecialFileWhenTheLastBlockIsComplete()
    {
        var entries = PlaylistComposer.Compose(["A", "B"], "ID", 2);

        Assert.Equal(["A", "B", "ID"], entries);
    }

    [Fact]
    public void AnIntervalOfOneAlternatesEveryTrack()
    {
        var entries = PlaylistComposer.Compose(["A", "B"], "ID", 1);

        Assert.Equal(["A", "ID", "B", "ID"], entries);
    }

    [Fact]
    public void AnIntervalLargerThanTheLibraryInsertsNothing()
    {
        var entries = PlaylistComposer.Compose(["A", "B"], "ID", 10);

        Assert.Equal(["A", "B"], entries);
    }

    [Fact]
    public void AnEmptyTrackListProducesNoEntries() =>
        Assert.Empty(PlaylistComposer.Compose([], "ID", 2));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsAnIntervalBelowOne(int insertEvery)
    {
        var exception = Assert.Throws<PlaylistValidationException>(
            () => PlaylistComposer.Compose(["A"], "ID", insertEvery));

        Assert.Contains("at least 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAMissingSpecialFile()
    {
        Assert.Throws<ArgumentNullException>(() => PlaylistComposer.Compose(["A"], null!, 2));
        Assert.Throws<ArgumentException>(() => PlaylistComposer.Compose(["A"], "  ", 2));
        Assert.Throws<ArgumentNullException>(() => PlaylistComposer.Compose(null!, "ID", 2));
    }
}
