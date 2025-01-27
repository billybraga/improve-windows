using System.Net.NetworkInformation;
using FluentAssertions;
using ImproveWindows.Core.Network;
using Xunit.Abstractions;

namespace ImproveWindows.Tests;

public class PingExtensionsTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public PingExtensionsTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task GetTraceRouteTest()
    {
        using var ping = new Ping();
        var traceRoute = await NetUtils.GetTraceRouteAsync("google.com", CancellationToken.None);
        _testOutputHelper.WriteLine(traceRoute);
        _ = traceRoute
            .Should()
            .Contain("ms");
    }
}