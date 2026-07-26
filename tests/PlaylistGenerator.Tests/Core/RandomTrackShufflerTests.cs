using PlaylistGenerator.Core.Infrastructure;

namespace PlaylistGenerator.Tests.Core;

public sealed class RandomTrackShufflerTests
{
    [Fact]
    public void ReturnsANewPermutationWithoutMutatingTheInput()
    {
        string[] tracks = ["A", "B", "C"];

        var shuffled = new RandomTrackShuffler(new ZeroRandom()).Shuffle(tracks);

        Assert.Equal(["B", "C", "A"], shuffled);
        Assert.Equal(["A", "B", "C"], tracks);
    }

    [Fact]
    public void PreservesEveryTrackExactlyOnce()
    {
        var tracks = Enumerable.Range(0, 200).Select(index => $"track-{index}").ToArray();

        var shuffled = new RandomTrackShuffler(new Random(Seed: 12345)).Shuffle(tracks);

        Assert.Equal(tracks.Length, shuffled.Count);
        Assert.Equal(tracks.Order(StringComparer.Ordinal), shuffled.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void HandlesDegenerateInputSizes(int count)
    {
        var tracks = Enumerable.Range(0, count).Select(index => $"track-{index}").ToArray();

        Assert.Equal(tracks, new RandomTrackShuffler().Shuffle(tracks));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new RandomTrackShuffler(null!));
        Assert.Throws<ArgumentNullException>(() => new RandomTrackShuffler().Shuffle(null!));
    }

    /// <summary>Always swaps with index zero, making the permutation predictable.</summary>
    private sealed class ZeroRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }
}
