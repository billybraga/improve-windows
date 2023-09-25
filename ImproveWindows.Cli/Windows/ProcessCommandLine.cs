using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ImproveWindows.Cli.Windows;

public static class ProcessCommandLineExtensions
{
    private static class Win32Native
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

        [DllImport("ntdll.dll")]
        public static extern uint NtQueryInformationProcess(
            IntPtr processHandle,
            uint processInformationClass,
            IntPtr processInformation,
            uint processInformationLength,
            out uint returnLength);

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(
            OpenProcessDesiredAccessFlags dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwProcessId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer,
            uint nSize, out uint lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }

    private static bool ReadStructFromProcessMemory<TStruct>(
        IntPtr hProcess, IntPtr lpBaseAddress, out TStruct? val)
    {
        val = default;
        var structSize = Marshal.SizeOf<TStruct>();
        var mem = Marshal.AllocHGlobal(structSize);
        try
        {
            if (Win32Native.ReadProcessMemory(
                    hProcess, lpBaseAddress, mem, (uint)structSize, out var len) &&
                (len == structSize))
            {
                val = Marshal.PtrToStructure<TStruct>(mem);
                return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }

        return false;
    }

    public static string GetCommandLine(this Process process)
    {
        var hProcess = Win32Native.OpenProcess(
            Win32Native.OpenProcessDesiredAccessFlags.ProcessQueryInformation |
            Win32Native.OpenProcessDesiredAccessFlags.ProcessVmRead, false, (uint)process.Id);
        
        if (hProcess == IntPtr.Zero)
        {
            // couldn't open process for VM read
            throw new InvalidOperationException("couldn't open process for VM read");
        }

        try
        {
            var sizePbi = Marshal.SizeOf<Win32Native.ProcessBasicInformation>();
            var memPbi = Marshal.AllocHGlobal(sizePbi);
            try
            {
                var ret = Win32Native.NtQueryInformationProcess(
                    hProcess, Win32Native.ProcessBasicInformationValue, memPbi,
                    (uint)sizePbi, out _);

                if (0 != ret)
                {
                    // NtQueryInformationProcess failed
                    throw new InvalidOperationException("NtQueryInformationProcess failed");
                }

                var pbiInfo = Marshal.PtrToStructure<Win32Native.ProcessBasicInformation>(memPbi);
                if (pbiInfo.PebBaseAddress == IntPtr.Zero)
                {
                    // PebBaseAddress is null
                    throw new InvalidOperationException("PebBaseAddress is null");
                }

                if (!ReadStructFromProcessMemory<Win32Native.Peb>(hProcess,
                        pbiInfo.PebBaseAddress, out var pebInfo))
                {
                    // couldn't read PEB information
                    throw new InvalidOperationException("couldn't read PEB information");
                }

                if (!ReadStructFromProcessMemory<Win32Native.RtlUserProcessParameters>(
                        hProcess, pebInfo.ProcessParameters, out var rtlParamsInfo))
                {
                    // couldn't read ProcessParameters
                    throw new InvalidOperationException("couldn't read ProcessParameters");
                }

                var clLen = rtlParamsInfo.CommandLine.MaximumLength;
                var memCl = Marshal.AllocHGlobal(clLen);
                try
                {
                    if (!Win32Native.ReadProcessMemory(hProcess,
                            rtlParamsInfo.CommandLine.Buffer, memCl, clLen, out _))
                    {
                        // couldn't read command line buffer
                        throw new InvalidOperationException("couldn't read command line buffer");
                    }

                    return Marshal.PtrToStringUni(memCl)
                           ?? throw new InvalidOperationException("Command line was null");
                }
                finally
                {
                    Marshal.FreeHGlobal(memCl);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(memPbi);
            }
        }
        finally
        {
            Win32Native.CloseHandle(hProcess);
        }
    }
}