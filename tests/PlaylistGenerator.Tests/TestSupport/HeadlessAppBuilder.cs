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
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
