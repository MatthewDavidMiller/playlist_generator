using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// Builds a shuffled interval playlist from a music library.
/// </summary>
public interface IPlaylistGenerator
{
    /// <summary>
    /// Writes the playlist described by <paramref name="request"/> and summarizes it.
    /// </summary>
    PlaylistResult Generate(PlaylistRequest request);
}
