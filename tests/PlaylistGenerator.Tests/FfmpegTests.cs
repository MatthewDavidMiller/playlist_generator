using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;

namespace PlaylistGenerator.Tests;

public sealed class FfmpegTests
{
    [Fact]
    public void AnalysisCommandUsesArgumentListWithoutShellQuoting()
    {
        const string source = "/music/an album/song.mp3";

        var command = FfmpegCommandBuilder.BuildAnalysis(source);

        Assert.Contains(source, command);
        Assert.Equal("-", command[^1]);
        Assert.Contains(FfmpegCommandBuilder.AnalysisFilter, command);
    }

    [Fact]
    public void EncodeCommandUsesMeasuredValuesAndOpusSettings()
    {
        var command = FfmpegCommandBuilder.BuildEncode(
            "in.mp3",
            "out.opus",
            new LoudnessStats("-18", "-2", "4", "-29", "0.1"));
        var filter = command[command.IndexOf("-af") + 1];

        Assert.Contains("measured_I=-18", filter, StringComparison.Ordinal);
        Assert.Contains("linear=true", filter, StringComparison.Ordinal);
        Assert.Equal("libopus", command[command.IndexOf("-c:a") + 1]);
        Assert.Equal("160k", command[command.IndexOf("-b:a") + 1]);
        Assert.Contains("-vn", command);
    }

    [Fact]
    public void ParserReadsTheLastJsonObjectFromFfmpegDiagnostics()
    {
        var output = $"prefix {{not json}} logs\n{TestSupport.FakeProcessRunner.ValidAnalysisJson}";

        var stats = LoudnessJsonParser.Parse(output, "song.mp3");

        Assert.Equal("-18.42", stats.InputIntegrated);
        Assert.Equal("0.12", stats.TargetOffset);
    }

    [Fact]
    public void ParserReportsMissingFields()
    {
        var exception = Assert.Throws<PlaylistIOException>(
            () => LoudnessJsonParser.Parse("""{"input_i":"-18"}""", "song.mp3"));

        Assert.Contains("input_tp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserReportsMalformedOutput()
    {
        var exception = Assert.Throws<PlaylistIOException>(
            () => LoudnessJsonParser.Parse("logs {not-json}", "song.mp3"));

        Assert.Contains("malformed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PauseControllerWaitsWithoutBlockingAThread()
    {
        var controller = new PauseController();
        controller.Pause();

        var wait = controller.WaitIfPausedAsync(TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        controller.Resume();
        await wait;
        Assert.False(controller.IsPaused);
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
