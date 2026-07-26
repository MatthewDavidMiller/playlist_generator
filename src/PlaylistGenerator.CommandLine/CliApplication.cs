using System.Globalization;
using System.Text.Json;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.CommandLine;

public sealed class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IPlaylistGenerator _playlistGenerator;
    private readonly IAudioNormalizer _audioNormalizer;
    private readonly IFfmpegInstallAdvisor _ffmpegInstallAdvisor;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CliApplication(
        IPlaylistGenerator playlistGenerator,
        IAudioNormalizer audioNormalizer,
        IFfmpegInstallAdvisor ffmpegInstallAdvisor,
        TextWriter output,
        TextWriter error)
    {
        _playlistGenerator = playlistGenerator;
        _audioNormalizer = audioNormalizer;
        _ffmpegInstallAdvisor = ffmpegInstallAdvisor;
        _output = output;
        _error = error;
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            if (arguments.Count == 0 || arguments[0] is "--help" or "-h")
            {
                await _output.WriteLineAsync(Usage).ConfigureAwait(false);
                return 0;
            }

            return arguments[0] switch
            {
                "normalize-volume" => await NormalizeAsync(
                        arguments.Skip(1).ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false),
                "install-ffmpeg" => await ShowFfmpegInstallPlanAsync(
                        arguments.Skip(1).ToArray())
                    .ConfigureAwait(false),
                _ => await GeneratePlaylistAsync(arguments.ToArray()).ConfigureAwait(false),
            };
        }
        catch (CliUsageException exception)
        {
            await _error.WriteLineAsync($"Error: {exception.Message}").ConfigureAwait(false);
            await _error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }
        catch (PlaylistGeneratorException exception)
        {
            await _error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _error.WriteLineAsync("Operation canceled.").ConfigureAwait(false);
            return 130;
        }
    }

    private async Task<int> GeneratePlaylistAsync(string[] arguments)
    {
        var options = ParseOptions(
            arguments,
            "--source-directory",
            "--special-file",
            "--insert-every",
            "--output-path");
        var insertEveryText = Required(options, "--insert-every");

        if (!int.TryParse(
                insertEveryText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var insertEvery))
        {
            throw new CliUsageException("--insert-every must be an integer.");
        }

        var result = _playlistGenerator.Generate(
            new PlaylistRequest(
                Required(options, "--source-directory"),
                Required(options, "--special-file"),
                insertEvery,
                Required(options, "--output-path")));

        await _output
            .WriteLineAsync($"Playlist written to {result.OutputPath}")
            .ConfigureAwait(false);
        await _output
            .WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions))
            .ConfigureAwait(false);
        return 0;
    }

    private async Task<int> NormalizeAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var options = ParseOptions(
            arguments,
            "--source-directory",
            "--output-directory");
        var result = await _audioNormalizer
            .NormalizeAsync(
                new NormalizationRequest(
                    Required(options, "--source-directory"),
                    Required(options, "--output-directory")),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _output
            .WriteLineAsync($"Normalized audio written to {result.OutputDirectory}")
            .ConfigureAwait(false);
        await _output
            .WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions))
            .ConfigureAwait(false);
        return result.Stopped && cancellationToken.IsCancellationRequested ? 130 : 0;
    }

    private async Task<int> ShowFfmpegInstallPlanAsync(string[] arguments)
    {
        if (arguments.Length > 0)
        {
            if (arguments.Length == 1 && arguments[0] is "--help" or "-h")
            {
                await _output
                    .WriteLineAsync(
                        "Usage: playlist-generator install-ffmpeg\n"
                        + "Shows a safe, platform-appropriate installation command.")
                    .ConfigureAwait(false);
                return 0;
            }

            throw new CliUsageException("install-ffmpeg does not accept options.");
        }

        var plan = _ffmpegInstallAdvisor.GetPlan();
        await _output.WriteLineAsync(plan.Message).ConfigureAwait(false);
        if (plan.Command.Count > 0)
        {
            await _output
                .WriteLineAsync($"Command: {FormatCommand(plan.Command)}")
                .ConfigureAwait(false);
        }

        return plan.IsInstalled ? 0 : 1;
    }

    private static Dictionary<string, string> ParseOptions(
        string[] arguments,
        params string[] allowedOptions)
    {
        var allowed = allowedOptions.ToHashSet(StringComparer.Ordinal);
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < arguments.Length; index += 2)
        {
            var option = arguments[index];
            if (option is "--help" or "-h")
            {
                throw new CliUsageException("Place --help before the command for full usage.");
            }

            if (!allowed.Contains(option))
            {
                throw new CliUsageException($"Unknown option '{option}'.");
            }

            if (index + 1 >= arguments.Length)
            {
                throw new CliUsageException($"Option '{option}' requires a value.");
            }

            if (!parsed.TryAdd(option, arguments[index + 1]))
            {
                throw new CliUsageException($"Option '{option}' was provided more than once.");
            }
        }

        return parsed;
    }

    private static string Required(Dictionary<string, string> options, string option)
    {
        if (!options.TryGetValue(option, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"Option '{option}' is required.");
        }

        return value;
    }

    private static string FormatCommand(IEnumerable<string> command) =>
        string.Join(
            ' ',
            command.Select(
                argument => argument.Any(char.IsWhiteSpace)
                    ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                    : argument));

    private const string Usage =
        """
        Usage:
          playlist-generator --source-directory <folder> --special-file <file>
            --insert-every <count> --output-path <playlist.m3u8>

          playlist-generator normalize-volume --source-directory <folder>
            --output-directory <folder>

          playlist-generator install-ffmpeg
        """;

    private sealed class CliUsageException : Exception
    {
        public CliUsageException(string message)
            : base(message)
        {
        }
    }
}
