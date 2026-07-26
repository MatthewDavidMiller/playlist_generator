using PlaylistGenerator.Core.Composition;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Models;
using PlaylistGenerator.Tests.TestSupport;

namespace PlaylistGenerator.Tests.Core;

public sealed class CoreServicesTests
{
    [Fact]
    public void TheDefaultGraphIsFullyPopulated()
    {
        var services = CoreServices.CreateDefault();

        Assert.NotNull(services.Catalog);
        Assert.NotNull(services.ExecutableLocator);
        Assert.NotNull(services.ProcessRunner);
        Assert.NotNull(services.PlaylistGenerator);
        Assert.NotNull(services.AudioNormalizer);
        Assert.NotNull(services.FfmpegInstallAdvisor);
    }

    [Fact]
    public void TheDefaultGraphUsesTheRealInfrastructure()
    {
        var services = CoreServices.CreateDefault();

        Assert.IsType<AudioFileCatalog>(services.Catalog);
        Assert.IsType<ExecutableLocator>(services.ExecutableLocator);
        Assert.IsType<ProcessRunner>(services.ProcessRunner);
    }

    [Fact]
    public void SuppliedInfrastructureReachesTheComposedServices()
    {
        using var temporary = new TemporaryDirectory();
        var source = temporary.CreateDirectory("music");
        var first = temporary.CreateFile("music/a.mp3");
        temporary.CreateFile("music/b.mp3");
        var special = temporary.CreateFile("id.mp3");

        var services = CoreServices.Create(
            new AudioFileCatalog(),
            new FakeExecutableLocator(),
            new FakeProcessRunner(),
            new FixedTrackShuffler());

        var result = services.PlaylistGenerator.Generate(
            new PlaylistRequest(source, special, 2, temporary.GetPath("mix.m3u8")));

        // The fixed shuffler is what keeps this order predictable.
        Assert.Equal(first, File.ReadAllLines(result.OutputPath)[1]);
        Assert.True(services.FfmpegInstallAdvisor.GetPlan().IsInstalled);
    }

    [Fact]
    public void RejectsNullInfrastructure()
    {
        var catalog = new AudioFileCatalog();
        var locator = new FakeExecutableLocator();
        var runner = new FakeProcessRunner();
        var shuffler = new FixedTrackShuffler();

        Assert.Throws<ArgumentNullException>(
            () => CoreServices.Create(null!, locator, runner, shuffler));
        Assert.Throws<ArgumentNullException>(
            () => CoreServices.Create(catalog, null!, runner, shuffler));
        Assert.Throws<ArgumentNullException>(
            () => CoreServices.Create(catalog, locator, null!, shuffler));
        Assert.Throws<ArgumentNullException>(
            () => CoreServices.Create(catalog, locator, runner, null!));
    }
}
