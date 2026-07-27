using System.Reflection;

namespace PlaylistGenerator.Presentation.ViewModels;

/// <summary>
/// What the application is, who owns it, and the terms it is distributed under.
/// </summary>
/// <remarks>
/// Everything here is fixed for the life of a build, so the type is plain and immutable
/// rather than observable. The licence text and the copyright holder are read from the
/// repository's <c>LICENSE</c>, embedded at build time, so a published binary carries the
/// notice its licence requires and there is only one copy of that text to keep current.
/// </remarks>
public sealed class AboutViewModel
{
    /// <summary>The project's public home.</summary>
    public const string ProjectAddress =
        "https://github.com/MatthewDavidMiller/playlist_generator";

    private const string LicenseResourceName = "PlaylistGenerator.Presentation.LICENSE";

    public AboutViewModel()
    {
        LicenseText = ReadLicense();
        Copyright = ReadCopyright(LicenseText);
    }

    /// <summary>The application's display name.</summary>
    public string ApplicationName { get; } = "Playlist Generator";

    /// <summary>The version this build was produced from.</summary>
    public string Version { get; } = ReadVersion();

    /// <summary>The copyright line, taken from the licence.</summary>
    public string Copyright { get; }

    /// <summary>The name of the licence, for the heading above its text.</summary>
    public string LicenseName { get; } = "MIT License";

    /// <summary>The full licence text.</summary>
    public string LicenseText { get; }

    /// <summary>The repository address, as a link the view can follow.</summary>
    public Uri ProjectUrl { get; } = new(ProjectAddress);

    private static string ReadLicense()
    {
        using var stream = typeof(AboutViewModel).Assembly
            .GetManifestResourceStream(LicenseResourceName);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    /// <summary>
    /// Returns the licence's copyright line, or nothing if it has none. The line is shown
    /// on its own for prominence; the licence text below it carries the notice regardless,
    /// so failing to find it costs emphasis rather than compliance.
    /// </summary>
    private static string ReadCopyright(string licenseText) =>
        licenseText
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("Copyright", StringComparison.Ordinal))
        ?? string.Empty;

    /// <summary>
    /// Reads the version this assembly was built with, without any build metadata a source
    /// link would append after a plus sign.
    /// </summary>
    private static string ReadVersion()
    {
        var informational = typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return string.Empty;
        }

        var metadata = informational.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? informational : informational[..metadata];
    }
}
