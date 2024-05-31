using System.Diagnostics;
using System.Reflection;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;
using ImproveWindows.Core.Extensions;
using ImproveWindows.Core.Services;
using ImproveWindows.Core.Windows;

namespace ImproveWindows.Core;

public class AudioLevelsService : AppService
{
    private readonly struct LevelStateSession
    {
        public required string Id { get; init; }
        public required int Volume { get; init; }
    }
    
    private sealed record LevelState
    {
        public LevelStateSession? CurrentSession { get; set; }
        
        public bool Valid => CurrentSession == null
            || (
                CurrentSession.Value.Volume >= MinExpectedLevel
                && CurrentSession.Value.Volume <= MaxExpectedLevel
            );
        
        public string Name { get; init; }
        public int MinExpectedLevel { get; init; }
        public int MaxExpectedLevel { get; init; }
        
        public LevelState(string name, int minExpectedLevel, int? maxExpectedLevel = null)
        {
            Name = name;
            MinExpectedLevel = minExpectedLevel;
            MaxExpectedLevel = maxExpectedLevel ?? MinExpectedLevel;
        }
        
        public override string ToString()
        {
            var state = Valid
                ? "✅"
                : "❌";
            
            return $"{Name} {state}";
        }
    }
    
    private static readonly LevelState TeamsNotificationsLevel = new("Notif", 50);
    private static readonly LevelState TeamsCallLevel = new("Call", 80, 100);
    private static readonly LevelState ChromeLevel = new("Chrome", 60, 100);
    private static readonly LevelState SystemLevel = new("System", 25);
    private static readonly LevelState YtmLevel = new("YTM", 40, 100);
    
    private static readonly Func<Process, int> GetParentProcessId = typeof(Process)
        .GetProperty("ParentProcessId", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetMethod!
        .CreateDelegate<Func<Process, int>>();
    
    private IAudioSession? _teamsCaptureSession;
    private bool _initialized;
    private IAudioController? _coreAudioController;
    
    public bool? IsMicMuteState => _teamsCaptureSession?.IsMuted ?? _coreAudioController?.DefaultCaptureDevice.IsMuted;
    
    private readonly IReadOnlyCollection<LevelState> _levels = new List<LevelState>
    {
        TeamsNotificationsLevel,
        TeamsCallLevel,
        ChromeLevel,
        YtmLevel,
        SystemLevel,
    };
    
    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting");
        using var coreAudioController = new CoreAudioController();
        _coreAudioController = coreAudioController;
        
        var defaultPlaybackSetup = ConfigureDefaultAudioPlaybackDevice();
        
        try
        {
            coreAudioController.AudioDeviceChanged.Subscribe(
                args =>
                {
                    LogInfo($"Received {args.ChangedType} for {args.Device.GetDeviceName()}");
                    switch (args.ChangedType)
                    {
                        case DeviceChangedType.DeviceAdded:
                            if (args.Device.IsCaptureDevice)
                            {
                                AddAudioCaptureDevice((CoreAudioDevice) args.Device);
                            }
                            
                            break;
                        
                        case DeviceChangedType.DefaultChanged:
                            if (args.Device.IsPlaybackDevice)
                            {
                                // ReSharper disable once AccessToDisposedClosure
                                defaultPlaybackSetup.Dispose();
                                defaultPlaybackSetup = ConfigureDefaultAudioPlaybackDevice();
                            }
                            
                            break;
                        
                        case DeviceChangedType.StateChanged:
                            LogInfo($"{args.Device.Name} is {args.Device.State}");
                            break;
                        
                        default:
                            throw new ArgumentOutOfRangeException($"Got {args.ChangedType}, what is it?");
                    }
                }
            );
            
            foreach (var captureDevice in (await coreAudioController.GetCaptureDevicesAsync()).OrderBy(x => x.State))
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
        finally
        {
            defaultPlaybackSetup.Dispose();
        }
        
        IDisposable ConfigureDefaultAudioPlaybackDevice()
        {
            LogInfo("Configuring default playback");
            
            // ReSharper disable once AccessToDisposedClosure
            var coreAudioDevice = coreAudioController.DefaultPlaybackDevice;
            
            if (coreAudioDevice is null)
            {
                return DelegateDisposable.Create(() => { });
            }
            
            var defaultPlaybackSessionController = coreAudioDevice.SessionController;
            var sessionCreatedSbn = defaultPlaybackSessionController.SessionCreated.Subscribe(
                session =>
                {
                    LogInfo($"Session created for {session.DisplayName}");
                    HandleSession(session);
                }
            );
            var sessionDisconnectSbn = defaultPlaybackSessionController.SessionDisconnected.Subscribe(HandleSessionDisconnected);
            
            foreach (var session in defaultPlaybackSessionController)
            {
                HandleSession(session);
            }
            
            _initialized = true;
            
            UpdateStatus();
            
            LogInfo("Configured default playback");
            
            return DelegateDisposable.Create(
                () =>
                {
                    sessionCreatedSbn.Dispose();
                    sessionDisconnectSbn.Dispose();
                }
            );
        }
    }
    
    private void AddAudioCaptureDevice(CoreAudioDevice captureDevice)
    {
        var captureController = captureDevice.SessionController;
        if (captureController is null)
        {
            return;
        }
        
        LogInfo($"Adding {captureDevice.GetDeviceName()}");
        var processCaptureSession = ProcessCaptureSession;
        var processCaptureSessionDisconnection = ProcessCaptureSessionDisconnection;
        captureController.SessionCreated.Subscribe(ProcessSessionCreated);
        captureController.SessionDisconnected.Subscribe(processCaptureSessionDisconnection);
        
        foreach (var session in captureController)
        {
            ProcessSessionCreated(session);
        }
        
        void ProcessSessionCreated(IAudioSession session)
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
    
    public bool? ChangeMicMuteState()
    {
        bool isMuted;
        IDevice captureDevice;
        IAudioSession? session;
        if (_teamsCaptureSession is not null)
        {
            session = _teamsCaptureSession;
            isMuted = _teamsCaptureSession.IsMuted;
            captureDevice = _teamsCaptureSession.Device;
        }
        else if (_coreAudioController is not null)
        {
            session = null;
            captureDevice = _coreAudioController.DefaultCaptureDevice;
            isMuted = captureDevice.IsMuted;
        }
        else
        {
            return null;
        }
        
        UpdateStatus();
        var newMuteState = !isMuted;
        var action = newMuteState
            ? "Muting"
            : "Opening";
        LogInfo($"{action} device {captureDevice.GetDeviceName()}");
        captureDevice.Mute(newMuteState);
        if (session != null)
        {
            LogInfo($"Muting session {session.DisplayName}");
            session.IsMuted = newMuteState;
        }
        
        return newMuteState;
    }
    
    private void UpdateStatus()
    {
        var levels = string.Join(", ", _levels.Where(x => x.CurrentSession != null));
        var error = _levels.Any(x => !x.Valid);
        SetStatus(
            _teamsCaptureSession is null
                ? $"No teams session. {levels}"
                : levels,
            error
        );
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
                    LogInfo(
                        $"Changing teams capture session from \"{_teamsCaptureSession?.Device.GetDeviceName()}\" to \"{captureDevice.GetDeviceName()}\""
                    );
                }
                
                _teamsCaptureSession = captureAudioSession;
                UpdateStatus();
            }
        }
        
        var vol = captureAudioSession.IsMuted ? " -  " : $"{captureAudioSession.Volume:00}%";
        var state = captureAudioSession.SessionState == AudioSessionState.Active
            ? ""
            : $" ({captureAudioSession.SessionState})";
        
        LogInfo($"Capture: [{vol}] [{captureDevice?.GetDeviceName()}] {captureAudioSession.DisplayName}{state}");
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
    
    private void HandleSession(IAudioSession args)
    {
        LogInfo($"Handling session {args.DisplayName} ({args.SessionState}) on {args.Device.Name}");
        
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
        
        session.Volume = state.MinExpectedLevel;
        UpdateTrackedVolume(state, session);
        LogInfo($"{state}, {session.Volume}%");
        return state;
    }
    
    private void UpdateTrackedVolume(LevelState levelState, IAudioSession session)
    {
        levelState.CurrentSession = new LevelStateSession { Id = session.Id, Volume = (int) session.Volume };
        
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
            using var process = Process.GetProcessById(session.ProcessId);
            var parentProcessId = GetParentProcessId(process);
            using var parentProcess = Process.GetProcessById(parentProcessId);
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
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _coreAudioController?.Dispose();
        }
        
        base.Dispose(disposing);
    }
}