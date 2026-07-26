using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Core.Infrastructure;

/// <summary>
/// Shuffles tracks with an unbiased Fisher-Yates pass.
/// </summary>
public sealed class RandomTrackShuffler : ITrackShuffler
{
    private readonly Random _random;

    /// <summary>Creates a shuffler backed by the shared thread-safe generator.</summary>
    public RandomTrackShuffler()
        : this(Random.Shared)
    {
    }

    /// <summary>Creates a shuffler backed by a caller-supplied generator, for repeatability.</summary>
    public RandomTrackShuffler(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var shuffled = tracks.ToArray();
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swapIndex = _random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled;
    }
}
