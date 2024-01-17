using System.Diagnostics;
using System.Windows.Automation;
using Windows.Win32;
using Windows.Win32.Foundation;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Wpf;

public class WindowService : AppService
{
    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(5);
    private int? _teamsProcessId;

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        _teamsProcessId = Process
            .GetProcesses()
            .FirstOrDefault(x => x.ProcessName == "ms-teams")
            ?.Id;

        Automation.AddAutomationEventHandler(
            eventId: WindowPattern.WindowOpenedEvent,
            element: AutomationElement.RootElement,
            scope: TreeScope.Children,
            eventHandler: (sender, _) =>
            {
                try
                {
                    if (sender is not AutomationElement automationElement)
                    {
                        return;
                    }

                    var current = automationElement.Current;
                    var start = DateTime.Now;
                    while (string.IsNullOrEmpty(current.Name) && (DateTime.Now - start) < MaxWaitForName)
                    {
                        Thread.Sleep(100);
                        current = automationElement.Current;
                    }

                    if (!current.Name.Contains("Microsoft Teams"))
                    {
                        return;
                    }

                    if (_teamsProcessId is null)
                    {
                        _teamsProcessId = current.ProcessId;
                        LogInfo("Skipped Teams main window");
                        return;
                    }

                    var size = current.BoundingRectangle;

                    if (size is { Height: < 500, Width: < 500 })
                    {
                        LogInfo("Teams thumbnail opened");

                        PInvoke.SetWindowPos(
                            new HWND(new IntPtr(automationElement.Current.NativeWindowHandle)),
                            default,
                            800,
                            100,
                            (int)size.Width,
                            (int)size.Height,
                            default
                        );

                        LogInfo("Teams thumbnail moved");
                    }
                    else
                    {
                        LogInfo("Teams meeting opened");

                        PInvoke.SetWindowPos(
                            new HWND(new IntPtr(automationElement.Current.NativeWindowHandle)),
                            default,
                            -8,
                            0,
                            1936,
                            1058,
                            default
                        );

                        LogInfo("Teams meeting moved");
                    }
                }
                catch (Exception e)
                {
                    LogError(e);
                }
            }
        );

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        finally
        {
            Automation.RemoveAllEventHandlers();
        }
    }
}