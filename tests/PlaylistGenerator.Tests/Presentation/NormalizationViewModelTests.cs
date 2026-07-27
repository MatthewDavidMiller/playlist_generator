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
        var normalizer = new FakeAudioNormalizer
        {
            // The closing progress report and the summary agree, which is what a real run
            // produces. Asserting on values the two disagree about would only be testing
            // which of them the runtime happened to deliver last.
            Handler = (request, progress, _, _) =>
            {
                progress?.Report(
                    new NormalizationProgress(
                        3,
                        3,
                        2,
                        1,
                        0,
                        "music/two.flac",
                        NormalizationAction.Completed));

                return Task.FromResult(
                    NormalizationResults.Create(
                        request.SourceDirectory,
                        request.OutputDirectory,
                        normalizedFileCount: 2,
                        skippedFileCount: 1));
            },
        };
        var status = new StatusViewModel();
        var viewModel = CreateViewModel(normalizer, status: status);
        viewModel.SourceDirectory = "music";
        viewModel.OutputDirectory = "normalized";

        await viewModel.NormalizeCommand.ExecuteAsync(null);

        // Only a progress report sets the bar's bounds, so waiting on them proves the report
        // arrived rather than assuming it beat the run's completion.
        await Eventually.TrueAsync(
            () => viewModel.ProgressMaximum == 3 && viewModel.ProgressValue == 3,
            TestContext.Current.CancellationToken,
            "The progress report never reached the view model.");

        Assert.Equal(2, viewModel.NormalizedFileCount);
        Assert.Equal(1, viewModel.SkippedFileCount);
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
    public async Task ProgressTextNamesTheCurrentFileAndStep()
    {
        var normalizer = new FakeAudioNormalizer
        {
            Handler = (request, progress, _, _) =>
            {
                progress!.Report(
                    new NormalizationProgress(
                        10,
                        3,
                        3,
                        0,
                        0,
                        Path.Combine("music", "album", "song.mp3"),
                        NormalizationAction.Encoding));
                return Task.FromResult(
                    NormalizationResults.Create(
                        request.SourceDirectory,
                        request.OutputDirectory));
            },
        };
        var viewModel = CreateViewModel(normalizer);
        const string expected = "3 / 10 files · song.mp3 · encoding";
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NormalizationViewModel.ProgressText)
                && viewModel.ProgressText == expected)
            {
                seen.TrySetResult();
            }
        };

        await viewModel.NormalizeCommand.ExecuteAsync(null);

        // Progress<T> delivers asynchronously, and the run resets the line to idle when it
        // finishes, so the text is waited for rather than read after the fact. The step is
        // worded in the view model rather than taken from the enum member's name, and this
        // is what pins the wording the window actually shows.
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
            return NormalizationResults.Create(
                request.SourceDirectory,
                request.OutputDirectory);
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
                    return NormalizationResults.Create(
                        request.SourceDirectory,
                        request.OutputDirectory,
                        stopped: true);
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
                return NormalizationResults.Create(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    stopped: true);
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
                    return NormalizationResults.Create(
                        request.SourceDirectory,
                        request.OutputDirectory,
                        stopped: true);
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
    public async Task ACanceledFolderDialogChangesNothing()
    {
        // An empty queue stands in for a dialog the user dismissed.
        var viewModel = CreateViewModel(picker: new FakeFilePickerService());

        await viewModel.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.SourceDirectory);
        Assert.Equal(string.Empty, viewModel.OutputDirectory);
    }

    [Fact]
    public async Task ARunCanceledBeforeAnyFileFinishesReportsAStoppedMessage()
    {
        var normalizer = new FakeAudioNormalizer
        {
            Handler = (_, _, _, token) => Task.FromException<NormalizationResult>(
                new OperationCanceledException(token)),
        };
        var status = new StatusViewModel();
        var coordinator = new OperationCoordinator();
        var viewModel = CreateViewModel(normalizer, status: status, coordinator: coordinator);

        await viewModel.NormalizeCommand.ExecuteAsync(null);

        Assert.Contains("stopped", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsRunning);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task FailedFilesAreCountedAndExplained()
    {
        var normalizer = new FakeAudioNormalizer
        {
            Failures =
            [
                new NormalizationFailure("/music/broken.mp3", "corrupt header"),
            ],
        };
        var status = new StatusViewModel();
        var viewModel = CreateViewModel(normalizer, status: status);

        await viewModel.NormalizeCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.FailedFileCount);
        Assert.Contains("1 failed", status.Message, StringComparison.Ordinal);

        // The reasons belong in the expander, not the one-line status.
        Assert.True(status.HasErrorDetails);
        Assert.Contains("corrupt header", status.ErrorDetails, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFailureFlagFollowsTheFailureCount()
    {
        var viewModel = CreateViewModel();
        var announced = new List<string?>();
        viewModel.PropertyChanged += (_, args) => announced.Add(args.PropertyName);

        Assert.False(viewModel.HasFailures);

        viewModel.FailedFileCount = 2;

        Assert.True(viewModel.HasFailures);
        Assert.Contains(nameof(NormalizationViewModel.HasFailures), announced);
    }

    [Fact]
    public async Task ASucceedingRunLeavesNoStaleFailureDetail()
    {
        var normalizer = new FakeAudioNormalizer
        {
            Failures = [new NormalizationFailure("/music/broken.mp3", "corrupt header")],
        };
        var status = new StatusViewModel();
        var viewModel = CreateViewModel(normalizer, status: status);
        await viewModel.NormalizeCommand.ExecuteAsync(null);

        normalizer.Failures = [];
        await viewModel.NormalizeCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.FailedFileCount);
        Assert.False(status.HasErrorDetails);
    }

    [Fact]
    public async Task DisposingWhileRunningUnwindsTheRun()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalizer = new FakeAudioNormalizer
        {
            Handler = async (request, _, pauseSignal, token) =>
            {
                started.SetResult();
                await pauseSignal!.WaitWhilePausedAsync(token);
                return NormalizationResults.Create(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    stopped: true);
            },
        };
        var viewModel = CreateViewModel(normalizer);

        var operation = viewModel.NormalizeCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.PauseCommand.Execute(null);

        // Disposal must release a parked run rather than leaving it holding forever.
        viewModel.Dispose();
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
