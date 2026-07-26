using System.Collections.Frozen;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;

namespace PlaylistGenerator.Core.Infrastructure;

public sealed class AudioFileCatalog : IAudioFileCatalog
{
    public static readonly FrozenSet<string> SupportedExtensions =
        new[]
        {
            ".mp3",
            ".flac",
            ".wav",
            ".m4a",
            ".aac",
            ".ogg",
            ".opus",
            ".wma",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

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
            return Directory
                .EnumerateFiles(sourcePath, "*", ScanOptions)
                .Where(IsSupported)
                .Select(Path.GetFullPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PlaylistIOException(
                $"Unable to scan '{sourcePath}' for audio files: {exception.Message}",
                exception);
        }
    }

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));
}
