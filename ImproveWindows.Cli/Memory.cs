using System.Diagnostics;
using ImproveWindows.Cli.Logging;

namespace ImproveWindows.Cli;

public static class Memory
{
    private static readonly Logger Logger = new("Memory");
    
    private const int MaxMemory = 200;
    private const int IdealMemory = MaxMemory / 2;

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        Logger.Log("Monitoring process usage");
        while (!cancellationToken.IsCancellationRequested)
        {
            var isWorkDayTime = DateTime.Now.Hour is >= 8 and <= 17;
            var memoryUsage = Process.GetCurrentProcess().PrivateMemorySize64 >> 20;
            switch (memoryUsage)
            {
                case > IdealMemory when isWorkDayTime:
                case > MaxMemory:
                    Console.Beep();
                    Logger.Log($"{memoryUsage}MB");
                    break;
            }

            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }
}