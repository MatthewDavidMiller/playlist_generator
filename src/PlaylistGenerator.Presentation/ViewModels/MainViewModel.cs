using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IPlaylistGenerator _playlistGenerator;
    private readonly IAudioNormalizer _audioNormalizer;
    private readonly IFilePickerService _filePicker;
    private readonly IThemeService _themeService;
    private CancellationTokenSource? _normalizationCancellation;
    private PauseController? _pauseController;

    [ObservableProperty]
    private string _playlistSourceDirectory = string.Empty;

    [ObservableProperty]
    private string _specialFile = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private int _insertEvery = 4;

    [ObservableProperty]
    private string _normalizationSourceDirectory = string.Empty;

    [ObservableProperty]
    private string _normalizedOutputDirectory = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GeneratePlaylistCommand))]
    [NotifyCanExecuteChangedFor(nameof(NormalizeVolumeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseNormalizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeNormalizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopNormalizationCommand))]
    private bool _isNormalizing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseNormalizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeNormalizationCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDetails))]
    private string _errorDetails = string.Empty;

    [ObservableProperty]
    private string _progressText = "No normalization is running.";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 1;

    [ObservableProperty]
    private int _normalizedFileCount;

    [ObservableProperty]
    private int _skippedFileCount;

    public bool HasErrorDetails => !string.IsNullOrWhiteSpace(ErrorDetails);

    public MainViewModel(
        IPlaylistGenerator playlistGenerator,
        IAudioNormalizer audioNormalizer,
        IFilePickerService filePicker,
        IThemeService themeService)
    {
        _playlistGenerator = playlistGenerator;
        _audioNormalizer = audioNormalizer;
        _filePicker = filePicker;
        _themeService = themeService;
    }

    private bool CanStartOperation() => !IsBusy;

    private bool CanPauseNormalization() => IsNormalizing && !IsPaused;

    private bool CanResumeNormalization() => IsNormalizing && IsPaused;

    private bool CanStopNormalization() => IsNormalizing;

    [RelayCommand]
    private async Task BrowsePlaylistSourceAsync()
    {
        var selected = await _filePicker
            .PickFolderAsync("Select the music folder for the playlist")
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        PlaylistSourceDirectory = selected;
        var folderName = GetDirectoryName(selected);
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = Path.Combine(selected, $"{folderName}-playlist.m3u8");
        }

        if (string.IsNullOrWhiteSpace(NormalizationSourceDirectory))
        {
            NormalizationSourceDirectory = selected;
        }

        if (string.IsNullOrWhiteSpace(NormalizedOutputDirectory))
        {
            NormalizedOutputDirectory = BuildNormalizedOutputPath(selected);
        }
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
    private async Task BrowsePlaylistOutputAsync()
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

    [RelayCommand]
    private async Task BrowseNormalizationSourceAsync()
    {
        var selected = await _filePicker
            .PickFolderAsync("Select the audio folder to normalize")
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        NormalizationSourceDirectory = selected;
        if (string.IsNullOrWhiteSpace(NormalizedOutputDirectory))
        {
            NormalizedOutputDirectory = BuildNormalizedOutputPath(selected);
        }
    }

    [RelayCommand]
    private async Task BrowseNormalizedOutputAsync()
    {
        var selected = await _filePicker
            .PickFolderAsync("Select the normalized audio output folder")
            .ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            NormalizedOutputDirectory = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task GeneratePlaylistAsync()
    {
        BeginOperation("Building playlist…");
        try
        {
            var request = new PlaylistRequest(
                PlaylistSourceDirectory,
                SpecialFile,
                InsertEvery,
                OutputPath);
            var result = await Task.Run(() => _playlistGenerator.Generate(request))
                .ConfigureAwait(true);
            StatusMessage =
                $"Created {Path.GetFileName(result.OutputPath)} with "
                + $"{result.PlaylistEntryCount} entries from {result.SourceTrackCount} tracks.";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task NormalizeVolumeAsync()
    {
        BeginOperation("Preparing volume normalization…");
        IsNormalizing = true;
        IsPaused = false;
        NormalizedFileCount = 0;
        SkippedFileCount = 0;
        ProgressValue = 0;
        ProgressMaximum = 1;
        _pauseController = new PauseController();
        _normalizationCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<NormalizationProgress>(UpdateProgress);
            var result = await _audioNormalizer
                .NormalizeAsync(
                    new NormalizationRequest(
                        NormalizationSourceDirectory,
                        NormalizedOutputDirectory),
                    progress,
                    _pauseController,
                    _normalizationCancellation.Token)
                .ConfigureAwait(true);

            NormalizedFileCount = result.NormalizedFileCount;
            SkippedFileCount = result.SkippedFileCount;
            StatusMessage = result.Stopped
                ? $"Normalization stopped. Completed {result.NormalizedFileCount} files."
                : $"Normalization complete. Created {result.NormalizedFileCount} files.";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            _pauseController?.Resume();
            _normalizationCancellation?.Dispose();
            _normalizationCancellation = null;
            _pauseController = null;
            IsPaused = false;
            IsNormalizing = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPauseNormalization))]
    private void PauseNormalization()
    {
        _pauseController?.Pause();
        IsPaused = true;
        StatusMessage = "Normalization will pause before the next FFmpeg step.";
    }

    [RelayCommand(CanExecute = nameof(CanResumeNormalization))]
    private void ResumeNormalization()
    {
        _pauseController?.Resume();
        IsPaused = false;
        StatusMessage = "Normalization resumed.";
    }

    [RelayCommand(CanExecute = nameof(CanStopNormalization))]
    private void StopNormalization()
    {
        _normalizationCancellation?.Cancel();
        StatusMessage = "Stopping normalization…";
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var theme = _themeService.Toggle();
        StatusMessage = $"{theme} theme enabled.";
    }

    public void CancelOperations() => _normalizationCancellation?.Cancel();

    public void Dispose()
    {
        _normalizationCancellation?.Cancel();
        _normalizationCancellation?.Dispose();
        _normalizationCancellation = null;
        GC.SuppressFinalize(this);
    }

    private void BeginOperation(string message)
    {
        IsBusy = true;
        ErrorDetails = string.Empty;
        StatusMessage = message;
    }

    private void UpdateProgress(NormalizationProgress progress)
    {
        ProgressMaximum = Math.Max(1, progress.TotalFileCount);
        ProgressValue = progress.CompletedFileCount;
        NormalizedFileCount = progress.NormalizedFileCount;
        SkippedFileCount = progress.SkippedFileCount;
        ProgressText =
            $"{progress.CompletedFileCount} / {progress.TotalFileCount} files · "
            + $"{Path.GetFileName(progress.CurrentSourcePath)} · "
            + progress.Action.ToString().ToLowerInvariant();
    }

    private void ShowError(Exception exception)
    {
        StatusMessage = $"Error: {exception.Message}";
        ErrorDetails = exception.ToString();
    }

    private static string GetDirectoryName(string directory)
    {
        var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : "music";
    }

    private static string BuildNormalizedOutputPath(string sourceDirectory)
    {
        var fullPath = Path.GetFullPath(sourceDirectory);
        var parent = Directory.GetParent(fullPath)?.FullName ?? fullPath;
        return Path.Combine(parent, $"{GetDirectoryName(fullPath)}-normalized");
    }
}
