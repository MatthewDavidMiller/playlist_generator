using System.Text.Json;

namespace PlaylistGenerator.CommandLine.Parsing;

/// <summary>
/// Serializes command results for machine consumers.
/// </summary>
/// <remarks>
/// Snake-case names keep the emitted contract stable and script-friendly, independent of
/// the .NET property names.
/// </remarks>
internal static class ResultJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
