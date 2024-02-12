using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ImproveWindows.Core.Wifi.Wlan;

public sealed class WlanInterface
{
    // FIELDS =================================================================

    private readonly WlanClient _client;

    // PROPERTIES =============================================================

    /// <summary>
    /// Gets network interface GUID.
    /// </summary>
    private Guid Guid { get; }

    /// <summary>
    /// Gets the attributes of the current connection.
    /// </summary>
    /// <key>The current connection attributes.</key>
    /// <exception cref="Win32Exception">An exception with code 0x0000139F (The group or resource is not in the correct state to perform the requested operation.) will be thrown if the interface is not connected to a network.</exception>
    public WlanConnectionAttributes CurrentConnection
    {
        get
        {
            WifiUtil.ThrowIfError(NativeMethods.WlanQueryInterface(_client.clientHandle, Guid, WlanIntfOpcode.CurrentConnection, IntPtr.Zero, out _, out var valuePtr, out _));
            try
            {
                return (WlanConnectionAttributes)Marshal.PtrToStructure(valuePtr, typeof(WlanConnectionAttributes))!;
            }
            finally
            {
                _ = NativeMethods.WlanFreeMemory(valuePtr);
            }
        }
    }

    /// <summary>
    /// Gets the network interface of this wireless interface.
    /// </summary>
    /// <remarks>
    /// The network interface allows querying of generic network properties such as the interface's IP address.
    /// </remarks>
    public NetworkInterface? NetworkInterface
    {
        get
        {
            // Do not cache the NetworkInterface; We need it fresh
            // each time cause otherwise it caches the IP information.
            foreach (var netIface in NetworkInterface.GetAllNetworkInterfaces())
            {
                var netIfaceGuid = new Guid(netIface.Id);
                if (netIfaceGuid.Equals(Guid))
                {
                    return netIface;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets network interface name.
    /// </summary>
    public string Name => NetworkInterface?.Name ?? throw new InvalidOperationException("Did not find network interface name");

    // CONSTRUCTORS ===========================================================

    private WlanInterface(WlanClient client, WlanInterfaceInfo info)
    {
        _client = client;
        Guid = info.Guid;
    }

    /// <summary>
    /// Creates an instance of an interface control class.
    /// </summary>
    /// <param name="client">Native Wi-Fi client control class.</param>
    /// <param name="info">Interface information provided by client control class.</param>
    /// <returns>Instance of an interface control class.</returns>
    public static WlanInterface CreateInterface(WlanClient client, WlanInterfaceInfo info)
    {
        return new WlanInterface(client, info);
    }
}