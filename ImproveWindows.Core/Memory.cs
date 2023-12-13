using System.Diagnostics;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Core;

public class Memory : AppService
{
    private const int MaxMemory = 264;
    private const int IdealMemory = MaxMemory / 2;

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        LogInfo("Monitoring process usage");
        while (!cancellationToken.IsCancellationRequested)
        {
            var isWorkDayTime = DateTime.Now.Hour is >= 8 and <= 17;
            var memoryUsage = Process.GetCurrentProcess().PrivateMemorySize64 >> 20;
            switch (memoryUsage)
            {
                case > IdealMemory when isWorkDayTime:
                case > MaxMemory:
                    SetStatus($"{memoryUsage}MB", true);
                    Console.Beep();
                    break;
                default:
                    SetStatus();
                    break;
            }

            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }
}