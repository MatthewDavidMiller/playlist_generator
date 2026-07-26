using PlaylistGenerator.CommandLine;
using PlaylistGenerator.Core.Composition;

var application = CliApplication.Create(CoreServices.CreateDefault(), Console.Out, Console.Error);

using var cancellation = new CancellationTokenSource();

// Handling the interrupt rather than letting it kill the process lets an in-flight FFmpeg
// run be stopped cleanly and its partial output removed.
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    return await application.RunAsync(args, cancellation.Token).ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
