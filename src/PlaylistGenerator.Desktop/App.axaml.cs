using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlaylistGenerator.Core.Composition;
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
            var services = CoreServices.CreateDefault();
            var window = new MainWindow();
            var viewModel = new MainViewModel(
                services.PlaylistGenerator,
                services.AudioNormalizer,

                // Resolved lazily so the picker always uses the live window rather than
                // capturing one that may not have a storage provider yet.
                new AvaloniaFilePickerService(() => window),
                new AvaloniaThemeService());

            window.DataContext = viewModel;
            desktop.Exit += (_, _) => viewModel.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
