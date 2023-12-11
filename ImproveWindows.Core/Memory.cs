using System.Diagnostics;
using ImproveWindows.Core.Logging;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Core;

public class Memory : IAppService
{
    private readonly Logger _logger;
    private const int MaxMemory = 200;
    private const int IdealMemory = MaxMemory / 2;

    public Memory(Logger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.Log("Monitoring process usage");
        while (!cancellationToken.IsCancellationRequested)
        {
            var isWorkDayTime = DateTime.Now.Hour is >= 8 and <= 17;
            var memoryUsage = Process.GetCurrentProcess().PrivateMemorySize64 >> 20;
            switch (memoryUsage)
            {
                case > IdealMemory when isWorkDayTime:
                case > MaxMemory:
                    Console.Beep();
                    _logger.Log($"{memoryUsage}MB");
                    break;
            }

            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }
}