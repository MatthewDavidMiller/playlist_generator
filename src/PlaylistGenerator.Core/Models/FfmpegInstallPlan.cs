namespace PlaylistGenerator.Core.Models;

/// <summary>
/// Advice describing how to install FFmpeg on the current platform.
/// </summary>
/// <param name="Command">
/// The suggested command, empty when none applies. It is only ever shown for review;
/// the application never runs it.
/// </param>
public sealed record FfmpegInstallPlan(
    bool IsInstalled,
    string Message,
    IReadOnlyList<string> Command);
