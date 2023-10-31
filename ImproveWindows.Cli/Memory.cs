using System.Diagnostics;

namespace ImproveWindows.Cli;

public static class Memory
{
    private const int MaxMemory = 200;
    private const int IdealMemory = MaxMemory / 4;

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var isWorkDayTime = DateTime.Now.Hour is >= 8 and <= 17;
            var memoryUsage = Process.GetCurrentProcess().PrivateMemorySize64 >> 20;
            switch (memoryUsage)
            {
                case > IdealMemory when isWorkDayTime:
                case > MaxMemory:
                    Console.Beep();
                    Console.WriteLine($"Memory is at {memoryUsage}MB");
                    break;
            }

            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }
}