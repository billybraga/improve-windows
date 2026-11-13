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
}
