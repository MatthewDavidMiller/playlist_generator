using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// Reports whether FFmpeg is installed and how to install it on this platform.
/// </summary>
public interface IFfmpegInstallAdvisor
{
    /// <summary>Returns installation advice. The advice is never executed.</summary>
    FfmpegInstallPlan GetPlan();
}
