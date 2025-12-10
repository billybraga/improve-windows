using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using ImproveWindows.Core.Network.Wlan;

namespace ImproveWindows.Core.Network;

internal static class NetUtils
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
        var major = (uint) version.Major;
        var minor = (uint) version.Minor;
        var dword = minor << 16 | major;
        return dword;
    }

    public static IReadOnlyCollection<NetworkInterface> GetLanInterfaces(Action<Exception> onError)
    {
        try
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(
                    x =>
                        x is
                        {
                            NetworkInterfaceType: NetworkInterfaceType.Ethernet, IsReceiveOnly: false, OperationalStatus: OperationalStatus.Up
                        }
                        && x.GetIPProperties().GatewayAddresses.Count != 0
                )
                .ToArray();
        }
        catch (Exception e)
        {
            onError(e);
            return [];
        }
    }

    public static bool IsWlanInterfaceValid(WlanClient wlanClient, Action<Exception> onError, Action<string> logInfo)
    {
        var wlanInterfaces = GetWlanInterfaces(wlanClient, onError);

        if (wlanInterfaces.Count != 1)
        {
            var names = string.Join(", ", wlanInterfaces.Select(x => x.Name));
            logInfo($"Got {wlanInterfaces.Count} Wi-Fi interfaces: {names}");
            return false;
        }

        var wlanInterface = wlanInterfaces.Single();
        var dot11PhyType = GetDot11PhyType(wlanInterface);
        if (dot11PhyType is not (Dot11PhyType.He or Dot11PhyType.Vht))
        {
            logInfo($"{wlanInterface.Name} is not running in AX, got PHY type {dot11PhyType}");
            return false;
        }

        return true;

        static Dot11PhyType GetDot11PhyType(WlanInterface wlanInterface)
        {
            try
            {
                return wlanInterface.CurrentConnection.AssociationAttributes.PhyType;
            }
            catch (Win32Exception)
            {
                return Dot11PhyType.Unknown;
            }
        }
    }

    private static IReadOnlyCollection<WlanInterface> GetWlanInterfaces(WlanClient wlanClient, Action<Exception> onError)
    {
        try
        {
            return wlanClient
                .Interfaces
                .Where(
                    x =>
                        x.Name.Contains("wi-fi", StringComparison.OrdinalIgnoreCase)
                        && !x.Name.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
        }
        catch (Exception e)
        {
            onError(e);
            return [];
        }
    }

    public static async Task<string> GetTraceRouteAsync(string hostname, CancellationToken cancellationToken)
    {
        return await IoUtils.CreateCommand("tracert", [hostname])
            .ExecuteValidateAndGetOutputAsync(cancellationToken);
    }
}