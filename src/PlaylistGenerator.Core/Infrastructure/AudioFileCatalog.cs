using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Infrastructure;

/// <summary>
/// Scans a directory tree for supported audio files.
/// </summary>
public sealed class AudioFileCatalog : IAudioFileCatalog
{
    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
        IgnoreInaccessible = false,

        // Skipping reparse points keeps a symbolic-link cycle from making the scan
        // unbounded, and keeps linked trees from silently joining the library.
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <inheritdoc />
    /// <exception cref="PlaylistValidationException">The directory is missing or unnamed.</exception>
    /// <exception cref="PlaylistIOException">The tree could not be read.</exception>
    public IReadOnlyList<string> Scan(string sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new PlaylistValidationException("Source directory is required.");
        }

        var sourcePath = PathUtility.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourcePath))
        {
            throw new PlaylistValidationException(
                $"Source directory '{sourceDirectory}' does not exist.");
        }

        try
        {
            var files = new List<string>();
            foreach (var path in Directory.EnumerateFiles(sourcePath, "*", ScanOptions))
            {
                // Enumerating from an absolute, normalized root yields absolute, normalized
                // results, so no per-file re-normalization is needed here.
                if (AudioFormats.IsSupported(path.AsSpan()))
                {
                    files.Add(path);
                }
            }

            // Sorting in place avoids copying the whole library into a second array.
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PlaylistIOException(
                $"Unable to scan '{sourcePath}' for audio files: {exception.Message}",
                exception);
        }
    }
}
