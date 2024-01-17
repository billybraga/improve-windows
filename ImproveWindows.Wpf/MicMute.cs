using System.Windows.Input;
using ImproveWindows.Core;
using ImproveWindows.Core.Services;
using ImproveWindows.Wpf.WindowsUtils;

namespace ImproveWindows.Wpf;

public class MicMute : AppService
{
    private readonly AudioLevelsService audioLevelsService;
    private readonly HotKey _h;

    public MicMute(AudioLevelsService audioLevelsService)
    {
        this.audioLevelsService = audioLevelsService;
        _h = new HotKey(
            Key.M,
            KeyModifier.Ctrl | KeyModifier.Shift | KeyModifier.Alt,
            _ =>
            {
                try
                {
                    var isMuted = audioLevelsService.GetTeamsMicMuteState();
                    if (isMuted is null)
                    {
                        Console.Beep(800, 1000);
                        return;
                    }


                    Console.Beep(isMuted.Value ? 600 : 1000, 200);
                    audioLevelsService.ChangeTeamsMicMuteState();
                    Console.Beep(isMuted.Value ? 1000 : 600, 200);
                }
                finally
                {
                    SetStatusFromAudio();
                }
            }
        );
    }

    private void SetStatusFromAudio()
    {
        var isMuted = audioLevelsService.GetTeamsMicMuteState();

        if (isMuted is null)
        {
            SetStatus("Found no teams to mute");
        }
        else
        {
            SetStatus(isMuted.Value ? "Muted" : "Opened");
        }
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            SetStatus();
            while (!cancellationToken.IsCancellationRequested)
            {
                SetStatusFromAudio();
                await Task.Delay(500, cancellationToken);
            }
        }
        finally
        {
            _h.Dispose();
        }
    }
}