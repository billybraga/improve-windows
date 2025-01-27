using System.Net;
using System.Net.NetworkInformation;
using ImproveWindows.Core.Network;
using ImproveWindows.Core.Network.Wlan;
using ImproveWindows.Core.Services;
using ImproveWindows.Core.Structures;

namespace ImproveWindows.Core;

public sealed class NetworkService : AppService
{
    private const int HighestGoodPing = 250;
    private readonly Ping _googlePinger = new();
    private readonly Ping _cfPinger = new();
    private readonly MovingAverage16 _movingAverage = new();
    private static readonly IPAddress CloudFlareDnsIpAddress = new([1, 1, 1, 1]);

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
                $"{pingState}, ({_movingAverage.ToStringWithTemplate()})ms, {netState}",
                pingState != PingState.Ok || netState is not (NetState.WifiOk or NetState.EthernetOk)
            );

            await Task.Delay(TimeSpan.FromMinutes(15), cancellationToken);
        }

        return;

        void CheckNetwork()
        {
            NetState newState;
            var lanInterfaces = NetUtils.GetLanInterfaces(LogError);
            if (lanInterfaces.Count != 0)
            {
                newState = NetState.EthernetOk;
            }
            else
            {
                if (!NetUtils.IsWlanInterfaceValid(wlanClient, LogError, LogInfo))
                {
                    netState = NetState.WifiBad;
                    Console.Beep();
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
                var roundTripTime = (int) results.Min(x => x.RoundtripTime);
                _movingAverage.Add((int) Math.Round((double) roundTripTime));
                if (ipStatus is not IPStatus.Success)
                {
                    var statuses = string.Join(", ", results.Select(x => $"{x.Address}: {x.Status}ms"));
                    return (PingState.InvalidStatus, $"Bad ping status: {statuses}");
                }

                if (roundTripTime > HighestGoodPing)
                {
                    var times = string.Join(", ", results.Select(x => $"{x.Address}: {x.RoundtripTime}ms"));
                    var traceRoute = await NetUtils.GetTraceRouteAsync("google.com", cancellationToken);
                    return (PingState.Slow, $"Slow ping: {times}\n{traceRoute}");
                }

                return (PingState.Ok, null);
            }
            catch (Exception e)
            {
                return (PingState.Exception, $"Ping exception: {e}");
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