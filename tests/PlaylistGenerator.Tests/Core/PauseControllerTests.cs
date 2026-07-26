using PlaylistGenerator.Core.Threading;

namespace PlaylistGenerator.Tests.Core;

public sealed class PauseControllerTests
{
    [Fact]
    public async Task WaitsWithoutBlockingAThreadAndReleasesOnResume()
    {
        var controller = new PauseController();
        controller.Pause();

        var wait = controller.WaitWhilePausedAsync(TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);
        Assert.True(controller.IsPaused);

        controller.Resume();
        await wait;
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task CompletesImmediatelyWhenNotPaused()
    {
        var controller = new PauseController();

        var wait = controller.WaitWhilePausedAsync(TestContext.Current.CancellationToken);

        Assert.True(wait.IsCompletedSuccessfully);
        await wait;
    }

    [Fact]
    public async Task CancellationReleasesAWaiterThatIsStillPaused()
    {
        var controller = new PauseController();
        controller.Pause();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var wait = controller.WaitWhilePausedAsync(cancellation.Token);
        await cancellation.CancelAsync();

        // A stop request must not leave the run parked on a pause that never resumes.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task AnAlreadyCanceledTokenIsObservedEvenWhenNotPaused()
    {
        var controller = new PauseController();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.WaitWhilePausedAsync(cancellation.Token));
    }

    [Fact]
    public async Task RepeatedPausesAndResumesAreIdempotent()
    {
        var controller = new PauseController();

        controller.Resume();
        Assert.False(controller.IsPaused);

        controller.Pause();
        controller.Pause();
        Assert.True(controller.IsPaused);

        controller.Resume();
        controller.Resume();
        Assert.False(controller.IsPaused);
        await controller.WaitWhilePausedAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReleasesEveryWaiterOnASingleResume()
    {
        var controller = new PauseController();
        controller.Pause();

        var waits = Enumerable
            .Range(0, 8)
            .Select(_ => controller.WaitWhilePausedAsync(TestContext.Current.CancellationToken))
            .ToArray();
        Assert.All(waits, wait => Assert.False(wait.IsCompleted));

        controller.Resume();
        await Task.WhenAll(waits);
    }
}
