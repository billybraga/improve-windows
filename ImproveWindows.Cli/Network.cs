using System.ComponentModel;
using System.Runtime.Versioning;
using NativeWifi;

namespace ImproveWindows.Cli;

public static class Network
{
    [SupportedOSPlatform("windows")]
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        var wlanClient = new WlanClient();
        Console.WriteLine("Network: Started");
        while (!cancellationToken.IsCancellationRequested)
        {
            var wlanInterfaces = GetWlanInterfaces();

            if (wlanInterfaces.Count != 1)
            {
                Console.Beep();
                var names = string.Join(", ", wlanInterfaces.Select(x => x.InterfaceName));
                Console.WriteLine($"Got {wlanInterfaces.Count} Wi-Fi interfaces: {names}");
            }
            else
            {
                var wlanInterface = wlanInterfaces.Single();
                var dot11PhyType = GetDot11PhyType(wlanInterface);
                if (dot11PhyType != 10)
                {
                    Console.Beep();
                    Console.WriteLine($"{wlanInterface.InterfaceName} is not running in AX, got PHY type {dot11PhyType}");
                }
            }

            await Task.Delay(5000, cancellationToken);
        }

        int GetDot11PhyType(WlanClient.WlanInterface wlanInterface)
        {
            try
            {
                return (int) wlanInterface.CurrentConnection.wlanAssociationAttributes.dot11PhyType;
            }
            catch (Win32Exception)
            {
                return -1;
            }
        }

        IReadOnlyCollection<WlanClient.WlanInterface> GetWlanInterfaces()
        {
            try
            {
                return wlanClient
                    .Interfaces
                    .Where(x =>
                        x.InterfaceName.Contains("wi-fi", StringComparison.OrdinalIgnoreCase)
                        && !x.InterfaceName.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return ArraySegment<WlanClient.WlanInterface>.Empty;
            }
        }
    }
}