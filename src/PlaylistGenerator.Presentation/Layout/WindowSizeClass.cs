namespace PlaylistGenerator.Presentation.Layout;

/// <summary>
/// How much horizontal room the window has, which is what the view adapts to.
/// </summary>
/// <remarks>
/// Width alone decides the class. Height changes what scrolls, which the view handles on
/// its own; width changes what has to move or disappear.
/// </remarks>
public enum WindowSizeClass
{
    /// <summary>A phone-sized, tablet-portrait, or half-screen window.</summary>
    Compact,

    /// <summary>A small laptop or a partially sized desktop window.</summary>
    Medium,

    /// <summary>A maximized desktop window on a large display.</summary>
    Expanded,
}
