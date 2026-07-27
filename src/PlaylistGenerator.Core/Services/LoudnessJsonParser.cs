using System.Text.Json;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

/// <summary>
/// Extracts loudness measurements from FFmpeg's <c>loudnorm</c> analysis output.
/// </summary>
/// <remarks>
/// FFmpeg prints the JSON summary among ordinary log lines, so the parser scans backwards
/// for the last well-formed object rather than assuming the whole stream is JSON.
/// </remarks>
public static class LoudnessJsonParser
{
    /// <exception cref="PlaylistIOException">No usable analysis object was present.</exception>
    public static LoudnessStats Parse(string output, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(output);

        return TryParse(output, sourcePath, out var stats, out var foundObjectSyntax)
            ? stats
            : throw NotFound(foundObjectSyntax, sourcePath);
    }

    /// <summary>
    /// Parses the stream FFmpeg normally prints to, falling back to the other one.
    /// </summary>
    /// <remarks>
    /// Which stream carries the summary has changed between FFmpeg versions. Trying them in
    /// turn avoids joining two potentially large logs into a third copy just to search it.
    /// </remarks>
    /// <exception cref="PlaylistIOException">Neither stream held a usable analysis object.</exception>
    public static LoudnessStats Parse(
        string primaryOutput,
        string fallbackOutput,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(primaryOutput);
        ArgumentNullException.ThrowIfNull(fallbackOutput);

        if (TryParse(primaryOutput, sourcePath, out var stats, out var foundInPrimary))
        {
            return stats;
        }

        if (TryParse(fallbackOutput, sourcePath, out stats, out var foundInFallback))
        {
            return stats;
        }

        throw NotFound(foundInPrimary || foundInFallback, sourcePath);
    }

    /// <summary>
    /// Scans <paramref name="output"/> backwards for the last parsable object.
    /// </summary>
    /// <param name="foundObjectSyntax">
    /// Whether anything object-shaped was present at all, which separates "FFmpeg printed
    /// nothing usable" from "FFmpeg printed something malformed".
    /// </param>
    private static bool TryParse(
        string output,
        string sourcePath,
        out LoudnessStats stats,
        out bool foundObjectSyntax)
    {
        foundObjectSyntax = false;
        stats = null!;

        var objectEnd = output.LastIndexOf('}');
        while (objectEnd >= 0)
        {
            var objectStart = output.LastIndexOf('{', objectEnd);
            if (objectStart < 0)
            {
                return false;
            }

            foundObjectSyntax = true;
            try
            {
                using var document = JsonDocument.Parse(
                    output.AsMemory(objectStart, objectEnd - objectStart + 1));

                // Argument evaluation is left to right, so the first missing field is the
                // one reported. A present-but-unusable object is a hard failure rather than
                // a reason to keep scanning, because it is the analysis FFmpeg produced.
                stats = new LoudnessStats(
                    ReadRequiredValue(document.RootElement, "input_i", sourcePath),
                    ReadRequiredValue(document.RootElement, "input_tp", sourcePath),
                    ReadRequiredValue(document.RootElement, "input_lra", sourcePath),
                    ReadRequiredValue(document.RootElement, "input_thresh", sourcePath),
                    ReadRequiredValue(document.RootElement, "target_offset", sourcePath));
                return true;
            }
            catch (JsonException)
            {
                // A closing brace at index 0 cannot have an opening brace before it, so the
                // search above has already returned by the time the index would underflow.
                objectEnd = output.LastIndexOf('}', objectEnd - 1);
            }
        }

        return false;
    }

    private static PlaylistIOException NotFound(bool foundObjectSyntax, string sourcePath) =>
        new(
            $"FFmpeg returned {(foundObjectSyntax ? "malformed" : "missing")} loudness "
            + $"analysis JSON for '{sourcePath}'.");

    private static string ReadRequiredValue(
        JsonElement root,
        string propertyName,
        string sourcePath)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(propertyName, out var value))
        {
            throw new PlaylistIOException(
                $"FFmpeg loudness analysis for '{sourcePath}' is missing '{propertyName}'.");
        }

        // FFmpeg has emitted these as quoted strings and as bare numbers across versions.
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new PlaylistIOException(
                $"FFmpeg loudness analysis for '{sourcePath}' has no value for '{propertyName}'.");
        }

        return text;
    }
}
