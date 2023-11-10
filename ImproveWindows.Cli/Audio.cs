using System.Diagnostics;
using System.Reflection;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;
using ImproveWindows.Cli.Logging;
using ImproveWindows.Cli.Windows;

namespace ImproveWindows.Cli;

public static class Audio
{
    private static readonly Logger Logger = new("Audio");
    
    private const int TeamsNotificationsLevel = 50;
    private const int TeamsCallLevel = 80;
    private const int ChromeLevel = 100;
    private const int SystemLevel = 25;
    private const int YtmLevel = 25;

    private static readonly Func<Process, int> GetParentProcessId = typeof(Process)
        .GetProperty("ParentProcessId", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetMethod!
        .CreateDelegate<Func<Process, int>>();

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        Logger.Log("Starting");
        var coreAudioController = new CoreAudioController();
        var defaultPlaybackDevice = coreAudioController.DefaultPlaybackDevice;
        var sessionController = defaultPlaybackDevice.SessionController;

        sessionController.SessionCreated.Subscribe(ConfigureSession);

        Logger.Log("Subscribed");

        foreach (var session in await sessionController.AllAsync())
        {
            ConfigureSession(session);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
        }
    }

    private static void ConfigureSession(IAudioSession args)
    {
        var disposables = new List<IDisposable>();

        var name = AdjustSessionVolume(args);

        if (name is null)
        {
            return;
        }

        Subscribe(args.VolumeChanged, x => Logger.Log($"{name} externally changed to {x.Volume}"));

        Subscribe(args.StateChanged, x =>
        {
            if (x.State is AudioSessionState.Expired or AudioSessionState.Inactive)
            {
                Dispose();
            }
        });

        Subscribe(args.Disconnected, _ => { Dispose(); });

        return;

        void Subscribe<T>(IObservable<T> observable, Action<T> callback)
        {
            disposables!.Add(observable.Subscribe(callback));
        }

        void Dispose()
        {
            Logger.Log($"{name}, disposing");
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }
    }

    private static string? AdjustSessionVolume(IAudioSession session)
    {
        var sessionInfo = GetSessionInfo(session);
        if (sessionInfo is null)
        {
            return null;
        }

        var (name, volume) = sessionInfo.Value;
        session.Volume = volume;
        Logger.Log($"{name}, {volume}%");
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