using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Core.Infrastructure;

public sealed class RandomTrackShuffler : ITrackShuffler
{
    private readonly Random _random;

    public RandomTrackShuffler()
        : this(Random.Shared)
    {
    }

    public RandomTrackShuffler(Random random)
    {
        _random = random;
    }

    public IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks)
    {
        var shuffled = tracks.ToArray();
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swapIndex = _random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled;
    }
}
