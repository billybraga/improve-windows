using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ImproveWindows.Wpf.WindowsUtils;

public sealed class HotKey : IDisposable
{
    private static Dictionary<int, HotKey>? _dictHotKeyToCalBackProc;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, UInt32 fsModifiers, UInt32 vlc);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WmHotKey = 0x0312;

    private bool _disposed;

    public Key Key { get; }
    public KeyModifier KeyModifiers { get; }
    public Action<HotKey>? Action { get; }
    public int Id { get; set; }

    public HotKey(Key k, KeyModifier keyModifiers, Action<HotKey> action, bool register = true)
    {
        Key = k;
        KeyModifiers = keyModifiers;
        Action = action;
        if (register)
        {
            Register();
        }
    }

    public bool Register()
    {
        var virtualKeyCode = KeyInterop.VirtualKeyFromKey(Key);
        Id = virtualKeyCode + ((int)KeyModifiers * 0x10000);
        var result = RegisterHotKey(
            IntPtr.Zero,
            Id,
            (UInt32)KeyModifiers,
            (UInt32)virtualKeyCode
        );

        if (_dictHotKeyToCalBackProc == null)
        {
            _dictHotKeyToCalBackProc = new Dictionary<int, HotKey>();
            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcherThreadFilterMessage;
        }

        _dictHotKeyToCalBackProc.Add(Id, this);

        Debug.Print(result.ToString() + ", " + Id + ", " + virtualKeyCode);
        return result;
    }

    // ******************************************************************
    public void Unregister()
    {
        if (_dictHotKeyToCalBackProc != null
            && _dictHotKeyToCalBackProc.TryGetValue(
                Id,
                out _
            ))
        {
            UnregisterHotKey(
                IntPtr.Zero,
                Id
            );
        }
    }

    // ******************************************************************
    private static void ComponentDispatcherThreadFilterMessage(ref MSG msg, ref bool handled)
    {
        if (!handled)
        {
            if (msg.message == WmHotKey)
            {
                if (_dictHotKeyToCalBackProc != null
                    && _dictHotKeyToCalBackProc.TryGetValue(
                        (int)msg.wParam,
                        out var hotKey
                    ))
                {
                    if (hotKey.Action != null)
                    {
                        hotKey.Action(hotKey);
                    }

                    handled = true;
                }
            }
        }
    }

    // ******************************************************************
    // Implement IDisposable.
    // Do not make this method virtual.
    // A derived class should not be able to override this method.
    public void Dispose()
    {
        Dispose(true);
        // This object will be cleaned up by the Dispose method.
        // Therefore, you should call GC.SupressFinalize to
        // take this object off the finalization queue
        // and prevent finalization code for this object
        // from executing a second time.
        GC.SuppressFinalize(this);
    }

    // ******************************************************************
    // Dispose(bool disposing) executes in two distinct scenarios.
    // If disposing equals true, the method has been called directly
    // or indirectly by a user's code. Managed and unmanaged resources
    // can be _disposed.
    // If disposing equals false, the method has been called by the
    // runtime from inside the finalizer and you should not reference
    // other objects. Only unmanaged resources can be _disposed.
    private void Dispose(bool disposing)
    {
        // Check to see if Dispose has already been called.
        if (!_disposed)
        {
            // If disposing equals true, dispose all managed
            // and unmanaged resources.
            if (disposing)
            {
                // Dispose managed resources.
                Unregister();
            }

            // Note disposing has been done.
            _disposed = true;
        }
    }
}

// ******************************************************************

// ******************************************************************