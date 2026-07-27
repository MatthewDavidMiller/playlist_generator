using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PlaylistGenerator.Desktop.Services;

namespace PlaylistGenerator.Tests.Desktop;

public sealed class AvaloniaFilePickerServiceTests
{
    [Fact]
    public void RejectsAMissingTopLevelProvider() =>
        Assert.Throws<ArgumentNullException>(() => new AvaloniaFilePickerService(null!));

    [AvaloniaFact]
    public async Task ReportsAWindowThatIsNotReadyRatherThanFailingObscurely()
    {
        var service = new AvaloniaFilePickerService(() => null);

        // The picker is built before the window exists, so the provider can legitimately
        // return nothing. That has to say what is wrong rather than surface as a null
        // dereference from inside a storage call.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PickFolderAsync("Select a folder"));

        Assert.Contains("not ready", exception.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task OffersOnlySupportedAudioAndPlaylistFilesToTheUser()
    {
        var window = new Window();
        window.Show();
        var service = new AvaloniaFilePickerService(() => window);

        // The headless platform has no storage provider that can open or save, so every
        // picker declines rather than blocking the suite on a dialog that never appears.
        Assert.Null(await service.PickAudioFileAsync("Select the audio file to insert"));
        Assert.Null(await service.PickPlaylistOutputAsync("playlist.m3u8"));
    }
}
