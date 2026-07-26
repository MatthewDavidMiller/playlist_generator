using PlaylistGenerator.Core.Abstractions;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Resolves executables from a fixed map, so tests never depend on the machine's PATH.
/// </summary>
public sealed class FakeExecutableLocator : IExecutableLocator
{
    private readonly Dictionary<string, string> _executables;

    /// <summary>Creates a locator that finds only FFmpeg, at the given path.</summary>
    public FakeExecutableLocator(string? ffmpeg = "ffmpeg")
    {
        _executables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ffmpeg is not null)
        {
            _executables["ffmpeg"] = ffmpeg;
        }
    }

    /// <summary>
    /// Creates a locator over an explicit executable map. This is a factory rather than a
    /// constructor so that <c>new FakeExecutableLocator(null)</c> stays unambiguous.
    /// </summary>
    public static FakeExecutableLocator ForExecutables(
        IReadOnlyDictionary<string, string> executables)
    {
        var locator = new FakeExecutableLocator(null);
        foreach (var (name, path) in executables)
        {
            locator._executables[name] = path;
        }

        return locator;
    }

    public List<string> Requested { get; } = [];

    public string? Find(string executableName)
    {
        Requested.Add(executableName);
        return _executables.GetValueOrDefault(executableName);
    }
}
