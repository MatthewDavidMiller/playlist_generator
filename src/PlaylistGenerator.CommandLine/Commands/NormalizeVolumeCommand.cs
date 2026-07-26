using PlaylistGenerator.CommandLine.Parsing;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.CommandLine.Commands;

/// <summary>
/// Creates loudness-normalized Opus copies of an audio folder.
/// </summary>
public sealed class NormalizeVolumeCommand : ICliCommand
{
    private const string SourceDirectoryOption = "--source-directory";
    private const string OutputDirectoryOption = "--output-directory";

    private readonly IAudioNormalizer _audioNormalizer;

    public NormalizeVolumeCommand(IAudioNormalizer audioNormalizer)
    {
        ArgumentNullException.ThrowIfNull(audioNormalizer);
        _audioNormalizer = audioNormalizer;
    }

    /// <inheritdoc />
    public string Name => "normalize-volume";

    /// <inheritdoc />
    public string Usage =>
        $"""
          playlist-generator {Name} {SourceDirectoryOption} <folder>
            {OutputDirectoryOption} <folder>
        """;

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var options = OptionParser.Parse(
            arguments,
            SourceDirectoryOption,
            OutputDirectoryOption);

        var result = await _audioNormalizer
            .NormalizeAsync(
                new NormalizationRequest(
                    options.Required(SourceDirectoryOption),
                    options.Required(OutputDirectoryOption)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await output
            .WriteLineAsync($"Normalized audio written to {result.OutputDirectory}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(ResultJson.Serialize(result)).ConfigureAwait(false);

        // A stopped run reports what it completed rather than throwing, so the interrupt
        // has to be turned back into the conventional exit code here.
        return result.Stopped && cancellationToken.IsCancellationRequested
            ? ExitCode.Canceled
            : ExitCode.Success;
    }
}
