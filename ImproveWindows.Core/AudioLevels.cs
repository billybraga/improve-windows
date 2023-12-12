using System.Diagnostics;
using System.Reflection;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;
using ImproveWindows.Core.Logging;
using ImproveWindows.Core.Services;
using ImproveWindows.Core.Windows;

namespace ImproveWindows.Core;

public class AudioLevels : IAppService
{
    private readonly Logger _logger;

    private const int TeamsNotificationsLevel = 50;
    private const int TeamsCallLevel = 80;
    private const int ChromeLevel = 100;
    private const int SystemLevel = 25;
    private const int YtmLevel = 25;

    private static readonly Func<Process, int> GetParentProcessId = typeof(Process)
        .GetProperty("ParentProcessId", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetMethod!
        .CreateDelegate<Func<Process, int>>();

    private IAudioSession? _teamsCapture;
    
    public AudioLevels(Logger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.Log("Starting");
        var coreAudioController = new CoreAudioController();
        var defaultPlaybackDevice = coreAudioController.DefaultPlaybackDevice;
        var defaultPlaybackSessionController = defaultPlaybackDevice.SessionController;

        defaultPlaybackSessionController.SessionCreated.Subscribe(ConfigureSession);

        _logger.Log("Subscribed");

        foreach (var session in defaultPlaybackSessionController)
        {
            ConfigureSession(session);
        }

        var defaultCaptureController = coreAudioController.DefaultCaptureDevice.SessionController;
        defaultCaptureController.SessionCreated.Subscribe(LogCaptureSession);

        foreach (var session in defaultCaptureController)
        {
            session.MuteChanged.Subscribe(x => LogCaptureSession(x.Session));
            session.StateChanged.Subscribe(x => LogCaptureSession(x.Session));
            session.VolumeChanged.Subscribe(x => LogCaptureSession(x.Session));
            LogCaptureSession(session);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
        }
    }

    public bool? ChangeTeamsMicMuteState()
    {
        if (_teamsCapture is null)
        {
            return null;
        }

        _teamsCapture.IsMuted = !_teamsCapture.IsMuted;
        return _teamsCapture.IsMuted;
    }

    private async Task BeepsAsync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            Console.Beep();
            if (i < count - 1) ;
            {
                await Task.Delay(400, cancellationToken);
            }
        }
    }

    private void LogCaptureSession(IAudioSession captureAudioSession)
    {
        if (captureAudioSession.DisplayName.Contains("teams", StringComparison.OrdinalIgnoreCase))
        {
            if (_teamsCapture is not null && _teamsCapture != captureAudioSession)
            {
                _logger.Log($"Changing teams capture session from \"{_teamsCapture.DisplayName}\" to \"{captureAudioSession.DisplayName}\"");
            }
            
            _teamsCapture = captureAudioSession;
        }
        
        var vol = captureAudioSession.IsMuted ? " -  " : $"{captureAudioSession.Volume:00}%";
        var state = captureAudioSession.SessionState == AudioSessionState.Active
            ? ""
            : $" ({captureAudioSession.SessionState})";
        _logger.Log($"Capture: [{vol}] {captureAudioSession.DisplayName}{state}");
    }

    private void ConfigureSession(IAudioSession args)
    {
        var disposables = new List<IDisposable>();

        var name = AdjustSessionVolume(args);

        if (name is null)
        {
            return;
        }
        
        Subscribe(args.VolumeChanged, x => _logger.Log($"{name} externally changed to {x.Volume}"));

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
            _logger.Log($"{name}, disposing");
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
        _logger.Log($"{name}, {volume}%");
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