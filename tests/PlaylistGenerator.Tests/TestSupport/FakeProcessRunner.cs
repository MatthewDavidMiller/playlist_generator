using PlaylistGenerator.Core.Abstractions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Stands in for FFmpeg, so the suite exercises the normalization workflow without
/// requiring FFmpeg to be installed.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public const string ValidAnalysisJson =
        """
        {
          "input_i": "-18.42",
          "input_tp": "-2.10",
          "input_lra": "4.70",
          "input_thresh": "-28.54",
          "target_offset": "0.12"
        }
        """;

    /// <summary>Arguments of every invocation, in order.</summary>
    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>Executable paths of every invocation, in order.</summary>
    public List<string> Executables { get; } = [];

    public int AnalysisExitCode { get; set; }

    public int EncodeExitCode { get; set; }

    /// <summary>Whether a successful encode actually produces its output file.</summary>
    public bool CreateEncodedFile { get; set; } = true;

    public string AnalysisOutput { get; set; } = ValidAnalysisJson;

    /// <summary>Overrides the default behavior entirely when set.</summary>
    public Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>>? Handler
    {
        get;
        set;
    }

    /// <summary>The analysis pass is the one that writes to the null muxer.</summary>
    public static bool IsAnalysisCall(IReadOnlyList<string> arguments) => arguments[^1] == "-";

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        Executables.Add(executable);
        Calls.Add(arguments.ToArray());

        if (Handler is not null)
        {
            return await Handler(arguments, cancellationToken).ConfigureAwait(false);
        }

        if (IsAnalysisCall(arguments))
        {
            return new ProcessResult(AnalysisExitCode, string.Empty, AnalysisOutput);
        }

        if (CreateEncodedFile && EncodeExitCode == 0)
        {
            await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], cancellationToken)
                .ConfigureAwait(false);
        }

        return new ProcessResult(
            EncodeExitCode,
            string.Empty,
            EncodeExitCode == 0 ? string.Empty : "encoding failed");
    }
}
