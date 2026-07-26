using PlaylistGenerator.Core.Exceptions;

namespace PlaylistGenerator.Core.Services;

public static class PlaylistComposer
{
    public static IReadOnlyList<string> Compose(
        IReadOnlyList<string> tracks,
        string specialFile,
        int insertEvery)
    {
        if (insertEvery < 1)
        {
            throw new PlaylistValidationException("Insert every must be at least 1.");
        }

        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentException.ThrowIfNullOrWhiteSpace(specialFile);

        var specialEntryCount = tracks.Count / insertEvery;
        var entries = new List<string>(tracks.Count + specialEntryCount);

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
