namespace ImproveWindows.Ui.Webcam;

internal sealed record WebcamControl(WebcamSetting Setting, int Min, int Max, int Step, int Default, bool AutoSupported);
