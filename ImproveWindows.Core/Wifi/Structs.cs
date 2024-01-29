using System.Runtime.InteropServices;

namespace ImproveWindows.Core.Wifi;

[StructLayout(LayoutKind.Sequential)]
public struct Dot11MacAddress
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Value;
}

[StructLayout(LayoutKind.Sequential)]
public struct Dot11Ssid
{
    private int ssidLength; //uint

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    private byte[] ssid;
}

//= WLAN ==========================================================================================
[StructLayout(LayoutKind.Sequential)]
public struct WlanAssociationAttributes
{
    public Dot11Ssid Ssid;
    public Dot11BssType BssType;
    public Dot11MacAddress MacAddress;
    public Dot11PhyType PhyType;
    public uint PhyIndex;
    public uint LanSignalQuality;
    public uint RxRate;
    public uint TxRate;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WlanConnectionAttributes
{
    public WlanInterfaceState InterfaceState;
    public WlanConnectionMode ConnectionMode;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string ProfileName;

    public WlanAssociationAttributes AssociationAttributes;
    public WlanSecurityAttributes SecurityAttributes;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WlanInterfaceInfo
{
    public Guid Guid;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Description;

    public WlanInterfaceState State;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanInterfaceInfoList
{
    public uint NumberOfItems; //dataSize of dynamic array

    public uint Index;

    //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    public IntPtr InterfaceInfo; //dynamic array WlanInterfaceInfo[]
}

[StructLayout(LayoutKind.Sequential)]
public struct WlanSecurityAttributes
{
    [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
    [MarshalAs(UnmanagedType.Bool)] public bool OneXEnabled;
    public Dot11AuthAlgorithm AuthAlgo;
    public Dot11CipherAlgorithm CipherAlgo;
}