namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// A cooperative pause signal that a long-running operation observes between steps.
/// </summary>
/// <remarks>
/// Declared here rather than alongside its implementation so that abstractions never
/// depend on a concrete service. Implementations must be safe to use from any thread.
/// </remarks>
public interface IPauseSignal
{
    /// <summary>Gets whether a pause is currently requested.</summary>
    bool IsPaused { get; }

    /// <summary>
    /// Completes immediately when no pause is requested, otherwise once the pause is
    /// released or <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    Task WaitWhilePausedAsync(CancellationToken cancellationToken = default);
}
