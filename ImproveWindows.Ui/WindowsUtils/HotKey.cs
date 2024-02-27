using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ImproveWindows.Ui.WindowsUtils;

internal sealed class HotKey : IDisposable
{
    private static Dictionary<int, HotKey>? DictHotKeyToCalBackProc;

    private const int WmHotKey = 0x0312;

    private bool _disposed;

    private readonly VIRTUAL_KEY _key;
    private readonly HOT_KEY_MODIFIERS _keyModifiers;
    private readonly Action<HotKey>? _action;
    
    private int Id { get; set; }

    public HotKey(VIRTUAL_KEY k, HOT_KEY_MODIFIERS keyModifiers, Action<HotKey> action, bool register = true)
    {
        _key = k;
        _keyModifiers = keyModifiers;
        _action = action;
        if (register)
        {
            Register();
        }
    }

    private void Register()
    {
        Id = HashCode.Combine(_key, _keyModifiers);
        var result = PInvoke.RegisterHotKey(
            HWND.Null,
            Id,
            _keyModifiers,
            (uint) _key
        );

        if (!result)
        {
            throw new InvalidOperationException($"Got result {result} when calling RegisterHotKey");
        }

        if (DictHotKeyToCalBackProc == null)
        {
            DictHotKeyToCalBackProc = new Dictionary<int, HotKey>();
            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcherThreadFilterMessage;
        }

        DictHotKeyToCalBackProc.Add(Id, this);
    }

    // ******************************************************************
    public void Unregister()
    {
        if (DictHotKeyToCalBackProc != null
            && DictHotKeyToCalBackProc.TryGetValue(
                Id,
                out _
            ))
        {
            PInvoke.UnregisterHotKey(
                HWND.Null,
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
                if (DictHotKeyToCalBackProc != null
                    && DictHotKeyToCalBackProc.TryGetValue(
                        (int)msg.wParam,
                        out var hotKey
                    ))
                {
                    if (hotKey._action != null)
                    {
                        hotKey._action(hotKey);
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