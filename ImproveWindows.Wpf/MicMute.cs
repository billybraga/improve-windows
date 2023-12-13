using System.Windows.Input;
using ImproveWindows.Core;
using ImproveWindows.Core.Services;
using ImproveWindows.Wpf.WindowsUtils;

namespace ImproveWindows.Wpf;

public class MicMute : AppService
{
    private readonly AudioLevels _audioLevels;
    private readonly HotKey _h;

    public MicMute(AudioLevels audioLevels)
    {
        _audioLevels = audioLevels;
        _h = new HotKey(
            Key.M,
            KeyModifier.Ctrl | KeyModifier.Shift | KeyModifier.Alt,
            _ =>
            {
                try
                {
                    var isMuted = audioLevels.GetTeamsMicMuteState();
                    if (isMuted is null)
                    {
                        Console.Beep(800, 1000);
                        return;
                    }


                    Console.Beep(isMuted.Value ? 600 : 1000, 200);
                    audioLevels.ChangeTeamsMicMuteState();
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
        var isMuted = _audioLevels.GetTeamsMicMuteState();

        if (isMuted is null)
        {
            SetStatus("Found no teams to mute", true);
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