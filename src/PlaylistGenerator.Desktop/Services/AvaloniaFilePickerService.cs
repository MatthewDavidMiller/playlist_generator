using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Desktop.Services;

/// <summary>
/// Avalonia-backed file and folder pickers.
/// </summary>
public sealed class AvaloniaFilePickerService : IFilePickerService
{
    private static readonly FilePickerFileType AudioFiles = new("Supported audio")
    {
        Patterns = AudioFormats.SupportedExtensions
            .Select(extension => $"*{extension}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray(),
    };

    private static readonly FilePickerFileType PlaylistFiles = new("VLC playlist")
    {
        Patterns = ["*.m3u8"],
        MimeTypes = ["audio/x-mpegurl", "application/vnd.apple.mpegurl"],
    };

    private readonly Func<TopLevel?> _topLevelProvider;

    public AvaloniaFilePickerService(Func<TopLevel?> topLevelProvider)
    {
        ArgumentNullException.ThrowIfNull(topLevelProvider);
        _topLevelProvider = topLevelProvider;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var storageProvider = GetStorageProvider();
        if (!storageProvider.CanPickFolder)
        {
            return null;
        }

        var folders = await storageProvider
            .OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                })
            .ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickAudioFileAsync(string title)
    {
        var storageProvider = GetStorageProvider();
        if (!storageProvider.CanOpen)
        {
            return null;
        }

        var files = await storageProvider
            .OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = [AudioFiles],
                })
            .ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickPlaylistOutputAsync(string suggestedFileName)
    {
        var storageProvider = GetStorageProvider();
        if (!storageProvider.CanSave)
        {
            return null;
        }

        var file = await storageProvider
            .SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Save VLC playlist",
                    SuggestedFileName = suggestedFileName,
                    DefaultExtension = "m3u8",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = [PlaylistFiles],
                })
            .ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }

    private IStorageProvider GetStorageProvider() =>
        _topLevelProvider()?.StorageProvider
        ?? throw new InvalidOperationException("The application window is not ready.");
}
