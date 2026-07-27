using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Presentation.ViewModels;

/// <summary>
/// The window shell: it owns the shared state and connects the two tabs.
/// </summary>
/// <remarks>
/// Tab behavior lives in <see cref="PlaylistViewModel"/> and
/// <see cref="NormalizationViewModel"/>. This type only composes them, so the coupling
/// between tabs stays visible in one place.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService _themeService;
    private bool _disposed;

    public MainViewModel(
        IPlaylistGenerator playlistGenerator,
        IAudioNormalizer audioNormalizer,
        IFilePickerService filePicker,
        IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(themeService);

        _themeService = themeService;
        Layout = new LayoutViewModel();
        About = new AboutViewModel();
        Status = new StatusViewModel();
        Operations = new OperationCoordinator();
        Playlist = new PlaylistViewModel(playlistGenerator, filePicker, Status, Operations);
        Normalization = new NormalizationViewModel(
            audioNormalizer,
            filePicker,
            Status,
            Operations);

        // Picking a music folder for a playlist usually means normalizing that same folder,
        // so the other tab is pre-filled with matching defaults.
        Playlist.SourceDirectorySelected += OnPlaylistSourceDirectorySelected;
    }

    /// <summary>The window's size class, which the view binds to as style classes.</summary>
    public LayoutViewModel Layout { get; }

    /// <summary>Shared status line and diagnostics.</summary>
    public StatusViewModel Status { get; }

    /// <summary>Shared busy state that keeps two long operations from overlapping.</summary>
    public OperationCoordinator Operations { get; }

    /// <summary>State and commands for the playlist tab.</summary>
    public PlaylistViewModel Playlist { get; }

    /// <summary>State and commands for the normalization tab.</summary>
    public NormalizationViewModel Normalization { get; }

    /// <summary>Ownership, licence, and project details for the about tab.</summary>
    public AboutViewModel About { get; }

    /// <summary>Requests cancellation of in-flight work, used when the window closes.</summary>
    public void CancelOperations() => Normalization.CancelActiveRun();

    [RelayCommand]
    private void ToggleTheme() => Status.Report($"{_themeService.Toggle()} theme enabled.");

    private void OnPlaylistSourceDirectorySelected(object? sender, string directory) =>
        Normalization.SuggestDefaults(directory);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Playlist.SourceDirectorySelected -= OnPlaylistSourceDirectorySelected;
        Playlist.Dispose();
        Normalization.Dispose();
    }
}
