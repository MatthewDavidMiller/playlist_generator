using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Core.Services;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class NormalizationPlannerTests
{
    [Fact]
    public void MapsEachSourceToAnOpusFileUnderTheOutputTree()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var first = temporary.CreateFile("music/one.mp3");
        var second = temporary.CreateFile("music/album/two.flac");
        var output = temporary.GetPath("normalized");

        var plan = NormalizationPlanner.Create([first, second], source, output);

        Assert.Empty(plan.SkippedSourcePaths);
        Assert.Equal(2, plan.TotalFileCount);
        Assert.Equal(
            [temporary.GetPath("normalized/one.opus"), temporary.GetPath("normalized/album/two.opus")],
            plan.Jobs.Select(job => job.DestinationPath));
    }

    [Fact]
    public void SkipsFilesWhoseOutputAlreadyExists()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var first = temporary.CreateFile("music/one.mp3");
        var second = temporary.CreateFile("music/two.mp3");
        var output = temporary.CreateDirectory("normalized");
        temporary.CreateFile("normalized/two.opus");

        var plan = NormalizationPlanner.Create([first, second], source, output);

        // Resuming must not redo completed work.
        Assert.Equal([second], plan.SkippedSourcePaths);
        Assert.Equal([first], plan.Jobs.Select(job => job.SourcePath));
    }

    [Fact]
    public void SkipsFilesThatAlreadyLiveInsideTheOutputTree()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var outside = temporary.CreateFile("music/one.mp3");
        var output = temporary.CreateDirectory("music/normalized");
        var inside = temporary.CreateFile("music/normalized/already-inside.mp3");

        var plan = NormalizationPlanner.Create([outside, inside], source, output);

        Assert.Equal([inside], plan.SkippedSourcePaths);
        Assert.Equal([outside], plan.Jobs.Select(job => job.SourcePath));
    }

    [Fact]
    public void AllowsTheOutputDirectoryToBeAParentOfTheSource()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music/raw");
        var track = temporary.CreateFile("music/raw/one.mp3");
        var output = temporary.GetPath("music");

        var plan = NormalizationPlanner.Create([track], source, output);

        Assert.Empty(plan.SkippedSourcePaths);
        Assert.Equal(temporary.GetPath("music/one.opus"), plan.Jobs[0].DestinationPath);
    }

    [Fact]
    public void SkipsASourceThatIsAlreadyItsOwnDestination()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var track = temporary.CreateFile("music/one.opus");

        // An .opus source in a same-named layout would otherwise be encoded over itself.
        var plan = NormalizationPlanner.Create([track], source, source);

        Assert.Equal([track], plan.SkippedSourcePaths);
        Assert.Empty(plan.Jobs);
    }

    [Fact]
    public void RejectsDestinationsThatCollapseToTheSameOpusFile()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var mp3 = temporary.CreateFile("music/song.mp3");
        var flac = temporary.CreateFile("music/song.flac");

        var exception = Assert.Throws<PlaylistValidationException>(
            () => NormalizationPlanner.Create([flac, mp3], source, temporary.GetPath("normalized")));

        Assert.Contains("same normalized output path", exception.Message, StringComparison.Ordinal);
        Assert.Contains(flac, exception.Message, StringComparison.Ordinal);
        Assert.Contains(mp3, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesRelativeSubfolderStructure()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var nested = temporary.CreateFile("music/artist/album/track.wav");

        var plan = NormalizationPlanner.Create([nested], source, temporary.GetPath("out"));

        Assert.Equal(
            temporary.GetPath("out/artist/album/track.opus"),
            plan.Jobs[0].DestinationPath);
    }

    [Fact]
    public void AnEmptyInputProducesAnEmptyPlan()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");

        var plan = NormalizationPlanner.Create([], source, temporary.GetPath("out"));

        Assert.Equal(0, plan.TotalFileCount);
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => NormalizationPlanner.Create(null!, "/music", "/out"));
        Assert.Throws<ArgumentException>(
            () => NormalizationPlanner.Create([], "  ", "/out"));
        Assert.Throws<ArgumentException>(
            () => NormalizationPlanner.Create([], "/music", "  "));
    }
}
