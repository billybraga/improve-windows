using ImproveWindows.Core.Services;
using ImproveWindows.Ui.Audio;

namespace ImproveWindows.Ui;

internal class HdmiAudioService : AppService
{
    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await LoopUntilStartedAsync(cancellationToken);

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

    private async Task LoopUntilStartedAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                AudioControl.Instance.Start();
                SetStatus("Started");
                return;
            }
            catch (Exception)
            {
                SetStatus("Retrying");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }
}