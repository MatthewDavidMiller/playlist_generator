using System.Globalization;

namespace PlaylistGenerator.CommandLine.Parsing;

/// <summary>
/// The options parsed from one command's arguments, with typed accessors.
/// </summary>
public sealed class OptionValues
{
    private readonly IReadOnlyDictionary<string, string> _values;

    internal OptionValues(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>Returns the value of a required option.</summary>
    /// <exception cref="CliUsageException">The option is absent or blank.</exception>
    public string Required(string option)
    {
        if (!_values.TryGetValue(option, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"Option '{option}' is required.");
        }

        return value;
    }

    /// <summary>Returns the value of a required option parsed as an integer.</summary>
    /// <exception cref="CliUsageException">The option is absent, blank, or not an integer.</exception>
    public int RequiredInteger(string option)
    {
        var text = Required(option);

        // Invariant parsing keeps the same command line valid under every locale.
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new CliUsageException($"{option} must be an integer.");
        }

        return value;
    }
}
