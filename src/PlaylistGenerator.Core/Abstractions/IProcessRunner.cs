using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// Runs an external process and captures its output.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="executable"/> with <paramref name="arguments"/> passed as
    /// discrete values, never through a shell.
    /// </summary>
    /// <remarks>Cancellation terminates the process tree before the task completes.</remarks>
    Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}
