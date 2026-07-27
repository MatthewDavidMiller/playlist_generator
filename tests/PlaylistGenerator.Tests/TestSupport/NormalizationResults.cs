using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Builds normalization summaries for tests that only care about one or two of the fields.
/// </summary>
/// <remarks>
/// Keeping construction here means a change to the result contract updates one call site
/// rather than every test that happens to need a summary.
/// </remarks>
internal static class NormalizationResults
{
    public static NormalizationResult Create(
        string sourceDirectory = "source",
        string outputDirectory = "output",
        int normalizedFileCount = 0,
        int skippedFileCount = 0,
        IReadOnlyList<NormalizationFailure>? failures = null,
        bool stopped = false) =>
        new(
            sourceDirectory,
            outputDirectory,
            normalizedFileCount,
            skippedFileCount,
            failures ?? [],
            stopped);
}
