namespace ImproveWindows.Core.Network.Wlan;

internal sealed class WlanHostedNetwork
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
        NetUtils.ThrowIfError(NativeMethods.WlanHostedNetworkInitSettings(_client.clientHandle, out _, IntPtr.Zero));
    }

    // INTERNALS ===============================================================
}