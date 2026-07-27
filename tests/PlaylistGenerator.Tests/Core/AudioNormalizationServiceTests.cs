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
                    progress.NormalizedFileCount
                        + progress.SkippedFileCount
                        + progress.FailedFileCount);
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

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.NormalizedFileCount);
        Assert.Contains("did not create", Assert.Single(result.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAnalysisFailuresWithFfmpegDiagnostics()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var track = temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner
        {
            AnalysisExitCode = 1,
            AnalysisOutput = "invalid audio stream",
        };

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(track, failure.SourcePath);
        Assert.Contains("failed to analyze", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("invalid audio stream", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsEncodingFailuresWithFfmpegDiagnostics()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner { EncodeExitCode = 1 };

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = Assert.Single(result.Failures);
        Assert.Contains("failed to encode", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("encoding failed", failure.Reason, StringComparison.Ordinal);
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

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        var reason = Assert.Single(result.Failures).Reason;
        Assert.Contains("REAL-CAUSE", reason, StringComparison.Ordinal);
        Assert.True(reason.Length < 5_000);
    }

    [Fact]
    public async Task OneUnusableFileDoesNotDiscardTheRestOfTheRun()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/a-good.mp3");
        var broken = temporary.CreateFile("music/b-broken.mp3");
        temporary.CreateFile("music/c-good.mp3");
        var runner = new FakeProcessRunner
        {
            Handler = async (arguments, token) =>
            {
                var input = arguments.ValueAfter("-i");
                if (string.Equals(input, broken, StringComparison.Ordinal))
                {
                    return new ProcessResult(1, string.Empty, "corrupt header");
                }

                if (FakeProcessRunner.IsAnalysisCall(arguments))
                {
                    return new ProcessResult(0, string.Empty, FakeProcessRunner.ValidAnalysisJson);
                }

                await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], token);
                return new ProcessResult(0, string.Empty, string.Empty);
            },
        };

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.NormalizedFileCount);
        Assert.Equal(1, result.FailedFileCount);
        Assert.False(result.Stopped);
        Assert.Equal(broken, Assert.Single(result.Failures).SourcePath);

        // The files on either side of the broken one still produced output.
        Assert.True(File.Exists(temporary.GetPath("normalized/a-good.opus")));
        Assert.True(File.Exists(temporary.GetPath("normalized/c-good.opus")));
        Assert.False(File.Exists(temporary.GetPath("normalized/b-broken.opus")));
    }

    [Fact]
    public async Task AFailedFileIsReportedAsProgressAndCountedAsCompleted()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner { AnalysisExitCode = 1 };
        var events = new List<NormalizationProgress>();

        await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            new ImmediateProgress<NormalizationProgress>(events.Add),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(NormalizationAction.Failed, events[^1].Action);
        Assert.Equal(1, events[^1].FailedFileCount);

        // A failed file is finished, so the progress bar must reach the end rather than stall.
        Assert.Equal(events[^1].TotalFileCount, events[^1].CompletedFileCount);
    }

    [Fact]
    public async Task ASourceThatDisappearsBeforeItsTurnFailsOnlyThatFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/a-one.mp3");
        var vanishing = temporary.CreateFile("music/b-two.mp3");
        var runner = new FakeProcessRunner
        {
            Handler = async (arguments, token) =>
            {
                // Delete the second file while the first is still being processed.
                File.Delete(vanishing);
                if (FakeProcessRunner.IsAnalysisCall(arguments))
                {
                    return new ProcessResult(0, string.Empty, FakeProcessRunner.ValidAnalysisJson);
                }

                await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], token);
                return new ProcessResult(0, string.Empty, string.Empty);
            },
        };

        var result = await CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.NormalizedFileCount);
        Assert.Contains(
            "became unavailable",
            Assert.Single(result.Failures).Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalizesEveryFileWhenSeveralRunAtOnce()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        for (var index = 0; index < 24; index++)
        {
            temporary.CreateFile($"music/track-{index:D2}.mp3");
        }

        var result = await CreateService(maxDegreeOfParallelism: 4).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(24, result.NormalizedFileCount);
        Assert.Empty(result.Failures);
        Assert.Equal(
            24,
            Directory.GetFiles(temporary.GetPath("normalized"), "*.opus").Length);
    }

    [Fact]
    public async Task SeveralFilesAreEncodedConcurrently()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        for (var index = 0; index < 8; index++)
        {
            temporary.CreateFile($"music/track-{index:D2}.mp3");
        }

        var inFlight = 0;
        var peakInFlight = 0;
        var peakGate = new Lock();
        var runner = new FakeProcessRunner
        {
            Handler = async (arguments, token) =>
            {
                var current = Interlocked.Increment(ref inFlight);
                lock (peakGate)
                {
                    peakInFlight = Math.Max(peakInFlight, current);
                }

                try
                {
                    // Long enough that a sequential run could not overlap two calls.
                    await Task.Delay(30, token);
                    if (FakeProcessRunner.IsAnalysisCall(arguments))
                    {
                        return new ProcessResult(
                            0,
                            string.Empty,
                            FakeProcessRunner.ValidAnalysisJson);
                    }

                    await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], token);
                    return new ProcessResult(0, string.Empty, string.Empty);
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            },
        };

        var result = await CreateService(runner, maxDegreeOfParallelism: 4).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(8, result.NormalizedFileCount);
        Assert.InRange(peakInFlight, 2, 4);
    }

    [Fact]
    public async Task ConcurrentProgressCountsStayConsistent()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        for (var index = 0; index < 32; index++)
        {
            temporary.CreateFile($"music/track-{index:D2}.mp3");
        }

        var events = new List<NormalizationProgress>();
        var gate = new Lock();

        await CreateService(maxDegreeOfParallelism: 4).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            new ImmediateProgress<NormalizationProgress>(progress =>
            {
                lock (gate)
                {
                    events.Add(progress);
                }
            }),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(
            events,
            progress =>
            {
                Assert.InRange(progress.CompletedFileCount, 0, progress.TotalFileCount);
                Assert.Equal(
                    progress.CompletedFileCount,
                    progress.NormalizedFileCount
                        + progress.SkippedFileCount
                        + progress.FailedFileCount);
            });

        // Reports are published under one lock, so the completed count never runs backwards.
        var completed = events.Select(progress => progress.CompletedFileCount).ToArray();
        Assert.Equal(completed.Order().ToArray(), completed);
    }

    [Fact]
    public async Task ARunCanceledWithNothingLeftToDoStillReportsStopped()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");

        // The output already exists, so the plan has no jobs to schedule at all.
        temporary.CreateFile("normalized/one.opus");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var result = await CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            cancellationToken: cancellation.Token);

        Assert.True(result.Stopped);
        Assert.Equal(1, result.SkippedFileCount);
    }

    [Fact]
    public async Task AnUnwritableDestinationBecomesAFailureRatherThanACrash()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file permissions are required.");
            return;
        }

        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("USER") == "root",
            "Root bypasses the permission bits this test relies on.");

        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/album/one.mp3");
        var output = temporary.CreateDirectory("normalized");
        File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var result = await CreateService().NormalizeAsync(
                new NormalizationRequest(source, output),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.NormalizedFileCount);
            Assert.Contains(
                "Unable to normalize",
                Assert.Single(result.Failures).Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(
                output,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task AnUnexpectedFaultSurfacesAsItselfRatherThanAnAggregate()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        temporary.CreateFile("music/one.mp3");
        var runner = new FakeProcessRunner
        {
            Handler = (_, _) => throw new InvalidOperationException("defective runner"),
        };

        // Only expected failures are recorded per file; a defect must still reach the caller,
        // and awaiting the parallel loop must not bury it inside an AggregateException.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(runner).NormalizeAsync(
                new NormalizationRequest(source, temporary.GetPath("normalized")),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("defective runner", exception.Message);
    }

    [Fact]
    public void RejectsAWorkerCountBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AudioNormalizationService(
                new AudioFileCatalog(),
                new FakeExecutableLocator(),
                new FakeProcessRunner(),
                0));
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
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            OnPaused(paused),
            controller,
            TestContext.Current.CancellationToken);

        // Waiting for the reported pause is deterministic; yielding once would only test
        // whether a pool thread happened to be scheduled yet.
        await paused.Task;
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
        var analyzed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessRunner
        {
            Handler = async (arguments, token) =>
            {
                if (FakeProcessRunner.IsAnalysisCall(arguments))
                {
                    controller.Pause();
                    analyzed.SetResult();
                    return new ProcessResult(0, string.Empty, FakeProcessRunner.ValidAnalysisJson);
                }

                await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], token);
                return new ProcessResult(0, string.Empty, string.Empty);
            },
        };

        var task = CreateService(runner).NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            OnPaused(paused),
            controller,
            TestContext.Current.CancellationToken);

        await analyzed.Task;
        await paused.Task;

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
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var task = CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            OnPaused(paused),
            controller,
            cancellation.Token);

        await paused.Task;
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
        var gate = new Lock();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = CreateService().NormalizeAsync(
            new NormalizationRequest(source, temporary.GetPath("normalized")),
            new ImmediateProgress<NormalizationProgress>(progress =>
            {
                lock (gate)
                {
                    events.Add(progress);
                }

                if (progress.Action == NormalizationAction.Paused)
                {
                    paused.TrySetResult();
                }
            }),
            controller,
            TestContext.Current.CancellationToken);

        await paused.Task;
        lock (gate)
        {
            Assert.Contains(events, progress => progress.Action == NormalizationAction.Paused);
        }

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

    /// <summary>
    /// Completes <paramref name="paused"/> once the run reports that it is holding, which is
    /// the only deterministic way to observe work that runs on a thread-pool worker.
    /// </summary>
    private static ImmediateProgress<NormalizationProgress> OnPaused(TaskCompletionSource paused) =>
        new(progress =>
        {
            if (progress.Action == NormalizationAction.Paused)
            {
                paused.TrySetResult();
            }
        });

    /// <summary>
    /// Builds a service that runs one file at a time, so ordering assertions stay meaningful.
    /// Concurrency is exercised deliberately by the tests that pass a higher worker count.
    /// </summary>
    private static AudioNormalizationService CreateService(
        FakeProcessRunner? runner = null,
        int maxDegreeOfParallelism = 1) =>
        new(
            new AudioFileCatalog(),
            new FakeExecutableLocator(),
            runner ?? new FakeProcessRunner(),
            maxDegreeOfParallelism);
}
