using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Builds FFmpeg argument lists for the two-pass loudness normalization workflow.
/// </summary>
/// <remarks>
/// Arguments are returned as discrete values so that no shell ever parses them.
/// </remarks>
public static class FfmpegCommandBuilder
{
    /// <summary>EBU R128 target used by both passes.</summary>
    public const string LoudnessTarget = "I=-16:TP=-1.5:LRA=11";

    /// <summary>Filter for the measurement pass, which decodes without writing audio.</summary>
    public const string AnalysisFilter = $"loudnorm={LoudnessTarget}:print_format=json";

    private const string OpusBitrate = "160k";

    /// <summary>Builds the measurement pass, which discards output and prints JSON.</summary>
    /// <remarks>
    /// Video is dropped here as well as in the encoding pass. Without it FFmpeg selects the
    /// cover art embedded in most music files and decodes that image for every track, which
    /// the loudness measurement cannot use.
    /// </remarks>
    public static IReadOnlyList<string> BuildAnalysis(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        return
        [
            "-hide_banner",
            "-nostdin",
            "-i",
            inputPath,
            "-af",
            AnalysisFilter,
            "-vn",
            "-f",
            "null",
            "-",
        ];
    }

    /// <summary>
    /// Builds the encoding pass, applying measurements from
    /// <paramref name="stats"/> so the correction is linear rather than dynamic.
    /// </summary>
    public static IReadOnlyList<string> BuildEncode(
        string inputPath,
        string outputPath,
        LoudnessStats stats)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(stats);

        var filter =
            $"loudnorm={LoudnessTarget}"
            + $":measured_I={stats.InputIntegrated}"
            + $":measured_TP={stats.InputTruePeak}"
            + $":measured_LRA={stats.InputLoudnessRange}"
            + $":measured_thresh={stats.InputThreshold}"
            + $":offset={stats.TargetOffset}"
            + ":linear=true";

        return
        [
            "-hide_banner",
            "-nostdin",
            "-y",
            "-i",
            inputPath,
            "-af",
            filter,
            "-c:a",
            "libopus",
            "-b:a",
            OpusBitrate,
            "-vbr",
            "on",
            "-map_metadata",
            "0",
            "-vn",
            outputPath,
        ];
    }
}
