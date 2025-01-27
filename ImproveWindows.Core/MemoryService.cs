using System.Diagnostics;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Core;

public class MemoryService : AppService
{
    private const int MaxMemory = 400;
    private const int IdealMemory = MaxMemory / 2;

    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        LogInfo("Monitoring process usage");
        var lastGcTime = DateTime.Now;
        while (!cancellationToken.IsCancellationRequested)
        {
            var isWorkDayTime = DateTime.Now.Hour is >= 8 and <= 17;
            using var currentProcess = Process.GetCurrentProcess();
            var memoryUsage = currentProcess.PrivateMemorySize64 >> 20;
            var maxMemory = isWorkDayTime ? IdealMemory : MaxMemory;
            var shouldAlert = memoryUsage > maxMemory;
            var timeSinceLastGc = DateTime.Now - lastGcTime;
            var shouldGc = timeSinceLastGc > TimeSpan.FromMinutes(15);
            if (
                shouldAlert
                && timeSinceLastGc < TimeSpan.FromMinutes(5)
            )
            {
                shouldGc = true;
                shouldAlert = false;
            }

            if (shouldGc)
            {
                LogInfo("Running GC");
                GC.Collect();
                lastGcTime = DateTime.Now;
            }

            SetStatus($"{memoryUsage}MB / {maxMemory}MB", shouldAlert);

            if (shouldAlert)
            {
                Console.Beep();
            }

            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
        }
    }
}