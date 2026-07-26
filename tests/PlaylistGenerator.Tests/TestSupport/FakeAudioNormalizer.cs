using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Records the request it receives and returns a fixed summary, unless a handler drives it.
/// </summary>
public sealed class FakeAudioNormalizer : IAudioNormalizer
{
    public const int NormalizedFileCount = 2;

    public const int SkippedFileCount = 1;

    public NormalizationRequest? Request { get; private set; }

    public Func<
        NormalizationRequest,
        IProgress<NormalizationProgress>?,
        IPauseSignal?,
        CancellationToken,
        Task<NormalizationResult>>? Handler
    { get; set; }

    public Task<NormalizationResult> NormalizeAsync(
        NormalizationRequest request,
        IProgress<NormalizationProgress>? progress = null,
        IPauseSignal? pauseSignal = null,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        return Handler?.Invoke(request, progress, pauseSignal, cancellationToken)
            ?? Task.FromResult(
                new NormalizationResult(
                    request.SourceDirectory,
                    request.OutputDirectory,
                    NormalizedFileCount,
                    SkippedFileCount,
                    false));
    }
}
