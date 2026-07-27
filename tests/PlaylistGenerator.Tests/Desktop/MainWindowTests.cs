using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PlaylistGenerator.Desktop.Views;
using PlaylistGenerator.Presentation.ViewModels;
using PlaylistGenerator.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(HeadlessAppBuilder))]

namespace PlaylistGenerator.Tests.Desktop;

/// <summary>
/// Covers the window itself: that it builds, that its compiled bindings move real values, and
/// that closing it unwinds an active run.
/// </summary>
/// <remarks>
/// A Release build only proves that binding paths compile. These tests prove the window loads
/// against a live view model and that the bindings carry data, which a build cannot show.
/// </remarks>
public sealed class MainWindowTests
{
    private const int NormalizationTabIndex = 1;

    [AvaloniaFact]
    public void TheWindowBuildsAndKeepsItsViewModel()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel);

        Assert.Same(viewModel, window.DataContext);
        Assert.Equal("Playlist Generator", window.Title);
    }

    [AvaloniaFact]
    public void StatusTextReachesTheWindow()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel);

        viewModel.Status.Report("Playlist written.");

        Assert.Contains(
            window.Descendants<TextBlock>(),
            block => block.Text == "Playlist written.");
    }

    [AvaloniaFact]
    public void ErrorDetailStaysHiddenUntilThereIsSomethingToShow()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel);
        var expander = Assert.Single(
            window.Descendants<Expander>(),
            candidate => candidate.Header as string == "Error details");

        Assert.False(expander.IsVisible);

        viewModel.Status.ReportFailure(new InvalidOperationException("ffmpeg exploded"));

        Assert.True(expander.IsVisible);
    }

    [AvaloniaFact]
    public void NormalizationCountsAreShown()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel, NormalizationTabIndex);

        viewModel.Normalization.NormalizedFileCount = 7;
        viewModel.Normalization.SkippedFileCount = 2;
        viewModel.Normalization.FailedFileCount = 3;

        var texts = window.Descendants<TextBlock>().Select(block => block.Text).ToArray();
        Assert.Contains("Normalized: 7", texts);
        Assert.Contains("Skipped: 2", texts);
        Assert.Contains("Failed: 3", texts);
    }

    [AvaloniaFact]
    public void TheTransportButtonsFollowTheRunState()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel, NormalizationTabIndex);
        var stop = Assert.Single(
            window.Descendants<Button>(),
            button => button.Content as string == "Stop");

        Assert.False(stop.IsEffectivelyEnabled);

        viewModel.Normalization.IsRunning = true;

        Assert.True(stop.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowCancelsAnActiveRun()
    {
        var canceled = false;
        var normalizer = new FakeAudioNormalizer
        {
            Handler = async (request, _, _, token) =>
            {
                await using var registration = token.Register(() => canceled = true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    // Expected: closing the window is what stops the run.
                }

                return NormalizationResults.Create(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    stopped: true);
            },
        };
        using var viewModel = CreateViewModel(normalizer);
        var window = Show(viewModel);

        // The run reaches its first await synchronously, so the token exists before the close.
        var operation = viewModel.Normalization.NormalizeCommand.ExecuteAsync(null);
        Assert.True(viewModel.Normalization.IsRunning);

        window.Close();
        await operation;

        Assert.True(canceled);
        Assert.False(viewModel.Normalization.IsRunning);
    }

    /// <summary>
    /// Shows the window and optionally switches tabs. A <see cref="TabControl"/> only realizes
    /// the selected tab's content, so a test asserting on the normalization tab has to select
    /// it before those controls exist in the visual tree.
    /// </summary>
    private static MainWindow Show(MainViewModel viewModel, int tabIndex = 0)
    {
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        if (tabIndex != 0)
        {
            window.Descendants<TabControl>()[0].SelectedIndex = tabIndex;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        return window;
    }

    private static MainViewModel CreateViewModel(FakeAudioNormalizer? normalizer = null) =>
        new(
            new FakePlaylistGenerator(),
            normalizer ?? new FakeAudioNormalizer(),
            new FakeFilePickerService(),
            new FakeThemeService());
}
