using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Reports whether FFmpeg is installed and suggests a platform-appropriate install command.
/// </summary>
/// <remarks>
/// The suggestion is only ever returned for display. Nothing here runs a package manager,
/// so the user stays in control of what is installed and with what privileges.
/// </remarks>
public sealed class FfmpegInstallAdvisor : IFfmpegInstallAdvisor
{
    /// <summary>The executable the rest of the application needs on the search path.</summary>
    public const string FfmpegExecutable = "ffmpeg";

    /// <summary>
    /// Known package managers in preference order. The first one present on the current
    /// platform provides the advice.
    /// </summary>
    private static readonly PackageManager[] PackageManagers =
    [
        new(
            "winget",
            OperatingSystem.IsWindows,
            "Install FFmpeg with winget.",
            ["winget", "install", "--id", "Gyan.FFmpeg", "--exact"]),
        new(
            "brew",
            OperatingSystem.IsMacOS,
            "Install FFmpeg with Homebrew.",
            ["brew", "install", "ffmpeg"]),
        new(
            "apt",
            OperatingSystem.IsLinux,
            "Install FFmpeg with the Debian/Ubuntu package manager.",
            ["sudo", "apt", "install", "ffmpeg"]),
        new(
            "dnf",
            OperatingSystem.IsLinux,
            "Install FFmpeg with the Fedora package manager.",
            ["sudo", "dnf", "install", "ffmpeg"]),
        new(
            "pacman",
            OperatingSystem.IsLinux,
            "Install FFmpeg with the Arch package manager.",
            ["sudo", "pacman", "-S", "ffmpeg"]),
        new(
            "zypper",
            OperatingSystem.IsLinux,
            "Install FFmpeg with the openSUSE package manager.",
            ["sudo", "zypper", "install", "ffmpeg"]),
    ];

    private const string NoPackageManagerMessage =
        "FFmpeg was not found. Install it with your operating system's package manager and "
        + "make sure it is available on PATH.";

    private readonly IExecutableLocator _locator;

    public FfmpegInstallAdvisor(IExecutableLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
    }

    /// <inheritdoc />
    public FfmpegInstallPlan GetPlan()
    {
        if (_locator.Find(FfmpegExecutable) is not null)
        {
            return new FfmpegInstallPlan(
                true,
                "FFmpeg is already installed and available on PATH.",
                []);
        }

        foreach (var manager in PackageManagers)
        {
            if (manager.IsCurrentPlatform() && _locator.Find(manager.Executable) is not null)
            {
                return new FfmpegInstallPlan(false, manager.Message, manager.Command);
            }
        }

        return new FfmpegInstallPlan(false, NoPackageManagerMessage, []);
    }

    private sealed record PackageManager(
        string Executable,
        Func<bool> IsCurrentPlatform,
        string Message,
        IReadOnlyList<string> Command);
}
