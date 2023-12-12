using System.Windows.Input;
using ImproveWindows.Core;
using ImproveWindows.Core.Logging;
using ImproveWindows.Core.Services;
using ImproveWindows.Wpf.WindowsUtils;

namespace ImproveWindows.Wpf;

public class MicMute : IAppService
{
    private readonly HotKey _h;

    public MicMute(AudioLevels audioLevels, Logger logger)
    {
        _h = new HotKey(
            Key.M,
            KeyModifier.Ctrl | KeyModifier.Shift | KeyModifier.Alt,
            _ =>
            {
                logger.Log("Changing mute state");
                var isMuted = audioLevels.ChangeTeamsMicMuteState();
                
                if (isMuted is null)
                {
                    logger.Log("Could not mute");
                    Console.Beep();
                    return;
                }

                logger.Log(isMuted.Value ? "Muted" : "Unmuted");
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