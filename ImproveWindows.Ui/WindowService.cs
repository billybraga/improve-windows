﻿using System.Runtime.InteropServices;
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

    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(2);

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
            var name = await WaitForNameAsync(automationElement, cancellationToken);

            if (name is null)
            {
                return;
            }

            if (name.Contains("Microsoft Teams"))
            {
                await HandleTeamsWindow(automationElement, cancellationToken);
            }

            if (name.Contains("Improve Windows"))
            {
                RestoreWindow(automationElement, false, true, (int) (FullWindowWidth * 0.75), HalvedWindowHeight);
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

    private async Task HandleTeamsWindow(
        AutomationElement automationElement,
        CancellationToken cancellationToken
    )
    {
        var size = automationElement.Current.BoundingRectangle;

        if (IsAbout(size.Height, MaxWindowHeight))
        {
            LogInfo($"Skipped Teams main window (height {size.Height})");
            return;
        }

        if (size is { Height: < 500, Width: < 500 })
        {
            LogInfo($"Teams thumbnail opened ({size.Width}x{size.Height})");

            PInvoke.SetWindowPos(
                new HWND(new IntPtr(automationElement.Current.NativeWindowHandle)),
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
                        UpdateMeetingLayout(MeetingState.Window, cancellationToken);
                    }
                }
            );

            UpdateMeetingLayout(MeetingState.Thumbnail, cancellationToken);

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
            _teamsMeetingTask = (ManageTeamsMeetingWindowAsync(automationElement, cancellationToken, cancellationTokenSource.Token), cancellationTokenSource);
        }
    }

    private static void PutWindowInQuadrant(AutomationElement automationElement, bool left, bool top)
    {
        RestoreWindow(
            automationElement,
            top,
            left,
            HalvedWindowWidth,
            HalvedWindowHeight
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

    private static void PutWindowInHalf(AutomationElement automationElement, bool top)
    {
        RestoreWindow(
            automationElement,
            top,
            true,
            FullWindowWidth,
            HalvedWindowHeight
        );
    }

    private static void RestoreWindow(AutomationElement automationElement, bool top, bool left, int width, int height)
    {
        var windowHandle = new HWND(new IntPtr(automationElement.Current.NativeWindowHandle));
        var placementResult = PInvoke.SetWindowPlacement(
            windowHandle,
            new WINDOWPLACEMENT
            {
                showCmd = SHOW_WINDOW_CMD.SW_RESTORE,
                length = (uint) Marshal.SizeOf<WINDOWPLACEMENT>(),
            }
        );

        if (!placementResult)
        {
            throw new InvalidOperationException("Error code restoring window");
        }
        
        var posResult = PInvoke.SetWindowPos(
            windowHandle,
            default,
            GetPosX(left),
            GetPosY(top),
            width,
            height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
        );

        if (!posResult)
        {
            throw new InvalidOperationException("Error code positioning window");
        }
    }

    private static void MaximizeWindow(AutomationElement automationElement)
    {
        var windowHandle = new HWND(new IntPtr(automationElement.Current.NativeWindowHandle));
        var placementResult = PInvoke.SetWindowPlacement(
            windowHandle,
            new WINDOWPLACEMENT
            {
                showCmd = SHOW_WINDOW_CMD.SW_MAXIMIZE,
                length = (uint) Marshal.SizeOf<WINDOWPLACEMENT>(),
            }
        );

        if (!placementResult)
        {
            throw new InvalidOperationException("Error code restoring window");
        }
    }

    private async Task ManageTeamsMeetingWindowAsync(AutomationElement automationElement, CancellationToken serviceCancellationToken, CancellationToken cancellationToken)
    {
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var meetingCancellationToken = cancellationTokenSource.Token;

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

            UpdateMeetingLayout(MeetingState.Window, serviceCancellationToken);

            LogInfo("Teams meeting started");

            while (!meetingCancellationToken.IsCancellationRequested)
            {
                if (!IsAlive(automationElement))
                {
                    return;
                }

                if (!_meetingWindowShareActive)
                {
                    ClickOnOpenContentInNewWindow(automationElement);
                }

                await Task.Delay(1000, meetingCancellationToken);
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
            UpdateMeetingLayout(MeetingState.None, serviceCancellationToken);
            _meetingWindowShareActive = false;
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

    private CancellationTokenSource? _layoutDuringMeetingCts;
    private Task? _layoutDuringMeetingTask;

    private void UpdateMeetingLayout(MeetingState state, CancellationToken serviceCancellationToken)
    {
        _meetingState = state;
        _layoutDuringMeetingTask = DoWork();
        LogInfo("--- Queued meeting layout ---");
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
                        automationElement => Task.Run(
                            async () =>
                            {
                                var name = await WaitForNameAsync(automationElement, cancellationToken);
                                if (automationElement.Current.BoundingRectangle.Height < 50)
                                {
                                    await Task.Delay(1000, cancellationToken);
                                }
                                var current = automationElement.Current;

                                if (name is null)
                                {
                                    LogInfo($"Did not find name for {current.ControlType.ProgrammaticName}#{current.AutomationId} (pid {current.ProcessId})");
                                    return;
                                }
                                
                                if (_meetingState == MeetingState.Window && IsAboutSize(automationElement, FullWindowWidth, MaxWindowHeight))
                                {
                                    LogInfo($"Lowering {name}");
                                    PutWindowInHalf(automationElement, false);
                                    return;
                                }

                                if (_meetingState != MeetingState.Window && IsAboutSize(automationElement, FullWindowWidth, HalvedWindowHeight))
                                {
                                    LogInfo($"Maximizing {name}");
                                    MaximizeWindow(automationElement);
                                    return;
                                }
                                
                                LogInfo($"Not changing {name} {current.BoundingRectangle.Width}x{current.BoundingRectangle.Height}");
                            },
                            cancellationToken
                        )
                    )
                    .ToArray();

                await Task.WhenAll(tasks);
                
                LogInfo("--- Finished meeting layout ---");
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

    private static async Task<string?> WaitForNameAsync(
        AutomationElement automationElement,
        CancellationToken cancellationToken
    )
    {
        var name = TryGetName(automationElement);
        var start = DateTime.Now;

        while (string.IsNullOrEmpty(name))
        {
            if ((DateTime.Now - start) > MaxWaitForName)
            {
                return null;
            }

            await Task.Delay(100, cancellationToken);
            name = TryGetName(automationElement);
        }

        return name;
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