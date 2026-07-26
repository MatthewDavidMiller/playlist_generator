using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Core.Threading;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class AudioNormalizationServiceTests
{
    [Fact]
    public async Task NormalizesRecursivelyWithTwoPassOpusEncoding()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        temporary.CreateFile("music/album/two.flac");
        var runner = new FakeProcessRunner();

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.NormalizedFileCount);
        Assert.Equal(0, result.SkippedFileCount);
        Assert.False(result.Stopped);
        Assert.True(File.Exists(temporary.GetPath("normalized/one.opus")));
        Assert.True(File.Exists(temporary.GetPath("normalized/album/two.opus")));

        // Two passes per file: measure, then encode.
        Assert.Equal(4, runner.Calls.Count);
        Assert.All(
            runner.Calls.Where(call => !FakeProcessRunner.IsAnalysisCall(call)),
            call => Assert.Equal("libopus", call.ValueAfter("-c:a")));
    }

    [Fact]
    public async Task NeverModifiesSourceFiles()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var track = temporary.CreateFile("music/one.mp3", "original audio");

        await CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("original audio", await File.ReadAllTextAsync(
            track,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LeavesNoTemporaryFileBehind()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");

        await CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            temporary.EnumerateRelativeFiles(),
            file => file.Contains(".tmp", StringComparison.Ordinal));
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

        var result = await CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("music")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.NormalizedFileCount);
        Assert.True(File.Exists(temporary.GetPath("music/one.opus")));
    }

    [Fact]
    public async Task RejectsWritingBackIntoTheSourceFolder()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");

        // Silently skipping everything here would look identical to a successful run.
        var exception = await Assert.ThrowsAsync<PlaylistValidationException>(
            () => CreateService().NormalizeAsync(
                new NormalizationRequest(source, source),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("must differ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsDestinationsThatCollapseToTheSameOpusFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/song.mp3");
        temporary.CreateFile("music/song.flac");
        var runner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<PlaylistValidationException>(
            () => CreateService(runner).NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("same normalized output path", exception.Message, StringComparison.Ordinal);

        // The collision is detected before any encoding starts.
        Assert.Empty(runner.Calls);
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
        Assert.All(events, progress => Assert.Equal(2, progress.TotalFileCount));
    }

    [Fact]
    public async Task ProgressCountsNeverExceedTheTotal()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        temporary.CreateFile("music/two.mp3");
        temporary.CreateFile("music/three.mp3");
        var events = new List<NormalizationProgress>();

        await CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            new ImmediateProgress<NormalizationProgress>(events.Add),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(
            events,
            progress =>
            {
                Assert.InRange(progress.CompletedFileCount, 0, progress.TotalFileCount);
                Assert.Equal(
                    progress.CompletedFileCount,
                    progress.NormalizedFileCount + progress.SkippedFileCount);
            });
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
                if (FakeProcessRunner.IsAnalysisCall(arguments))
                {
                    return Task.FromResult(
                        new ProcessResult(0, string.Empty, FakeProcessRunner.ValidAnalysisJson));
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
        Assert.False(File.Exists(temporary.GetPath("normalized/one.opus")));
    }

    [Fact]
    public async Task ACanceledRunKeepsFilesThatAlreadyFinished()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/a-one.mp3");
        temporary.CreateFile("music/b-two.mp3");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var encodes = 0;
        var runner = new FakeProcessRunner
        {
            Handler = async (arguments, token) =>
            {
                if (FakeProcessRunner.IsAnalysisCall(arguments))
                {
                    return new ProcessResult(0, string.Empty, FakeProcessRunner.ValidAnalysisJson);
                }

                if (Interlocked.Increment(ref encodes) > 1)
                {
                    await cancellation.CancelAsync();
                    token.ThrowIfCancellationRequested();
                }

                await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], token);
                return new ProcessResult(0, string.Empty, string.Empty);
            },
        };

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: cancellation.Token);

        Assert.True(result.Stopped);
        Assert.Equal(1, result.NormalizedFileCount);
        Assert.True(File.Exists(temporary.GetPath("normalized/a-one.opus")));
        Assert.False(File.Exists(temporary.GetPath("normalized/b-two.opus")));
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
    public async Task TruncatesOverlongFfmpegDiagnostics()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner
        {
            AnalysisExitCode = 1,

            // The real cause is at the end, so the tail is what must survive truncation.
            AnalysisOutput = new string('x', 10_000) + "REAL-CAUSE",
        };

        var exception = await Assert.ThrowsAsync<PlaylistIOException>(
            () => CreateService(runner).NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("REAL-CAUSE", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length < 5_000);
    }

    [Fact]
    public async Task RequiresFfmpegBeforeScanning()
    {
        var service = new AudioNormalizationService(
            new AudioFileCatalog(),
            new FakeExecutableLocator(null),
            new FakeProcessRunner());

        var exception = await Assert.ThrowsAsync<PlaylistValidationException>(
            () => service.NormalizeAsync(
                new NormalizationRequest("missing", "output"),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("FFmpeg is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunsTheFfmpegExecutableTheLocatorResolved()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner();
        var service = new AudioNormalizationService(
            new AudioFileCatalog(),
            new FakeExecutableLocator("/opt/bin/ffmpeg"),
            runner);

        await service.NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(runner.Executables, executable => Assert.Equal("/opt/bin/ffmpeg", executable));
    }

    [Fact]
    public async Task RejectsAnEmptyLibrary()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/readme.txt");

        var exception = await Assert.ThrowsAsync<PlaylistValidationException>(
            () => CreateService().NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("No supported audio files", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "out", "Source directory is required.")]
    [InlineData("music", "  ", "Output directory is required.")]
    public async Task RejectsBlankDirectories(string source, string output, string expected)
    {
        var exception = await Assert.ThrowsAsync<PlaylistValidationException>(
            () => CreateService().NormalizeAsync(
                new NormalizationRequest(source, output),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(expected, exception.Message);
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
            pauseSignal: controller,
            cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield();
        Assert.False(task.IsCompleted);

        controller.Resume();
        Assert.Equal(1, (await task).NormalizedFileCount);
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
            Handler = async (arguments, token) =>
            {
                if (FakeProcessRunner.IsAnalysisCall(arguments))
                {
                    controller.Pause();
                    return new ProcessResult(0, string.Empty, FakeProcessRunner.ValidAnalysisJson);
                }

                await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], token);
                return new ProcessResult(0, string.Empty, string.Empty);
            },
        };

        var task = CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            pauseSignal: controller,
            cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield();

        // The encode pass must not have started while the pause is held.
        Assert.False(task.IsCompleted);
        Assert.Single(runner.Calls);

        controller.Resume();
        Assert.Equal(1, (await task).NormalizedFileCount);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task CancellingWhilePausedStopsTheRun()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var controller = new PauseController();
        controller.Pause();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var task = CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            pauseSignal: controller,
            cancellationToken: cancellation.Token);
        await Task.Yield();
        Assert.False(task.IsCompleted);

        await cancellation.CancelAsync();
        var result = await task;

        Assert.True(result.Stopped);
        Assert.Equal(0, result.NormalizedFileCount);
    }

    [Fact]
    public async Task ReportsAPausedProgressEvent()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var controller = new PauseController();
        controller.Pause();
        var events = new List<NormalizationProgress>();

        var task = CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            new ImmediateProgress<NormalizationProgress>(events.Add),
            controller,
            TestContext.Current.CancellationToken);
        await Task.Yield();

        Assert.Contains(events, progress => progress.Action == NormalizationAction.Paused);

        controller.Resume();
        await task;
    }

    [Fact]
    public async Task RejectsANullRequest() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateService().NormalizeAsync(
                null!,
                cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public void RejectsNullDependencies()
    {
        var catalog = new AudioFileCatalog();
        var locator = new FakeExecutableLocator();
        var runner = new FakeProcessRunner();

        Assert.Throws<ArgumentNullException>(
            () => new AudioNormalizationService(null!, locator, runner));
        Assert.Throws<ArgumentNullException>(
            () => new AudioNormalizationService(catalog, null!, runner));
        Assert.Throws<ArgumentNullException>(
            () => new AudioNormalizationService(catalog, locator, null!));
    }

    private static AudioNormalizationService CreateService(FakeProcessRunner? runner = null) =>
        new(new AudioFileCatalog(), new FakeExecutableLocator(), runner ?? new FakeProcessRunner());
}
