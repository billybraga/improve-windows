namespace ImproveWindows.Ui.Webcam;

internal sealed record WebcamSetting(string Kind, string Property, int Value, bool Auto)
{
    /// <summary>
    /// When auto, the camera changes the value itself (e.g. autofocus), so only the auto flag has to match.
    /// </summary>
    public bool Matches(WebcamSetting current)
    {
        return current.Auto == Auto && (Auto || current.Value == Value);
    }

    /// <summary>
    /// Auto exposure drives the gain too: writing it succeeds but the camera keeps its own value.
    /// </summary>
    public bool IsDrivenByCamera(IEnumerable<WebcamSetting> allSettings)
    {
        return Property == "Gain" && allSettings.Any(x => x is { Property: "Exposure", Auto: true });
    }
}
