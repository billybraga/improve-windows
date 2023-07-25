using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;

namespace ImproveWindows.Cli;

public static class Program
{
    public static async Task Main(string[] _)
    {
        try
        {
            Console.Write("Starting, ");
            var coreAudioController = new CoreAudioController();
            var defaultPlaybackDevice = coreAudioController.DefaultPlaybackDevice;
            var sessionController = defaultPlaybackDevice.SessionController;

            sessionController.SessionCreated.Subscribe(OnSessionCreated);
            
            Console.Write("subscribed, ");

            foreach (var session in await sessionController.AllAsync())
            {
                AdjustSessionVolume(session);
            }
            
            Console.Write("and adjusted. Running, press on a key to quit!");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        Console.ReadKey();
    }

    private static void OnSessionCreated(IAudioSession args)
    {
        AdjustSessionVolume(args);
    }

    private static void AdjustSessionVolume(IAudioSession session)
    {
        if (session.IsSystemSession)
        {
            session.Volume = 25;
        }
        
        if (session.DisplayName.Equals("microsoft teams", StringComparison.OrdinalIgnoreCase))
        {
            session.Volume = 50;
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