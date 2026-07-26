namespace PlaylistGenerator.Core.Exceptions;

/// <summary>
/// Reports input that cannot produce a valid result, such as a missing directory.
/// </summary>
public sealed class PlaylistValidationException : PlaylistGeneratorException
{
    public PlaylistValidationException(string message)
        : base(message)
    {
    }

    public PlaylistValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
