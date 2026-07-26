using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Services;

namespace PlaylistGenerator.Core.Composition;

/// <summary>
/// The application's composition root for platform-neutral services.
/// </summary>
/// <remarks>
/// Both the desktop and command-line hosts build their object graph from here, so the two
/// front ends cannot drift apart. Wiring is explicit and typed rather than resolved from a
/// container, which keeps a missing registration a compile error instead of a startup crash.
/// </remarks>
public sealed class CoreServices
{
    private CoreServices(
        IAudioFileCatalog catalog,
        IExecutableLocator executableLocator,
        IProcessRunner processRunner,
        IPlaylistGenerator playlistGenerator,
        IAudioNormalizer audioNormalizer,
        IFfmpegInstallAdvisor ffmpegInstallAdvisor)
    {
        Catalog = catalog;
        ExecutableLocator = executableLocator;
        ProcessRunner = processRunner;
        PlaylistGenerator = playlistGenerator;
        AudioNormalizer = audioNormalizer;
        FfmpegInstallAdvisor = ffmpegInstallAdvisor;
    }

    public IAudioFileCatalog Catalog { get; }

    public IExecutableLocator ExecutableLocator { get; }

    public IProcessRunner ProcessRunner { get; }

    public IPlaylistGenerator PlaylistGenerator { get; }

    public IAudioNormalizer AudioNormalizer { get; }

    public IFfmpegInstallAdvisor FfmpegInstallAdvisor { get; }

    /// <summary>
    /// Builds the production object graph backed by the real filesystem and process APIs.
    /// </summary>
    public static CoreServices CreateDefault() =>
        Create(new AudioFileCatalog(), new ExecutableLocator(), new ProcessRunner(), new RandomTrackShuffler());

    /// <summary>
    /// Builds the object graph over caller-supplied infrastructure, for tests and for hosts
    /// that need a deterministic shuffle.
    /// </summary>
    public static CoreServices Create(
        IAudioFileCatalog catalog,
        IExecutableLocator executableLocator,
        IProcessRunner processRunner,
        ITrackShuffler shuffler)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(executableLocator);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(shuffler);

        return new CoreServices(
            catalog,
            executableLocator,
            processRunner,
            new PlaylistGeneratorService(catalog, shuffler),
            new AudioNormalizationService(catalog, executableLocator, processRunner),
            new FfmpegInstallAdvisor(executableLocator));
    }
}
