using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Presentation.ViewModels;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Presentation;

public sealed class NormalizationViewModelTests
{
    [Fact]
    public async Task BrowsingASourceFolderSuggestsAnOutputFolder()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");
        var viewModel = CreateViewModel(picker: new FakeFilePickerService().AddFolder(music));

        await viewModel.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal(music, viewModel.SourceDirectory);
        Assert.Equal(temporary.GetPath("music-normalized"), viewModel.OutputDirectory);
    }

    [Fact]
    public async Task BrowsingTheOutputFolderUpdatesOnlyThatField()
    {
        var viewModel = CreateViewModel(picker: new FakeFilePickerService().AddFolder("/normalized"));

        await viewModel.BrowseOutputCommand.ExecuteAsync(null);

        Assert.Equal("/normalized", viewModel.OutputDirectory);
        Assert.Equal(string.Empty, viewModel.SourceDirectory);
    }

    [Fact]
    public void SuggestedDefaultsFillOnlyEmptyFields()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");
        var viewModel = CreateViewModel();

        viewModel.SuggestDefaults(music);

        Assert.Equal(music, viewModel.SourceDirectory);
        Assert.Equal(temporary.GetPath("music-normalized"), viewModel.OutputDirectory);
    }

    [Fact]
    public void SuggestedDefaultsNeverOverwriteAUserChoice()
    {
        var viewModel = CreateViewModel();
        viewModel.SourceDirectory = "/chosen-source";
        viewModel.OutputDirectory = "/chosen-output";

        viewModel.SuggestDefaults("/music");

        Assert.Equal("/chosen-source", viewModel.SourceDirectory);
        Assert.Equal("/chosen-output", viewModel.OutputDirectory);
    }

    [Fact]
    public async Task ProgressAndResultCountsReachTheViewModel()
    {
        NormalizationViewModel? viewModel = null;
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
        var status = new StatusViewModel();
        viewModel = CreateViewModel(normalizer, status: status);
        viewModel.SourceDirectory = "music";
        viewModel.OutputDirectory = "normalized";

        await viewModel.NormalizeCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.NormalizedFileCount);
        Assert.Equal(1, viewModel.SkippedFileCount);
        Assert.Equal(3, viewModel.ProgressMaximum);
        Assert.Equal(2, viewModel.ProgressValue);
        Assert.Contains("complete", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProgressTextReturnsToIdleWhenTheRunFinishes()
    {
        var viewModel = CreateViewModel();

        await viewModel.NormalizeCommand.ExecuteAsync(null);

        Assert.Equal(NormalizationViewModel.IdleProgressText, viewModel.ProgressText);
    }

    [Fact]
    public async Task CountsFromAPreviousRunAreClearedWhenANewOneStarts()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalizer = new FakeAudioNormalizer();
        var viewModel = CreateViewModel(normalizer);

        await viewModel.NormalizeCommand.ExecuteAsync(null);
        Assert.Equal(FakeAudioNormalizer.NormalizedFileCount, viewModel.NormalizedFileCount);

        normalizer.Handler = async (request, _, _, _) =>
        {
            started.SetResult();

            // Hold the run open so the reset state can be observed mid-flight.
            await Task.Yield();
            return new NormalizationResult(
                request.SourceDirectory,
                request.OutputDirectory,
                0,
                0,
                false);
        };

        var operation = viewModel.NormalizeCommand.ExecuteAsync(null);
        await started.Task;
        await operation;

        Assert.Equal(0, viewModel.NormalizedFileCount);
        Assert.Equal(0, viewModel.SkippedFileCount);
    }

    [Fact]
    public async Task PauseResumeAndStopControlAnActiveRun()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IPauseSignal? observedSignal = null;
        var normalizer = new FakeAudioNormalizer
        {
            Handler = async (request, _, pauseSignal, token) =>
            {
                observedSignal = pauseSignal;
                started.SetResult();
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
        var status = new StatusViewModel();
        var viewModel = CreateViewModel(normalizer, status: status);

        var operation = viewModel.NormalizeCommand.ExecuteAsync(null);
        await started.Task;

        Assert.True(viewModel.IsRunning);
        viewModel.PauseCommand.Execute(null);
        Assert.True(viewModel.IsPaused);
        Assert.True(observedSignal?.IsPaused);

        viewModel.ResumeCommand.Execute(null);
        Assert.False(viewModel.IsPaused);
        Assert.False(observedSignal?.IsPaused);

        viewModel.StopCommand.Execute(null);
        await operation;

        Assert.False(viewModel.IsRunning);
        Assert.Contains("stopped", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoppingWhilePausedReleasesThePauseSoTheRunCanUnwind()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalizer = new FakeAudioNormalizer
        {
            Handler = async (request, _, pauseSignal, token) =>
            {
                started.SetResult();

                // A paused run must still observe cancellation rather than parking forever.
                await pauseSignal!.WaitWhilePausedAsync(token);
                return new NormalizationResult(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    0,
                    0,
                    true);
            },
        };
        var viewModel = CreateViewModel(normalizer);

        var operation = viewModel.NormalizeCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.PauseCommand.Execute(null);
        viewModel.StopCommand.Execute(null);

        await operation;
        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.IsPaused);
    }

    [Fact]
    public void TransportCommandsAreDisabledWhileIdle()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.PauseCommand.CanExecute(null));
        Assert.False(viewModel.ResumeCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.NormalizeCommand.CanExecute(null));
    }

    [Fact]
    public async Task AFailureBecomesAUserMessageWithDebugDetail()
    {
        var normalizer = new FakeAudioNormalizer
        {
            Handler = (_, _, _, _) => throw new InvalidOperationException("ffmpeg exploded"),
        };
        var status = new StatusViewModel();
        var coordinator = new OperationCoordinator();

        await CreateViewModel(normalizer, status: status, coordinator: coordinator)
            .NormalizeCommand.ExecuteAsync(null);

        Assert.Contains("ffmpeg exploded", status.Message, StringComparison.Ordinal);
        Assert.True(status.HasErrorDetails);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task CancelActiveRunStopsAnInFlightRun()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalizer = new FakeAudioNormalizer
        {
            Handler = async (request, _, _, token) =>
            {
                started.SetResult();
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
        var viewModel = CreateViewModel(normalizer);

        var operation = viewModel.NormalizeCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.CancelActiveRun();
        await operation;

        Assert.False(viewModel.IsRunning);
    }

    [Fact]
    public void DisposingIsIdempotentAndSafeWhileIdle()
    {
        var viewModel = CreateViewModel();

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.False(viewModel.IsRunning);
    }

    [Fact]
    public void RejectsNullDependencies()
    {
        var status = new StatusViewModel();
        var coordinator = new OperationCoordinator();
        var picker = new FakeFilePickerService();
        var normalizer = new FakeAudioNormalizer();

        Assert.Throws<ArgumentNullException>(
            () => new NormalizationViewModel(null!, picker, status, coordinator));
        Assert.Throws<ArgumentNullException>(
            () => new NormalizationViewModel(normalizer, null!, status, coordinator));
        Assert.Throws<ArgumentNullException>(
            () => new NormalizationViewModel(normalizer, picker, null!, coordinator));
        Assert.Throws<ArgumentNullException>(
            () => new NormalizationViewModel(normalizer, picker, status, null!));
    }

    private static NormalizationViewModel CreateViewModel(
        FakeAudioNormalizer? normalizer = null,
        FakeFilePickerService? picker = null,
        StatusViewModel? status = null,
        OperationCoordinator? coordinator = null) =>
        new(
            normalizer ?? new FakeAudioNormalizer(),
            picker ?? new FakeFilePickerService(),
            status ?? new StatusViewModel(),
            coordinator ?? new OperationCoordinator());
}
