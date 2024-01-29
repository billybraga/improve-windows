using System.ComponentModel;
using System.Diagnostics;

namespace ImproveWindows.Core.Wifi;

public static class Util
{
    /// <summary>
    /// Helper method to wrap calls to Native WiFi API methods.
    /// If the method falls, throws an exception containing the error code.
    /// </summary>
    /// <param name="win32ErrorCode">The error code.</param>
    [DebuggerStepThrough]
    internal static void ThrowIfError(int win32ErrorCode)
    {
        if (win32ErrorCode != 0)
            throw new Win32Exception(win32ErrorCode);
    }

    internal static uint VersionToDword(Version version)
    {
        var major = (uint)version.Major;
        var minor = (uint)version.Minor;
        var dword = minor << 16 | major;
        return dword;
    }
}