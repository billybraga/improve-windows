using AudioSwitcher.AudioApi;

namespace ImproveWindows.Core.Extensions;

public static class AudioExtensions
{
    private static readonly Guid Nc25Id = new Guid("9e55699f-bc3b-4181-b4b1-774998be1110");

    public static string GetDeviceName(this IDevice device)
    {
        if (device.Id == Nc25Id)
        {
            return "NC-25";
        }

        return device.Name;
    }
}