using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Returns a fixed installation plan.
/// </summary>
public sealed class FakeFfmpegInstallAdvisor(FfmpegInstallPlan plan) : IFfmpegInstallAdvisor
{
    /// <summary>A plan reporting FFmpeg as already present.</summary>
    public static FakeFfmpegInstallAdvisor Installed() =>
        new(new FfmpegInstallPlan(true, "installed", []));

    public FfmpegInstallPlan GetPlan() => plan;
}
