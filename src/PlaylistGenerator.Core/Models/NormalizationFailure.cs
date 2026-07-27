namespace PlaylistGenerator.Core.Models;

/// <summary>
/// One source file a normalization run could not produce output for, and why.
/// </summary>
/// <remarks>
/// A failure is recorded rather than thrown so that one unreadable or unsupported file does
/// not discard the work already done for the rest of a large library.
/// </remarks>
/// <param name="SourcePath">Absolute path of the file that could not be normalized.</param>
/// <param name="Reason">User-facing explanation, already free of stack-trace detail.</param>
public sealed record NormalizationFailure(string SourcePath, string Reason);
