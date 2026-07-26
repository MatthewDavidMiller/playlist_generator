using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

public static class FfmpegCommandBuilder
{
    public const string LoudnessTarget = "I=-16:TP=-1.5:LRA=11";
    public const string AnalysisFilter = $"loudnorm={LoudnessTarget}:print_format=json";

    public static IReadOnlyList<string> BuildAnalysis(string inputPath) =>
        ["-hide_banner", "-nostdin", "-i", inputPath, "-af", AnalysisFilter, "-f", "null", "-"];

    public static IReadOnlyList<string> BuildEncode(
        string inputPath,
        string outputPath,
        LoudnessStats stats)
    {
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
            "160k",
            "-vbr",
            "on",
            "-map_metadata",
            "0",
            "-vn",
            outputPath,
        ];
    }
}
