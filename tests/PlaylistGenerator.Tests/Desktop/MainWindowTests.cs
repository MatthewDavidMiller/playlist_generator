using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PlaylistGenerator.Desktop.Views;
using PlaylistGenerator.Presentation.Layout;
using PlaylistGenerator.Presentation.ViewModels;
using PlaylistGenerator.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(HeadlessAppBuilder))]

namespace PlaylistGenerator.Tests.Desktop;

/// <summary>
/// Covers the window itself: that it builds, that its compiled bindings move real values, that
/// it changes shape with its width, and that closing it unwinds an active run.
/// </summary>
/// <remarks>
/// A Release build only proves that binding paths compile. These tests prove the window loads
/// against a live view model and that the bindings carry data, which a build cannot show.
/// </remarks>
public sealed class MainWindowTests
{
    private const int NormalizationTabIndex = 1;
    private const int AboutTabIndex = 2;

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

    /// <summary>
    /// Realizes a paragraph of wrapped text, which the collapsed expander otherwise never
    /// does.
    /// </summary>
    /// <remarks>
    /// This is the regression test for a headless application that registers no font: text
    /// wrapping then never settles on a line break and the layout pass runs forever, so the
    /// symptom is the suite hanging rather than a test failing.
    /// </remarks>
    [AvaloniaFact]
    public void ExpandedErrorDetailLaysOutItsWholeText()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel);
        var expander = Assert.Single(
            window.Descendants<Expander>(),
            candidate => candidate.Header as string == "Error details");

        viewModel.Status.ReportFailure(
            new InvalidOperationException(
                string.Join(" ", Enumerable.Repeat("ffmpeg could not read the file", 40))));
        expander.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var diagnostics = Assert.Single(
            window.Descendants<SelectableTextBlock>(),
            block => block.Classes.Contains("diagnostics"));
        Assert.Equal(viewModel.Status.ErrorDetails, diagnostics.Text);
        Assert.True(diagnostics.Bounds.Height > 0, "The diagnostics text was never laid out.");
    }

    [AvaloniaFact]
    public void NormalizationCountsAreShown()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel, NormalizationTabIndex);

        viewModel.Normalization.NormalizedFileCount = 7;
        viewModel.Normalization.SkippedFileCount = 2;
        viewModel.Normalization.FailedFileCount = 3;

        Assert.Equal("7", ReadStatTile(window, "Normalized"));
        Assert.Equal("2", ReadStatTile(window, "Skipped"));
        Assert.Equal("3", ReadStatTile(window, "Failed"));
    }

    /// <summary>
    /// The failure count is marked so the view can colour it, but it keeps its own label, so
    /// the state is never carried by colour alone.
    /// </summary>
    [AvaloniaFact]
    public void TheFailureCountIsMarkedOnlyOnceAFileHasFailed()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel, NormalizationTabIndex);

        Assert.DoesNotContain("danger", FindStatValue(window, "Failed").Classes);

        viewModel.Normalization.FailedFileCount = 1;

        Assert.Contains("danger", FindStatValue(window, "Failed").Classes);
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
    public void TheAboutTabShowsTheOwnerTheLicenceAndTheProjectLink()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel, AboutTabIndex);
        var texts = window.Descendants<TextBlock>().Select(block => block.Text).ToArray();

        Assert.Contains(viewModel.About.Copyright, texts);
        Assert.Contains(viewModel.About.LicenseName, texts);

        var licence = Assert.Single(
            window.Descendants<SelectableTextBlock>(),
            block => block.Classes.Contains("license"));
        Assert.Equal(viewModel.About.LicenseText, licence.Text);

        var link = Assert.Single(window.Descendants<HyperlinkButton>());
        Assert.Equal(viewModel.About.ProjectUrl, link.NavigateUri);
    }

    [AvaloniaFact]
    public void AWideWindowKeepsEachBrowseButtonBesideItsField()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel);

        Resize(window, viewModel, WindowLayout.ExpandedWidth);

        var buttons = BrowseButtons(window);
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Equal(0, Grid.GetRow(button)));
        Assert.All(buttons, button => Assert.Equal(1, Grid.GetColumn(button)));
    }

    [AvaloniaFact]
    public void ANarrowWindowStacksEachBrowseButtonUnderItsField()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel);

        Resize(window, viewModel, WindowLayout.CompactWidth - 1);

        var buttons = BrowseButtons(window);
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Equal(1, Grid.GetRow(button)));
        Assert.All(buttons, button => Assert.Equal(0, Grid.GetColumn(button)));
    }

    /// <summary>
    /// The window's own width has to reach the view model, or nothing else adapts.
    /// </summary>
    [AvaloniaFact]
    public void TheWindowReportsItsWidthToTheLayout()
    {
        using var viewModel = CreateViewModel();
        var window = Show(viewModel);

        Assert.False(viewModel.Layout.IsCompact);

        window.Width = WindowLayout.MinimumWidth;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.True(viewModel.Layout.IsCompact);
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

    /// <summary>
    /// Reports a width the way a real resize would, then lets the styles that width selects
    /// take effect.
    /// </summary>
    private static void Resize(MainWindow window, MainViewModel viewModel, double width)
    {
        viewModel.Layout.Resize(width);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static IReadOnlyList<Button> BrowseButtons(MainWindow window) =>
        [.. window.Descendants<Button>().Where(button => button.Classes.Contains("browse"))];

    /// <summary>Returns the value shown on the tile carrying the given label.</summary>
    private static string? ReadStatTile(MainWindow window, string label) =>
        FindStatValue(window, label).Text;

    private static TextBlock FindStatValue(MainWindow window, string label)
    {
        var tile = Assert.Single(
            window.Descendants<Border>().Where(border => border.Classes.Contains("stat")),
            candidate => candidate.Descendants<TextBlock>().Any(block => block.Text == label));
        return Assert.Single(
            tile.Descendants<TextBlock>(),
            block => block.Classes.Contains("stat-value"));
    }

    private static MainViewModel CreateViewModel(FakeAudioNormalizer? normalizer = null) =>
        new(
            new FakePlaylistGenerator(),
            normalizer ?? new FakeAudioNormalizer(),
            new FakeFilePickerService(),
            new FakeThemeService());
}
