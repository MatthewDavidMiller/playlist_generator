using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Desktop.Services;
using PlaylistGenerator.Desktop.Views;
using PlaylistGenerator.Presentation.ViewModels;

namespace PlaylistGenerator.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var catalog = new AudioFileCatalog();
            var locator = new ExecutableLocator();
            var window = new MainWindow();
            var viewModel = new MainViewModel(
                new PlaylistGeneratorService(catalog, new RandomTrackShuffler()),
                new AudioNormalizationService(catalog, locator, new ProcessRunner()),
                new AvaloniaFilePickerService(() => window),
                new AvaloniaThemeService());

            window.DataContext = viewModel;
            desktop.Exit += (_, _) => viewModel.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
