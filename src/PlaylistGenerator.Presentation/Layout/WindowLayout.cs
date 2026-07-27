namespace PlaylistGenerator.Presentation.Layout;

/// <summary>
/// The window's responsive rules: where the layout changes shape and how large it may open.
/// </summary>
/// <remarks>
/// These are plain functions of a width, so the breakpoints can be tested without a display
/// server and the view is left with nothing to decide.
/// </remarks>
public static class WindowLayout
{
    /// <summary>Below this width the layout switches to its single-column form.</summary>
    public const double CompactWidth = 720;

    /// <summary>At or above this width the layout has room for its full form.</summary>
    public const double ExpandedWidth = 1024;

    /// <summary>Narrowest usable window, sized for a small tablet or a split screen.</summary>
    public const double MinimumWidth = 360;

    /// <summary>Shortest usable window; everything below the header scrolls.</summary>
    public const double MinimumHeight = 420;

    /// <summary>Opening size on a display with room for it.</summary>
    public const double PreferredWidth = 1040;

    /// <summary>Opening height on a display with room for it.</summary>
    public const double PreferredHeight = 780;

    /// <summary>
    /// Share of the work area a first-run window may occupy, leaving room for the window
    /// frame, which the work area does not account for.
    /// </summary>
    private const double WorkAreaFillRatio = 0.94;

    /// <summary>Classifies a window width.</summary>
    /// <remarks>
    /// A window that has not been measured yet reports a width of zero. That is treated as
    /// <see cref="WindowSizeClass.Medium"/> so the first frame cannot flash the compact
    /// layout before the real width arrives.
    /// </remarks>
    public static WindowSizeClass Classify(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return WindowSizeClass.Medium;
        }

        if (width < CompactWidth)
        {
            return WindowSizeClass.Compact;
        }

        return width < ExpandedWidth ? WindowSizeClass.Medium : WindowSizeClass.Expanded;
    }

    /// <summary>
    /// Returns the largest sensible opening size for a display's work area.
    /// </summary>
    /// <remarks>
    /// The preferred size is taller than the work area of a 1366x768 laptop, and wider than
    /// a half-screen snap on many displays, so it is capped rather than assumed. An unknown
    /// or nonsensical work area falls back to the preferred size instead of guessing.
    /// </remarks>
    public static WindowSize FitToWorkArea(double workAreaWidth, double workAreaHeight)
    {
        if (!IsUsable(workAreaWidth) || !IsUsable(workAreaHeight))
        {
            return new WindowSize(PreferredWidth, PreferredHeight);
        }

        return new WindowSize(
            Fit(PreferredWidth, workAreaWidth, MinimumWidth),
            Fit(PreferredHeight, workAreaHeight, MinimumHeight));
    }

    private static bool IsUsable(double length) => double.IsFinite(length) && length > 0;

    /// <summary>
    /// Never larger than the work area allows, and never smaller than the window can go,
    /// which matters on a display smaller than the minimum size.
    /// </summary>
    private static double Fit(double preferred, double workArea, double minimum) =>
        Math.Max(minimum, Math.Min(preferred, workArea * WorkAreaFillRatio));
}
