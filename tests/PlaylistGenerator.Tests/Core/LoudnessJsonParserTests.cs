using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class LoudnessJsonParserTests
{
    [Fact]
    public void ReadsTheLastJsonObjectFromFfmpegDiagnostics()
    {
        var output = $"prefix {{not json}} logs\n{FakeProcessRunner.ValidAnalysisJson}";

        var stats = LoudnessJsonParser.Parse(output, "song.mp3");

        Assert.Equal("-18.42", stats.InputIntegrated);
        Assert.Equal("-2.10", stats.InputTruePeak);
        Assert.Equal("4.70", stats.InputLoudnessRange);
        Assert.Equal("-28.54", stats.InputThreshold);
        Assert.Equal("0.12", stats.TargetOffset);
    }

    [Fact]
    public void PrefersTheLastObjectWhenSeveralArePresent()
    {
        var stale = FakeProcessRunner.ValidAnalysisJson.Replace(
            "-18.42",
            "-99.99",
            StringComparison.Ordinal);
        var output = $"{stale}\nmore logs\n{FakeProcessRunner.ValidAnalysisJson}";

        Assert.Equal("-18.42", LoudnessJsonParser.Parse(output, "song.mp3").InputIntegrated);
    }

    [Fact]
    public void AcceptsBareNumbersAsWellAsQuotedValues()
    {
        // FFmpeg has emitted these unquoted in some releases.
        const string output = """
            {"input_i":-18.42,"input_tp":-2.1,"input_lra":4.7,
             "input_thresh":-28.54,"target_offset":0.12}
            """;

        var stats = LoudnessJsonParser.Parse(output, "song.mp3");

        Assert.Equal("-18.42", stats.InputIntegrated);
        Assert.Equal("0.12", stats.TargetOffset);
    }

    [Fact]
    public void ReportsMissingFields()
    {
        var exception = Assert.Throws<PlaylistIOException>(
            () => LoudnessJsonParser.Parse("""{"input_i":"-18"}""", "song.mp3"));

        Assert.Contains("input_tp", exception.Message, StringComparison.Ordinal);
        Assert.Contains("song.mp3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsFieldsPresentButEmpty()
    {
        const string output = """
            {"input_i":"","input_tp":"-2.1","input_lra":"4.7",
             "input_thresh":"-28.54","target_offset":"0.12"}
            """;

        var exception = Assert.Throws<PlaylistIOException>(
            () => LoudnessJsonParser.Parse(output, "song.mp3"));

        Assert.Contains("no value for 'input_i'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsFieldsOfAnUnusableType()
    {
        const string output = """
            {"input_i":null,"input_tp":"-2.1","input_lra":"4.7",
             "input_thresh":"-28.54","target_offset":"0.12"}
            """;

        Assert.Throws<PlaylistIOException>(() => LoudnessJsonParser.Parse(output, "song.mp3"));
    }

    [Theory]
    [InlineData("logs {not-json}", "malformed")]
    [InlineData("}", "missing")]
    [InlineData("", "missing")]
    [InlineData("no json at all", "missing")]
    public void ReportsUnusableOutput(string output, string expectedReason)
    {
        var exception = Assert.Throws<PlaylistIOException>(
            () => LoudnessJsonParser.Parse(output, "song.mp3"));

        Assert.Contains(expectedReason, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsANonObjectJsonDocument()
    {
        // A bare array is well-formed JSON but carries no measurements.
        var exception = Assert.Throws<PlaylistIOException>(
            () => LoudnessJsonParser.Parse("[{\"input_i\":\"-18\"}]", "song.mp3"));

        Assert.Contains("song.mp3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrefersThePrimaryStreamWhenItCarriesTheAnalysis()
    {
        var stats = LoudnessJsonParser.Parse(
            """{"input_i":"-18.42","input_tp":"-2.10","input_lra":"4.70","input_thresh":"-28.54","target_offset":"0.12"}""",
            """{"input_i":"-99","input_tp":"-99","input_lra":"99","input_thresh":"-99","target_offset":"9"}""",
            "song.mp3");

        Assert.Equal("-18.42", stats.InputIntegrated);
    }

    [Fact]
    public void FallsBackToTheOtherStreamWhenThePrimaryHasNoAnalysis()
    {
        // Older FFmpeg builds print the summary to standard output instead.
        var stats = LoudnessJsonParser.Parse(
            "frame= 100 fps=0.0 q=-0.0 size=N/A time=00:00:04.00",
            """{"input_i":"-18.42","input_tp":"-2.10","input_lra":"4.70","input_thresh":"-28.54","target_offset":"0.12"}""",
            "song.mp3");

        Assert.Equal("-18.42", stats.InputIntegrated);
    }

    [Fact]
    public void ReportsWhenNeitherStreamCarriesTheAnalysis()
    {
        var exception = Assert.Throws<PlaylistIOException>(
            () => LoudnessJsonParser.Parse("just logs", "more logs", "song.mp3"));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("song.mp3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNullOutput()
    {
        Assert.Throws<ArgumentNullException>(() => LoudnessJsonParser.Parse(null!, "song.mp3"));
        Assert.Throws<ArgumentNullException>(
            () => LoudnessJsonParser.Parse(null!, "fallback", "song.mp3"));
        Assert.Throws<ArgumentNullException>(
            () => LoudnessJsonParser.Parse("primary", null!, "song.mp3"));
    }
}
