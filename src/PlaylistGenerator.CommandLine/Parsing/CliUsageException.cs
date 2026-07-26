namespace PlaylistGenerator.CommandLine.Parsing;

/// <summary>
/// Reports a command line the application cannot interpret.
/// </summary>
/// <remarks>
/// Kept separate from domain failures so that mistyped arguments exit with the usage code
/// and print usage text, while a valid command that fails does neither.
/// </remarks>
public sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }

    public CliUsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
