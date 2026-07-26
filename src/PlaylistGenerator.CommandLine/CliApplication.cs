using PlaylistGenerator.CommandLine.Commands;
using PlaylistGenerator.CommandLine.Parsing;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Composition;
using PlaylistGenerator.Core.Exceptions;

namespace PlaylistGenerator.CommandLine;

/// <summary>
/// Dispatches a command line to a command and turns failures into exit codes.
/// </summary>
/// <remarks>
/// Writers are injected rather than using <see cref="Console"/> directly so the whole
/// surface, including its output, is testable in process.
/// </remarks>
public sealed class CliApplication
{
    private readonly IReadOnlyList<ICliCommand> _commands;
    private readonly ICliCommand _defaultCommand;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    /// <summary>Builds the standard command set over the given services.</summary>
    public CliApplication(
        IPlaylistGenerator playlistGenerator,
        IAudioNormalizer audioNormalizer,
        IFfmpegInstallAdvisor ffmpegInstallAdvisor,
        TextWriter output,
        TextWriter error)
        : this(
            [
                new GeneratePlaylistCommand(playlistGenerator),
                new NormalizeVolumeCommand(audioNormalizer),
                new InstallFfmpegCommand(ffmpegInstallAdvisor),
            ],
            output,
            error)
    {
    }

    /// <summary>Builds an application over an explicit command set.</summary>
    /// <exception cref="ArgumentException">No command is marked as the default.</exception>
    public CliApplication(
        IReadOnlyList<ICliCommand> commands,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        _commands = commands;
        _output = output;
        _error = error;
        _defaultCommand = commands.SingleOrDefault(command => command.Name is null)
            ?? throw new ArgumentException(
                "Exactly one command must be the default, with a null name.",
                nameof(commands));
    }

    /// <summary>Builds the standard command set from the shared composition root.</summary>
    public static CliApplication Create(CoreServices services, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new CliApplication(
            services.PlaylistGenerator,
            services.AudioNormalizer,
            services.FfmpegInstallAdvisor,
            output,
            error);
    }

    /// <summary>
    /// Runs the command line and returns the process exit code. This never throws; every
    /// failure becomes a code from <see cref="ExitCode"/>.
    /// </summary>
    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            if (arguments.Count == 0 || OptionParser.IsHelpRequest(arguments))
            {
                await _output.WriteLineAsync(BuildUsage()).ConfigureAwait(false);
                return ExitCode.Success;
            }

            var command = _commands.FirstOrDefault(
                candidate => string.Equals(candidate.Name, arguments[0], StringComparison.Ordinal));

            // An unrecognized first argument belongs to the default command, so
            // "--source-directory ..." keeps working without a command name.
            var commandArguments = command is null
                ? arguments
                : arguments.Skip(1).ToArray();
            command ??= _defaultCommand;

            if (OptionParser.IsHelpRequest(commandArguments))
            {
                await _output.WriteLineAsync($"Usage:{Environment.NewLine}{command.Usage}")
                    .ConfigureAwait(false);
                return ExitCode.Success;
            }

            return await command
                .ExecuteAsync(commandArguments, _output, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CliUsageException exception)
        {
            await _error.WriteLineAsync($"Error: {exception.Message}").ConfigureAwait(false);
            await _error.WriteLineAsync(BuildUsage()).ConfigureAwait(false);
            return ExitCode.UsageError;
        }
        catch (PlaylistGeneratorException exception)
        {
            // An expected failure gets a plain message; a stack trace would only be noise.
            await _error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCode.Failure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _error.WriteLineAsync("Operation canceled.").ConfigureAwait(false);
            return ExitCode.Canceled;
        }
        catch (Exception exception)
        {
            // An unexpected fault still needs a clean exit code, and its detail is worth
            // keeping because it indicates a defect rather than bad input.
            await _error.WriteLineAsync($"Unexpected error: {exception}").ConfigureAwait(false);
            return ExitCode.InternalError;
        }
    }

    private string BuildUsage()
    {
        var sections = _commands.Select(command => command.Usage);
        return $"""
            Usage:
            {string.Join($"{Environment.NewLine}{Environment.NewLine}", sections)}

            Run "playlist-generator <command> --help" for one command's usage.
            """;
    }
}
