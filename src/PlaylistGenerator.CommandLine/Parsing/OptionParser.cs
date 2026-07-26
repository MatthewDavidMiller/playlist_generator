namespace PlaylistGenerator.CommandLine.Parsing;

/// <summary>
/// Parses <c>--option value</c> pairs against a fixed set of allowed options.
/// </summary>
public static class OptionParser
{
    /// <summary>Flags that request help rather than an operation.</summary>
    public static readonly string[] HelpFlags = ["--help", "-h"];

    /// <summary>Returns whether <paramref name="arguments"/> is a lone request for help.</summary>
    public static bool IsHelpRequest(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 && HelpFlags.Contains(arguments[0], StringComparer.Ordinal);

    /// <summary>
    /// Parses <paramref name="arguments"/>, rejecting unknown, repeated, valueless, and
    /// option-shaped inputs.
    /// </summary>
    /// <exception cref="CliUsageException">The arguments could not be interpreted.</exception>
    public static OptionValues Parse(
        IReadOnlyList<string> arguments,
        params string[] allowedOptions)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(allowedOptions);

        var allowed = allowedOptions.ToHashSet(StringComparer.Ordinal);
        var parsed = new Dictionary<string, string>(allowedOptions.Length, StringComparer.Ordinal);

        for (var index = 0; index < arguments.Count; index += 2)
        {
            var option = arguments[index];
            if (HelpFlags.Contains(option, StringComparer.Ordinal))
            {
                throw new CliUsageException(
                    "Use --help on its own, or after the command name, to see usage.");
            }

            if (!allowed.Contains(option))
            {
                throw new CliUsageException($"Unknown option '{option}'.");
            }

            if (index + 1 >= arguments.Count)
            {
                throw new CliUsageException($"Option '{option}' requires a value.");
            }

            var value = arguments[index + 1];

            // A forgotten value would otherwise consume the next option silently, as in
            // "--source-directory --insert-every 4".
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliUsageException(
                    $"Option '{option}' requires a value, but '{value}' looks like an option.");
            }

            if (!parsed.TryAdd(option, value))
            {
                throw new CliUsageException($"Option '{option}' was provided more than once.");
            }
        }

        return new OptionValues(parsed);
    }
}
