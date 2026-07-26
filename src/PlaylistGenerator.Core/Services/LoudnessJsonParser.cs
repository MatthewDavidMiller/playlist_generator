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

        var objectEnd = output.LastIndexOf('}');
        var foundObjectSyntax = false;

        while (objectEnd >= 0)
        {
            var objectStart = output.LastIndexOf('{', objectEnd);
            if (objectStart < 0)
            {
                break;
            }

            foundObjectSyntax = true;
            try
            {
                using var document = JsonDocument.Parse(
                    output.AsMemory(objectStart, objectEnd - objectStart + 1));
                var root = document.RootElement;

                // Argument evaluation is left to right, so the first missing field is the
                // one reported.
                return new LoudnessStats(
                    ReadRequiredValue(root, "input_i", sourcePath),
                    ReadRequiredValue(root, "input_tp", sourcePath),
                    ReadRequiredValue(root, "input_lra", sourcePath),
                    ReadRequiredValue(root, "input_thresh", sourcePath),
                    ReadRequiredValue(root, "target_offset", sourcePath));
            }
            catch (JsonException)
            {
                if (objectEnd == 0)
                {
                    break;
                }

                objectEnd = output.LastIndexOf('}', objectEnd - 1);
            }
        }

        var reason = foundObjectSyntax ? "malformed" : "missing";
        throw new PlaylistIOException(
            $"FFmpeg returned {reason} loudness analysis JSON for '{sourcePath}'.");
    }

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
