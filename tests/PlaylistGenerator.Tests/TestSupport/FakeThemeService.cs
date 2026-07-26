using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Counts toggles and reports a fixed theme name.
/// </summary>
public sealed class FakeThemeService : IThemeService
{
    public const string ThemeName = "Dark";

    public int ToggleCount { get; private set; }

    public string Toggle()
    {
        ToggleCount++;
        return ThemeName;
    }
}
