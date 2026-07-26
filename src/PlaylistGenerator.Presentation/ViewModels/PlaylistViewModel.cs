using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Presentation.Infrastructure;
using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Presentation.ViewModels;

/// <summary>
/// State and commands for the playlist tab.
/// </summary>
public sealed partial class PlaylistViewModel : ObservableObject, IDisposable
{
    /// <summary>Interval offered before the user chooses one.</summary>
    public const int DefaultInsertEvery = 4;

    private readonly IPlaylistGenerator _playlistGenerator;
    private readonly IFilePickerService _filePicker;
    private readonly StatusViewModel _status;
    private readonly OperationCoordinator _coordinator;

    [ObservableProperty]
    private string _sourceDirectory = string.Empty;

    [ObservableProperty]
    private string _specialFile = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private int _insertEvery = DefaultInsertEvery;

    public PlaylistViewModel(
        IPlaylistGenerator playlistGenerator,
        IFilePickerService filePicker,
        StatusViewModel status,
        OperationCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(playlistGenerator);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(coordinator);

        _playlistGenerator = playlistGenerator;
        _filePicker = filePicker;
        _status = status;
        _coordinator = coordinator;
        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
    }

    /// <summary>
    /// Raised with the absolute path when the user picks a music folder, so the
    /// normalization tab can offer matching defaults.
    /// </summary>
    public event EventHandler<string>? SourceDirectorySelected;

    private bool CanGeneratePlaylist => !_coordinator.IsBusy;

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var selected = await _filePicker
            .PickFolderAsync("Select the music folder for the playlist")
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        SourceDirectory = selected;
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = PathSuggestion.BuildPlaylistPath(selected);
        }

        SourceDirectorySelected?.Invoke(this, selected);
    }

    [RelayCommand]
    private async Task BrowseSpecialFileAsync()
    {
        var selected = await _filePicker
            .PickAudioFileAsync("Select the audio file to insert")
            .ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SpecialFile = selected;
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var suggestedName = string.IsNullOrWhiteSpace(OutputPath)
            ? "playlist.m3u8"
            : Path.GetFileName(OutputPath);
        var selected = await _filePicker
            .PickPlaylistOutputAsync(suggestedName)
            .ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            OutputPath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGeneratePlaylist))]
    private async Task GenerateAsync()
    {
        _status.BeginOperation("Building playlist…");
        using var operation = _coordinator.BeginOperation();

        try
        {
            var request = new PlaylistRequest(
                SourceDirectory,
                SpecialFile,
                InsertEvery,
                OutputPath);

            // Scanning and writing are synchronous and can be slow on a large library, so
            // they run off the UI thread.
            var result = await Task.Run(() => _playlistGenerator.Generate(request))
                .ConfigureAwait(true);

            _status.Report(
                $"Created {Path.GetFileName(result.OutputPath)} with "
                + $"{result.PlaylistEntryCount} entries from {result.SourceTrackCount} tracks.");
        }
        catch (Exception exception)
        {
            // Deliberately broad: a failure on the background task must not take down the
            // window. The cause goes to the diagnostics expander instead.
            _status.ReportFailure(exception);
        }
    }

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(OperationCoordinator.IsBusy))
        {
            GenerateCommand.NotifyCanExecuteChanged();
        }
    }

    public void Dispose() => _coordinator.PropertyChanged -= OnCoordinatorPropertyChanged;
}
