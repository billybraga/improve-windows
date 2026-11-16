using ImproveWindows.Core.Services;
using ImproveWindows.Ui.Actions;

namespace ImproveWindows.Ui;

/// <summary>
/// One-off fixes run from buttons in the service's pane.
/// </summary>
internal sealed class ActionsService : AppService
{
    private readonly SemaphoreSlim _runLock = new(1);
    private CancellationToken _cancellationToken;

    public IReadOnlyList<(string Label, string ToolTip, Func<Action<string>, CancellationToken, Task> Run)> Actions { get; } =
    [
        (
            "Remove English (US)",
            "Removes the English (United States) language and the US keyboard layouts loaded without being in the language settings",
            EnglishUsRemoval.RunAsync
        ),
    ];

    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        SetStatus("Ready");

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }

    public void Run(string label, Func<Action<string>, CancellationToken, Task> action)
    {
        _ = Task.Run(
            async () =>
            {
                if (!await _runLock.WaitAsync(0, _cancellationToken))
                {
                    LogInfo($"Skipped {label}, another action is running");
                    return;
                }

                try
                {
                    SetStatus($"Running {label}");
                    LogInfo($"--- {label} ---");
                    await action(LogInfo, _cancellationToken);
                    SetStatus($"{label} done at {DateTime.Now:HH:mm}");
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    SetStatus($"{label} failed", true);
                    LogError(e);
                }
                finally
                {
                    _ = _runLock.Release();
                }
            }
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runLock.Dispose();
        }

        base.Dispose(disposing);
    }
}
