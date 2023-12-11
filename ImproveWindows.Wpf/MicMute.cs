using System.Windows.Input;
using ImproveWindows.Core.Logging;
using ImproveWindows.Core.Services;
using ImproveWindows.Wpf.Windows;

namespace ImproveWindows.Wpf;

public class MicMute : IAppService
{
    private readonly HotKey _h;

    public MicMute(Logger logger)
    {
        _h = new HotKey(
            Key.M,
            KeyModifier.Ctrl | KeyModifier.Shift | KeyModifier.Alt,
            _ =>
            {
                logger.Log("Will focus teams");
                var processId = WindowsHelper.FocusProcess(x => x.ProcessName.Contains("teams", StringComparison.InvariantCultureIgnoreCase));
                logger.Log($"Focused pid={processId}");
            }
        );
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(-1, cancellationToken);
        }
        finally
        {
            _h.Dispose();
        }
    }
}