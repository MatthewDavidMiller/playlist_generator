namespace PlaylistGenerator.Core.Exceptions;

/// <summary>
/// Base type for every failure this application reports to a user without a stack trace.
/// </summary>
public abstract class PlaylistGeneratorException : Exception
{
    protected PlaylistGeneratorException(string message)
        : base(message)
    {
    }

    protected PlaylistGeneratorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
