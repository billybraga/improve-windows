using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ImproveWindows.Wpf.WindowsUtils;

public static class WindowsHelper
{
    public static int? FocusProcess(Predicate<Process> processPredicate)
    {
        var pr = Process.GetProcesses().FirstOrDefault(x => processPredicate(x));
        
        if (pr is null)
        {
            return null;
        }
        
        var hWnd = new HWND(pr.MainWindowHandle);
        PInvoke.SetForegroundWindow(hWnd);
        return pr.Id;
    }

    internal static INPUT CreateKeyDown(VIRTUAL_KEY key)
    {
        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous =
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                },
            },
        };
    }

    internal static INPUT CreateKeyUp(VIRTUAL_KEY key)
    {
        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous =
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP,
                },
            },
        };
    }
}