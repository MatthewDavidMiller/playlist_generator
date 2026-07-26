namespace PlaylistGenerator.Core.Exceptions;

/// <summary>
/// Reports a filesystem or external-process failure encountered while producing output.
/// </summary>
public sealed class PlaylistIOException : PlaylistGeneratorException
{
    public PlaylistIOException(string message)
        : base(message)
    {
    }

    public PlaylistIOException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
