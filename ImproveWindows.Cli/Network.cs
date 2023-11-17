using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using ImproveWindows.Cli.Logging;
using ImproveWindows.Cli.Wifi;
using ImproveWindows.Cli.Wifi.Wlan;

namespace ImproveWindows.Cli;

public static class Network
{
    private const int HighestGoodPing = 40;
    private static readonly Ping Pinger = new();
    private static readonly Logger Logger = new("Network");

    enum NetState
    {
        None,
        EthernetOk,
        WifiOk,
        WifiBad,
    }

    enum PingState
    {
        None,
        Ok,
        InvalidStatus,
        Slow,
        Exception,
    }

    [SupportedOSPlatform("windows")]
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var wlanClient = WlanClient.CreateClient();

        Logger.Log("Started");
        var netState = NetState.None;
        var pingState = PingState.None;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            CheckNetwork();

            await CheckPingAsync();

            await Task.Delay(5000, cancellationToken);
        }

        return;

        void CheckNetwork()
        {
            NetState newState;
            var lanInterfaces = GetLanInterfaces();
            if (lanInterfaces.Any())
            {
                newState = NetState.EthernetOk;
            }
            else
            {
                var wlanInterfaces = GetWlanInterfaces();

                if (wlanInterfaces.Count != 1)
                {
                    netState = NetState.WifiBad;
                    Console.Beep();
                    var names = string.Join(", ", wlanInterfaces.Select(x => x.Name));
                    Logger.Log($"Got {wlanInterfaces.Count} Wi-Fi interfaces: {names}");
                    return;
                }

                var wlanInterface = wlanInterfaces.Single();
                var dot11PhyType = GetDot11PhyType(wlanInterface);
                if (dot11PhyType != Dot11PhyType.He)
                {
                    netState = NetState.WifiBad;
                    Console.Beep();
                    Logger.Log($"{wlanInterface.Name} is not running in AX, got PHY type {dot11PhyType}");
                    return;
                }

                newState = NetState.WifiOk;
            }

            if (newState == netState)
            {
                return;
            }

            netState = newState;
            Logger.Log($"{netState}");
        }

        async Task CheckPingAsync()
        {
            var oldPingState = pingState;

            (pingState, var error) = await GetPingStateAsync();

            var criticalError = error is not null && pingState is PingState.Exception or PingState.InvalidStatus;
            var wasSlowFor5S = pingState == PingState.Slow && oldPingState == PingState.Slow;

            if (criticalError || wasSlowFor5S)
            {
                Console.Beep();
                Logger.Log(error ?? $"Bad ping state: {pingState}");
            }

            if (oldPingState != pingState)
            {
                Logger.Log($"Ping state: {pingState}");
            }
        }

        async Task<(PingState State, string? Error)> GetPingStateAsync()
        {
            try
            {
                var result = await Pinger.SendPingAsync("google.com");
                if (result.Status is not IPStatus.Success)
                {
                    return (PingState.InvalidStatus, $"Bad ping status: {result.Status}");
                }

                if (result.RoundtripTime > HighestGoodPing)
                {
                    return (PingState.Slow, $"Slow ping: {result.RoundtripTime}ms");
                }

                return (PingState.Ok, null);
            }
            catch (Exception e)
            {
                return (PingState.Exception, $"Ping exception: {e}");
            }
        }

        IReadOnlyCollection<NetworkInterface> GetLanInterfaces()
        {
            try
            {
                return NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(x =>
                        x is { NetworkInterfaceType: NetworkInterfaceType.Ethernet, IsReceiveOnly: false, OperationalStatus: OperationalStatus.Up }
                        && x.GetIPProperties().GatewayAddresses.Any()
                    )
                    .ToArray();
            }
            catch (Exception e)
            {
                Logger.Log(e);
                return ArraySegment<NetworkInterface>.Empty;
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
                Logger.Log(e);
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