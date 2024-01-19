using System.Windows.Automation;
using Windows.Win32;
using Windows.Win32.Foundation;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Ui;

public class WindowService : AppService
{
    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(5);

    private (Task Task, CancellationTokenSource CancellationTokenSource)? teamsMeetingTask;
    private bool _isInMeeting;

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            Automation.AddAutomationEventHandler(
                WindowPattern.WindowOpenedEvent,
                AutomationElement.RootElement,
                TreeScope.Children,
                (sender, _) =>
                {
                    if (sender is not AutomationElement automationElement)
                    {
                        return;
                    }

                    HandleWindowAsync(automationElement, cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            );

            await Task.Run(
                async () =>
                {
                    try
                    {
                        var currentWindows = AutomationElement
                            .RootElement
                            .FindAll(
                                TreeScope.Children,
                                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)
                            );

                        var tasks = currentWindows
                            .OfType<AutomationElement>()
                            .Select(x => HandleWindowAsync(x, cancellationToken))
                            .ToArray();

                        await Task.WhenAll(tasks);
                    }
                    catch (Exception e)
                    {
                        LogError(e);
                    }
                },
                cancellationToken
            );

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

    private async Task HandleWindowAsync(AutomationElement automationElement, CancellationToken cancellationToken)
    {
        try
        {
            var current = await WaitForNameAsync(automationElement, cancellationToken);

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
            else if (_isInMeeting)
            {
                LogInfo("Teams screen share window opened");
                
                PInvoke.SetWindowPos(
                    new HWND(new IntPtr(current.NativeWindowHandle)),
                    default,
                    1912,
                    0,
                    1936,
                    1058,
                    default
                );
                
                LogInfo("Teams screen share window moved");
            }
            else
            {
                _isInMeeting = true;
                
                LogInfo("Teams meeting opened");

                if (teamsMeetingTask.HasValue)
                {
                    await teamsMeetingTask.Value.CancellationTokenSource.CancelAsync();
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
        catch (ElementNotAvailableException)
        {
        }
        catch (Exception e)
        {
            LogError(e);
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
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                    // new OrCondition(
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolTip),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Custom),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Thumb),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.HeaderItem),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem),
                    //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)
                    // )
                );

                foreach (AutomationElement menuItem in menuItems)
                {
                    var current = menuItem.Current;
                    if (!items.Add(current.Name))
                    {
                        continue;
                    }

                    if (!current.Name.Contains("Open content in new window", StringComparison.OrdinalIgnoreCase))
                    {
                        // LogInfo($"Skipping {current.Name} ({current.ItemType}, {current.ControlType.ProgrammaticName}) in meeting");
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
            _isInMeeting = false;
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

    private async Task<AutomationElement.AutomationElementInformation> WaitForNameAsync(AutomationElement automationElement, CancellationToken cancellationToken)
    {
        var current = automationElement.Current;
        var start = DateTime.Now;

        while (string.IsNullOrEmpty(current.Name) && (DateTime.Now - start) < MaxWaitForName)
        {
            await Task.Delay(100, cancellationToken);
            current = automationElement.Current;
        }

        return current;
    }
}