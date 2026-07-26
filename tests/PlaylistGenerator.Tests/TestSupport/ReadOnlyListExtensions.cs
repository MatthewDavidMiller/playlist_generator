namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Index lookup for the read-only argument lists that command builders return.
/// </summary>
internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Returns the argument immediately after <paramref name="flag"/>.</summary>
    public static T ValueAfter<T>(this IReadOnlyList<T> values, T flag)
    {
        var index = values.IndexOf(flag);
        Assert.InRange(index, 0, values.Count - 2);
        return values[index + 1];
    }
}
