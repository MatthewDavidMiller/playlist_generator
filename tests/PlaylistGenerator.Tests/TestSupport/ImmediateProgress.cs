namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Invokes the callback on the reporting thread.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts asynchronously, which would make reports arrive after the
/// assertions that check them.
/// </remarks>
public sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
