using Avalonia;
using Avalonia.Headless;
using PlaylistGenerator.Desktop;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Hosts the real <see cref="App"/> on Avalonia's headless platform.
/// </summary>
/// <remarks>
/// Using the production application means the styles and theme the views are written against
/// are the ones under test. The headless platform needs no display server, so these tests run
/// anywhere the rest of the suite does.
/// </remarks>
public static class HeadlessAppBuilder
{
    /// <summary>
    /// Mirrors <c>Program.BuildAvaloniaApp</c> apart from the platform.
    /// </summary>
    /// <remarks>
    /// The font matters. Without one registered, wrapped text of any length never converges
    /// on a line break and the layout pass runs forever, so a view holding a paragraph hangs
    /// the whole suite rather than failing. The application registers Inter, so the tests do
    /// too, and then measure text the way the shipped application measures it.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
