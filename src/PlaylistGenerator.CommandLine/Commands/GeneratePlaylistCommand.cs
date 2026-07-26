using PlaylistGenerator.CommandLine.Parsing;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.CommandLine.Commands;

/// <summary>
/// Builds a shuffled interval playlist. This is the command used when no name is given.
/// </summary>
public sealed class GeneratePlaylistCommand : ICliCommand
{
    private const string SourceDirectoryOption = "--source-directory";
    private const string SpecialFileOption = "--special-file";
    private const string InsertEveryOption = "--insert-every";
    private const string OutputPathOption = "--output-path";

    private readonly IPlaylistGenerator _playlistGenerator;

    public GeneratePlaylistCommand(IPlaylistGenerator playlistGenerator)
    {
        ArgumentNullException.ThrowIfNull(playlistGenerator);
        _playlistGenerator = playlistGenerator;
    }

    /// <inheritdoc />
    public string? Name => null;

    /// <inheritdoc />
    public string Usage =>
        $"""
          playlist-generator {SourceDirectoryOption} <folder> {SpecialFileOption} <file>
            {InsertEveryOption} <count> {OutputPathOption} <playlist.m3u8>
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
            SpecialFileOption,
            InsertEveryOption,
            OutputPathOption);

        var request = new PlaylistRequest(
            options.Required(SourceDirectoryOption),
            options.Required(SpecialFileOption),
            options.RequiredInteger(InsertEveryOption),
            options.Required(OutputPathOption));

        var result = _playlistGenerator.Generate(request);

        await output
            .WriteLineAsync($"Playlist written to {result.OutputPath}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(ResultJson.Serialize(result)).ConfigureAwait(false);
        return ExitCode.Success;
    }
}
