using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Tracks a normalization run's counters and publishes progress, keeping the counting rules
/// in one place.
/// </summary>
/// <remarks>
/// Several files are normalized at once, so every counter update and the report describing it
/// happen under one lock. Publishing inside the lock costs a little concurrency but keeps the
/// reported counts monotonic, which a progress bar would otherwise show running backwards
/// when two workers finish at the same moment.
/// </remarks>
internal sealed class NormalizationProgressReporter(
    IProgress<NormalizationProgress>? progress,
    int totalFileCount)
{
    private readonly Lock _gate = new();
    private readonly List<NormalizationFailure> _failures = [];
    private int _normalizedCount;
    private int _skippedCount;

    public int NormalizedCount
    {
        get
        {
            lock (_gate)
            {
                return _normalizedCount;
            }
        }
    }

    public int SkippedCount
    {
        get
        {
            lock (_gate)
            {
                return _skippedCount;
            }
        }
    }

    /// <summary>Gets a snapshot of the failures recorded so far, in the order observed.</summary>
    public IReadOnlyList<NormalizationFailure> Failures
    {
        get
        {
            lock (_gate)
            {
                return _failures.ToArray();
            }
        }
    }

    /// <summary>Records a file that needed no work.</summary>
    public void ReportSkipped(string sourcePath) =>
        Publish(sourcePath, NormalizationAction.Skipped, skipped: 1);

    /// <summary>Records a file that finished encoding.</summary>
    public void ReportCompleted(string sourcePath) =>
        Publish(sourcePath, NormalizationAction.Completed, normalized: 1);

    /// <summary>Records a file the run could not normalize and moved past.</summary>
    public void ReportFailed(string sourcePath, string reason) =>
        Publish(
            sourcePath,
            NormalizationAction.Failed,
            failure: new NormalizationFailure(sourcePath, reason));

    /// <summary>Reports a step that does not change any counter.</summary>
    public void Report(string sourcePath, NormalizationAction action) =>
        Publish(sourcePath, action);

    private void Publish(
        string sourcePath,
        NormalizationAction action,
        int normalized = 0,
        int skipped = 0,
        NormalizationFailure? failure = null)
    {
        lock (_gate)
        {
            _normalizedCount += normalized;
            _skippedCount += skipped;
            if (failure is not null)
            {
                _failures.Add(failure);
            }

            progress?.Report(
                new NormalizationProgress(
                    totalFileCount,
                    _normalizedCount + _skippedCount + _failures.Count,
                    _normalizedCount,
                    _skippedCount,
                    _failures.Count,
                    sourcePath,
                    action));
        }
    }
}
