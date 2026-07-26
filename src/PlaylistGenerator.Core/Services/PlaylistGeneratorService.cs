using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Builds a shuffled interval playlist and writes it as UTF-8 <c>.m3u8</c>.
/// </summary>
public sealed class PlaylistGeneratorService : IPlaylistGenerator
{
    private const string ExtendedM3uHeader = "#EXTM3U";

    private readonly IAudioFileCatalog _catalog;
    private readonly ITrackShuffler _shuffler;

    public PlaylistGeneratorService(IAudioFileCatalog catalog, ITrackShuffler shuffler)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(shuffler);
        _catalog = catalog;
        _shuffler = shuffler;
    }

    /// <inheritdoc />
    /// <exception cref="PlaylistValidationException">The request cannot produce a playlist.</exception>
    /// <exception cref="PlaylistIOException">The library changed, or the file could not be written.</exception>
    public PlaylistResult Generate(PlaylistRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var specialFile = ValidateRequest(request);
        var sourceTracks = _catalog
            .Scan(request.SourceDirectory)
            .Where(track => !PathUtility.AreSameFull(track, specialFile))
            .ToArray();

        if (sourceTracks.Length == 0)
        {
            throw new PlaylistValidationException(
                "No supported audio files were found after excluding the special file.");
        }

        var entries = PlaylistComposer.Compose(
            _shuffler.Shuffle(sourceTracks),
            specialFile,
            request.InsertEvery);

        // Shuffling and composition only reorder and repeat known paths, so verifying the
        // distinct inputs covers every entry without re-checking the special file once per
        // block. A library that changed mid-run must not overwrite a good playlist.
        EnsureStillPresent(specialFile);
        foreach (var track in sourceTracks)
        {
            EnsureStillPresent(track);
        }

        var outputPath = PathUtility.GetFullPath(request.OutputPath);
        AtomicFileWriter.WriteAllLines(outputPath, [ExtendedM3uHeader, .. entries]);

        return new PlaylistResult(
            PathUtility.GetFullPath(request.SourceDirectory),
            specialFile,
            outputPath,
            sourceTracks.Length,
            entries.Count,
            request.InsertEvery);
    }

    /// <summary>Validates the request and returns the resolved special-file path.</summary>
    private static string ValidateRequest(PlaylistRequest request)
    {
        if (request.InsertEvery < 1)
        {
            throw new PlaylistValidationException("Insert every must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(request.SpecialFile))
        {
            throw new PlaylistValidationException("Special file is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new PlaylistValidationException("Output path is required.");
        }

        var specialFile = PathUtility.GetFullPath(request.SpecialFile);
        if (!File.Exists(specialFile))
        {
            throw new PlaylistValidationException(
                $"Special file '{request.SpecialFile}' does not exist.");
        }

        if (!AudioFormats.IsSupported(specialFile))
        {
            throw new PlaylistValidationException(
                $"Special file '{request.SpecialFile}' must use a supported audio extension.");
        }

        return specialFile;
    }

    private static void EnsureStillPresent(string path)
    {
        if (!File.Exists(path))
        {
            throw new PlaylistIOException(
                $"Audio file '{path}' became unavailable while the playlist was being built.");
        }
    }
}
