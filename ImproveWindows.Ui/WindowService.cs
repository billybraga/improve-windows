using System.Windows.Automation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Ui;

public class WindowService : AppService
{
    private const int ScreenHeight = 2160;
    private const int ScreenWidth = 3840;
    private const int TaskBarHeight = 60;
    private const int MaxWindowHeight = ScreenHeight - TaskBarHeight;
    private const int WindowPadding = 8;
    private const int HalvedWindowWidth = (ScreenWidth / 2) + (WindowPadding * 2);
    private const int FullWindowWidth = ScreenWidth + (WindowPadding * 2);
    private const int HalvedWindowHeight = (MaxWindowHeight / 2) + (WindowPadding * 1);

    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(5);

    private (Task Task, CancellationTokenSource CancellationTokenSource)? _teamsMeetingTask;
    private MeetingState _meetingState;
    private bool _meetingWindowShareActive;

    private enum MeetingState
    {
        None,
        Window,
        Thumbnail,
    }

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

            if (_layoutDuringMeetingTask != null)
            {
                await _layoutDuringMeetingTask;
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
                await HandleTeamsWindow(automationElement, current, cancellationToken);
            }

            if (current.Name.Contains("Improve Windows"))
            {
                PutWindowInQuadrant(automationElement, true, false);
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

    private async Task HandleTeamsWindow(AutomationElement automationElement, AutomationElement.AutomationElementInformation current,
        CancellationToken cancellationToken)
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

            Once(
                automationElement,
                WindowPattern.WindowClosedEvent,
                () =>
                {
                    if (_meetingState == MeetingState.Thumbnail)
                    {
                        _meetingState = MeetingState.Window;
                        QueueLayoutDuringMeeting(cancellationToken);
                    }
                }
            );

            _meetingState = MeetingState.Thumbnail;
            QueueLayoutDuringMeeting(cancellationToken);

            LogInfo("Teams thumbnail moved");
        }
        else if (_meetingState > MeetingState.None)
        {
            if (_meetingWindowShareActive)
            {
                return;
            }

            _meetingWindowShareActive = true;

            LogInfo("Teams screen share window opened");

            PutWindowInQuadrant(automationElement, false, true);

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

    private static void PutWindowInQuadrant(AutomationElement automationElement, bool left, bool top)
    {
        if (automationElement.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
        {
            var windowPattern = (WindowPattern) pattern;
            windowPattern.SetWindowVisualState(WindowVisualState.Normal);
        }
        var windowHandle = new HWND(new IntPtr(automationElement.Current.NativeWindowHandle));
        PInvoke.SetWindowPos(
            windowHandle,
            default,
            GetPosX(left),
            GetPosY(top),
            HalvedWindowWidth,
            HalvedWindowHeight,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
        );
    }

    private static int GetPosY(bool top)
    {
        return top ? 0 : MaxWindowHeight / 2;
    }

    private static int GetPosX(bool left)
    {
        return (left ? 0 : (ScreenWidth / 2)) - WindowPadding;
    }

    private void PutWindowInHalf(AutomationElement automationElement, bool top)
    {
        var location = top ? "top" : "bottom";
        LogInfo($"Putting {automationElement.Current.Name} to {location}");
        if (automationElement.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
        {
            var windowPattern = (WindowPattern) pattern;
            windowPattern.SetWindowVisualState(WindowVisualState.Normal);
        }
        var windowHandle = new HWND(new IntPtr(automationElement.Current.NativeWindowHandle));
        PInvoke.SetWindowPos(
            windowHandle,
            default,
            GetPosX(true),
            GetPosY(top),
            FullWindowWidth,
            HalvedWindowHeight,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
        );
    }

    private void MaximizeWindow(AutomationElement automationElement)
    {
        LogInfo($"Maximizing {automationElement.Current.Name}");
        if (automationElement.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
        {
            var windowPattern = (WindowPattern) pattern;
            windowPattern.SetWindowVisualState(WindowVisualState.Maximized);
        }
        LogInfo($"Maximized {automationElement.Current.Name}");
    }

    private async Task ManageTeamsMeetingWindowAsync(AutomationElement automationElement, CancellationToken cancellationToken)
    {
        _meetingState = MeetingState.Window;

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

            PutWindowInQuadrant(automationElement, true, true);

            QueueLayoutDuringMeeting(cancellationToken);

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
            _meetingState = MeetingState.None;
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

            foreach (var automationElement in automationElements)
            {
                var name = TryGetName(automationElement);
                if (name is null)
                {
                    continue;
                }

                if (IsAboutSize(automationElement, FullWindowWidth, HalvedWindowHeight))
                {
                    tasks.Add(
                        Task.Run(
                            () => MaximizeWindow(automationElement),
                            cancellationToken
                        )
                    );
                }
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception e)
        {
            LogError(e);
        }
    }

    private CancellationTokenSource? _layoutDuringMeetingCts;
    private Task? _layoutDuringMeetingTask;

    private void QueueLayoutDuringMeeting(CancellationToken serviceCancellationToken)
    {
        _layoutDuringMeetingTask = DoWork();
        LogInfo("Queued meeting layout");
        return;

        async Task DoWork()
        {
            using var newCts = CancellationTokenSource.CreateLinkedTokenSource(serviceCancellationToken);
            var oldCts = Interlocked.CompareExchange(ref _layoutDuringMeetingCts, newCts, null);

            if (oldCts is not null && !oldCts.IsCancellationRequested)
            {
                LogInfo("Cancelling old layout");
                try
                {
                    await oldCts.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            var cancellationToken = newCts.Token;

            try
            {
                var tasks = AutomationElement
                    .RootElement
                    .FindAll(
                        TreeScope.Children,
                        new OrCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane)
                        )
                    )
                    .OfType<AutomationElement>()
                    .Select(
                        window => Task.Run(
                            () =>
                            {
                                if (_meetingState == MeetingState.Window && IsAboutSize(window, FullWindowWidth, MaxWindowHeight))
                                {
                                    PutWindowInHalf(window, false);
                                }

                                if (_meetingState == MeetingState.Thumbnail && IsAboutSize(window, FullWindowWidth, HalvedWindowHeight))
                                {
                                    MaximizeWindow(window);
                                }
                            },
                            cancellationToken
                        )
                    )
                    .ToArray();

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                LogError(e);
            }
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