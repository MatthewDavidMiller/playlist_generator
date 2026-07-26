namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// Discovers supported audio files beneath a directory.
/// </summary>
public interface IAudioFileCatalog
{
    /// <summary>
    /// Returns absolute paths to every supported audio file under
    /// <paramref name="sourceDirectory"/>, in a stable order.
    /// </summary>
    IReadOnlyList<string> Scan(string sourceDirectory);
}
