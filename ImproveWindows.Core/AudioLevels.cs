using System.Diagnostics;
using System.Reflection;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;
using ImproveWindows.Core.Services;
using ImproveWindows.Core.Windows;

namespace ImproveWindows.Core;

public class AudioLevels : AppService
{
    private record LevelState(string Name, int ExpectedLevel)
    {
        public IAudioSession? CurrentSession { get; set; }
        public bool Valid => CurrentSession is null || Math.Abs(ExpectedLevel - CurrentSession.Volume) < double.Epsilon;

        public override string ToString()
        {
            var state = Valid
                ? "✅"
                : "❌";

            return $"{Name} {state}";
        }
    }

    private static readonly LevelState TeamsNotificationsLevel = new("Notif", 50);
    private static readonly LevelState TeamsCallLevel = new("Call", 80);
    private static readonly LevelState ChromeLevel = new("Chrome", 100);
    private static readonly LevelState SystemLevel = new("System", 25);
    private static readonly LevelState YtmLevel = new("YTM", 25);

    private static readonly Func<Process, int> GetParentProcessId = typeof(Process)
        .GetProperty("ParentProcessId", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetMethod!
        .CreateDelegate<Func<Process, int>>();

    private IAudioSession? _teamsCaptureSession;
    private bool _initialized;

    private readonly IReadOnlyCollection<LevelState> _levels = new List<LevelState>
    {
        TeamsNotificationsLevel,
        TeamsCallLevel,
        ChromeLevel,
        YtmLevel,
        SystemLevel,
    };

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting");
        var coreAudioController = new CoreAudioController();
        var defaultPlaybackDevice = coreAudioController.DefaultPlaybackDevice;
        var defaultPlaybackSessionController = defaultPlaybackDevice.SessionController;

        defaultPlaybackSessionController.SessionCreated.Subscribe(ConfigureSession);
        defaultPlaybackSessionController.SessionDisconnected.Subscribe(HandleSessionDisconnected);

        LogInfo("Subscribed");

        foreach (var session in defaultPlaybackSessionController)
        {
            ConfigureSession(session);
        }

        _initialized = true;

        UpdateStatus();

        coreAudioController.AudioDeviceChanged.Subscribe(
            args =>
            {
                switch (args.ChangedType)
                {
                    case DeviceChangedType.DeviceAdded:
                        AddAudioCaptureDevice((CoreAudioDevice)args.Device);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        );

        foreach (var captureDevice in await coreAudioController.GetCaptureDevicesAsync())
        {
            AddAudioCaptureDevice(captureDevice);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_levels.Any(x => !x.Valid))
            {
                Console.Beep();
            }

            await Task.Delay(10000, cancellationToken);
        }
    }

    private void AddAudioCaptureDevice(CoreAudioDevice captureDevice)
    {
        var processCaptureSession = ProcessCaptureSession;
        var processCaptureSessionDisconnection = ProcessCaptureSessionDisconnection;
        var captureController = captureDevice.SessionController;
        if (captureController is null)
        {
            return;
        }

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

    private void ProcessCaptureSessionDisconnection(string id)
    {
        if (_teamsCaptureSession?.Id == id)
        {
            _teamsCaptureSession = null;
            UpdateStatus();
        }
    }

    public bool? ChangeTeamsMicMuteState()
    {
        if (_teamsCaptureSession is null)
        {
            return null;
        }

        UpdateStatus();
        var newMuteState = !_teamsCaptureSession.IsMuted;
        _teamsCaptureSession.Device.Mute(newMuteState);
        _teamsCaptureSession.IsMuted = newMuteState;
        return newMuteState;
    }

    private void UpdateStatus()
    {
        var levels = string.Join(", ", _levels.Where(x => x.CurrentSession is not null));
        var error = _levels.Any(x => !x.Valid);
        SetStatus(
            _teamsCaptureSession is null
                ? $"No teams session. {levels}"
                : levels,
            error
        );
    }

    public bool? GetTeamsMicMuteState()
    {
        return _teamsCaptureSession?.IsMuted;
    }

    private void ProcessCaptureSession(IAudioSession captureAudioSession)
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
                UpdateStatus();
            }
        }

        var vol = captureAudioSession.IsMuted ? " -  " : $"{captureAudioSession.Volume:00}%";
        var state = captureAudioSession.SessionState == AudioSessionState.Active
            ? ""
            : $" ({captureAudioSession.SessionState})";

        LogInfo($"Capture: [{vol}] [{captureDevice?.Name}] {captureAudioSession.DisplayName}{state}");
    }

    private void HandleSessionDisconnected(string id)
    {
        foreach (var levelState in _levels)
        {
            if (levelState.CurrentSession?.Id == id)
            {
                levelState.CurrentSession = null;
                UpdateStatus();
            }
        }
    }

    private void ConfigureSession(IAudioSession args)
    {
        var state = AdjustSessionVolume(args);

        if (state is null)
        {
            return;
        }

        args.VolumeChanged.Subscribe(
            x =>
            {
                UpdateTrackedVolume(state, x.Session);
                LogInfo($"{state} externally changed to {x.Volume}");
            }
        );
    }

    private LevelState? AdjustSessionVolume(IAudioSession session)
    {
        var state = GetSessionInfo(session);
        if (state is null)
        {
            return null;
        }

        session.Volume = state.ExpectedLevel;
        UpdateTrackedVolume(state, session);
        LogInfo($"{state}, {session.Volume}%");
        return state;
    }

    private void UpdateTrackedVolume(LevelState levelState, IAudioSession session)
    {
        levelState.CurrentSession = session;

        if (_initialized)
        {
            UpdateStatus();
        }
    }

    private static LevelState? GetSessionInfo(IAudioSession session)
    {
        if (session.IsSystemSession)
        {
            return SystemLevel;
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
            return YtmLevel;
        }

        if (session.ExecutablePath is not null
            && session.ExecutablePath.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase)
            && !session.ExecutablePath.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            return ChromeLevel;
        }

        return null;

        LevelState AdjustTeamsNotifications()
        {
            return TeamsNotificationsLevel;
        }

        LevelState AdjustTeamsCalls()
        {
            return TeamsCallLevel;
        }
    }
}