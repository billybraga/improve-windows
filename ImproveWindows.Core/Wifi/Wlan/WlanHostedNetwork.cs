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

    private void InitSettings()
    {
        WifiUtil.ThrowIfError(NativeMethods.WlanHostedNetworkInitSettings(_client.clientHandle, out _, IntPtr.Zero));
    }

    // INTERNALS ===============================================================
}