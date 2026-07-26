using PlaylistGenerator.CommandLine.Parsing;
using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.CommandLine.Commands;

/// <summary>
/// Shows a platform-appropriate FFmpeg installation command.
/// </summary>
/// <remarks>
/// The command is only printed. Nothing here installs software or elevates privileges.
/// </remarks>
public sealed class InstallFfmpegCommand : ICliCommand
{
    private readonly IFfmpegInstallAdvisor _advisor;

    public InstallFfmpegCommand(IFfmpegInstallAdvisor advisor)
    {
        ArgumentNullException.ThrowIfNull(advisor);
        _advisor = advisor;
    }

    /// <inheritdoc />
    public string Name => "install-ffmpeg";

    /// <inheritdoc />
    public string Usage => $"  playlist-generator {Name}";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count > 0)
        {
            throw new CliUsageException($"{Name} does not accept options.");
        }

        var plan = _advisor.GetPlan();
        await output.WriteLineAsync(plan.Message).ConfigureAwait(false);
        if (plan.Command.Count > 0)
        {
            await output
                .WriteLineAsync($"Command: {FormatCommand(plan.Command)}")
                .ConfigureAwait(false);
        }

        // A missing dependency is a failure for a script that is checking for it.
        return plan.IsInstalled ? ExitCode.Success : ExitCode.Failure;
    }

    /// <summary>
    /// Renders the advice as a copyable shell command, quoting only what needs it.
    /// </summary>
    private static string FormatCommand(IEnumerable<string> command) =>
        string.Join(
            ' ',
            command.Select(
                argument => argument.Any(char.IsWhiteSpace)
                    ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                    : argument));
}
