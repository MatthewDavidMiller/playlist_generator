using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Tests.TestSupport;

public sealed class FixedTrackShuffler : ITrackShuffler
{
    public IReadOnlyList<string> Shuffle(IReadOnlyList<string> tracks) => tracks.ToArray();
}

public sealed class FakeFfmpegLocator(string? ffmpeg = "ffmpeg") : IFfmpegLocator
{
    public string? Ffmpeg { get; set; } = ffmpeg;

    public string? Find() => Ffmpeg;

    public string? Find(string executableName) =>
        executableName == "ffmpeg" ? Ffmpeg : null;
}

public sealed class FakeProcessRunner : IProcessRunner
{
    public const string ValidAnalysisJson =
        """
        {
          "input_i": "-18.42",
          "input_tp": "-2.10",
          "input_lra": "4.70",
          "input_thresh": "-28.54",
          "target_offset": "0.12"
        }
        """;

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public int AnalysisExitCode { get; set; }

    public int EncodeExitCode { get; set; }

    public bool CreateEncodedFile { get; set; } = true;

    public string AnalysisOutput { get; set; } = ValidAnalysisJson;

    public Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>>? Handler
    {
        get;
        set;
    }

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(arguments.ToArray());
        if (Handler is not null)
        {
            return await Handler(arguments, cancellationToken).ConfigureAwait(false);
        }

        if (arguments[^1] == "-")
        {
            return new ProcessResult(AnalysisExitCode, string.Empty, AnalysisOutput);
        }

        if (CreateEncodedFile && EncodeExitCode == 0)
        {
            File.WriteAllBytes(arguments[^1], [1, 2, 3]);
        }

        return new ProcessResult(
            EncodeExitCode,
            string.Empty,
            EncodeExitCode == 0 ? string.Empty : "encoding failed");
    }
}

public sealed class FakeFilePickerService : IFilePickerService
{
    private readonly Queue<string?> _folders = new();
    private readonly Queue<string?> _audioFiles = new();
    private readonly Queue<string?> _playlistFiles = new();

    public void AddFolder(string? path) => _folders.Enqueue(path);

    public void AddAudioFile(string? path) => _audioFiles.Enqueue(path);

    public void AddPlaylistFile(string? path) => _playlistFiles.Enqueue(path);

    public Task<string?> PickFolderAsync(string title) =>
        Task.FromResult(_folders.TryDequeue(out var path) ? path : null);

    public Task<string?> PickAudioFileAsync(string title) =>
        Task.FromResult(_audioFiles.TryDequeue(out var path) ? path : null);

    public Task<string?> PickPlaylistOutputAsync(string suggestedFileName) =>
        Task.FromResult(_playlistFiles.TryDequeue(out var path) ? path : null);
}

public sealed class FakeThemeService : IThemeService
{
    public int ToggleCount { get; private set; }

    public string Toggle()
    {
        ToggleCount++;
        return "Dark";
    }
}

public sealed class FakePlaylistGenerator : IPlaylistGenerator
{
    public PlaylistRequest? Request { get; private set; }

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
            4,
            5,
            request.InsertEvery);
    }
}

public sealed class FakeAudioNormalizer : IAudioNormalizer
{
    public NormalizationRequest? Request { get; private set; }

    public Func<
        NormalizationRequest,
        IProgress<NormalizationProgress>?,
        PauseController?,
        CancellationToken,
        Task<NormalizationResult>>? Handler
    { get; set; }

    public Task<NormalizationResult> NormalizeAsync(
        NormalizationRequest request,
        IProgress<NormalizationProgress>? progress = null,
        PauseController? pauseController = null,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        return Handler?.Invoke(request, progress, pauseController, cancellationToken)
            ?? Task.FromResult(
                new NormalizationResult(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    2,
                    1,
                    false));
    }
}

public sealed class FakeFfmpegInstallAdvisor(FfmpegInstallPlan plan)
    : IFfmpegInstallAdvisor
{
    public FfmpegInstallPlan GetPlan() => plan;
}
