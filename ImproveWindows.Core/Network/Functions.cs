using System.Runtime.InteropServices;

namespace ImproveWindows.Core.Network;

internal static class NativeMethods
{
    [DllImport("wlanapi.dll")]
    internal static extern int WlanCloseHandle(
        [In] IntPtr hClientHandle,
        [In, Out] IntPtr pReserved
    );

    [DllImport("wlanapi.dll")]
    internal static extern int WlanEnumInterfaces(
            [In] IntPtr hClientHandle,
            [In, Out] IntPtr pReserved,
        [Out] out IntPtr ppInterfaceList
    );

    [DllImport("wlanapi.dll")]
    internal static extern int WlanFreeMemory(
        IntPtr pMemory
    );

    [DllImport("wlanapi.dll")]
    internal static extern int WlanOpenHandle(
        [In] uint dwClientVersion,
        [In, Out] IntPtr pReserved,
        [Out] out uint pdwNegotiatedVersion,
        [Out] out IntPtr phClientHandle
    );

    [DllImport("wlanapi.dll")]
    internal static extern int WlanQueryInterface(
        IntPtr hClientHandle,
        [In, MarshalAs(UnmanagedType.LPStruct)]
        Guid interfaceGuid,
        [In] WlanIntfOpcode opCode,
        IntPtr pReserved,
        [Out] out uint pdwDataSize,
        [Out] out IntPtr ppData,
        [Out] out WlanOpcodeValueType pWlanOpcodeValueType
    );


    [DllImport("wlanapi.dll")]
    internal static extern int WlanRegisterVirtualStationNotification(
        IntPtr hClientHandle,
        [In] bool bRegister,
        IntPtr pvReserved
    );

    [DllImport("wlanapi.dll")]
    internal static extern int WlanHostedNetworkInitSettings(
        IntPtr hClientHandle,
        out WlanHostedNetworkReason pFailReason,
        IntPtr pvReserved
    );
}