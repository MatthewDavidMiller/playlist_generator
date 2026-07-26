using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// Creates loudness-normalized copies of an audio library without changing the source.
/// </summary>
public interface IAudioNormalizer
{
    /// <summary>
    /// Normalizes every supported file under the request's source directory.
    /// </summary>
    /// <param name="progress">Receives per-file progress reports, if supplied.</param>
    /// <param name="pauseSignal">Observed between steps so a run can be held, if supplied.</param>
    /// <returns>
    /// A summary of the run. Cancellation stops the run and reports partial counts rather
    /// than throwing.
    /// </returns>
    Task<NormalizationResult> NormalizeAsync(
        NormalizationRequest request,
        IProgress<NormalizationProgress>? progress = null,
        IPauseSignal? pauseSignal = null,
        CancellationToken cancellationToken = default);
}
