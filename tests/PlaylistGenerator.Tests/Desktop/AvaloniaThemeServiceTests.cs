using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using PlaylistGenerator.Desktop.Services;

namespace PlaylistGenerator.Tests.Desktop;

public sealed class AvaloniaThemeServiceTests
{
    [AvaloniaFact]
    public void TogglingAlternatesBetweenTheDarkAndLightVariants()
    {
        var application = Application.Current!;
        var original = application.RequestedThemeVariant;
        var service = new AvaloniaThemeService();

        try
        {
            application.RequestedThemeVariant = ThemeVariant.Light;

            Assert.Equal("Dark", service.Toggle());
            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);

            Assert.Equal("Light", service.Toggle());
            Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);
        }
        finally
        {
            // The application is shared by every headless test in this assembly.
            application.RequestedThemeVariant = original;
        }
    }
}
