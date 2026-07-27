namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Waits for a condition that becomes true on another thread.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> delivers asynchronously. Under the GUI's dispatcher those posts
/// are ordered against the run's completion, but a test has no synchronization context, so a
/// report can land just after the awaited run returns.
/// </remarks>
internal static class Eventually
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task TrueAsync(
        Func<bool> condition,
        CancellationToken cancellationToken,
        string message = "The expected state was never reached.")
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.False(DateTime.UtcNow > deadline, message);
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }
}
