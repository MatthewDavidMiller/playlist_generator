using Avalonia;
using Avalonia.Styling;
using PlaylistGenerator.Presentation.Services;

namespace PlaylistGenerator.Desktop.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    public string Toggle()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("The application is not ready.");
        var useLightTheme = application.ActualThemeVariant == ThemeVariant.Dark;
        application.RequestedThemeVariant = useLightTheme
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        return useLightTheme ? "Light" : "Dark";
    }
}
