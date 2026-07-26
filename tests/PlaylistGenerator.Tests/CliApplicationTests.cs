using System.Text.Json;
using PlaylistGenerator.CommandLine;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task GeneratesPlaylistAndPrintsSnakeCaseJson()
    {
        var generator = new FakePlaylistGenerator();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = CreateApplication(generator, output, error);

        var exitCode = await application.RunAsync(
        [
            "--source-directory",
            "music",
            "--special-file",
            "id.mp3",
            "--insert-every",
            "4",
            "--output-path",
            "mix.m3u8",
        ],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(generator.Request);
        var json = JsonDocument.Parse(output.ToString().Split(Environment.NewLine)[1]);
        Assert.Equal(4, json.RootElement.GetProperty("insert_every").GetInt32());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task NormalizesAndPrintsSummary()
    {
        var normalizer = new FakeAudioNormalizer();
        var output = new StringWriter();
        var application = new CliApplication(
            new FakePlaylistGenerator(),
            normalizer,
            new FakeFfmpegInstallAdvisor(new FfmpegInstallPlan(true, "installed", [])),
            output,
            new StringWriter());

        var exitCode = await application.RunAsync(
        [
            "normalize-volume",
            "--source-directory",
            "music",
            "--output-directory",
            "normalized",
        ],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new NormalizationRequest("music", "normalized"),
            normalizer.Request);
        Assert.Contains("normalized_file_count", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUsageErrorForUnknownOptions()
    {
        var error = new StringWriter();
        var application = CreateApplication(
            new FakePlaylistGenerator(),
            new StringWriter(),
            error);

        var exitCode = await application.RunAsync(
            ["--unknown", "value"],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown option", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUsageErrorForDuplicateOptions()
    {
        var error = new StringWriter();
        var application = CreateApplication(
            new FakePlaylistGenerator(),
            new StringWriter(),
            error);

        var exitCode = await application.RunAsync(
        [
            "--source-directory",
            "first",
            "--source-directory",
            "second",
        ],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("provided more than once", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsConventionalExitCodeWhenNormalizationIsCanceled()
    {
        var normalizer = new FakeAudioNormalizer
        {
            Handler = (request, _, _, _) => Task.FromResult(
                new NormalizationResult(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    0,
                    0,
                    true)),
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var application = new CliApplication(
            new FakePlaylistGenerator(),
            normalizer,
            new FakeFfmpegInstallAdvisor(new FfmpegInstallPlan(true, "installed", [])),
            new StringWriter(),
            new StringWriter());

        var exitCode = await application.RunAsync(
        [
            "normalize-volume",
            "--source-directory",
            "music",
            "--output-directory",
            "normalized",
        ],
            cancellation.Token);

        Assert.Equal(130, exitCode);
    }

    [Fact]
    public async Task ReturnsExpectedErrorWithoutAStackTrace()
    {
        var error = new StringWriter();
        var generator = new FakePlaylistGenerator
        {
            Exception = new PlaylistValidationException("bad input"),
        };
        var application = CreateApplication(generator, new StringWriter(), error);

        var exitCode = await application.RunAsync(
        [
            "--source-directory",
            "music",
            "--special-file",
            "id.mp3",
            "--insert-every",
            "2",
            "--output-path",
            "mix.m3u8",
        ],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Equal($"bad input{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public async Task FfmpegCommandIsAdviceOnly()
    {
        var output = new StringWriter();
        var application = new CliApplication(
            new FakePlaylistGenerator(),
            new FakeAudioNormalizer(),
            new FakeFfmpegInstallAdvisor(
                new FfmpegInstallPlan(
                    false,
                    "Install it.",
                    ["sudo", "apt", "install", "ffmpeg"])),
            output,
            new StringWriter());

        var exitCode = await application.RunAsync(
            ["install-ffmpeg"],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Command: sudo apt install ffmpeg",
            output.ToString(),
            StringComparison.Ordinal);
    }

    private static CliApplication CreateApplication(
        FakePlaylistGenerator generator,
        TextWriter output,
        TextWriter error) =>
        new(
            generator,
            new FakeAudioNormalizer(),
            new FakeFfmpegInstallAdvisor(new FfmpegInstallPlan(true, "installed", [])),
            output,
            error);
}
