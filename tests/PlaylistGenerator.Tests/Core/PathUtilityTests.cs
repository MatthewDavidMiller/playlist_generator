using PlaylistGenerator.Core.Infrastructure;

namespace PlaylistGenerator.Tests.Core;

public sealed class PathUtilityTests
{
    [Fact]
    public void ExpandsATildeToTheUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.GetFullPath(home), PathUtility.GetFullPath("~"));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(home, "Music")),
            PathUtility.GetFullPath($"~{Path.DirectorySeparatorChar}Music"));
    }

    [Fact]
    public void LeavesATildeThatIsPartOfANameAlone()
    {
        // "~backup" is an ordinary relative name, not a home-directory reference.
        var expanded = PathUtility.GetFullPath("~backup");

        Assert.Equal(Path.GetFullPath("~backup"), expanded);
        Assert.EndsWith("~backup", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizesRelativeSegmentsBeforeComparing()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "library"));
        var indirect = Path.Combine(root, "albums", "..", "one.mp3");

        Assert.True(PathUtility.AreSame(Path.Combine(root, "one.mp3"), indirect));
    }

    [Theory]
    [InlineData("/music", "/music", true)]
    [InlineData("/music/album/song.mp3", "/music", true)]
    [InlineData("/musicbox/song.mp3", "/music", false)]
    [InlineData("/music", "/music/album", false)]
    [InlineData("/other/song.mp3", "/music", false)]
    public void RecognizesContainmentWithoutTreatingSiblingsAsChildren(
        string path,
        string directory,
        bool expected)
    {
        // Full paths are compared directly; a shared name prefix must not imply containment.
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);

        Assert.Equal(expected, PathUtility.IsWithinFullDirectory(fullPath, fullDirectory));
    }

    [Fact]
    public void TreatsAFilesystemRootAsAParentOfEverythingBeneathIt()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("/music"))!;

        Assert.True(PathUtility.IsWithinFullDirectory(Path.GetFullPath("/music"), root));
    }

    [Fact]
    public void RejectsEscapingRelativePaths()
    {
        var root = Path.GetFullPath("/music");
        var escaping = Path.Combine(root, "..", "elsewhere", "song.mp3");

        Assert.False(PathUtility.IsWithinDirectory(escaping, root));
    }

    [Fact]
    public void AppliesThePlatformCaseRule()
    {
        var lower = Path.GetFullPath("/music/song.mp3");
        var upper = Path.GetFullPath("/MUSIC/SONG.MP3");

        Assert.Equal(OperatingSystem.IsWindows(), PathUtility.AreSameFull(lower, upper));
    }

    [Fact]
    public void RejectsNullPaths()
    {
        Assert.Throws<ArgumentNullException>(() => PathUtility.GetFullPath(null!));
        Assert.Throws<ArgumentNullException>(
            () => PathUtility.IsWithinFullDirectory(null!, "/music"));
    }
}
