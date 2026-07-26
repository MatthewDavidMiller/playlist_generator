namespace PlaylistGenerator.Presentation.Services;

public interface IFilePickerService
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> PickAudioFileAsync(string title);

    Task<string?> PickPlaylistOutputAsync(string suggestedFileName);
}
