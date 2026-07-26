using PlaylistGenerator.Core.Exceptions;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Interleaves a special file into a track list at a fixed interval.
/// </summary>
public static class PlaylistComposer
{
    /// <summary>
    /// Returns <paramref name="tracks"/> with <paramref name="specialFile"/> inserted after
    /// each complete block of <paramref name="insertEvery"/> tracks.
    /// </summary>
    /// <remarks>
    /// An incomplete trailing block gets no insertion, so three tracks at an interval of two
    /// produce <c>A, B, ID, C</c>.
    /// </remarks>
    /// <exception cref="PlaylistValidationException">The interval is below one.</exception>
    public static IReadOnlyList<string> Compose(
        IReadOnlyList<string> tracks,
        string specialFile,
        int insertEvery)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentException.ThrowIfNullOrWhiteSpace(specialFile);

        if (insertEvery < 1)
        {
            throw new PlaylistValidationException("Insert every must be at least 1.");
        }

        var entries = new List<string>(tracks.Count + (tracks.Count / insertEvery));
        for (var index = 0; index < tracks.Count; index++)
        {
            entries.Add(tracks[index]);
            if ((index + 1) % insertEvery == 0)
            {
                entries.Add(specialFile);
            }
        }

        return entries;
    }
}
