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
                audioLevels.ChangeTeamsMicMuteState();
                SetStatusFromAudio();
            }
        );
    }

    private void SetStatusFromAudio(bool beep = true)
    {
        var isMuted = _audioLevels.GetTeamsMicMuteState();
        
        if (isMuted is null)
        {
            SetStatus("Found no teams to mute", true);
            if (beep)
            {
                Console.Beep();   
            }
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
                SetStatusFromAudio(false);
                await Task.Delay(500, cancellationToken);
            }
        }
        finally
        {
            _h.Dispose();
        }
    }
}