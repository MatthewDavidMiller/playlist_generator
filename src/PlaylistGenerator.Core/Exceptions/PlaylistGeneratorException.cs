namespace PlaylistGenerator.Core.Exceptions;

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

public sealed class PlaylistValidationException : PlaylistGeneratorException
{
    public PlaylistValidationException(string message)
        : base(message)
    {
    }
}

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
