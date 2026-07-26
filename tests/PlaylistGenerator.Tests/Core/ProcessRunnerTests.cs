using System.Diagnostics;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task PreservesArgumentBoundariesAndDiagnostics()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // "value with spaces" must arrive as one argument, proving no shell reparsed it.
        var result = await new ProcessRunner().RunAsync(
            "/bin/sh",
            [
                "-c",
                """printf '%s' "$1"; printf '%s' "$2" >&2; exit 7""",
                "sh",
                "value with spaces",
                "error output",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("value with spaces", result.StandardOutput);
        Assert.Equal("error output", result.StandardError);
    }

    [Fact]
    public async Task DoesNotLetShellMetacharactersChangeTheCommand()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        const string hostile = "; rm -rf /tmp/should-not-happen; echo $(whoami)";

        var result = await new ProcessRunner().RunAsync(
            "/bin/sh",
            ["-c", """printf '%s' "$1" """, "sh", hostile],
            TestContext.Current.CancellationToken);

        Assert.Equal(hostile, result.StandardOutput);
    }

    [Fact]
    public async Task ReadsOutputLargerThanAPipeBuffer()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Well past a typical 64 KiB pipe buffer: a process filling both pipes would
        // deadlock if the runner did not drain them concurrently with the wait.
        // head reads a fixed count from /dev/zero, so neither producer sees a broken pipe.
        var result = await new ProcessRunner().RunAsync(
            "/bin/sh",
            [
                "-c",
                "head -c 300000 /dev/zero | tr '\\0' 'a'; "
                + "head -c 300000 /dev/zero | tr '\\0' 'b' >&2",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(300_000, result.StandardOutput.Length);
        Assert.Equal(300_000, result.StandardError.Length);
        Assert.Equal('a', result.StandardOutput[^1]);
        Assert.Equal('b', result.StandardError[^1]);
    }

    [Fact]
    public async Task KillsTheProcessTreeWhenCanceled()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessRunner().RunAsync(
                "/bin/sh",
                ["-c", "sleep 30"],
                cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancellationLeavesNoUnobservedTaskException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args) =>
            unobserved.Add(args.Exception);

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken))
            {
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => new ProcessRunner().RunAsync(
                        "/bin/sh",
                        ["-c", "sleep 30"],
                        cancellation.Token));
            }

            // The abandoned stream readers must have been observed, or their faults would
            // surface here once they are collected.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Fact]
    public async Task ObservesATokenThatIsAlreadyCanceled()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessRunner().RunAsync("/bin/sh", ["-c", "true"], cancellation.Token));
    }

    [Fact]
    public async Task WrapsStartFailures()
    {
        using var temporary = new TemporaryDirectory();

        var exception = await Assert.ThrowsAsync<PlaylistIOException>(
            () => new ProcessRunner().RunAsync(
                temporary.GetPath("missing-executable"),
                [],
                TestContext.Current.CancellationToken));

        Assert.Contains("Unable to run", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsInvalidArguments()
    {
        var runner = new ProcessRunner();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runner.RunAsync(null!, [], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync("  ", [], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runner.RunAsync("/bin/sh", null!, TestContext.Current.CancellationToken));
    }
}
