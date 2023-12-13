using System.Diagnostics;
using System.Reflection;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;
using ImproveWindows.Core.Services;
using ImproveWindows.Core.Windows;

namespace ImproveWindows.Core;

public class AudioLevels : AppService
{
    private const int TeamsNotificationsLevel = 50;
    private const int TeamsCallLevel = 80;
    private const int ChromeLevel = 100;
    private const int SystemLevel = 25;
    private const int YtmLevel = 25;

    private static readonly Func<Process, int> GetParentProcessId = typeof(Process)
        .GetProperty("ParentProcessId", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetMethod!
        .CreateDelegate<Func<Process, int>>();

    private IAudioSession? _teamsCaptureSession;

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting");
        var coreAudioController = new CoreAudioController();
        var defaultPlaybackDevice = coreAudioController.DefaultPlaybackDevice;
        var defaultPlaybackSessionController = defaultPlaybackDevice.SessionController;

        defaultPlaybackSessionController.SessionCreated.Subscribe(ConfigureSession);

        LogInfo("Subscribed");

        foreach (var session in defaultPlaybackSessionController)
        {
            ConfigureSession(session);
        }
        
        SetStatus();

        foreach (var captureDevice in await coreAudioController.GetCaptureDevicesAsync())
        {
            var captureController = captureDevice.SessionController;
            captureController.SessionCreated.Subscribe(LogCaptureSession);

            foreach (var session in captureController)
            {
                session.MuteChanged.Subscribe(x => LogCaptureSession(x.Session));
                session.StateChanged.Subscribe(x => LogCaptureSession(x.Session));
                session.VolumeChanged.Subscribe(x => LogCaptureSession(x.Session));
                LogCaptureSession(session);
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
        }
    }

    public bool? ChangeTeamsMicMuteState()
    {
        if (_teamsCaptureSession is null)
        {
            SetStatus("No teams session", true);
            return null;
        }

        SetStatus();
        return _teamsCaptureSession.IsMuted = !_teamsCaptureSession.IsMuted;
    }

    public bool? GetTeamsMicMuteState()
    {
        return _teamsCaptureSession?.IsMuted;
    }

    private void LogCaptureSession(IAudioSession captureAudioSession)
    {
        if (captureAudioSession.IsSystemSession)
        {
            return;
        }
        
        var captureDevice = captureAudioSession.Device;
        if (captureAudioSession.DisplayName.Contains("teams", StringComparison.OrdinalIgnoreCase))
        {
            if (_teamsCaptureSession is null
                || (
                    captureAudioSession.SessionState == AudioSessionState.Active
                    && _teamsCaptureSession.SessionState != AudioSessionState.Active
                ))
            {
                if (_teamsCaptureSession != captureAudioSession)
                {
                    LogInfo($"Changing teams capture session from \"{_teamsCaptureSession?.Device.Name}\" to \"{captureDevice.Name}\"");
                }

                _teamsCaptureSession = captureAudioSession;
                SetStatus();
            }
        }

        var vol = captureAudioSession.IsMuted ? " -  " : $"{captureAudioSession.Volume:00}%";
        var state = captureAudioSession.SessionState == AudioSessionState.Active
            ? ""
            : $" ({captureAudioSession.SessionState})";
        
        LogInfo($"Capture: [{vol}] [{captureDevice?.Name}] {captureAudioSession.DisplayName}{state}");
    }

    private void ConfigureSession(IAudioSession args)
    {
        var disposables = new List<IDisposable>();

        var name = AdjustSessionVolume(args);

        if (name is null)
        {
            return;
        }

        Subscribe(args.VolumeChanged, x => LogInfo($"{name} externally changed to {x.Volume}"));

        Subscribe(
            args.StateChanged,
            x =>
            {
                if (x.State is AudioSessionState.Expired or AudioSessionState.Inactive)
                {
                    Dispose();
                }
            }
        );

        Subscribe(args.Disconnected, _ => { Dispose(); });

        return;

        void Subscribe<T>(IObservable<T> observable, Action<T> callback)
        {
            disposables.Add(observable.Subscribe(callback));
        }

        void Dispose()
        {
            LogInfo($"{name}, disposing");
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }
    }

    private string? AdjustSessionVolume(IAudioSession session)
    {
        var sessionInfo = GetSessionInfo(session);
        if (sessionInfo is null)
        {
            return null;
        }

        var (name, volume) = sessionInfo.Value;
        session.Volume = volume;
        LogInfo($"{name}, {volume}%");
        return name;
    }

    private static (string Name, int Volume)? GetSessionInfo(IAudioSession session)
    {
        if (session.IsSystemSession)
        {
            return ("System", SystemLevel);
        }

        if (session.DisplayName.Equals("microsoft teams", StringComparison.OrdinalIgnoreCase))
        {
            var process = Process.GetProcessById(session.ProcessId);
            var commandLine = process.GetCommandLine();
            var isAudioService = commandLine.Contains("AudioService");
            return isAudioService
                ? AdjustTeamsNotifications()
                : AdjustTeamsCalls();
        }

        if (session.DisplayName.Equals("Microsoft Teams (work or school)", StringComparison.OrdinalIgnoreCase))
        {
            return AdjustTeamsCalls();
        }

        if (session.DisplayName == "Microsoft Edge WebView2")
        {
            var process = Process.GetProcessById(session.ProcessId);
            var parentProcessId = GetParentProcessId(process);
            var parentProcess = Process.GetProcessById(parentProcessId);
            var parentProcessCommandLine = parentProcess.GetCommandLine();
            if (parentProcessCommandLine.Contains("--webview-exe-name=ms-teams.exe"))
            {
                return AdjustTeamsNotifications();
            }
        }

        if (session.ExecutablePath is not null
            && session.ExecutablePath.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase)
            && session.ExecutablePath.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            return ("Chrome Beta", YtmLevel);
        }

        if (session.ExecutablePath is not null
            && session.ExecutablePath.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase)
            && !session.ExecutablePath.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            return ("Chrome", ChromeLevel);
        }

        return null;

        (string Name, int Volume) AdjustTeamsNotifications()
        {
            return ("Teams Notifications", TeamsNotificationsLevel);
        }

        (string Name, int Volume) AdjustTeamsCalls()
        {
            return ("Teams Call", TeamsCallLevel);
        }
    }
}