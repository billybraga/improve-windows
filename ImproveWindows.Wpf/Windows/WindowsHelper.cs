using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ImproveWindows.Wpf.Windows;

public static class WindowsHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow); //ShowWindow needs an IntPtr

    public static int? FocusProcess(Predicate<Process> processPredicate)
    {
        var processRunning = Process.GetProcesses();
        foreach (var pr in processRunning)
        {
            if (processPredicate(pr))
            {
                var hWnd = pr.MainWindowHandle; //change this to IntPtr
                SetForegroundWindow(hWnd); //set to topmost
                return pr.Id;
            }
        }

        return null;
    }
}