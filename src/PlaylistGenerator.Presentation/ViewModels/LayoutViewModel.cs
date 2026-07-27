using CommunityToolkit.Mvvm.ComponentModel;
using PlaylistGenerator.Presentation.Layout;

namespace PlaylistGenerator.Presentation.ViewModels;

/// <summary>
/// The window's current size class, which the view binds to as style classes.
/// </summary>
/// <remarks>
/// The view reports its width here and reads booleans back, so the breakpoints stay in one
/// tested place rather than being spread across markup.
/// </remarks>
public sealed class LayoutViewModel : ObservableObject
{
    private double _width = WindowLayout.PreferredWidth;

    /// <summary>Last reported window width, in device-independent pixels.</summary>
    public double Width => _width;

    /// <summary>How much horizontal room the window currently has.</summary>
    public WindowSizeClass SizeClass => WindowLayout.Classify(_width);

    /// <summary>Gets whether the single-column layout applies.</summary>
    public bool IsCompact => SizeClass == WindowSizeClass.Compact;

    /// <summary>Gets whether the intermediate layout applies.</summary>
    public bool IsMedium => SizeClass == WindowSizeClass.Medium;

    /// <summary>Gets whether the full-width layout applies.</summary>
    public bool IsExpanded => SizeClass == WindowSizeClass.Expanded;

    /// <summary>
    /// Reports a new window width.
    /// </summary>
    /// <remarks>
    /// A resize raises this for every intermediate pixel, so the derived properties are only
    /// announced when the size class actually changes. Widths that are not a real measurement
    /// are ignored rather than treated as a resize to nothing.
    /// </remarks>
    public void Resize(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return;
        }

        var previousClass = SizeClass;
        if (!SetProperty(ref _width, width, nameof(Width)))
        {
            return;
        }

        if (previousClass == SizeClass)
        {
            return;
        }

        OnPropertyChanged(nameof(SizeClass));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsExpanded));
    }
}
