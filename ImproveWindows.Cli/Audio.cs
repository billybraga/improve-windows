using System.Diagnostics;
using System.Reflection;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;
using ImproveWindows.Cli.Windows;

namespace ImproveWindows.Cli;

public static class Audio
{
    private const int TeamsNotifLevel = 50;
    private const int TeamsCallLevel = 100;

    private static readonly Func<Process, int> GetParentProcessId = typeof(Process)
        .GetProperty("ParentProcessId", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetMethod!
        .CreateDelegate<Func<Process, int>>();

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Audio: Starting");
        var coreAudioController = new CoreAudioController();
        var defaultPlaybackDevice = coreAudioController.DefaultPlaybackDevice;
        var sessionController = defaultPlaybackDevice.SessionController;

        sessionController.SessionCreated.Subscribe(OnSessionCreated);

        Console.WriteLine("Audio: Subscribed");

        foreach (var session in await sessionController.AllAsync())
        {
            AdjustSessionVolume(session);
        }

        Console.WriteLine("Audio: Adjusted");

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
        }
    }

    private static void OnSessionCreated(IAudioSession args)
    {
        AdjustSessionVolume(args);
    }

    private static void AdjustSessionVolume(IAudioSession session)
    {
        Debug.WriteLine("adjust");

        if (session.IsSystemSession)
        {
            session.Volume = 25;
        }

        if (session.DisplayName.Equals("microsoft teams", StringComparison.OrdinalIgnoreCase))
        {
            var process = Process.GetProcessById(session.ProcessId);
            var commandLine = process.GetCommandLine();
            var isAudioService = commandLine.Contains("AudioService");
            session.Volume = isAudioService
                ? TeamsNotifLevel
                : TeamsCallLevel;
        }

        if (session.DisplayName.Equals("Microsoft Teams (work or school)", StringComparison.OrdinalIgnoreCase))
        {
            var process = Process.GetProcessById(session.ProcessId);
            var isAudioService = process.MainWindowTitle.Contains("AudioService");
            session.Volume = isAudioService
                ? TeamsNotifLevel
                : TeamsCallLevel;
        }

        if (session.DisplayName == "Microsoft Edge WebView2")
        {
            var process = Process.GetProcessById(session.ProcessId);
            var parentProcessId = GetParentProcessId(process);
            var parentProcess = Process.GetProcessById(parentProcessId);
            var parentProcessCommandLine = parentProcess.GetCommandLine();
            if (parentProcessCommandLine.Contains("--webview-exe-name=ms-teams.exe"))
            {
                session.Volume = TeamsNotifLevel;
            }
        }

        if (session.ExecutablePath is not null
            && session.ExecutablePath.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase)
            && session.ExecutablePath.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            session.Volume = 25;
        }

        if (session.ExecutablePath is not null
            && session.ExecutablePath.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase)
            && !session.ExecutablePath.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            session.Volume = 100;
        }
    }
}