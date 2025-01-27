using AudioSwitcher.AudioApi;

namespace ImproveWindows.Core.Extensions;

public static class AudioExtensions
{
    private static readonly Guid Nc25Id = new("9e55699f-bc3b-4181-b4b1-774998be1110");

    public static string GetDeviceName(this IDevice device)
    {
        return device.Id == Nc25Id ? "NC-25" : device.Name;
    }
}