namespace PlaylistGenerator.CommandLine.Commands;

/// <summary>
/// One command the executable can run.
/// </summary>
/// <remarks>
/// Each command owns its own option set and usage text, so adding a command does not require
/// touching the dispatcher's parsing logic.
/// </remarks>
public interface ICliCommand
{
    /// <summary>
    /// The leading argument that selects this command, or <see langword="null"/> for the
    /// command used when no name is given.
    /// </summary>
    string? Name { get; }

    /// <summary>Usage text shown by <c>--help</c>.</summary>
    string Usage { get; }

    /// <summary>Runs the command with its own arguments, excluding the command name.</summary>
    Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        CancellationToken cancellationToken);
}
