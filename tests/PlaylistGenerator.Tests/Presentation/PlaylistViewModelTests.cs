using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Presentation.ViewModels;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Presentation;

public sealed class PlaylistViewModelTests
{
    [Fact]
    public async Task BrowsingASourceFolderSuggestsAPlaylistPath()
    {
        using var temporary = new TemporaryDirectory();
        var music = temporary.CreateDirectory("music");
        var viewModel = CreateViewModel(picker: new FakeFilePickerService().AddFolder(music));

        await viewModel.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal(music, viewModel.SourceDirectory);
        Assert.Equal(Path.Combine(music, "music-playlist.m3u8"), viewModel.OutputPath);
    }

    [Fact]
    public async Task BrowsingDoesNotOverwriteAnExistingOutputPath()
    {
        var viewModel = CreateViewModel(picker: new FakeFilePickerService().AddFolder("/music"));
        viewModel.OutputPath = "/chosen/mix.m3u8";

        await viewModel.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal("/chosen/mix.m3u8", viewModel.OutputPath);
    }

    [Fact]
    public async Task AnnouncesTheChosenSourceFolder()
    {
        var viewModel = CreateViewModel(picker: new FakeFilePickerService().AddFolder("/music"));
        var announced = new List<string>();
        viewModel.SourceDirectorySelected += (_, directory) => announced.Add(directory);

        await viewModel.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal(["/music"], announced);
    }

    [Fact]
    public async Task ACanceledFolderDialogChangesNothing()
    {
        var viewModel = CreateViewModel(picker: new FakeFilePickerService());
        var announced = 0;
        viewModel.SourceDirectorySelected += (_, _) => announced++;

        await viewModel.BrowseSourceCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.SourceDirectory);
        Assert.Equal(string.Empty, viewModel.OutputPath);
        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task BrowseCommandsUpdateTheirOwnFields()
    {
        using var temporary = new TemporaryDirectory();
        var special = temporary.CreateFile("station-id.mp3");
        var playlist = temporary.GetPath("mix.m3u8");
        var picker = new FakeFilePickerService().AddAudioFile(special).AddPlaylistFile(playlist);
        var viewModel = CreateViewModel(picker: picker);

        await viewModel.BrowseSpecialFileCommand.ExecuteAsync(null);
        await viewModel.BrowseOutputCommand.ExecuteAsync(null);

        Assert.Equal(special, viewModel.SpecialFile);
        Assert.Equal(playlist, viewModel.OutputPath);
    }

    [Fact]
    public async Task BuildsTheRequestAndReportsCounts()
    {
        var generator = new FakePlaylistGenerator();
        var status = new StatusViewModel();
        var viewModel = CreateViewModel(generator, status: status);
        viewModel.SourceDirectory = "music";
        viewModel.SpecialFile = "id.mp3";
        viewModel.OutputPath = "mix.m3u8";
        viewModel.InsertEvery = 3;

        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(new PlaylistRequest("music", "id.mp3", 3, "mix.m3u8"), generator.Request);
        Assert.Contains("5 entries", status.Message, StringComparison.Ordinal);
        Assert.Contains("4 tracks", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureBecomesAUserMessageWithDebugDetail()
    {
        var generator = new FakePlaylistGenerator
        {
            Exception = new PlaylistValidationException("source is invalid"),
        };
        var status = new StatusViewModel();

        await CreateViewModel(generator, status: status).GenerateCommand.ExecuteAsync(null);

        Assert.Contains("source is invalid", status.Message, StringComparison.Ordinal);
        Assert.True(status.HasErrorDetails);
    }

    [Fact]
    public async Task ReleasesTheBusyStateEvenWhenGenerationFails()
    {
        var coordinator = new OperationCoordinator();
        var generator = new FakePlaylistGenerator { Exception = new InvalidOperationException() };

        await CreateViewModel(generator, coordinator: coordinator)
            .GenerateCommand.ExecuteAsync(null);

        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void TheGenerateCommandIsDisabledWhileAnotherOperationRuns()
    {
        var coordinator = new OperationCoordinator();
        var viewModel = CreateViewModel(coordinator: coordinator);

        Assert.True(viewModel.GenerateCommand.CanExecute(null));

        using (coordinator.BeginOperation())
        {
            Assert.False(viewModel.GenerateCommand.CanExecute(null));
        }

        Assert.True(viewModel.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public void DisposingStopsObservingTheCoordinator()
    {
        var coordinator = new OperationCoordinator();
        var viewModel = CreateViewModel(coordinator: coordinator);
        viewModel.Dispose();

        using var operation = coordinator.BeginOperation();

        // CanExecute still reflects the shared state; only the notification is unsubscribed.
        Assert.False(viewModel.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public void DefaultsToAUsableInterval() =>
        Assert.Equal(PlaylistViewModel.DefaultInsertEvery, CreateViewModel().InsertEvery);

    [Fact]
    public void RejectsNullDependencies()
    {
        var status = new StatusViewModel();
        var coordinator = new OperationCoordinator();
        var picker = new FakeFilePickerService();

        Assert.Throws<ArgumentNullException>(
            () => new PlaylistViewModel(null!, picker, status, coordinator));
        Assert.Throws<ArgumentNullException>(
            () => new PlaylistViewModel(new FakePlaylistGenerator(), null!, status, coordinator));
        Assert.Throws<ArgumentNullException>(
            () => new PlaylistViewModel(new FakePlaylistGenerator(), picker, null!, coordinator));
        Assert.Throws<ArgumentNullException>(
            () => new PlaylistViewModel(new FakePlaylistGenerator(), picker, status, null!));
    }

    private static PlaylistViewModel CreateViewModel(
        FakePlaylistGenerator? generator = null,
        FakeFilePickerService? picker = null,
        StatusViewModel? status = null,
        OperationCoordinator? coordinator = null) =>
        new(
            generator ?? new FakePlaylistGenerator(),
            picker ?? new FakeFilePickerService(),
            status ?? new StatusViewModel(),
            coordinator ?? new OperationCoordinator());
}
