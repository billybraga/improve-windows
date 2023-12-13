using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;

namespace ImproveWindows.Core.Audio;

public class AudioCaptureDeviceMonitor : IDisposable
{
    public AudioCaptureDeviceMonitor(CoreAudioDevice captureDevice, Action<IAudioSession> processCaptureSession, Action<string> processCaptureSessionDisconnection)
    {
        var captureController = captureDevice.SessionController;
        captureController.SessionCreated.Subscribe(processCaptureSession);
        captureController.SessionDisconnected.Subscribe(processCaptureSessionDisconnection);

        foreach (var session in captureController)
        {
            session.MuteChanged.Subscribe(x => processCaptureSession(x.Session));
            session.StateChanged.Subscribe(x => processCaptureSession(x.Session));
            session.VolumeChanged.Subscribe(x => processCaptureSession(x.Session));
            processCaptureSession(session);
        }
    }
    
    public void Dispose()
    {
    }
}