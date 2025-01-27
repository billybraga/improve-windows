using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ImproveWindows.Core.Network.Wlan;

internal sealed class WlanClient : IDisposable
{
    // FIELDS =================================================================

    internal IntPtr clientHandle;

    private readonly Dictionary<Guid, WlanInterface> _interfaceMap = [];
    private volatile WlanInterface[] _interfaceList = [];

    private WlanHostedNetwork? _hostedNetwork;
    private readonly object _hostedNetworkLock = new();

    // PROPERTIES =============================================================

    public Version ClientVersion
    {
        get
        {
            var os = Environment.OSVersion;
            if (os.Platform == PlatformID.Win32NT)
            {
                var vs = os.Version;
                if (vs.Major >= 6)
                {
                    return new Version(2, 0);
                }

                if (vs is { Major: 5, Minor: >= 1 })
                {
                    return new Version(1, 0);
                }
            }

            return new Version(0, 0);
        }
    }

    private WlanHostedNetwork? HostedNetwork
    {
        get
        {
            if (_hostedNetwork == null)
            {
                lock (_hostedNetworkLock)
                {
                    if (_hostedNetwork == null)
                    {
                        try
                        {
                            _hostedNetwork = WlanHostedNetwork.CreateHostedNetwork(this);
                        }
                        catch (EntryPointNotFoundException)
                        {
                            throw new InvalidOperationException("System is not Hosted Network capable.");
                        }
                    }
                }
            }

            return _hostedNetwork;
        }
    }

    public WlanInterface[] Interfaces => _interfaceList;

    // CONSTRUCTORS, DESTRUCTOR ===============================================

    private void ReloadInterfaces()
    {
        NetUtils.ThrowIfError(
            NativeMethods.WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var listPtr)
        );
        try
        {
            var list = Marshal.PtrToStructure<WlanInterfaceInfoList>(listPtr);
            var numberOfItems = list.NumberOfItems;
            var listIterator = listPtr.ToInt64() + Marshal.OffsetOf<WlanInterfaceInfoList>("InterfaceInfo").ToInt64();
            var interfaces = new WlanInterface[numberOfItems];
            var currentIfaceGuids = new List<Guid>();
            for (var i = 0; i < numberOfItems; i++)
            {
                var info = Marshal.PtrToStructure<WlanInterfaceInfo>(new IntPtr(listIterator));
                listIterator += Marshal.SizeOf(info);
                currentIfaceGuids.Add(info.Guid);
                if (!_interfaceMap.TryGetValue(info.Guid, out var wlanInterface))
                {
                    wlanInterface = WlanInterface.CreateInterface(this, info);
                }

                interfaces[i] = wlanInterface;
                _interfaceMap[info.Guid] = wlanInterface;
            }

            // Remove stale interfaceList
            var deadIfacesGuids = new Queue<Guid>();
            foreach (var ifaceGuid in _interfaceMap.Keys)
            {
                if (!currentIfaceGuids.Contains(ifaceGuid))
                    deadIfacesGuids.Enqueue(ifaceGuid);
            }

            while (deadIfacesGuids.Count != 0)
            {
                var deadIfaceGuid = deadIfacesGuids.Dequeue();
                _ = _interfaceMap.Remove(deadIfaceGuid);
            }

            _interfaceList = interfaces;
        }
        finally
        {
            _ = NativeMethods.WlanFreeMemory(listPtr);
        }
    }

    private WlanClient()
    {
        var clientVersionDword = NetUtils.VersionToDword(ClientVersion);
        NetUtils.ThrowIfError(NativeMethods.WlanOpenHandle(clientVersionDword, IntPtr.Zero, out _, out clientHandle));
    }

    ~WlanClient()
    {
        if (clientHandle != IntPtr.Zero)
        {
            _ = NativeMethods.WlanCloseHandle(clientHandle, IntPtr.Zero);
            clientHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (clientHandle != IntPtr.Zero)
        {
            _ = NativeMethods.WlanCloseHandle(clientHandle, IntPtr.Zero);
            clientHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Creates a Native Wi-Fi Client control class.
    /// </summary>
    /// <returns>Wlan Client instance.</returns>
    /// <exception cref="Win32Exception">On any error related to opening handle, registering notifications.</exception>
    /// <exception cref="EntryPointNotFoundException">When WlanApi is not available.</exception>
    public static WlanClient CreateClient()
    {
        var client = new WlanClient();

        client.ReloadInterfaces();

        if (client.HostedNetwork != null)
        {
            NetUtils.ThrowIfError(NativeMethods.WlanRegisterVirtualStationNotification(client.clientHandle, true, IntPtr.Zero));
        }

        return client;
    }
}