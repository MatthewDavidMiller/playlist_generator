using System.Text.Json;
using PlaylistGenerator.CommandLine;
using PlaylistGenerator.CommandLine.Parsing;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.CommandLine;

public sealed class CliApplicationTests
{
    private static readonly string[] PlaylistArguments =
    [
        "--source-directory",
        "music",
        "--special-file",
        "id.mp3",
        "--insert-every",
        "4",
        "--output-path",
        "mix.m3u8",
    ];

    private static readonly string[] NormalizeArguments =
    [
        "normalize-volume",
        "--source-directory",
        "music",
        "--output-directory",
        "normalized",
    ];

    [Fact]
    public async Task GeneratesPlaylistAndPrintsSnakeCaseJson()
    {
        var generator = new FakePlaylistGenerator();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CreateApplication(generator, output, error)
            .RunAsync(PlaylistArguments, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Success, exitCode);
        Assert.Equal(
            new PlaylistRequest("music", "id.mp3", 4, "mix.m3u8"),
            generator.Request);

        var json = JsonDocument.Parse(output.ToString().Split(Environment.NewLine)[1]);
        Assert.Equal(4, json.RootElement.GetProperty("insert_every").GetInt32());
        Assert.Equal("mix.m3u8", json.RootElement.GetProperty("output_path").GetString());
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
            FakeFfmpegInstallAdvisor.Installed(),
            output,
            new StringWriter());

        var exitCode = await application.RunAsync(
            NormalizeArguments,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Success, exitCode);
        Assert.Equal(new NormalizationRequest("music", "normalized"), normalizer.Request);
        Assert.Contains("normalized_file_count", output.ToString(), StringComparison.Ordinal);
    }

    // Each case is wrapped so the string array is one argument rather than the params list.
    [Theory]
    [InlineData(new object[] { new string[0] })]
    [InlineData(new object[] { new[] { "--help" } })]
    [InlineData(new object[] { new[] { "-h" } })]
    public async Task PrintsUsageForEveryCommand(string[] arguments)
    {
        var output = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), output, new StringWriter())
            .RunAsync(arguments, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Success, exitCode);
        var text = output.ToString();
        Assert.Contains("--source-directory", text, StringComparison.Ordinal);
        Assert.Contains("normalize-volume", text, StringComparison.Ordinal);
        Assert.Contains("install-ffmpeg", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("normalize-volume", "--output-directory")]
    [InlineData("install-ffmpeg", "install-ffmpeg")]
    public async Task PrintsUsageForOneCommand(string command, string expected)
    {
        var output = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), output, new StringWriter())
            .RunAsync([command, "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Success, exitCode);
        Assert.Contains(expected, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUsageErrorForUnknownOptions()
    {
        var error = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), new StringWriter(), error)
            .RunAsync(["--unknown", "value"], TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("Unknown option", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUsageErrorForDuplicateOptions()
    {
        var error = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), new StringWriter(), error)
            .RunAsync(
                ["--source-directory", "first", "--source-directory", "second"],
                TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("provided more than once", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUsageErrorForANonIntegerInterval()
    {
        var error = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), new StringWriter(), error)
            .RunAsync(
                [
                    "--source-directory", "music",
                    "--special-file", "id.mp3",
                    "--insert-every", "four",
                    "--output-path", "mix.m3u8",
                ],
                TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("must be an integer", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUsageErrorForAMissingRequiredOption()
    {
        var error = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), new StringWriter(), error)
            .RunAsync(
                ["--source-directory", "music"],
                TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("is required", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUsageErrorWhenInstallFfmpegIsGivenOptions()
    {
        var error = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), new StringWriter(), error)
            .RunAsync(
                ["install-ffmpeg", "--source-directory", "music"],
                TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("does not accept options", error.ToString(), StringComparison.Ordinal);
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
        await cancellation.CancelAsync();

        var application = new CliApplication(
            new FakePlaylistGenerator(),
            normalizer,
            FakeFfmpegInstallAdvisor.Installed(),
            new StringWriter(),
            new StringWriter());

        var exitCode = await application.RunAsync(NormalizeArguments, cancellation.Token);

        Assert.Equal(ExitCode.Canceled, exitCode);
    }

    [Fact]
    public async Task ReturnsCanceledWhenTheOperationThrowsOnInterrupt()
    {
        var normalizer = new FakeAudioNormalizer
        {
            Handler = (_, _, _, token) => Task.FromCanceled<NormalizationResult>(token),
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var error = new StringWriter();

        var application = new CliApplication(
            new FakePlaylistGenerator(),
            normalizer,
            FakeFfmpegInstallAdvisor.Installed(),
            new StringWriter(),
            error);

        var exitCode = await application.RunAsync(NormalizeArguments, cancellation.Token);

        Assert.Equal(ExitCode.Canceled, exitCode);
        Assert.Contains("canceled", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReturnsExpectedErrorWithoutAStackTrace()
    {
        var error = new StringWriter();
        var generator = new FakePlaylistGenerator
        {
            Exception = new PlaylistValidationException("bad input"),
        };

        var exitCode = await CreateApplication(generator, new StringWriter(), error)
            .RunAsync(PlaylistArguments, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Equal($"bad input{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public async Task ReturnsAnInternalErrorForAnUnexpectedFault()
    {
        var error = new StringWriter();
        var generator = new FakePlaylistGenerator
        {
            Exception = new InvalidOperationException("defect"),
        };

        var exitCode = await CreateApplication(generator, new StringWriter(), error)
            .RunAsync(PlaylistArguments, TestContext.Current.CancellationToken);

        // An unexpected fault still exits cleanly, and keeps its detail for diagnosis.
        Assert.Equal(ExitCode.InternalError, exitCode);
        Assert.Contains("Unexpected error", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("defect", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegCommandIsAdviceOnly()
    {
        var output = new StringWriter();
        var application = new CliApplication(
            new FakePlaylistGenerator(),
            new FakeAudioNormalizer(),
            new FakeFfmpegInstallAdvisor(
                new FfmpegInstallPlan(false, "Install it.", ["sudo", "apt", "install", "ffmpeg"])),
            output,
            new StringWriter());

        var exitCode = await application.RunAsync(
            ["install-ffmpeg"],
            TestContext.Current.CancellationToken);

        // Reported, never run: a missing dependency is a failure exit for scripts.
        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Contains(
            "Command: sudo apt install ffmpeg",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAnInstalledFfmpegAsSuccess()
    {
        var output = new StringWriter();

        var exitCode = await CreateApplication(new FakePlaylistGenerator(), output, new StringWriter())
            .RunAsync(["install-ffmpeg"], TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Success, exitCode);
        Assert.DoesNotContain("Command:", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuotesInstallCommandArgumentsThatContainSpaces()
    {
        var output = new StringWriter();
        var application = new CliApplication(
            new FakePlaylistGenerator(),
            new FakeAudioNormalizer(),
            new FakeFfmpegInstallAdvisor(
                new FfmpegInstallPlan(false, "Install it.", ["setup", "C:\\Program Files\\x"])),
            output,
            new StringWriter());

        await application.RunAsync(["install-ffmpeg"], TestContext.Current.CancellationToken);

        Assert.Contains(
            """Command: setup "C:\Program Files\x" """.TrimEnd(),
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsACommandSetWithoutExactlyOneDefault()
    {
        Assert.Throws<ArgumentException>(
            () => new CliApplication([], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task RejectsNullArguments()
    {
        var application = CreateApplication(
            new FakePlaylistGenerator(),
            new StringWriter(),
            new StringWriter());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => application.RunAsync(null!, TestContext.Current.CancellationToken));
    }

    private static CliApplication CreateApplication(
        FakePlaylistGenerator generator,
        TextWriter output,
        TextWriter error) =>
        new(
            generator,
            new FakeAudioNormalizer(),
            FakeFfmpegInstallAdvisor.Installed(),
            output,
            error);
}
