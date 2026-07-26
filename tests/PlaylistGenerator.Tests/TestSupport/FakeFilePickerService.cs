using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Returns queued selections, so browse commands can be driven without a display server.
/// </summary>
/// <remarks>An empty queue returns <see langword="null"/>, standing in for a canceled dialog.</remarks>
public sealed class FakeFilePickerService : IFilePickerService
{
    private readonly Queue<string?> _folders = new();
    private readonly Queue<string?> _audioFiles = new();
    private readonly Queue<string?> _playlistFiles = new();

    public FakeFilePickerService AddFolder(string? path)
    {
        _folders.Enqueue(path);
        return this;
    }

    public FakeFilePickerService AddAudioFile(string? path)
    {
        _audioFiles.Enqueue(path);
        return this;
    }

    public FakeFilePickerService AddPlaylistFile(string? path)
    {
        _playlistFiles.Enqueue(path);
        return this;
    }

    public Task<string?> PickFolderAsync(string title) =>
        Task.FromResult(_folders.TryDequeue(out var path) ? path : null);

    public Task<string?> PickAudioFileAsync(string title) =>
        Task.FromResult(_audioFiles.TryDequeue(out var path) ? path : null);

    public Task<string?> PickPlaylistOutputAsync(string suggestedFileName) =>
        Task.FromResult(_playlistFiles.TryDequeue(out var path) ? path : null);
}
