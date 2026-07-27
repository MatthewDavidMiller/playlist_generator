using Avalonia;
using Avalonia.VisualTree;

namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Finds controls in a realized visual tree, so a view test can assert on what a window
/// actually shows rather than on the markup that describes it.
/// </summary>
public static class VisualTreeSearch
{
    /// <summary>Returns every descendant of <paramref name="root"/> of the given type.</summary>
    public static IReadOnlyList<T> Descendants<T>(this Visual root)
        where T : Visual
    {
        ArgumentNullException.ThrowIfNull(root);
        return [.. root.GetVisualDescendants().OfType<T>()];
    }
}
