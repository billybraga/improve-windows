using FluentAssertions;
using ImproveWindows.Core.Network;
using ImproveWindows.Core.Network.Wlan;

namespace ImproveWindows.Tests;

public class NetUtilsTests
{
    [Fact]
    public void IsWlanInterfaceValidTest()
    {
        using var wlanClient = WlanClient.CreateClient();
        _ = NetUtils.IsWlanInterfaceValid(wlanClient, exception => throw exception, Console.WriteLine)
            .Should()
            .BeTrue();
    }
}