using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Presentation.ViewModels;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task PlaylistFolderSelectionSuggestsIndependentDefaults()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");
        var picker = new FakeFilePickerService();
        picker.AddFolder(music);
        var viewModel = CreateViewModel(picker: picker);

        await viewModel.BrowsePlaylistSourceCommand.ExecuteAsync(null);

        Assert.Equal(music, viewModel.PlaylistSourceDirectory);
        Assert.Equal(music, viewModel.NormalizationSourceDirectory);
        Assert.Equal(Path.Combine(music, "music-playlist.m3u8"), viewModel.OutputPath);
        Assert.Equal(temporary.GetPath("music-normalized"), viewModel.NormalizedOutputDirectory);
    }

    [Fact]
    public async Task PlaylistFolderSelectionDoesNotOverwriteNormalizationChoices()
    {
        var picker = new FakeFilePickerService();
        picker.AddFolder("/playlist");
        var viewModel = CreateViewModel(picker: picker);
        viewModel.NormalizationSourceDirectory = "/normalization";
        viewModel.NormalizedOutputDirectory = "/normalized";

        await viewModel.BrowsePlaylistSourceCommand.ExecuteAsync(null);

        Assert.Equal("/normalization", viewModel.NormalizationSourceDirectory);
        Assert.Equal("/normalized", viewModel.NormalizedOutputDirectory);
    }

    [Fact]
    public async Task IndividualBrowseCommandsUpdateTheirOwnPaths()
    {
        using var temporary = new TemporaryDirectory();
        var normalizationSource = temporary.CreateDirectory("normalization-source");
        var normalizedOutput = temporary.CreateDirectory("normalized-output");
        var special = temporary.CreateFile("station-id.mp3");
        var playlist = temporary.GetPath("mix.m3u8");
        var picker = new FakeFilePickerService();
        picker.AddAudioFile(special);
        picker.AddPlaylistFile(playlist);
        picker.AddFolder(normalizationSource);
        picker.AddFolder(normalizedOutput);
        var viewModel = CreateViewModel(picker: picker);

        await viewModel.BrowseSpecialFileCommand.ExecuteAsync(null);
        await viewModel.BrowsePlaylistOutputCommand.ExecuteAsync(null);
        await viewModel.BrowseNormalizationSourceCommand.ExecuteAsync(null);
        await viewModel.BrowseNormalizedOutputCommand.ExecuteAsync(null);

        Assert.Equal(special, viewModel.SpecialFile);
        Assert.Equal(playlist, viewModel.OutputPath);
        Assert.Equal(normalizationSource, viewModel.NormalizationSourceDirectory);
        Assert.Equal(normalizedOutput, viewModel.NormalizedOutputDirectory);
    }

    [Fact]
    public async Task GenerateCommandBuildsRequestAndReportsCounts()
    {
        var generator = new FakePlaylistGenerator();
        var viewModel = CreateViewModel(generator);
        viewModel.PlaylistSourceDirectory = "music";
        viewModel.SpecialFile = "id.mp3";
        viewModel.OutputPath = "mix.m3u8";
        viewModel.InsertEvery = 3;

        await viewModel.GeneratePlaylistCommand.ExecuteAsync(null);

        Assert.Equal(
            new PlaylistRequest("music", "id.mp3", 3, "mix.m3u8"),
            generator.Request);
        Assert.Contains("5 entries", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ExpectedErrorIncludesAUserMessageAndDebugDetails()
    {
        var generator = new FakePlaylistGenerator
        {
            Exception = new PlaylistValidationException("source is invalid"),
        };
        var viewModel = CreateViewModel(generator);

        await viewModel.GeneratePlaylistCommand.ExecuteAsync(null);

        Assert.Contains("source is invalid", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(
            nameof(PlaylistValidationException),
            viewModel.ErrorDetails,
            StringComparison.Ordinal);
        Assert.True(viewModel.HasErrorDetails);
    }

    [Fact]
    public async Task PauseResumeAndStopControlAnActiveNormalization()
    {
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PauseController? observedController = null;
        var normalizer = new FakeAudioNormalizer
        {
            Handler = async (request, _, controller, token) =>
            {
                observedController = controller;
                started.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    return new NormalizationResult(
                        request.SourceDirectory,
                        request.OutputDirectory,
                        0,
                        0,
                        true);
                }

                throw new InvalidOperationException("Unreachable");
            },
        };
        var viewModel = CreateViewModel(normalizer: normalizer);
        viewModel.NormalizationSourceDirectory = "music";
        viewModel.NormalizedOutputDirectory = "normalized";

        var operation = viewModel.NormalizeVolumeCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.PauseNormalizationCommand.Execute(null);

        Assert.True(viewModel.IsPaused);
        Assert.True(observedController?.IsPaused);

        viewModel.ResumeNormalizationCommand.Execute(null);
        Assert.False(viewModel.IsPaused);
        Assert.False(observedController?.IsPaused);

        viewModel.StopNormalizationCommand.Execute(null);
        await operation;

        Assert.False(viewModel.IsNormalizing);
        Assert.False(viewModel.IsBusy);
        Assert.Contains("stopped", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalizationProgressAndResultCountsReachTheViewModel()
    {
        MainViewModel? viewModel = null;
        var normalizer = new FakeAudioNormalizer
        {
            Handler = async (request, progress, _, token) =>
            {
                progress?.Report(
                    new NormalizationProgress(
                        3,
                        2,
                        1,
                        1,
                        "music/two.flac",
                        NormalizationAction.Encoding));

                while (viewModel?.ProgressMaximum != 3)
                {
                    await Task.Delay(1, token).ConfigureAwait(false);
                }

                return new NormalizationResult(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    2,
                    1,
                    false);
            },
        };
        viewModel = CreateViewModel(normalizer: normalizer);
        viewModel.NormalizationSourceDirectory = "music";
        viewModel.NormalizedOutputDirectory = "normalized";

        await viewModel.NormalizeVolumeCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.NormalizedFileCount);
        Assert.Equal(1, viewModel.SkippedFileCount);
        Assert.Equal(3, viewModel.ProgressMaximum);
        Assert.Equal(2, viewModel.ProgressValue);
        Assert.Contains("two.flac", viewModel.ProgressText, StringComparison.Ordinal);
        Assert.Contains("complete", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThemeCommandDelegatesToThemeService()
    {
        var theme = new FakeThemeService();
        var viewModel = CreateViewModel(theme: theme);

        viewModel.ToggleThemeCommand.Execute(null);

        Assert.Equal(1, theme.ToggleCount);
        Assert.Equal("Dark theme enabled.", viewModel.StatusMessage);
    }

    private static MainViewModel CreateViewModel(
        FakePlaylistGenerator? generator = null,
        FakeAudioNormalizer? normalizer = null,
        FakeFilePickerService? picker = null,
        FakeThemeService? theme = null) =>
        new(
            generator ?? new FakePlaylistGenerator(),
            normalizer ?? new FakeAudioNormalizer(),
            picker ?? new FakeFilePickerService(),
            theme ?? new FakeThemeService());
}
