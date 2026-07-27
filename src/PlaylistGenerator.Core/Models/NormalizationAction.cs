namespace PlaylistGenerator.Core.Models;

/// <summary>
/// The step a normalization run is performing for the file named in a progress report.
/// </summary>
public enum NormalizationAction
{
    /// <summary>Output already existed, or the file belongs to the output tree.</summary>
    Skipped,

    /// <summary>Waiting for a requested pause to be released.</summary>
    Paused,

    /// <summary>Running the FFmpeg loudness measurement pass.</summary>
    Analyzing,

    /// <summary>Running the FFmpeg Opus encoding pass.</summary>
    Encoding,

    /// <summary>The file finished successfully.</summary>
    Completed,

    /// <summary>The file could not be normalized; the rest of the run continued.</summary>
    Failed,

    /// <summary>The run was canceled before this file finished.</summary>
    Stopped,
}
