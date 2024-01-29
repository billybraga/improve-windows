using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ImproveWindows.Core.Windows;

public static class ProcessCommandLineExtensions
{
    private static bool ReadStructFromProcessMemory<TStruct>(
        IntPtr hProcess, IntPtr lpBaseAddress, out TStruct? val)
    {
        val = default;
        var structSize = Marshal.SizeOf<TStruct>();
        var mem = Marshal.AllocHGlobal(structSize);
        try
        {
            if (NativeMethods.ReadProcessMemory(
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
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.OpenProcessDesiredAccessFlags.ProcessQueryInformation |
            NativeMethods.OpenProcessDesiredAccessFlags.ProcessVmRead, false, (uint)process.Id);
        
        if (hProcess == IntPtr.Zero)
        {
            // couldn't open process for VM read
            throw new InvalidOperationException("couldn't open process for VM read");
        }

        try
        {
            var sizePbi = Marshal.SizeOf<NativeMethods.ProcessBasicInformation>();
            var memPbi = Marshal.AllocHGlobal(sizePbi);
            try
            {
                var ret = NativeMethods.NtQueryInformationProcess(
                    hProcess, NativeMethods.ProcessBasicInformationValue, memPbi,
                    (uint)sizePbi, out _);

                if (0 != ret)
                {
                    // NtQueryInformationProcess failed
                    throw new InvalidOperationException("NtQueryInformationProcess failed");
                }

                var pbiInfo = Marshal.PtrToStructure<NativeMethods.ProcessBasicInformation>(memPbi);
                if (pbiInfo.PebBaseAddress == IntPtr.Zero)
                {
                    // PebBaseAddress is null
                    throw new InvalidOperationException("PebBaseAddress is null");
                }

                if (!ReadStructFromProcessMemory<NativeMethods.Peb>(hProcess,
                        pbiInfo.PebBaseAddress, out var pebInfo))
                {
                    // couldn't read PEB information
                    throw new InvalidOperationException("couldn't read PEB information");
                }

                if (!ReadStructFromProcessMemory<NativeMethods.RtlUserProcessParameters>(
                        hProcess, pebInfo.ProcessParameters, out var rtlParamsInfo))
                {
                    // couldn't read ProcessParameters
                    throw new InvalidOperationException("couldn't read ProcessParameters");
                }

                var clLen = rtlParamsInfo.CommandLine.MaximumLength;
                var memCl = Marshal.AllocHGlobal(clLen);
                try
                {
                    if (!NativeMethods.ReadProcessMemory(hProcess,
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
            NativeMethods.CloseHandle(hProcess);
        }
    }
}