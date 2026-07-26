using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

public sealed class PlaylistGeneratorService : IPlaylistGenerator
{
    private readonly IAudioFileCatalog _catalog;
    private readonly ITrackShuffler _shuffler;

    public PlaylistGeneratorService(IAudioFileCatalog catalog, ITrackShuffler shuffler)
    {
        _catalog = catalog;
        _shuffler = shuffler;
    }

    public PlaylistResult Generate(PlaylistRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.InsertEvery < 1)
        {
            throw new PlaylistValidationException("Insert every must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(request.SpecialFile))
        {
            throw new PlaylistValidationException("Special file is required.");
        }

        var specialFile = PathUtility.GetFullPath(request.SpecialFile);
        if (!File.Exists(specialFile))
        {
            throw new PlaylistValidationException(
                $"Special file '{request.SpecialFile}' does not exist.");
        }

        if (!AudioFileCatalog.IsSupported(specialFile))
        {
            throw new PlaylistValidationException(
                $"Special file '{request.SpecialFile}' must use a supported audio extension.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new PlaylistValidationException("Output path is required.");
        }

        var sourceTracks = _catalog
            .Scan(request.SourceDirectory)
            .Where(track => !PathUtility.AreSame(track, specialFile))
            .ToArray();

        if (sourceTracks.Length == 0)
        {
            throw new PlaylistValidationException(
                "No supported audio files were found after excluding the special file.");
        }

        var shuffledTracks = _shuffler.Shuffle(sourceTracks);
        var entries = PlaylistComposer.Compose(
            shuffledTracks,
            specialFile,
            request.InsertEvery);
        var outputPath = PathUtility.GetFullPath(request.OutputPath);

        foreach (var entry in entries)
        {
            if (!File.Exists(entry))
            {
                throw new PlaylistIOException(
                    $"Audio file '{entry}' became unavailable while the playlist was being built.");
            }
        }

        AtomicFileWriter.WriteAllLines(outputPath, ["#EXTM3U", .. entries]);

        return new PlaylistResult(
            PathUtility.GetFullPath(request.SourceDirectory),
            specialFile,
            outputPath,
            sourceTracks.Length,
            entries.Count,
            request.InsertEvery);
    }
}
