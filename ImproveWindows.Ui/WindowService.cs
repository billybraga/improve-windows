using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using ImproveWindows.Core.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ImproveWindows.Ui;

internal sealed class WindowService : AppService
{
    private const float TopRatio = 0.45f;
    private const decimal WindowsZoom = 1.25m;
    private const int NativeScreenWidth = 3840;
    private const int NativeScreenHeight = 2160;
    private const int EffectiveScreenWidth = 3840;
    private const int EffectiveScreenHeight = 2160;
    private const int ActualTaskBarHeight = (int) (48 * WindowsZoom);
    private const int ScreenOffsetLeft = (NativeScreenWidth - EffectiveScreenWidth) / 2;
    private const int ScreenOffsetTop = (NativeScreenHeight - EffectiveScreenHeight) / 2;
#pragma warning disable CA1508
    private const int EffectiveTaskBarHeight = ScreenOffsetTop == 0 ? ActualTaskBarHeight : 0;
#pragma warning restore CA1508
    private const int FreeScreenHeight = EffectiveScreenHeight - EffectiveTaskBarHeight;
    private const int WindowPadding = 8;
    private const int FullWindowHeight = FreeScreenHeight + WindowPadding;
    private const int HalvedWindowWidth = (EffectiveScreenWidth / 2) + (WindowPadding * 2);
    private const int FullWindowWidth = EffectiveScreenWidth + (WindowPadding * 2);
    private const int TeamsShareWindowStatusBarPadding = 36;
    private const int TeamsShareWindowToolbarPadding = 74;
    private const int TeamsShareWindowBottomPadding = 15;

    private static readonly TimeSpan MaxWaitForName = TimeSpan.FromSeconds(2);

    private (Task Task, CancellationTokenSource CancellationTokenSource)? _teamsMeeting;
    private MeetingState _meetingState;
    private bool _meetingWindowShareActive;
    private readonly Dictionary<string, HashSet<string>> _elementFindingCache = [];

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

            SetStatus("Started");

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
                await HandleTeamsWindowOpeningAsync(automationElement, cancellationToken);
                return;
            }

            if (name.Contains("Improve Windows"))
            {
                // PutWindowInQuadrant(automationElement, true, false);
                const bool top = false;
                RestoreWindowToQuadrant(
                    automationElement,
                    top,
                    true,
                    false,
                    HalvedWindowHeight(top),
                    WindowPosInsertAfter.None
                );
                MinimizeWindow(automationElement);
                return;
            }

            if (name.StartsWith("Outlook", StringComparison.InvariantCulture) && !name.Contains("Mail") && !name.Contains("Calendar"))
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

            if (_meetingState == MeetingState.Window && IsAboutSize(automationElement, FullWindowWidth, FullWindowHeight))
            {
                PutWindowInHorizontalHalf(automationElement, false, WindowPosInsertAfter.TopMost);
                return;
            }

            if (_meetingState is MeetingState.Thumbnail or MeetingState.None
                && IsAboutSize(automationElement, FullWindowWidth, HalvedWindowHeight(top: false)))
            {
                MaximizeWindow(automationElement);
                return;
            }

            LogInfo($"Skipped window {name}");
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (Exception e)
        {
            LogError(e);
        }
    }

    private async Task HandleTeamsWindowOpeningAsync(
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
            HandleTeamsThumbnail(automationElement, size, cancellationToken);
        }
        else if (_meetingState == MeetingState.Window)
        {
            HandleTeamsMeetingShareWindow(automationElement);
        }
        else
        {
            await HandleTeamsMeetingWindowOpeningAsync(automationElement, cancellationToken);
        }
    }

    private async Task HandleTeamsMeetingWindowOpeningAsync(AutomationElement automationElement, CancellationToken cancellationToken)
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
        _teamsMeeting = (
            ManageTeamsMeetingWindowLifeAsync(automationElement, cancellationToken, cancellationTokenSource.Token),
            cancellationTokenSource
        );
    }

    private void HandleTeamsMeetingShareWindow(AutomationElement automationElement)
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

        // var hasRequestControlButtonCheckCount = 0;
        // while (!hasRequestControlButton && hasRequestControlButtonCheckCount++ < 5)
        // {
        //     await Task.Delay(250, cancellationToken);
        //     hasRequestControlButton = FindTakeControlElement(automationElement) != null;
        // }
        //
        // if (hasRequestControlButton)
        // {
        //     PositionTeamsShareWindow(automationElement, hasRequestControlButton);
        //     LogInfo("Teams screen share window moved again after seeing take control");
        // }
    }

    private void HandleTeamsThumbnail(AutomationElement automationElement, Rect size, CancellationToken cancellationToken)
    {
        LogInfo($"Teams thumbnail opened ({size.Width}x{size.Height})");

        _ = PInvoke.SetWindowPos(
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
                _ = Task.Run(
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

    private void PositionTeamsShareWindow(AutomationElement automationElement, bool hasRequestControlButton)
    {
        var topPadding = TeamsShareWindowStatusBarPadding
            + (hasRequestControlButton ? TeamsShareWindowToolbarPadding : 0);

        RestoreWindow(
            automationElement,
            x: GetPosX(false),
            y: TeamsShareWindowBottomPadding - topPadding,
            width: HalvedWindowWidth,
            height: HalvedWindowHeight(true) + WindowPadding + topPadding + TeamsShareWindowBottomPadding,
            WindowPosInsertAfter.None
        );
    }

    private void PutWindowInQuadrant(AutomationElement automationElement, bool left, bool top, WindowPosInsertAfter insertAfter)
    {
        SnapWindow(
            automationElement,
            top,
            left,
            false,
            HalvedWindowHeight(top),
            insertAfter
        );
    }

    [Pure]
    private static int HalvedWindowHeight(bool top)
    {
        var ratio = top
            ? TopRatio
            : (1 - TopRatio);
        return (int) Math.Round(FreeScreenHeight * ratio) + WindowPadding;
    }

    [Pure]
    private static int GetPosY(bool top)
    {
        return top ? 0 : (int) Math.Round(TopRatio * FreeScreenHeight);
    }

    [Pure]
    private static int GetPosX(bool left)
    {
        return (left ? 0 : (EffectiveScreenWidth / 2)) - WindowPadding;
    }

    private void PutWindowInHorizontalHalf(AutomationElement automationElement, bool top, WindowPosInsertAfter insertAfter)
    {
        SnapWindow(
            automationElement,
            top,
            true,
            true,
            HalvedWindowHeight(top),
            insertAfter
        );
    }

    private void PutWindowInVerticalHalf(AutomationElement automationElement, bool left, WindowPosInsertAfter insertAfter)
    {
        SnapWindow(
            automationElement,
            true,
            left,
            false,
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

    private void SnapWindow(AutomationElement automationElement, bool top, bool left, bool fullWidth, int height, WindowPosInsertAfter insertAfter)
    {
        RestoreWindowToQuadrant(automationElement, top, left, fullWidth, height, insertAfter);
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

    private void RestoreWindowToQuadrant(AutomationElement automationElement, bool top, bool left, bool fullWidth, int height,
        WindowPosInsertAfter insertAfter)
    {
        var width = fullWidth ? FullWindowWidth : HalvedWindowWidth;
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
            x + ScreenOffsetLeft,
            y + ScreenOffsetTop,
            width,
            height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
        );

        if (!posResult)
        {
            throw new InvalidOperationException($"Error code {posResult} positioning window {name}");
        }
    }

    private static void MinimizeWindow(AutomationElement automationElement)
    {
        var windowHandle = new HWND(new IntPtr(automationElement.Current.NativeWindowHandle));
        var placementResult = PInvoke.SetWindowPlacement(
            windowHandle,
            new WINDOWPLACEMENT
            {
                showCmd = SHOW_WINDOW_CMD.SW_MINIMIZE,
                length = (uint) Marshal.SizeOf<WINDOWPLACEMENT>(),
            }
        );

        if (!placementResult)
        {
            throw new InvalidOperationException("Error code restoring window");
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

    private async Task ManageTeamsMeetingWindowLifeAsync(AutomationElement automationElement, CancellationToken serviceCancellationToken,
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
                _ = PInvoke.EnumWindows(
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
                    .Select(x => new
                        {
                            previousWindow = windowPrevs[x.Current.NativeWindowHandle],
                            automationElement = x,
                        }
                    )
                    .Select(x => Task.Run(
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

                                    if (_meetingState != MeetingState.Window
                                        && IsAboutSize(automationElement, FullWindowWidth, HalvedWindowHeight(top: false)))
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