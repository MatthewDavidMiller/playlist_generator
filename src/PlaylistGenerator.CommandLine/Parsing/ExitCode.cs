namespace PlaylistGenerator.CommandLine.Parsing;

/// <summary>
/// Process exit codes, following common shell conventions.
/// </summary>
public static class ExitCode
{
    /// <summary>The command completed.</summary>
    public const int Success = 0;

    /// <summary>The command was understood but could not complete.</summary>
    public const int Failure = 1;

    /// <summary>The command line could not be interpreted.</summary>
    public const int UsageError = 2;

    /// <summary>An unexpected internal error, matching the <c>sysexits</c> software code.</summary>
    public const int InternalError = 70;

    /// <summary>Terminated by an interrupt, following the 128 + SIGINT convention.</summary>
    public const int Canceled = 130;
}
