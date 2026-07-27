using PlaylistGenerator.Presentation.Layout;
using PlaylistGenerator.Presentation.ViewModels;

namespace PlaylistGenerator.Tests.Presentation;

public sealed class LayoutViewModelTests
{
    [Fact]
    public void StartsAtThePreferredWidthRatherThanAtNothing()
    {
        var layout = new LayoutViewModel();

        Assert.Equal(WindowLayout.PreferredWidth, layout.Width);
        Assert.False(layout.IsCompact);
    }

    [Fact]
    public void ANarrowWindowSwitchesToTheCompactLayout()
    {
        var layout = new LayoutViewModel();

        layout.Resize(480);

        Assert.True(layout.IsCompact);
        Assert.False(layout.IsMedium);
        Assert.False(layout.IsExpanded);
        Assert.Equal(WindowSizeClass.Compact, layout.SizeClass);
    }

    [Fact]
    public void AWideWindowSwitchesBack()
    {
        var layout = new LayoutViewModel();
        layout.Resize(480);

        layout.Resize(1600);

        Assert.False(layout.IsCompact);
        Assert.True(layout.IsExpanded);
    }

    /// <summary>
    /// A drag reports every intermediate width. Announcing the size class each time would
    /// re-evaluate every bound style class for a change that did not happen.
    /// </summary>
    [Fact]
    public void ResizingWithinOneSizeClassDoesNotAnnounceIt()
    {
        var layout = new LayoutViewModel();
        layout.Resize(1200);
        var announced = new List<string?>();
        layout.PropertyChanged += (_, args) => announced.Add(args.PropertyName);

        layout.Resize(1201);
        layout.Resize(1400);

        Assert.Equal([nameof(LayoutViewModel.Width), nameof(LayoutViewModel.Width)], announced);
    }

    [Fact]
    public void CrossingABreakpointAnnouncesEveryDerivedProperty()
    {
        var layout = new LayoutViewModel();
        layout.Resize(1200);
        var announced = new List<string?>();
        layout.PropertyChanged += (_, args) => announced.Add(args.PropertyName);

        layout.Resize(500);

        Assert.Contains(nameof(LayoutViewModel.SizeClass), announced);
        Assert.Contains(nameof(LayoutViewModel.IsCompact), announced);
        Assert.Contains(nameof(LayoutViewModel.IsMedium), announced);
        Assert.Contains(nameof(LayoutViewModel.IsExpanded), announced);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void AWidthThatIsNotAMeasurementIsIgnored(double width)
    {
        var layout = new LayoutViewModel();
        layout.Resize(1200);

        layout.Resize(width);

        Assert.Equal(1200, layout.Width);
    }
}
