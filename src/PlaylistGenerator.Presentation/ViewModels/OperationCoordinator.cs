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
public sealed class OperationCoordinator : ObservableObject
{
    private int _activeOperationCount;

    /// <summary>Gets whether any operation is currently active.</summary>
    public bool IsBusy => _activeOperationCount > 0;

    /// <summary>
    /// Marks an operation as active until the returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// Scopes are counted rather than latched. The commands that open one are disabled while
    /// this reports busy, so a second scope should not arise; if one ever does, counting keeps
    /// the first scope's release from re-enabling every command while the second still runs.
    /// </remarks>
    public IDisposable BeginOperation()
    {
        if (_activeOperationCount++ == 0)
        {
            OnPropertyChanged(nameof(IsBusy));
        }

        return new OperationScope(this);
    }

    private void EndOperation()
    {
        if (--_activeOperationCount == 0)
        {
            OnPropertyChanged(nameof(IsBusy));
        }
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
            coordinator.EndOperation();
        }
    }
}
