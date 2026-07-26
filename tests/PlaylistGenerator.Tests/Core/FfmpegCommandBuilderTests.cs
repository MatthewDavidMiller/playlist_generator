using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void AnalysisCommandUsesAnArgumentListWithoutShellQuoting()
    {
        const string source = "/music/an album/song.mp3";

        var command = FfmpegCommandBuilder.BuildAnalysis(source);

        // The path is one argument, unquoted; no shell ever sees it.
        Assert.Contains(source, command);
        Assert.Equal("-", command[^1]);
        Assert.Contains(FfmpegCommandBuilder.AnalysisFilter, command);
        Assert.Contains("-nostdin", command);
    }

    [Fact]
    public void EncodeCommandUsesMeasuredValuesAndOpusSettings()
    {
        var command = FfmpegCommandBuilder.BuildEncode(
            "in.mp3",
            "out.opus",
            new LoudnessStats("-18", "-2", "4", "-29", "0.1"));
        var filter = command.ValueAfter("-af");

        Assert.Contains("measured_I=-18", filter, StringComparison.Ordinal);
        Assert.Contains("measured_TP=-2", filter, StringComparison.Ordinal);
        Assert.Contains("measured_LRA=4", filter, StringComparison.Ordinal);
        Assert.Contains("measured_thresh=-29", filter, StringComparison.Ordinal);
        Assert.Contains("offset=0.1", filter, StringComparison.Ordinal);
        Assert.Contains("linear=true", filter, StringComparison.Ordinal);
        Assert.Equal("libopus", command.ValueAfter("-c:a"));
        Assert.Equal("160k", command.ValueAfter("-b:a"));
        Assert.Equal("0", command.ValueAfter("-map_metadata"));
        Assert.Contains("-vn", command);
        Assert.Equal("out.opus", command[^1]);
    }

    [Fact]
    public void BothPassesTargetTheSameLoudness()
    {
        var analysis = FfmpegCommandBuilder.BuildAnalysis("in.mp3").ValueAfter("-af");
        var encode = FfmpegCommandBuilder
            .BuildEncode("in.mp3", "out.opus", new LoudnessStats("-18", "-2", "4", "-29", "0.1"))
            .ValueAfter("-af");

        Assert.Contains(FfmpegCommandBuilder.LoudnessTarget, analysis, StringComparison.Ordinal);
        Assert.Contains(FfmpegCommandBuilder.LoudnessTarget, encode, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsBlankPathsAndMissingMeasurements()
    {
        var stats = new LoudnessStats("-18", "-2", "4", "-29", "0.1");

        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.BuildAnalysis(" "));
        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.BuildEncode(" ", "o", stats));
        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.BuildEncode("i", " ", stats));
        Assert.Throws<ArgumentNullException>(
            () => FfmpegCommandBuilder.BuildEncode("i", "o", null!));
    }
}
