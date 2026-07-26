using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

public sealed class FfmpegInstallAdvisor : IFfmpegInstallAdvisor
{
    private readonly IFfmpegLocator _locator;

    public FfmpegInstallAdvisor(IFfmpegLocator locator)
    {
        _locator = locator;
    }

    public FfmpegInstallPlan GetPlan()
    {
        if (_locator.Find() is not null)
        {
            return new FfmpegInstallPlan(
                true,
                "FFmpeg is already installed and available on PATH.",
                []);
        }

        if (OperatingSystem.IsWindows() && _locator.Find("winget") is not null)
        {
            return Missing(
                "Install FFmpeg with winget.",
                ["winget", "install", "--id", "Gyan.FFmpeg", "--exact"]);
        }

        if (OperatingSystem.IsMacOS() && _locator.Find("brew") is not null)
        {
            return Missing("Install FFmpeg with Homebrew.", ["brew", "install", "ffmpeg"]);
        }

        if (OperatingSystem.IsLinux() && _locator.Find("apt") is not null)
        {
            return Missing(
                "Install FFmpeg with the Debian/Ubuntu package manager.",
                ["sudo", "apt", "install", "ffmpeg"]);
        }

        if (OperatingSystem.IsLinux() && _locator.Find("dnf") is not null)
        {
            return Missing(
                "Install FFmpeg with the Fedora package manager.",
                ["sudo", "dnf", "install", "ffmpeg"]);
        }

        return Missing(
            "FFmpeg was not found. Install it with your operating system's package "
            + "manager and make sure it is available on PATH.",
            []);
    }

    private static FfmpegInstallPlan Missing(
        string message,
        IReadOnlyList<string> command) =>
        new(false, message, command);
}
