using Avalonia;
using Avalonia.Controls;
using PlaylistGenerator.Presentation.Layout;
using PlaylistGenerator.Presentation.ViewModels;

namespace PlaylistGenerator.Desktop.Views;

/// <summary>
/// The application window.
/// </summary>
/// <remarks>
/// The only logic here is the part that cannot be expressed in markup: reporting the measured
/// width to the view model, which owns the breakpoints, and fitting the first-run size to the
/// display it opens on. Everything the window then does about that size is declared in AXAML.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Closing += (_, _) => ViewModel?.CancelOperations();

        // Both are needed: a resize reports a new width, and the assignment of the view model
        // itself has to pick up the width the window already has.
        SizeChanged += (_, args) => ViewModel?.Layout.Resize(args.NewSize.Width);
        DataContextChanged += (_, _) => ViewModel?.Layout.Resize(Bounds.Width);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();
    }

    /// <summary>
    /// Shrinks a window that would not fit the display it opened on.
    /// </summary>
    /// <remarks>
    /// The declared size suits a desktop monitor but is taller than the work area of a common
    /// 1366x768 laptop, where an oversized window puts the status line off screen. Only
    /// shrinking is done here, so a large display keeps the declared size, and the window is
    /// re-centred afterwards to match its startup location.
    /// </remarks>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var workArea = screen.WorkingArea;
        var fitted = WindowLayout.FitToWorkArea(workArea.Width / scaling, workArea.Height / scaling);
        var width = Math.Min(Width, fitted.Width);
        var height = Math.Min(Height, fitted.Height);
        if (width >= Width && height >= Height)
        {
            return;
        }

        Width = width;
        Height = height;
        Position = new PixelPoint(
            workArea.X + (int)((workArea.Width - (width * scaling)) / 2),
            workArea.Y + (int)((workArea.Height - (height * scaling)) / 2));
    }
}
