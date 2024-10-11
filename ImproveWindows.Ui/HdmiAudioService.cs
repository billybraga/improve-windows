using ImproveWindows.Core.Services;
using ImproveWindows.Ui.Audio;

namespace ImproveWindows.Ui;

public class HdmiAudioService : AppService
{
    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        AudioControl.Instance.Start();
        SetStatus("Started");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        finally
        {
            if (AudioControl.Instance.IsRunning)
            {
                AudioControl.Instance.Stop();
            }
        }
    }
}