using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Threading;
using PlaylistGenerator.Presentation.Infrastructure;
using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Presentation.ViewModels;

/// <summary>
/// State and commands for the volume-normalization tab.
/// </summary>
public sealed partial class NormalizationViewModel : ObservableObject, IDisposable
{
    /// <summary>Progress text shown while no run is active.</summary>
    public const string IdleProgressText = "No normalization is running.";

    private readonly IAudioNormalizer _audioNormalizer;
    private readonly IFilePickerService _filePicker;
    private readonly StatusViewModel _status;
    private readonly OperationCoordinator _coordinator;

    private CancellationTokenSource? _cancellation;
    private PauseController? _pauseController;
    private bool _disposed;

    [ObservableProperty]
    private string _sourceDirectory = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private string _progressText = IdleProgressText;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 1;

    [ObservableProperty]
    private int _normalizedFileCount;

    [ObservableProperty]
    private int _skippedFileCount;

    public NormalizationViewModel(
        IAudioNormalizer audioNormalizer,
        IFilePickerService filePicker,
        StatusViewModel status,
        OperationCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(audioNormalizer);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(coordinator);

        _audioNormalizer = audioNormalizer;
        _filePicker = filePicker;
        _status = status;
        _coordinator = coordinator;
        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
    }

    private bool CanNormalize => !_coordinator.IsBusy;

    private bool CanPause => IsRunning && !IsPaused;

    private bool CanResume => IsRunning && IsPaused;

    private bool CanStop => IsRunning;

    /// <summary>
    /// Fills any still-empty field with a default derived from a music folder the user chose
    /// on another tab. Existing choices are never overwritten.
    /// </summary>
    public void SuggestDefaults(string musicDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(musicDirectory);

        if (string.IsNullOrWhiteSpace(SourceDirectory))
        {
            SourceDirectory = musicDirectory;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = PathSuggestion.BuildNormalizedOutputPath(musicDirectory);
        }
    }

    /// <summary>Requests cancellation of any active run, used when the window closes.</summary>
    public void CancelActiveRun() => _cancellation?.Cancel();

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var selected = await _filePicker
            .PickFolderAsync("Select the audio folder to normalize")
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        SourceDirectory = selected;
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = PathSuggestion.BuildNormalizedOutputPath(selected);
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var selected = await _filePicker
            .PickFolderAsync("Select the normalized audio output folder")
            .ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            OutputDirectory = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanNormalize))]
    private async Task NormalizeAsync()
    {
        _status.BeginOperation("Preparing volume normalization…");
        ResetProgress();

        using var operation = _coordinator.BeginOperation();
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _pauseController = new PauseController();
        IsRunning = true;

        try
        {
            var result = await _audioNormalizer
                .NormalizeAsync(
                    new NormalizationRequest(SourceDirectory, OutputDirectory),

                    // Progress<T> is created here so reports marshal back to the UI thread.
                    new Progress<NormalizationProgress>(UpdateProgress),
                    _pauseController,
                    cancellation.Token)
                .ConfigureAwait(true);

            NormalizedFileCount = result.NormalizedFileCount;
            SkippedFileCount = result.SkippedFileCount;
            _status.Report(
                result.Stopped
                    ? $"Normalization stopped. Completed {result.NormalizedFileCount} files."
                    : $"Normalization complete. Created {result.NormalizedFileCount} files.");
        }
        catch (OperationCanceledException)
        {
            _status.Report("Normalization stopped before any file finished.");
        }
        catch (Exception exception)
        {
            _status.ReportFailure(exception);
        }
        finally
        {
            // Release any waiter before the controller goes away, so a run that was paused
            // and then stopped cannot leave a task parked forever.
            _pauseController?.Resume();
            _pauseController = null;
            _cancellation = null;
            IsPaused = false;
            IsRunning = false;
            ProgressText = IdleProgressText;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _pauseController?.Pause();
        IsPaused = true;
        _status.Report("Normalization will pause before the next FFmpeg step.");
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        _pauseController?.Resume();
        IsPaused = false;
        _status.Report("Normalization resumed.");
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        // Releasing the pause first lets a paused run observe cancellation immediately
        // instead of staying parked.
        _pauseController?.Resume();
        IsPaused = false;
        _cancellation?.Cancel();
        _status.Report("Stopping normalization…");
    }

    private void ResetProgress()
    {
        NormalizedFileCount = 0;
        SkippedFileCount = 0;
        ProgressValue = 0;
        ProgressMaximum = 1;
        IsPaused = false;
        ProgressText = "Scanning for audio files…";
    }

    private void UpdateProgress(NormalizationProgress progress)
    {
        // A maximum of zero would make the bar render as complete before any work happens.
        ProgressMaximum = Math.Max(1, progress.TotalFileCount);
        ProgressValue = progress.CompletedFileCount;
        NormalizedFileCount = progress.NormalizedFileCount;
        SkippedFileCount = progress.SkippedFileCount;
        ProgressText = string.Format(
            CultureInfo.CurrentCulture,
            "{0} / {1} files · {2} · {3}",
            progress.CompletedFileCount,
            progress.TotalFileCount,
            Path.GetFileName(progress.CurrentSourcePath),
            progress.Action.ToString().ToLowerInvariant());
    }

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(OperationCoordinator.IsBusy))
        {
            NormalizeCommand.NotifyCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.PropertyChanged -= OnCoordinatorPropertyChanged;

        // The run owns and disposes its own token source; cancelling is enough to unwind it.
        _cancellation?.Cancel();
        _pauseController?.Resume();
    }
}
