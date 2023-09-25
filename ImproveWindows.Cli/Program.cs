using System.Runtime.Versioning;

namespace ImproveWindows.Cli;

public static class Program
{
    [SupportedOSPlatform("windows")]
    public static async Task Main(string[] _)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var readKeyTask = Task.Run(
            () =>
            {
                Console.ReadKey();
                cancellationTokenSource.Cancel();
            }, CancellationToken.None
        );
        try
        {
            await await Task.WhenAny(
                Audio.RunAsync(cancellationTokenSource.Token),
                Network.RunAsync(cancellationTokenSource.Token)
            );
            await readKeyTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.Beep();
            Console.WriteLine(e);
        }
    }
}