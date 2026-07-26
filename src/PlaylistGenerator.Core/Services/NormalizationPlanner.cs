using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Decides which files a normalization run must encode, before any encoding begins.
/// </summary>
/// <remarks>
/// Planning up front keeps the run resumable and lets a destination collision be reported
/// before any FFmpeg process starts, rather than halfway through a long library.
/// </remarks>
public static class NormalizationPlanner
{
    /// <summary>
    /// Builds the plan for <paramref name="audioFiles"/>, which must be absolute paths under
    /// <paramref name="fullSourceDirectory"/>.
    /// </summary>
    /// <exception cref="PlaylistValidationException">
    /// Two source files would produce the same normalized output path.
    /// </exception>
    public static NormalizationPlan Create(
        IReadOnlyList<string> audioFiles,
        string fullSourceDirectory,
        string fullOutputDirectory)
    {
        ArgumentNullException.ThrowIfNull(audioFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullSourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullOutputDirectory);

        var jobs = new List<NormalizationJob>(audioFiles.Count);
        var skipped = new List<string>();
        var destinations = new Dictionary<string, string>(audioFiles.Count, PathUtility.Comparer);

        // Files can only fall inside the output tree when that tree sits within the source
        // tree, so the per-file containment test is skipped entirely otherwise.
        var outputIsInsideSource =
            PathUtility.IsWithinFullDirectory(fullOutputDirectory, fullSourceDirectory);

        foreach (var sourcePath in audioFiles)
        {
            if (outputIsInsideSource
                && PathUtility.IsWithinFullDirectory(sourcePath, fullOutputDirectory))
            {
                skipped.Add(sourcePath);
                continue;
            }

            var relativePath = Path.GetRelativePath(fullSourceDirectory, sourcePath);
            var destination = Path.ChangeExtension(
                Path.Combine(fullOutputDirectory, relativePath),
                AudioFormats.NormalizedExtension);

            // An existing output makes the run resumable; a self-referential destination
            // would otherwise rewrite the source in place.
            if (PathUtility.AreSameFull(sourcePath, destination) || File.Exists(destination))
            {
                skipped.Add(sourcePath);
                continue;
            }

            if (destinations.TryGetValue(destination, out var existingSource))
            {
                throw new PlaylistValidationException(
                    "Multiple source files would write to the same normalized output path "
                    + $"'{destination}': '{existingSource}' and '{sourcePath}'.");
            }

            destinations.Add(destination, sourcePath);
            jobs.Add(new NormalizationJob(sourcePath, destination));
        }

        return new NormalizationPlan(jobs, skipped);
    }
}
