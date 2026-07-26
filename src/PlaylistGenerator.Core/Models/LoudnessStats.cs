namespace PlaylistGenerator.Core.Models;

/// <summary>
/// Measured loudness values from an FFmpeg <c>loudnorm</c> analysis pass.
/// </summary>
/// <remarks>
/// Values stay as text so they are passed to the encoding pass exactly as FFmpeg
/// reported them, without round-tripping through a floating-point parse.
/// </remarks>
public sealed record LoudnessStats(
    string InputIntegrated,
    string InputTruePeak,
    string InputLoudnessRange,
    string InputThreshold,
    string TargetOffset);
