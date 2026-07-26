using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;

namespace PlaylistGenerator.Core.Abstractions;

public interface IAudioFileCatalog
{
    IReadOnlyList<string> Scan(string sourceDirectory);
}

public interface ITrackShuffler
{
    IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks);
}

public interface IPlaylistGenerator
{
    PlaylistResult Generate(PlaylistRequest request);
}

public interface IAudioNormalizer
{
    Task<NormalizationResult> NormalizeAsync(
        NormalizationRequest request,
        IProgress<NormalizationProgress>? progress = null,
        PauseController? pauseController = null,
        CancellationToken cancellationToken = default);
}

public interface IFfmpegLocator
{
    string? Find();

    string? Find(string executableName);
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public interface IFfmpegInstallAdvisor
{
    FfmpegInstallPlan GetPlan();
}
