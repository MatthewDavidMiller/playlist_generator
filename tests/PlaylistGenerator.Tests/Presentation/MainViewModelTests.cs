using PlaylistGenerator.Presentation.ViewModels;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Presentation;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task PickingAMusicFolderSeedsTheNormalizationTab()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");
        var viewModel = CreateViewModel(new FakeFilePickerService().AddFolder(music));

        await viewModel.Playlist.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal(music, viewModel.Playlist.SourceDirectory);
        Assert.Equal(music, viewModel.Normalization.SourceDirectory);
        Assert.Equal(
            Path.Combine(music, "music-playlist.m3u8"),
            viewModel.Playlist.OutputPath);
        Assert.Equal(
            temporary.GetPath("music-normalized"),
            viewModel.Normalization.OutputDirectory);
    }

    [Fact]
    public async Task PickingAMusicFolderDoesNotOverwriteNormalizationChoices()
    {
        var viewModel = CreateViewModel(new FakeFilePickerService().AddFolder("/playlist"));
        viewModel.Normalization.SourceDirectory = "/normalization";
        viewModel.Normalization.OutputDirectory = "/normalized";

        await viewModel.Playlist.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal("/normalization", viewModel.Normalization.SourceDirectory);
        Assert.Equal("/normalized", viewModel.Normalization.OutputDirectory);
    }

    [Fact]
    public void TheTabsShareOneStatusLineAndOneBusyState()
    {
        var viewModel = CreateViewModel();

        using (viewModel.Operations.BeginOperation())
        {
            Assert.False(viewModel.Playlist.GenerateCommand.CanExecute(null));
            Assert.False(viewModel.Normalization.NormalizeCommand.CanExecute(null));
        }

        Assert.True(viewModel.Playlist.GenerateCommand.CanExecute(null));
        Assert.True(viewModel.Normalization.NormalizeCommand.CanExecute(null));
    }

    [Fact]
    public void TheThemeCommandDelegatesToTheThemeService()
    {
        var theme = new FakeThemeService();
        var viewModel = CreateViewModel(theme: theme);

        viewModel.ToggleThemeCommand.Execute(null);

        Assert.Equal(1, theme.ToggleCount);
        Assert.Equal($"{FakeThemeService.ThemeName} theme enabled.", viewModel.Status.Message);
    }

    [Fact]
    public async Task DisposingUnsubscribesTheCrossTabSuggestion()
    {
        var picker = new FakeFilePickerService().AddFolder("/music");
        var viewModel = CreateViewModel(picker);
        viewModel.Dispose();

        await viewModel.Playlist.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.Normalization.SourceDirectory);
    }

    [Fact]
    public void DisposingIsIdempotent()
    {
        var viewModel = CreateViewModel();

        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Fact]
    public void CancelOperationsIsSafeWhileIdle() => CreateViewModel().CancelOperations();

    [Fact]
    public void RejectsANullThemeService() =>
        Assert.Throws<ArgumentNullException>(
            () => new MainViewModel(
                new FakePlaylistGenerator(),
                new FakeAudioNormalizer(),
                new FakeFilePickerService(),
                null!));

    private static MainViewModel CreateViewModel(
        FakeFilePickerService? picker = null,
        FakeThemeService? theme = null) =>
        new(
            new FakePlaylistGenerator(),
            new FakeAudioNormalizer(),
            picker ?? new FakeFilePickerService(),
            theme ?? new FakeThemeService());
}
