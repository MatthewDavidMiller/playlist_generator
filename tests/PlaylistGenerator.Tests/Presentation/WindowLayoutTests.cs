using PlaylistGenerator.Presentation.Layout;

namespace PlaylistGenerator.Tests.Presentation;

/// <summary>
/// Covers the responsive breakpoints and the first-run window size, which are the only two
/// layout decisions the application makes for itself.
/// </summary>
public sealed class WindowLayoutTests
{
    [Theory]
    [InlineData(360)]
    [InlineData(600)]
    [InlineData(WindowLayout.CompactWidth - 1)]
    public void NarrowWidthsAreCompact(double width) =>
        Assert.Equal(WindowSizeClass.Compact, WindowLayout.Classify(width));

    [Theory]
    [InlineData(WindowLayout.CompactWidth)]
    [InlineData(900)]
    [InlineData(WindowLayout.ExpandedWidth - 1)]
    public void MiddlingWidthsAreMedium(double width) =>
        Assert.Equal(WindowSizeClass.Medium, WindowLayout.Classify(width));

    [Theory]
    [InlineData(WindowLayout.ExpandedWidth)]
    [InlineData(1920)]
    [InlineData(3840)]
    public void WideWidthsAreExpanded(double width) =>
        Assert.Equal(WindowSizeClass.Expanded, WindowLayout.Classify(width));

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnmeasuredWidthDoesNotFallIntoTheCompactLayout(double width) =>
        Assert.Equal(WindowSizeClass.Medium, WindowLayout.Classify(width));

    [Fact]
    public void ADisplayWithRoomToSpareGetsThePreferredSize()
    {
        var size = WindowLayout.FitToWorkArea(2560, 1440);

        Assert.Equal(WindowLayout.PreferredWidth, size.Width);
        Assert.Equal(WindowLayout.PreferredHeight, size.Height);
    }

    [Fact]
    public void AShortLaptopDisplayGetsAShorterWindow()
    {
        var size = WindowLayout.FitToWorkArea(1366, 728);

        Assert.Equal(WindowLayout.PreferredWidth, size.Width);
        Assert.True(
            size.Height < 728,
            $"A window of {size.Height} would not fit a work area of 728.");
    }

    [Fact]
    public void ASmallDisplayGetsAWindowThatFitsItInBothDirections()
    {
        var size = WindowLayout.FitToWorkArea(800, 600);

        Assert.True(size.Width < 800, $"Width {size.Width} does not fit 800.");
        Assert.True(size.Height < 600, $"Height {size.Height} does not fit 600.");
        Assert.True(size.Width >= WindowLayout.MinimumWidth);
        Assert.True(size.Height >= WindowLayout.MinimumHeight);
    }

    [Fact]
    public void ADisplaySmallerThanTheMinimumStillGetsTheMinimum()
    {
        var size = WindowLayout.FitToWorkArea(320, 240);

        Assert.Equal(WindowLayout.MinimumWidth, size.Width);
        Assert.Equal(WindowLayout.MinimumHeight, size.Height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 1080)]
    [InlineData(1920, double.NaN)]
    [InlineData(-1920, -1080)]
    public void AnUnknownWorkAreaFallsBackToThePreferredSize(double width, double height)
    {
        var size = WindowLayout.FitToWorkArea(width, height);

        Assert.Equal(WindowLayout.PreferredWidth, size.Width);
        Assert.Equal(WindowLayout.PreferredHeight, size.Height);
    }
}
