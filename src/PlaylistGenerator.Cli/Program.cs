using PlaylistGenerator.CommandLine;
using PlaylistGenerator.Core.Infrastructure;
using PlaylistGenerator.Core.Services;

var catalog = new AudioFileCatalog();
var locator = new ExecutableLocator();
var application = new CliApplication(
    new PlaylistGeneratorService(catalog, new RandomTrackShuffler()),
    new AudioNormalizationService(catalog, locator, new ProcessRunner()),
    new FfmpegInstallAdvisor(locator),
    Console.Out,
    Console.Error);

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    return await application
        .RunAsync(args, cancellation.Token)
        .ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
