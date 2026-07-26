using System.Text;
using PlaylistGenerator.Core.Exceptions;

namespace PlaylistGenerator.Core.Services;

internal static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8WithoutByteOrderMark = new(false);

    public static void WriteAllLines(string outputPath, IReadOnlyList<string> lines)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new PlaylistValidationException("Output path must include a file name.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                temporaryPath,
                string.Join(Environment.NewLine, lines) + Environment.NewLine,
                Utf8WithoutByteOrderMark);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new PlaylistIOException(
                $"Unable to write playlist to '{outputPath}': {exception.Message}",
                exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // The primary write result is more useful than a temporary-file cleanup error.
            }
            catch (UnauthorizedAccessException)
            {
                // The primary write result is more useful than a temporary-file cleanup error.
            }
        }
    }
}
