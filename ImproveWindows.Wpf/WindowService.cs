using System.Windows.Automation;
using Windows.Win32;
using Windows.Win32.Foundation;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Wpf;

public class WindowService : AppService
{
    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(5);

    private (Task Task, CancellationTokenSource CancellationTokenSource)? teamsMeetingTask;

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
                            new HWND(new IntPtr(current.NativeWindowHandle)),
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

                        if (teamsMeetingTask.HasValue)
                        {
                            teamsMeetingTask.Value.CancellationTokenSource.Cancel();
                        }

                        var cancellationTokenSource = new CancellationTokenSource();
                        teamsMeetingTask = (ManageTeamsMeetingWindowAsync(automationElement, cancellationTokenSource.Token), cancellationTokenSource);

                        PInvoke.SetWindowPos(
                            new HWND(new IntPtr(current.NativeWindowHandle)),
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
                catch (ElementNotAvailableException) {}
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

    private async Task ManageTeamsMeetingWindowAsync(AutomationElement automationElement, CancellationToken cancellationToken)
    {
        try
        {
            var items = new HashSet<string>();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!IsAlive(automationElement))
                {
                    return;
                }

                var menuItems = automationElement.FindAll(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolTip),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                    )
                );

                foreach (AutomationElement menuItem in menuItems)
                {
                    var current = menuItem.Current;
                    if (!items.Add(current.Name))
                    {
                        continue;
                    }

                    if (!current.Name.Contains("Pop out", StringComparison.OrdinalIgnoreCase)
                        && !current.Name.Contains("Chat", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Click(menuItem);
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            LogError(e);
        }
        finally
        {
            LogInfo("Meeting task done");
        }
    }

    private bool IsAlive(AutomationElement automationElement)
    {
        try
        {
            _ = automationElement.Current.Name;
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private void Click(AutomationElement menuItem)
    {
        var invokePattern = menuItem.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
        var current = menuItem.Current;
        
        if (invokePattern is null)
        {
            LogInfo($"Could not get invoke pattern on {current.Name} {current.ControlType.ProgrammaticName}");
            return;
        }

        LogInfo($"Clicking {current.Name} in meeting");
        invokePattern.Invoke();
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