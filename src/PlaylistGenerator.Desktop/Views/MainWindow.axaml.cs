using Avalonia.Controls;
using PlaylistGenerator.Presentation.ViewModels;

namespace PlaylistGenerator.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as MainViewModel)?.CancelOperations();
    }
}
