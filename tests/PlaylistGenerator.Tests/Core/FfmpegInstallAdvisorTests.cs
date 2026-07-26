using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class FfmpegInstallAdvisorTests
{
    [Fact]
    public void ReportsAnExistingInstallation()
    {
        var plan = new FfmpegInstallAdvisor(new FakeExecutableLocator("/tools/ffmpeg")).GetPlan();

        Assert.True(plan.IsInstalled);
        Assert.Empty(plan.Command);
        Assert.Contains("already installed", plan.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apt", "sudo", "apt", "install", "ffmpeg")]
    [InlineData("dnf", "sudo", "dnf", "install", "ffmpeg")]
    [InlineData("pacman", "sudo", "pacman", "-S", "ffmpeg")]
    [InlineData("zypper", "sudo", "zypper", "install", "ffmpeg")]
    public void ProvidesLinuxPackageManagerAdvice(string manager, params string[] expected)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var locator = FakeExecutableLocator.ForExecutables(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [manager] = $"/usr/bin/{manager}",
            });

        var plan = new FfmpegInstallAdvisor(locator).GetPlan();

        Assert.False(plan.IsInstalled);
        Assert.Equal(expected, plan.Command);
    }

    [Fact]
    public void PrefersTheFirstAvailablePackageManager()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var locator = FakeExecutableLocator.ForExecutables(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnf"] = "/usr/bin/dnf",
                ["apt"] = "/usr/bin/apt",
            });

        // apt is listed first, so a machine with both gets the Debian advice.
        Assert.Equal(["sudo", "apt", "install", "ffmpeg"], new FfmpegInstallAdvisor(locator).GetPlan().Command);
    }

    [Fact]
    public void FallsBackToGenericAdviceWithNoKnownPackageManager()
    {
        var plan = new FfmpegInstallAdvisor(new FakeExecutableLocator(null)).GetPlan();

        Assert.False(plan.IsInstalled);
        Assert.Empty(plan.Command);
        Assert.Contains("was not found", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresPackageManagersFromOtherPlatforms()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // winget and brew exist on this PATH but neither applies to Linux.
        var locator = FakeExecutableLocator.ForExecutables(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["winget"] = "/usr/bin/winget",
                ["brew"] = "/usr/bin/brew",
            });

        Assert.Empty(new FfmpegInstallAdvisor(locator).GetPlan().Command);
    }

    [Fact]
    public void LooksForFfmpegBeforeAnyPackageManager()
    {
        var locator = new FakeExecutableLocator("/tools/ffmpeg");

        new FfmpegInstallAdvisor(locator).GetPlan();

        Assert.Equal("ffmpeg", locator.Requested[0]);
        Assert.Single(locator.Requested);
    }

    [Fact]
    public void RejectsANullLocator() =>
        Assert.Throws<ArgumentNullException>(() => new FfmpegInstallAdvisor(null!));
}
