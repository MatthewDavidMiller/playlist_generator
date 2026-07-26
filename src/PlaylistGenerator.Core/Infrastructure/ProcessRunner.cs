using System.ComponentModel;
using System.Diagnostics;
using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Infrastructure;

/// <summary>
/// Runs external processes with arguments passed as discrete values.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    /// <exception cref="PlaylistIOException">The process could not be started or run.</exception>
    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,

            // Bypassing the shell means no argument string is ever parsed, so paths
            // containing spaces or quotes cannot change the command's meaning.
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new PlaylistIOException($"Unable to start '{executable}'.");
            }

            // Both streams must be drained concurrently with the wait, or a process that
            // fills a pipe buffer would block forever.
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return new ProcessResult(
                    process.ExitCode,
                    await standardOutput.ConfigureAwait(false),
                    await standardError.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

                // The reader tasks fault when the token trips. Observing them keeps the
                // failures from surfacing later as unobserved task exceptions.
                await ObserveAsync(standardOutput).ConfigureAwait(false);
                await ObserveAsync(standardError).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new PlaylistIOException(
                $"Unable to run '{executable}': {exception.Message}",
                exception);
        }
    }

    private static async Task ObserveAsync(Task<string> readTask)
    {
        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Cancellation is the reported outcome; a truncated stream adds nothing.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and the kill request.
        }
        catch (SystemException)
        {
            // Cancellation remains the primary result even if the OS rejects termination.
        }
    }
}
