using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Tests.Core;

public sealed class AudioFormatsTests
{
    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.FLAC")]
    [InlineData("/music/an album/song.Opus")]
    [InlineData("song.WmA")]
    public void AcceptsSupportedExtensionsRegardlessOfCase(string path) =>
        Assert.True(AudioFormats.IsSupported(path));

    [Theory]
    [InlineData("cover.jpg")]
    [InlineData("notes.txt")]
    [InlineData("no-extension")]
    [InlineData("")]
    [InlineData("archive.mp3.zip")]
    public void RejectsUnsupportedExtensions(string path) =>
        Assert.False(AudioFormats.IsSupported(path));

    [Fact]
    public void TheSpanAndStringOverloadsAgree()
    {
        foreach (var extension in AudioFormats.SupportedExtensions)
        {
            var path = $"track{extension}";

            Assert.True(AudioFormats.IsSupported(path));
            Assert.True(AudioFormats.IsSupported(path.AsSpan()));
        }
    }

    [Fact]
    public void NormalizedOutputIsAlwaysOpus()
    {
        Assert.Equal(".opus", AudioFormats.NormalizedExtension);

        // Normalized output must itself be re-scannable as supported input. The cast picks
        // one of the two set interfaces a frozen set implements.
        Assert.Contains(
            AudioFormats.NormalizedExtension,
            (IReadOnlySet<string>)AudioFormats.SupportedExtensions);
    }

    [Fact]
    public void RejectsANullPath() =>
        Assert.Throws<ArgumentNullException>(() => AudioFormats.IsSupported((string)null!));
}
