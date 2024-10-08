using System.Runtime.InteropServices;
using System.Windows.Automation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using ImproveWindows.Core.Services;
using ImproveWindows.Ui.Extensions;

namespace ImproveWindows.Ui;

public class WindowService : AppService
{
    private const int ScreenHeight = 2160;
    private const int ScreenWidth = 3840;
    private const int TaskBarHeight = 60;
    private const int FreeScreenHeight = ScreenHeight - TaskBarHeight;
    private const int WindowPadding = 8;
    private const int FullWindowHeight = FreeScreenHeight + WindowPadding;
    private const int HalvedWindowWidth = (ScreenWidth / 2) + (WindowPadding * 2);
    private const int FullWindowWidth = ScreenWidth + (WindowPadding * 2);
    private const int HalvedWindowHeight = (FreeScreenHeight / 2) + (WindowPadding * 1);
    private const int TeamsShareWindowStatusBarPadding = 36;
    private const int TeamsShareWindowToolbarPadding = 74;
    private const int TeamsShareWindowBottomPadding = 3;

    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(2);

    private (Task Task, CancellationTokenSource CancellationTokenSource)? _teamsMeeting;
    private MeetingState _meetingState;
    private bool _meetingWindowShareActive;
    private readonly Dictionary<string, HashSet<string>> _elementFindingCache = new();

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
                                new OrCondition(
                                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane)
                                )
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
                LogInfo("Skipped window with no name");
                return;
            }

            if (name.Contains("Microsoft Teams"))
            {
                await HandleTeamsWindow(automationElement, cancellationToken);
                return;
            }

            if (name.Contains("Improve Windows"))
            {
                // PutWindowInQuadrant(automationElement, true, false);
                RestoreWindowToQuadrant(
                    automationElement,
                    false,
                    true,
                    (int) (FullWindowWidth * 0.75),
                    HalvedWindowHeight,
                    WindowPosInsertAfter.None
                );
                return;
            }

            if (name == "Outlook")
            {
                name = await WaitForDifferentNameAsync(name, automationElement, cancellationToken)
                    ?? name;
            }

            if (name.Contains("Mail - "))
            {
                PutWindowInQuadrant(automationElement, true, false, WindowPosInsertAfter.None);
                return;
            }

            if (name.Contains("Calendar - "))
            {
                PutWindowInQuadrant(automationElement, true, true, WindowPosInsertAfter.None);
                return;
            }

            LogInfo($"Skipped window {name}");

            if (_meetingState == MeetingState.Window && IsAboutSize(automationElement, FullWindowWidth, FullWindowHeight))
            {
                PutWindowInHorizontalHalf(automationElement, false, WindowPosInsertAfter.TopMost);
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

        if (IsAboutSize(automationElement, HalvedWindowWidth, FreeScreenHeight))
        {
            _teamsMainWindow = automationElement;
            LogInfo($"Skipped Teams main window (height {size.Height})");
            return;
        }

        if (size is { Height: < 500, Width: < 500 })
        {
            LogInfo($"Teams thumbnail opened ({size.Width}x{size.Height})");

            PInvoke.SetWindowPos(
                new HWND(new IntPtr(automationElement.Current.NativeWindowHandle)),
                // Keep the thumbnail top-most
                WindowPosInsertAfter.TopMost.Value,
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
                    Task.Run(
                        async () =>
                        {
                            LogInfo("Thumbnail closed");

                            await Task.Delay(500, cancellationToken);

                            if (_meetingState == MeetingState.Thumbnail)
                            {
                                UpdateMeetingLayout(MeetingState.Window, cancellationToken);
                            }
                        },
                        cancellationToken
                    );
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

            UpdateTeamsMainWindowPosition();

            var hasRequestControlButton = true; // FindTakeControlElement(automationElement) != null;
            PositionTeamsShareWindow(automationElement, hasRequestControlButton);

            Once(
                automationElement,
                WindowPattern.WindowClosedEvent,
                () =>
                {
                    _meetingWindowShareActive = false;

                    UpdateTeamsMainWindowPosition();
                }
            );

            LogInfo("Teams screen share window moved");

            var hasRequestControlButtonCheckCount = 0;
            while (!hasRequestControlButton && hasRequestControlButtonCheckCount++ < 5)
            {
                await Task.Delay(250, cancellationToken);
                hasRequestControlButton = FindTakeControlElement(automationElement) != null;
            }

            if (hasRequestControlButton)
            {
                PositionTeamsShareWindow(automationElement, hasRequestControlButton);
                LogInfo("Teams screen share window moved again after seeing take control");
            }
        }
        else
        {
            LogInfo("Teams meeting opened");

            if (_teamsMeeting.HasValue)
            {
                var teamsMeeting = _teamsMeeting.Value;
                try
                {
                    if (!teamsMeeting.CancellationTokenSource.IsCancellationRequested)
                    {
                        await teamsMeeting.CancellationTokenSource.CancelAsync();
                    }
                }
                finally
                {
                    teamsMeeting.CancellationTokenSource.Dispose();
                    teamsMeeting.Task.Dispose();
                }
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _teamsMeeting = (ManageTeamsMeetingWindowAsync(automationElement, cancellationToken, cancellationTokenSource.Token),
                cancellationTokenSource);
        }
    }

    private void PositionTeamsShareWindow(AutomationElement automationElement, bool hasRequestControlButton)
    {
        var topPadding = TeamsShareWindowStatusBarPadding
            + (hasRequestControlButton ? TeamsShareWindowToolbarPadding : 0);

        RestoreWindow(
            automationElement,
            GetPosX(false),
            GetPosY(true) - topPadding + TeamsShareWindowBottomPadding,
            HalvedWindowWidth,
            HalvedWindowHeight + topPadding + TeamsShareWindowBottomPadding,
            WindowPosInsertAfter.None
        );
    }

    private void PutWindowInQuadrant(AutomationElement automationElement, bool left, bool top, WindowPosInsertAfter insertAfter)
    {
        SnapWindow(
            automationElement,
            top,
            left,
            HalvedWindowWidth,
            HalvedWindowHeight,
            insertAfter
        );
    }

    private static int GetPosY(bool top)
    {
        return top ? 0 : FreeScreenHeight / 2;
    }

    private static int GetPosX(bool left)
    {
        return (left ? 0 : (ScreenWidth / 2)) - WindowPadding;
    }

    private void PutWindowInHorizontalHalf(AutomationElement automationElement, bool top, WindowPosInsertAfter insertAfter)
    {
        SnapWindow(
            automationElement,
            top,
            true,
            FullWindowWidth,
            HalvedWindowHeight,
            insertAfter
        );
    }

    private void PutWindowInVerticalHalf(AutomationElement automationElement, bool left, WindowPosInsertAfter insertAfter)
    {
        SnapWindow(
            automationElement,
            true,
            left,
            HalvedWindowWidth,
            FullWindowHeight,
            insertAfter
        );
    }

    // private void SnapWindow(AutomationElement automationElement, bool top, bool left, int width, int height)
    // {
    //     var rectangle = automationElement.Current.BoundingRectangle;
    //     if (IsAboutSize(automationElement, width, height))
    //     {
    //         LogInfo($"Skipping resize to {width}x{height} (currently {rectangle.Width}x{rectangle.Height}) of {automationElement.Current.Name}");
    //         return;
    //     }
    //
    //     MaximizeWindow(
    //         automationElement,
    //         GetPosX(left),
    //         GetPosY(top),
    //         width,
    //         height
    //     );
    // }

    private void SnapWindow(AutomationElement automationElement, bool top, bool left, int width, int height, WindowPosInsertAfter insertAfter)
    {
        RestoreWindowToQuadrant(automationElement, top, left, width, height, insertAfter);
    }

    private struct WindowPosInsertAfter
    {
        public static readonly WindowPosInsertAfter Bottom = new(new HWND(1));
        public static readonly WindowPosInsertAfter NoTopMost = new(new HWND(-2));
        public static readonly WindowPosInsertAfter Top = new(new HWND(0));
        public static readonly WindowPosInsertAfter TopMost = new(new HWND(-1));
        public static readonly WindowPosInsertAfter None = new(default);

        public HWND Value { get; private set; }

        public WindowPosInsertAfter(HWND insertAfterWindow)
        {
            Value = insertAfterWindow;
        }
    }

    private void RestoreWindowToQuadrant(AutomationElement automationElement, bool top, bool left, int width, int height,
        WindowPosInsertAfter insertAfter)
    {
        RestoreWindow(automationElement, GetPosX(left), GetPosY(top), width, height, insertAfter);
    }

    private void RestoreWindow(AutomationElement automationElement, int x, int y, int width, int height, WindowPosInsertAfter insertAfter)
    {
        var currentRectangle = automationElement.Current.BoundingRectangle;
        var name = automationElement.Current.Name;
        if (IsAboutSize(automationElement, width, height)
            && IsAbout(x / currentRectangle.X, 1)
            && IsAbout(y / currentRectangle.Y, 1))
        {
            LogInfo(
                $"Skipping resize to {width}x{height} and position to {x} {y} "
                + $"(currently {currentRectangle.Width}x{currentRectangle.Height} at {currentRectangle.X},{currentRectangle.Y}) of {name}"
            );
            return;
        }

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
            insertAfter.Value,
            x,
            y,
            width,
            height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
        );

        if (!posResult)
        {
            throw new InvalidOperationException($"Error code {posResult} positioning window {name}");
        }
    }

    // private static void MaximizeWindow(AutomationElement automationElement, int? x = null, int? y = null, int width = FullWindowWidth, int height = MaxWindowHeight)
    // {
    //     var windowHandle = new HWND(new IntPtr(automationElement.Current.NativeWindowHandle));
    //     
    //     if (x is null || y is null)
    //     {
    //         var placement = new WINDOWPLACEMENT();
    //         if (!PInvoke.GetWindowPlacement(windowHandle, ref placement))
    //         {
    //             throw new InvalidOperationException("Error GetWindowPlacement");
    //         }
    //         
    //         var maxPos = placement.ptMaxPosition;
    //         x ??= maxPos.X;
    //         y ??= maxPos.Y;
    //     }
    //     
    //     var windowLongExStyle = PInvoke.GetWindowLong(windowHandle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
    //     if ((windowLongExStyle & 0x00000080L) != 0)
    //     {
    //         throw new InvalidOperationException($"Invalid window long ex style {windowLongExStyle}");
    //     }
    //     
    //     var l = PInvoke.GetWindowLong(windowHandle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
    //     if (0 == PInvoke.SetWindowLong(windowHandle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (int) ((l | 0x01000000L) & (~0x20000000L))))
    //     {
    //         throw new InvalidOperationException("Error SetWindowLong");
    //     }
    //     
    //     var posResult = PInvoke.SetWindowPos(
    //         windowHandle,
    //         default,
    //         x.Value,
    //         y.Value,
    //         width,
    //         height,
    //         SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS
    //         | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
    //         | SET_WINDOW_POS_FLAGS.SWP_DRAWFRAME
    //         | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED
    //         | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
    //     );
    //     
    //     if (!posResult)
    //     {
    //         throw new InvalidOperationException("Error SetWindowPos");
    //     }
    // }

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

    private async Task ManageTeamsMeetingWindowAsync(AutomationElement automationElement, CancellationToken serviceCancellationToken,
        CancellationToken cancellationToken)
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

            PutWindowInQuadrant(automationElement, true, true, WindowPosInsertAfter.None);

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
                    // ClickOnOpenContentInNewWindow(automationElement);
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
            _meetingWindowShareActive = false;
            UpdateMeetingLayout(MeetingState.None, serviceCancellationToken);
            LogInfo("Meeting task finished");
        }
    }

    private void UpdateTeamsMainWindowPosition()
    {
        if (_teamsMainWindow == null)
            return;

        if (_meetingState == MeetingState.None)
        {
            PutWindowInVerticalHalf(_teamsMainWindow, false, WindowPosInsertAfter.None);
        }
        else
        {
            if (_meetingWindowShareActive)
                PutWindowInQuadrant(_teamsMainWindow, false, false, WindowPosInsertAfter.None);
            else if (_meetingState == MeetingState.Window)
                PutWindowInQuadrant(_teamsMainWindow, false, true, WindowPosInsertAfter.None);
        }
    }

    // private void ClickOnOpenContentInNewWindow(AutomationElement automationElement)
    // {
    //     try
    //     {
    //         var openContentElement = FindPopoutElement(automationElement);
    //
    //         if (openContentElement is null)
    //         {
    //             return;
    //         }
    //
    //         var message = openContentElement.Click();
    //         if (message is not null)
    //         {
    //             LogInfo(message);
    //         }
    //     }
    //     catch (ElementNotAvailableException)
    //     {
    //     }
    // }

    // private AutomationElement? GetPopoutElement(AutomationElement automationElement)
    // {
    //     var openContentElements = automationElement
    //         .FindAll(
    //             TreeScope.Descendants,
    //             new AndCondition(
    //                 new PropertyCondition(AutomationElement.NameProperty, "Pop out"),
    //                 new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
    //             )
    //         )
    //         .OfType<AutomationElement>()
    //         .ToArray();
    //     
    //     return openContentElements.SingleOrDefault();
    // }

    // private static AutomationElement? FindPopoutElement(AutomationElement automationElement)
    // {
    //     const string controlName = "Pop out";
    //     return FindControlElement(automationElement, controlName);
    // }

    private static AutomationElement? FindTakeControlElement(AutomationElement automationElement)
    {
        const string controlName = "Take control";
        return FindControlElement(automationElement, controlName);
    }

#pragma warning disable IDE0051
    private AutomationElement? FindControlElementWithLogging(AutomationElement parentAutomationElement, string nameMatch)
#pragma warning restore IDE0051
    {
        if (!_elementFindingCache.TryGetValue(nameMatch, out var cache))
        {
            cache = [];
            _elementFindingCache[nameMatch] = cache;
        }
        
        var openContentElements = parentAutomationElement
            .FindAll(
                TreeScope.Descendants,
                new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, nameMatch),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)
                )
            )
            .OfType<AutomationElement>()
            .ToArray();

        foreach (var contentElement in openContentElements)
        {
            try
            {
                var currentName = contentElement.Current.Name;

                if (!cache.Add(currentName))
                {
                    continue;
                }

                LogInfo($"[finding control] {contentElement.Current.ControlType.ProgrammaticName}: {currentName}");
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        return openContentElements.SingleOrDefault(x => x.Current.Name.Contains(nameMatch));
    }

    private static AutomationElement? FindControlElement(AutomationElement parentAutomationElement, string nameMatch)
    {
        return parentAutomationElement
            .FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, nameMatch)
            )
            .OfType<AutomationElement>()
            .FirstOrDefault();
    }

    private CancellationTokenSource? _layoutDuringMeetingCts;
    private Task? _layoutDuringMeetingTask;
    private AutomationElement? _teamsMainWindow;

    private void UpdateMeetingLayout(MeetingState state, CancellationToken serviceCancellationToken)
    {
        _meetingState = state;
        _layoutDuringMeetingTask = DoWork();
        LogInfo($"--- Queued meeting layout to {state} ---");
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
                var windowPrevs = new Dictionary<int, HWND?>();
                HWND? prev = null;
                PInvoke.EnumWindows(
                    (hwnd, _) =>
                    {
                        windowPrevs[hwnd.Value.ToInt32()] = prev;
                        prev = hwnd;
                        return true;
                    },
                    IntPtr.Zero
                );

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
                        x => new
                        {
                            previousWindow = windowPrevs[x.Current.NativeWindowHandle],
                            automationElement = x,
                        }
                    )
                    .Select(
                        x => Task.Run(
                            async () =>
                            {
                                var automationElement = x.automationElement;
                                try
                                {
                                    var name = await WaitForNameAsync(automationElement, cancellationToken);
                                    if (automationElement.Current.BoundingRectangle.Height < 50)
                                    {
                                        await Task.Delay(1000, cancellationToken);
                                    }

                                    if (name is null)
                                    {
                                        return;
                                    }

                                    var windowPosInsertAfter = x.previousWindow is not null
                                        ? new WindowPosInsertAfter(x.previousWindow.Value)
                                        : WindowPosInsertAfter.Top;

                                    if (_meetingState == MeetingState.Window && IsAboutSize(automationElement, FullWindowWidth, FreeScreenHeight))
                                    {
                                        LogInfo($"Lowering {name}");
                                        PutWindowInHorizontalHalf(
                                            automationElement,
                                            false,
                                            windowPosInsertAfter
                                        );
                                        return;
                                    }

                                    if (_meetingState != MeetingState.Window && IsAboutSize(automationElement, FullWindowWidth, HalvedWindowHeight))
                                    {
                                        LogInfo($"Maximizing {name}");
                                        MaximizeWindow(automationElement);
                                    }
                                }
                                catch (ElementNotAvailableException)
                                {
                                }
                            },
                            cancellationToken
                        )
                    )
                    .Concat([Task.Run(UpdateTeamsMainWindowPosition, cancellationToken)])
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

    private static async Task<string?> WaitForDifferentNameAsync(
        string currentName,
        AutomationElement automationElement,
        CancellationToken cancellationToken
    )
    {
        var name = TryGetName(automationElement);
        var start = DateTime.Now;

        while (currentName == name)
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