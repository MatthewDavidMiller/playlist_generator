using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests;

public sealed class AudioNormalizationServiceTests
{
    [Fact]
    public async Task NormalizesRecursivelyWithTwoPassOpusEncoding()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        temporary.CreateFile("music/album/two.flac");
        var output = temporary.GetPath("normalized");
        var runner = new FakeProcessRunner();

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, output),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.NormalizedFileCount);
        Assert.Equal(0, result.SkippedFileCount);
        Assert.True(File.Exists(temporary.GetPath("normalized/one.opus")));
        Assert.True(File.Exists(temporary.GetPath("normalized/album/two.opus")));
        Assert.Equal(4, runner.Calls.Count);
        Assert.All(
            runner.Calls.Where(call => call[^1] != "-"),
            call => Assert.Equal("libopus", call[call.IndexOf("-c:a") + 1]));
    }

    [Fact]
    public async Task RejectsDestinationsThatCollapseToTheSameOpusFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/song.mp3");
        temporary.CreateFile("music/song.flac");

        var exception = await Assert.ThrowsAsync<PlaylistValidationException>(
            () => CreateService().NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("same normalized output path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkipsExistingDestinationsAndFilesInsideTheOutputTree()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var output = temporary.CreateDirectory("music/normalized");
        temporary.CreateFile("music/one.mp3");
        temporary.CreateFile("music/two.mp3");
        temporary.CreateFile("music/normalized/two.opus");
        temporary.CreateFile("music/normalized/already-inside.mp3");
        var runner = new FakeProcessRunner();

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, output),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.NormalizedFileCount);
        Assert.Equal(3, result.SkippedFileCount);
        Assert.Equal(2, runner.Calls.Count);
        Assert.True(File.Exists(temporary.GetPath("music/normalized/one.opus")));
    }

    [Fact]
    public async Task AllowsTheOutputDirectoryToBeAParentOfTheSource()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music/raw");
        temporary.CreateFile("music/raw/one.mp3");
        var output = temporary.GetPath("music");

        var result = await CreateService().NormalizeAsync(
            new NormalizationRequest(source, output),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.NormalizedFileCount);
        Assert.Equal(0, result.SkippedFileCount);
        Assert.True(File.Exists(temporary.GetPath("music/one.opus")));
    }

    [Fact]
    public async Task ReportsProgressForSkippedAndCompletedFiles()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var output = temporary.CreateDirectory("normalized");
        temporary.CreateFile("music/one.mp3");
        temporary.CreateFile("music/two.mp3");
        temporary.CreateFile("normalized/two.opus");
        var events = new List<NormalizationProgress>();

        var result = await CreateService().NormalizeAsync(
            new NormalizationRequest(source, output),
            new ImmediateProgress<NormalizationProgress>(events.Add),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.NormalizedFileCount);
        Assert.Equal(1, result.SkippedFileCount);
        Assert.Contains(events, progress => progress.Action == NormalizationAction.Skipped);
        Assert.Contains(events, progress => progress.Action == NormalizationAction.Analyzing);
        Assert.Contains(events, progress => progress.Action == NormalizationAction.Encoding);
        Assert.Equal(NormalizationAction.Completed, events[^1].Action);
        Assert.Equal(2, events[^1].CompletedFileCount);
    }

    [Fact]
    public async Task CancellationStopsAnActiveEncodeAndReturnsPartialCounts()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var runner = new FakeProcessRunner
        {
            Handler = (arguments, token) =>
            {
                if (arguments[^1] == "-")
                {
                    return Task.FromResult(
                        new ProcessResult(
                            0,
                            string.Empty,
                            FakeProcessRunner.ValidAnalysisJson));
                }

                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Unreachable");
            },
        };

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: cancellation.Token);

        Assert.True(result.Stopped);
        Assert.Equal(0, result.NormalizedFileCount);
    }

    [Fact]
    public async Task ReportsWhenFfmpegReturnsSuccessWithoutAnOutputFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner { CreateEncodedFile = false };

        var exception = await Assert.ThrowsAsync<PlaylistIOException>(
            () => CreateService(runner).NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("did not create", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAnalysisFailuresWithFfmpegDiagnostics()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner
        {
            AnalysisExitCode = 1,
            AnalysisOutput = "invalid audio stream",
        };

        var exception = await Assert.ThrowsAsync<PlaylistIOException>(
            () => CreateService(runner).NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("failed to analyze", exception.Message, StringComparison.Ordinal);
        Assert.Contains("invalid audio stream", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsEncodingFailuresWithFfmpegDiagnostics()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner { EncodeExitCode = 1 };

        var exception = await Assert.ThrowsAsync<PlaylistIOException>(
            () => CreateService(runner).NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("failed to encode", exception.Message, StringComparison.Ordinal);
        Assert.Contains("encoding failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiresFfmpegBeforeScanning()
    {
        var service = new AudioNormalizationService(
            new AudioFileCatalog(),
            new FakeFfmpegLocator(null),
            new FakeProcessRunner());

        var exception = await Assert.ThrowsAsync<PlaylistValidationException>(
            () => service.NormalizeAsync(
                new NormalizationRequest("missing", "output"),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("FFmpeg is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitsWhilePausedAndContinuesAfterResume()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var controller = new PauseController();
        controller.Pause();

        var task = CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            pauseController: controller,
            cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield();
        Assert.False(task.IsCompleted);

        controller.Resume();
        var result = await task;
        Assert.Equal(1, result.NormalizedFileCount);
    }

    [Fact]
    public async Task ARequestedPauseTakesEffectBetweenAnalysisAndEncoding()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var controller = new PauseController();
        var runner = new FakeProcessRunner
        {
            Handler = (arguments, _) =>
            {
                if (arguments[^1] == "-")
                {
                    controller.Pause();
                    return Task.FromResult(
                        new ProcessResult(
                            0,
                            string.Empty,
                            FakeProcessRunner.ValidAnalysisJson));
                }

                File.WriteAllBytes(arguments[^1], [1, 2, 3]);
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
            },
        };

        var task = CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            pauseController: controller,
            cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield();

        Assert.False(task.IsCompleted);
        Assert.Single(runner.Calls);

        controller.Resume();
        var result = await task;
        Assert.Equal(1, result.NormalizedFileCount);
        Assert.Equal(2, runner.Calls.Count);
    }

    private static AudioNormalizationService CreateService(
        FakeProcessRunner? runner = null) =>
        new(
            new AudioFileCatalog(),
            new FakeFfmpegLocator(),
            runner ?? new FakeProcessRunner());

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
