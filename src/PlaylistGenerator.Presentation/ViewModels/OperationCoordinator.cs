using CommunityToolkit.Mvvm.ComponentModel;

namespace PlaylistGenerator.Presentation.ViewModels;

/// <summary>
/// Tracks whether any long-running operation is active, across every tab.
/// </summary>
/// <remarks>
/// Playlist generation and normalization both walk the same library, so only one runs at a
/// time. Holding that state here rather than in either tab keeps both commands disabled
/// while the other is working.
/// </remarks>
public sealed partial class OperationCoordinator : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Marks an operation as active until the returned scope is disposed.
    /// </summary>
    public IDisposable BeginOperation()
    {
        IsBusy = true;
        return new OperationScope(this);
    }

    private sealed class OperationScope(OperationCoordinator coordinator) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            coordinator.IsBusy = false;
        }
    }
}
