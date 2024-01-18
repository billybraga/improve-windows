using System.Windows.Automation;
using Windows.Win32;
using Windows.Win32.Foundation;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Wpf;

public class WindowService : AppService
{
    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(5);

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        Automation.AddAutomationEventHandler(
            WindowPattern.WindowOpenedEvent,
            AutomationElement.RootElement,
            TreeScope.Children,
            (sender, _) =>
            {
                try
                {
                    if (sender is not AutomationElement automationElement)
                    {
                        return;
                    }
                    
                    var current = WaitForName(automationElement);

                    if (!current.Name.Contains("Microsoft Teams"))
                    {
                        return;
                    }
                    
                    var size = current.BoundingRectangle;

                    if (size.Height > 1800)
                    {
                        LogInfo($"Skipped Teams main window (height {size.Height})");
                        return;
                    }

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

    private AutomationElement.AutomationElementInformation WaitForName(AutomationElement automationElement)
    {
        var current = automationElement.Current;
        var start = DateTime.Now;
        
        while (string.IsNullOrEmpty(current.Name) && (DateTime.Now - start) < MaxWaitForName)
        {
            Thread.Sleep(100);
            current = automationElement.Current;
        }

        return current;
    }
}