using System.Text.Json;
using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Core.Services;

public static class LoudnessJsonParser
{
    private static readonly string[] RequiredProperties =
    [
        "input_i",
        "input_tp",
        "input_lra",
        "input_thresh",
        "target_offset",
    ];

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

                var values = RequiredProperties.ToDictionary(
                    property => property,
                    property => ReadRequiredValue(root, property, sourcePath),
                    StringComparer.Ordinal);

                return new LoudnessStats(
                    values["input_i"],
                    values["input_tp"],
                    values["input_lra"],
                    values["input_thresh"],
                    values["target_offset"]);
            }
            catch (JsonException)
            {
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
        if (!root.TryGetProperty(propertyName, out var value))
        {
            throw new PlaylistIOException(
                $"FFmpeg loudness analysis for '{sourcePath}' is missing '{propertyName}'.");
        }

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
