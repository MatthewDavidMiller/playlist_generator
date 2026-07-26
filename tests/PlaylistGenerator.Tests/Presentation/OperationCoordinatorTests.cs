using PlaylistGenerator.Presentation.ViewModels;

namespace PlaylistGenerator.Tests.Presentation;

public sealed class OperationCoordinatorTests
{
    [Fact]
    public void IsIdleUntilAnOperationBegins()
    {
        var coordinator = new OperationCoordinator();

        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void TracksAnOperationForTheLifetimeOfItsScope()
    {
        var coordinator = new OperationCoordinator();

        using (coordinator.BeginOperation())
        {
            Assert.True(coordinator.IsBusy);
        }

        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void DisposingAScopeTwiceIsHarmless()
    {
        var coordinator = new OperationCoordinator();
        var scope = coordinator.BeginOperation();

        scope.Dispose();
        scope.Dispose();

        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void NotifiesObserversWhenBusyStateChanges()
    {
        var coordinator = new OperationCoordinator();
        var changes = new List<string?>();
        coordinator.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        coordinator.BeginOperation().Dispose();

        Assert.Equal(
            [nameof(OperationCoordinator.IsBusy), nameof(OperationCoordinator.IsBusy)],
            changes);
    }
}
