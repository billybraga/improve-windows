using System.Windows.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using ImproveWindows.Core;
using ImproveWindows.Core.Services;
using ImproveWindows.Ui.WindowsUtils;

namespace ImproveWindows.Ui;

public sealed class MicMute : AppService
{
    private readonly AudioLevelsService _audioLevelsService;
    private readonly HotKey _h;

    public MicMute(AudioLevelsService audioLevelsService)
    {
        _audioLevelsService = audioLevelsService;
        _h = new HotKey(
            Key.M,
            HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_ALT,
            _ =>
            {
                try
                {
                    var isMuted = audioLevelsService.TeamsMicMuteState;
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
        var isMuted = _audioLevelsService.TeamsMicMuteState;

        if (isMuted is null)
        {
            SetStatus("Found no teams to mute");
        }
        else
        {
            SetStatus(isMuted.Value ? "Muted" : "Opened");
        }
    }

    protected override async Task StartAsync(CancellationToken cancellationToken)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _h.Dispose();
        }
        
        base.Dispose(disposing);
    }
}