using System.Diagnostics;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void ExecutableLocatorAcceptsExplicitExecutablePaths()
    {
        using var temporary = new TemporaryDirectory();
        var executable = temporary.CreateFile("tools/test-tool");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        var result = new ExecutableLocator().Find(executable);

        Assert.Equal(Path.GetFullPath(executable), result);
    }

    [Fact]
    public void ExecutableLocatorRejectsMissingAndNonExecutableFiles()
    {
        using var temporary = new TemporaryDirectory();
        var locator = new ExecutableLocator();

        Assert.Null(locator.Find(temporary.GetPath("missing-tool")));

        if (!OperatingSystem.IsWindows())
        {
            var nonExecutable = temporary.CreateFile("tools/not-executable");
            File.SetUnixFileMode(
                nonExecutable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Null(locator.Find(nonExecutable));
        }
    }

    [Fact]
    public void SeedableShufflerReturnsANewPermutation()
    {
        string[] tracks = ["A", "B", "C"];
        var shuffled = new RandomTrackShuffler(new ZeroRandom()).Shuffle(tracks);

        Assert.Equal(["B", "C", "A"], shuffled);
        Assert.Equal(["A", "B", "C"], tracks);
    }

    [Fact]
    public void FfmpegAdvisorReportsAnExistingInstallation()
    {
        var plan = new FfmpegInstallAdvisor(new FakeFfmpegLocator("/tools/ffmpeg"))
            .GetPlan();

        Assert.True(plan.IsInstalled);
        Assert.Empty(plan.Command);
    }

    [Fact]
    public void FfmpegAdvisorProvidesLinuxPackageManagerAdvice()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var locator = new MappingExecutableLocator(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["apt"] = "/usr/bin/apt",
            });

        var plan = new FfmpegInstallAdvisor(locator).GetPlan();

        Assert.False(plan.IsInstalled);
        Assert.Equal(["sudo", "apt", "install", "ffmpeg"], plan.Command);
    }

    [Fact]
    public async Task ProcessRunnerPreservesArgumentBoundariesAndDiagnostics()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

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
    public async Task ProcessRunnerKillsTheProcessTreeWhenCanceled()
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
    public async Task ProcessRunnerWrapsStartFailures()
    {
        using var temporary = new TemporaryDirectory();
        var exception = await Assert.ThrowsAsync<PlaylistIOException>(
            () => new ProcessRunner().RunAsync(
                temporary.GetPath("missing-executable"),
                [],
                TestContext.Current.CancellationToken));

        Assert.Contains("Unable to run", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ZeroRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    private sealed class MappingExecutableLocator(
        IReadOnlyDictionary<string, string> executables)
        : PlaylistGenerator.Core.Abstractions.IFfmpegLocator
    {
        public string? Find() => Find("ffmpeg");

        public string? Find(string executableName) =>
            executables.GetValueOrDefault(executableName);
    }
}
