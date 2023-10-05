using System.ComponentModel;
using System.Runtime.Versioning;
using ImproveWindows.Cli.Wifi;
using ImproveWindows.Cli.Wifi.Wlan;

namespace ImproveWindows.Cli;

public static class Network
{
    [SupportedOSPlatform("windows")]
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var wlanClient = WlanClient.CreateClient();
        
        Console.WriteLine("Network: Started");
        bool? state = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            CheckNetwork();

            await Task.Delay(5000, cancellationToken);
        }

        void CheckNetwork()
        {
            var wlanInterfaces = GetWlanInterfaces();

            if (wlanInterfaces.Count != 1)
            {
                state = false;
                Console.Beep();
                var names = string.Join(", ", wlanInterfaces.Select(x => x.Name));
                Console.WriteLine($"Network: Got {wlanInterfaces.Count} Wi-Fi interfaces: {names}");
                return;
            }

            var wlanInterface = wlanInterfaces.Single();
            var dot11PhyType = GetDot11PhyType(wlanInterface);
            if (dot11PhyType != Dot11PhyType.He)
            {
                state = false;
                Console.Beep();
                Console.WriteLine($"Network: {wlanInterface.Name} is not running in AX, got PHY type {dot11PhyType}");
                return;
            }

            if (state != true)
            {
                state = true;
                Console.WriteLine("Network: OK");
            }
        }
        
        IReadOnlyCollection<WlanInterface> GetWlanInterfaces()
        {
            try
            {
                return wlanClient
                    .Interfaces
                    .Where(x =>
                        x.Name.Contains("wi-fi", StringComparison.OrdinalIgnoreCase)
                        && !x.Name.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return ArraySegment<WlanInterface>.Empty;
            }
        }
        
        Dot11PhyType GetDot11PhyType(WlanInterface wlanInterface)
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
}