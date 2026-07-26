namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// Produces a playback order for a set of tracks.
/// </summary>
public interface ITrackShuffler
{
    /// <summary>
    /// Returns a reordered copy of <paramref name="tracks"/>, leaving the input untouched.
    /// </summary>
    IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks);
}
