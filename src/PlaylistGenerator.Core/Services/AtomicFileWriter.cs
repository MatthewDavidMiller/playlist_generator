using System.Text;
using PlaylistGenerator.Core.Exceptions;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Writes a text file so that a failure never replaces good output with a partial file.
/// </summary>
/// <remarks>
/// Content goes to a temporary file in the destination directory and is then moved into
/// place. A same-directory move is atomic on every supported filesystem, so readers see
/// either the previous file or the complete new one.
/// </remarks>
internal static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8WithoutByteOrderMark = new(false);

    /// <exception cref="PlaylistIOException">The file could not be written.</exception>
    public static void WriteAllLines(string outputPath, IReadOnlyList<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(lines);

        // A bare file name yields an empty directory, which means the current directory.
        var directory = Path.GetDirectoryName(outputPath) is { Length: > 0 } parent
            ? parent
            : Directory.GetCurrentDirectory();
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);

            // Streaming keeps a large library from materializing the whole playlist as one
            // string before any of it reaches disk.
            using (var writer = new StreamWriter(temporaryPath, false, Utf8WithoutByteOrderMark))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
        {
            throw new PlaylistIOException(
                $"Unable to write playlist to '{outputPath}': {exception.Message}",
                exception);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The primary write result is more useful than a temporary-file cleanup error.
        }
    }
}
