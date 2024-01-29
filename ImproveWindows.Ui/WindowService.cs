using System.Windows.Automation;
using Windows.Win32;
using Windows.Win32.Foundation;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Ui;

public class WindowService : AppService
{
    private const int ScreenHeight = 2160;
    private const int ScreenWidth = 3840;
    private const int TaskBarHeight = 60;
    private const int MaxWindowHeight = ScreenHeight - TaskBarHeight;
    private const int WindowPadding = 8;
    private const int HalvedWindowWidth = (ScreenWidth / 2) + WindowPadding * 2;
    private const int HalvedWindowHeight = (MaxWindowHeight / 2) + WindowPadding * 2;

    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(5);

    private (Task Task, CancellationTokenSource CancellationTokenSource)? _teamsMeetingTask;
    private bool _isInMeeting;
    private bool _meetingWindowShareActive;

    protected override async Task StartAsync(CancellationToken cancellationToken)
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

                    using var handleWindowTask = HandleWindowAsync(automationElement, cancellationToken);
                    handleWindowTask
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

            if (current.Name.Contains("Microsoft Teams"))
            {
                await HandleTeamsWindow(automationElement, current);
            }

            if (current.Name.Contains("Improve Windows"))
            {
                HandleImproveWindowsWindow(current);
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

    private static void HandleImproveWindowsWindow(AutomationElement.AutomationElementInformation current)
    {
        PInvoke.SetWindowPos(
            new HWND(new IntPtr(current.NativeWindowHandle)),
            default,
            -WindowPadding,
            (MaxWindowHeight / 2) - WindowPadding,
            HalvedWindowWidth,
            HalvedWindowHeight,
            default
        );
    }

    private async Task HandleTeamsWindow(AutomationElement automationElement, AutomationElement.AutomationElementInformation current)
    {
        var size = current.BoundingRectangle;

        if (IsAbout(size.Height, MaxWindowHeight))
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
                (int) size.Width,
                (int) size.Height,
                default
            );

            LogInfo("Teams thumbnail moved");
        }
        else if (_isInMeeting)
        {
            if (_meetingWindowShareActive)
            {
                return;
            }

            _meetingWindowShareActive = true;

            LogInfo("Teams screen share window opened");

            PInvoke.SetWindowPos(
                new HWND(new IntPtr(current.NativeWindowHandle)),
                default,
                (ScreenWidth / 2) - WindowPadding,
                0,
                HalvedWindowWidth,
                HalvedWindowHeight,
                default
            );

            Once(
                automationElement,
                WindowPattern.WindowClosedEvent,
                () =>
                {
                    _meetingWindowShareActive = false;
                }
            );

            LogInfo("Teams screen share window moved");
        }
        else
        {
            LogInfo("Teams meeting opened");

            if (_teamsMeetingTask.HasValue)
            {
                var teamsMeetingTask = _teamsMeetingTask.Value;
                try
                {
                    if (!teamsMeetingTask.CancellationTokenSource.IsCancellationRequested)
                    {
                        await teamsMeetingTask.CancellationTokenSource.CancelAsync();
                    }
                }
                finally
                {
                    teamsMeetingTask.CancellationTokenSource.Dispose();
                    teamsMeetingTask.Task.Dispose();
                }
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _teamsMeetingTask = (ManageTeamsMeetingWindowAsync(automationElement, cancellationTokenSource.Token), cancellationTokenSource);
        }
    }

    private async Task ManageTeamsMeetingWindowAsync(AutomationElement automationElement, CancellationToken cancellationToken)
    {
        _isInMeeting = true;

        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            Once(
                automationElement,
                WindowPattern.WindowClosedEvent,
                () =>
                {
                    LogInfo("Handling meeting close event");
                    // ReSharper disable once AccessToDisposedClosure
                    cancellationTokenSource.Cancel();
                }
            );

            PInvoke.SetWindowPos(
                new HWND(new IntPtr(automationElement.Current.NativeWindowHandle)),
                default,
                -WindowPadding,
                0,
                HalvedWindowWidth,
                HalvedWindowHeight,
                default
            );

            LogInfo("Teams meeting started");

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!IsAlive(automationElement))
                {
                    return;
                }

                if (!_meetingWindowShareActive)
                {
                    ClickOnOpenContentInNewWindow(automationElement);
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException oce)
        {
            LogInfo($"Meeting task got {oce.GetType().Name}");
        }
        catch (Exception e)
        {
            LogError(e);
        }
        finally
        {
            LogInfo("Meeting task finishing");
            _isInMeeting = false;
            _meetingWindowShareActive = false;
            await OnMeetingStoppedAsync(cancellationToken);
            LogInfo("Meeting task finished");
        }
    }

    private void ClickOnOpenContentInNewWindow(AutomationElement automationElement)
    {
        try
        {
            var openContentElement = automationElement
                .FindAll(
                    TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.NameProperty, "Open content in new window"),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                    )
                )
                .OfType<AutomationElement>()
                .FirstOrDefault();

            if (openContentElement is null)
            {
                return;
            }

            Click(openContentElement);
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private async Task OnMeetingStoppedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var automationElements = AutomationElement
                .RootElement
                .FindAll(
                    TreeScope.Children,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane)
                    )
                )
                .OfType<AutomationElement>()
                .ToArray();

            var tasks = new List<Task>();
            var skippedWindows = new List<string>();

            foreach (var automationElement in automationElements)
            {
                var name = TryGetName(automationElement);
                if (name is null)
                {
                    continue;
                }

                if (IsAboutSize(automationElement, ScreenWidth, MaxWindowHeight / 2.0))
                {
                    tasks.Add(
                        Task.Run(
                            () =>
                            {
                                LogInfo($"Maximizing {name}");
                                var windowPattern = (WindowPattern) automationElement.GetCurrentPattern(WindowPattern.Pattern);
                                windowPattern.SetWindowVisualState(WindowVisualState.Maximized);
                                LogInfo($"Maximized {name}");
                            },
                            cancellationToken
                        )
                    );
                }
                else
                {
                    skippedWindows.Add(name);
                }
            }

            var skippedWindowNames = string.Join("\n", skippedWindows);
            LogInfo($"Skipped window resize:\n{skippedWindowNames}");

            await Task.WhenAll(tasks);
        }
        catch (Exception e)
        {
            LogError(e);
        }
    }

    private static string? TryGetName(AutomationElement automationElement)
    {
        try
        {
            return automationElement.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool IsAlive(AutomationElement automationElement)
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

    private static async Task<AutomationElement.AutomationElementInformation> WaitForNameAsync(AutomationElement automationElement,
        CancellationToken cancellationToken)
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

    private static bool IsAbout(double value, double reference)
    {
        return (Math.Abs(value - reference) / reference) < 0.05;
    }

    private static bool IsAboutSize(AutomationElement element, double referenceWidth, double referenceHeight)
    {
        try
        {
            return IsAbout(element.Current.BoundingRectangle.Height, referenceHeight)
                && IsAbout(element.Current.BoundingRectangle.Width, referenceWidth);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private void Once(AutomationElement automationElement, AutomationEvent automationEvent, Action onClose)
    {
        AutomationEventHandler automationEventHandler = default!;
        automationEventHandler = (_, _) =>
        {
            try
            {
                // ReSharper disable once AccessToModifiedClosure
                Automation.RemoveAutomationEventHandler(automationEvent, automationElement, automationEventHandler);
            }
            catch (Exception e)
            {
                LogError(e);
            }

            onClose();
        };

        Automation.AddAutomationEventHandler(
            automationEvent,
            automationElement,
            TreeScope.Element,
            automationEventHandler
        );
    }
}