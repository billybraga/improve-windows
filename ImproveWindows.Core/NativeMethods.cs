using System.Runtime.InteropServices;

namespace ImproveWindows.Core;

internal static partial class NativeMethods
{
    public const uint ProcessBasicInformationValue = 0;

    [Flags]
    public enum OpenProcessDesiredAccessFlags : uint
    {
        ProcessVmRead = 0x0010,
        ProcessQueryInformation = 0x0400,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        private readonly IntPtr Reserved1;
        public readonly IntPtr PebBaseAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        private readonly IntPtr[] Reserved2;

        private readonly IntPtr UniqueProcessId;
        private readonly IntPtr Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        private readonly ushort Length;
        public readonly ushort MaximumLength;
        public readonly IntPtr Buffer;
    }

    // This is not the real struct!
    // I faked it to get ProcessParameters address.
    // Actual struct definition:
    // https://learn.microsoft.com/en-us/windows/win32/api/winternl/ns-winternl-peb
    [StructLayout(LayoutKind.Sequential)]
    public struct Peb
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        private readonly IntPtr[] Reserved;

        public readonly IntPtr ProcessParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RtlUserProcessParameters
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        private readonly byte[] Reserved1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        private readonly IntPtr[] Reserved2;

        private readonly UnicodeString ImagePathName;
        public readonly UnicodeString CommandLine;
    }

    [LibraryImport("ntdll.dll")]
    public static partial uint NtQueryInformationProcess(
        IntPtr processHandle,
        uint processInformationClass,
        IntPtr processInformation,
        uint processInformationLength,
        out uint returnLength);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr OpenProcess(
        OpenProcessDesiredAccessFlags dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer,
        uint nSize, out uint lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);
}