using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using ImproveWindows.Core.Services;
using ImproveWindows.Core.Structures;
using ImproveWindows.Core.Wifi;
using ImproveWindows.Core.Wifi.Wlan;

namespace ImproveWindows.Core;

public sealed class NetworkService : AppService
{
    private const int HighestGoodPing = 75;
    private readonly Ping _googlePinger = new();
    private readonly Ping _cfPinger = new();
    private readonly MovingAverage16 _movingAverage = new();
    private static readonly IPAddress CloudFlareDnsIpAddress = new(new byte[] { 1, 1, 1, 1 });

    private enum NetState
    {
        None,
        EthernetOk,
        WifiOk,
        WifiBad,
    }

    private enum PingState
    {
        None,
        Ok,
        InvalidStatus,
        Slow,
        Exception,
    }

    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        using var wlanClient = WlanClient.CreateClient();

        LogInfo("Started");
        var netState = NetState.None;
        var pingState = PingState.None;

        while (!cancellationToken.IsCancellationRequested)
        {
            CheckNetwork();

            await CheckPingAsync();

            SetStatusKey(
                $"{pingState}, {netState}",
                $"{pingState} (~{_movingAverage.GetAverage()}ms), {netState}",
                pingState != PingState.Ok || netState is not (NetState.WifiOk or NetState.EthernetOk)
            );

            await Task.Delay(5000, cancellationToken);
        }

        return;

        void CheckNetwork()
        {
            NetState newState;
            var lanInterfaces = GetLanInterfaces();
            if (lanInterfaces.Count != 0)
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
                    LogInfo($"Got {wlanInterfaces.Count} Wi-Fi interfaces: {names}");
                    return;
                }

                var wlanInterface = wlanInterfaces.Single();
                var dot11PhyType = GetDot11PhyType(wlanInterface);
                if (dot11PhyType != Dot11PhyType.He)
                {
                    netState = NetState.WifiBad;
                    Console.Beep();
                    LogInfo($"{wlanInterface.Name} is not running in AX, got PHY type {dot11PhyType}");
                    return;
                }

                newState = NetState.WifiOk;
            }

            if (newState == netState)
            {
                return;
            }

            netState = newState;
            LogInfo($"{netState}");
        }

        async Task CheckPingAsync()
        {
            var oldPingState = pingState;

            (pingState, var error) = await GetPingStateAsync();

            if (error is not null)
            {
                LogInfo(error);
            }

            var criticalError = pingState is PingState.Exception or PingState.InvalidStatus;
            var wasSlowFor5S = pingState == PingState.Slow && oldPingState == PingState.Slow;

            if (criticalError || wasSlowFor5S)
            {
                Console.Beep();
            }
        }

        async Task<(PingState State, string? Error)> GetPingStateAsync()
        {
            try
            {
                var results = await Task.WhenAll(_googlePinger.SendPingAsync("google.com"), _cfPinger.SendPingAsync(CloudFlareDnsIpAddress));
                var ipStatus = results.Min(x => x.Status);
                var roundTripTime = (int)results.Min(x => x.RoundtripTime);
                _movingAverage.Add((int)Math.Round((double)roundTripTime));
                if (ipStatus is not IPStatus.Success)
                {
                    var statuses = string.Join(", ", results.Select(x => $"{x.Address}: {x.Status}ms"));
                    return (PingState.InvalidStatus, $"Bad ping status: {statuses}");
                }

                if (roundTripTime > HighestGoodPing)
                {
                    var times = string.Join(", ", results.Select(x => $"{x.Address}: {x.RoundtripTime}ms"));
                    return (PingState.Slow, $"Slow ping: {times}");
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
                    .Where(
                        x =>
                            x is { NetworkInterfaceType: NetworkInterfaceType.Ethernet, IsReceiveOnly: false, OperationalStatus: OperationalStatus.Up }
                            && x.GetIPProperties().GatewayAddresses.Count != 0
                    )
                    .ToArray();
            }
            catch (Exception e)
            {
                LogError(e);
                return ArraySegment<NetworkInterface>.Empty;
            }
        }

        IReadOnlyCollection<WlanInterface> GetWlanInterfaces()
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
                LogError(e);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _googlePinger.Dispose();
            _cfPinger.Dispose();
        }
        base.Dispose(disposing);
    }
}