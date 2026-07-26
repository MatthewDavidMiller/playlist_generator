namespace PlaylistGenerator.Core.Services;

public sealed class PauseController
{
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _resumeSignal;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _resumeSignal is not null;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _resumeSignal ??= new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource<bool>? signal;
        lock (_gate)
        {
            signal = _resumeSignal;
            _resumeSignal = null;
        }

        signal?.TrySetResult(true);
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        Task waitTask;
        lock (_gate)
        {
            waitTask = _resumeSignal?.Task ?? Task.CompletedTask;
        }

        return waitTask.WaitAsync(cancellationToken);
    }
}
