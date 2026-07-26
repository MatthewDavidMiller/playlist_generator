using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Records the request it receives and returns a fixed summary.
/// </summary>
public sealed class FakePlaylistGenerator : IPlaylistGenerator
{
    public const int SourceTrackCount = 4;

    public const int PlaylistEntryCount = 5;

    public PlaylistRequest? Request { get; private set; }

    /// <summary>Thrown instead of returning a result when set.</summary>
    public Exception? Exception { get; set; }

    public PlaylistResult Generate(PlaylistRequest request)
    {
        Request = request;
        if (Exception is not null)
        {
            throw Exception;
        }

        return new PlaylistResult(
            request.SourceDirectory,
            request.SpecialFile,
            request.OutputPath,
            SourceTrackCount,
            PlaylistEntryCount,
            request.InsertEvery);
    }
}
