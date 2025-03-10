using ImproveWindows.Core;
using ImproveWindows.Core.Services;
using ImproveWindows.Ui.WindowsUtils;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ImproveWindows.Ui;

internal sealed class MicMute : AppService
{
    private readonly AudioLevelsService _audioLevelsService;

    public MicMute(AudioLevelsService audioLevelsService)
    {
        _audioLevelsService = audioLevelsService;
    }

    private void SetStatusFromAudio()
    {
        var isMuted = _audioLevelsService.IsMicMuteState;

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
        using var h = new HotKey(
            VIRTUAL_KEY.VK_M,
            HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_ALT,
            _ =>
            {
                try
                {
                    var isMuted = _audioLevelsService.IsMicMuteState;
                    if (isMuted is null)
                    {
                        Console.Beep(800, 1000);
                        return;
                    }


                    _audioLevelsService.ChangeMicMuteState();
                    Console.Beep(isMuted.Value ? 300 : 150, 100);
                }
                finally
                {
                    SetStatusFromAudio();
                }
            }
        );

        SetStatus();
        while (!cancellationToken.IsCancellationRequested)
        {
            SetStatusFromAudio();
            await Task.Delay(500, cancellationToken);
        }
    }
}