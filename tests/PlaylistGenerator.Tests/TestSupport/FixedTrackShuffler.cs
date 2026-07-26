using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Preserves input order so playlist assertions stay deterministic.
/// </summary>
public sealed class FixedTrackShuffler : ITrackShuffler
{
    public IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks) => tracks.ToArray();
}
