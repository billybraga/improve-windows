namespace ImproveWindows.Core.Wifi.Wlan;

public sealed class WlanHostedNetwork
{
    // FIELDS ==================================================================

    private readonly WlanClient _client;

    // CONSTRUCTORS ============================================================

    private WlanHostedNetwork(WlanClient client)
    {
        _client = client;
        InitSettings();
    }

    public static WlanHostedNetwork CreateHostedNetwork(WlanClient client)
    {
        return new WlanHostedNetwork(client);
    }

    // METHODS =================================================================

    private WlanHostedNetworkReason InitSettings()
    {
        Util.ThrowIfError(NativeMethods.WlanHostedNetworkInitSettings(_client.clientHandle, out var failReason, IntPtr.Zero));
        return failReason;
    }

    // INTERNALS ===============================================================
}