namespace PlaylistGenerator.Core.Models;

/// <summary>
/// The captured outcome of one external process invocation.
/// </summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// Gets the stream most likely to explain a failure, preferring standard error.
    /// </summary>
    public string Diagnostics =>
        string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
}
