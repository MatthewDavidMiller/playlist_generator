using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Core.Threading;

/// <summary>
/// A thread-safe, non-blocking pause signal driven by a user interface.
/// </summary>
/// <remarks>
/// Pausing parks awaiting callers on a task rather than blocking a thread, so a paused
/// run holds no thread-pool thread.
/// </remarks>
public sealed class PauseController : IPauseSignal
{
    private readonly Lock _gate = new();
    private TaskCompletionSource? _resumeSignal;

    /// <inheritdoc />
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

    /// <summary>
    /// Requests a pause. Calling this while already paused has no effect.
    /// </summary>
    public void Pause()
    {
        lock (_gate)
        {
            _resumeSignal ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Releases a pause and wakes every waiter. Calling this while running has no effect.
    /// </summary>
    public void Resume()
    {
        TaskCompletionSource? signal;
        lock (_gate)
        {
            signal = _resumeSignal;
            _resumeSignal = null;
        }

        signal?.TrySetResult();
    }

    /// <inheritdoc />
    public Task WaitWhilePausedAsync(CancellationToken cancellationToken = default)
    {
        Task? waitTask;
        lock (_gate)
        {
            waitTask = _resumeSignal?.Task;
        }

        // The common case is "not paused". Returning directly avoids the cancellation
        // registration that WaitAsync would allocate for every step of every file.
        if (waitTask is null)
        {
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
        }

        return waitTask.WaitAsync(cancellationToken);
    }
}
