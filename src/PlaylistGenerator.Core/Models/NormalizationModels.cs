namespace PlaylistGenerator.Core.Models;

public sealed record NormalizationRequest(
    string SourceDirectory,
    string OutputDirectory);

public sealed record NormalizationResult(
    string SourceDirectory,
    string OutputDirectory,
    int NormalizedFileCount,
    int SkippedFileCount,
    bool Stopped);

public enum NormalizationAction
{
    Skipped,
    Paused,
    Analyzing,
    Encoding,
    Completed,
    Stopped,
}

public sealed record NormalizationProgress(
    int TotalFileCount,
    int CompletedFileCount,
    int NormalizedFileCount,
    int SkippedFileCount,
    string CurrentSourcePath,
    NormalizationAction Action);

public sealed record LoudnessStats(
    string InputIntegrated,
    string InputTruePeak,
    string InputLoudnessRange,
    string InputThreshold,
    string TargetOffset);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record FfmpegInstallPlan(
    bool IsInstalled,
    string Message,
    IReadOnlyList<string> Command);
