using ImproveWindows.Core.Services;
using ImproveWindows.Ui.Webcam;
using Microsoft.Win32;

namespace ImproveWindows.Ui;

/// <summary>
/// The C615 forgets its image settings (white balance, brightness, ...) whenever it resets
/// (reconnect, sleep, reboot, sometimes when an app opens it), so the saved settings are re-applied whenever they drift.
/// </summary>
internal sealed class WebcamService : AppService
{
    private const string CameraNameMatch = "C615";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    // The camera can take a few seconds to come back after resume / unlock
    private static readonly TimeSpan DelayAfterSystemEvent = TimeSpan.FromSeconds(3);

    private readonly WebcamSettingsStore _store = new();
    private readonly SemaphoreSlim _checkNow = new(0);
    private bool? _wasConnected;

    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Run(EnsureSettings, cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    SetStatus("Error", true);
                    LogError(e);
                }

                if (await _checkNow.WaitAsync(CheckInterval, cancellationToken))
                {
                    await Task.Delay(DelayAfterSystemEvent, cancellationToken);
                }
            }
        }
        finally
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _wasConnected = null;
        }
    }

    public void SaveCurrentSettings()
    {
        _ = Task.Run(
            () =>
            {
                try
                {
                    var current = UvcCamera.Read(CameraNameMatch);
                    if (current is null)
                    {
                        LogInfo($"Can't save, no camera matching {CameraNameMatch}");
                        return;
                    }

                    _store.Save(current.Value.Settings);
                    LogInfo($"Saved {current.Value.Settings.Count} settings from {current.Value.CameraName} to {_store.FilePath}");
                    foreach (var setting in current.Value.Settings)
                    {
                        LogInfo($"  {Describe(setting)}");
                    }

                    _ = _checkNow.Release();
                }
                catch (Exception e)
                {
                    LogError(e);
                }
            }
        );
    }

    private void EnsureSettings()
    {
        var saved = _store.Load();
        if (saved is null)
        {
            SetStatus("No saved settings, set them in Logitech Camera Settings then click 💾");
            return;
        }

        var current = UvcCamera.Read(CameraNameMatch);
        if (current is null)
        {
            if (_wasConnected != false)
            {
                LogInfo($"No camera matching {CameraNameMatch}");
            }

            _wasConnected = false;
            SetStatus("Camera not connected");
            return;
        }

        if (_wasConnected != true)
        {
            LogInfo($"Camera connected: {current.Value.CameraName}");
        }

        _wasConnected = true;

        var drifted = saved
            .Where(s => current.Value.Settings.FirstOrDefault(c => c.Kind == s.Kind && c.Property == s.Property) is not { } c || !s.Matches(c))
            .ToArray();

        if (drifted.Length == 0)
        {
            SetStatus("Settings ok");
            return;
        }

        var results = UvcCamera.Write(CameraNameMatch, drifted);
        if (results is null)
        {
            SetStatus("Camera not connected");
            return;
        }

        var failures = results.Where(r => r.Result != 0).ToArray();
        foreach (var (setting, result) in failures)
        {
            LogInfo($"Setting {setting.Property} failed: 0x{result:X8}");
        }

        LogInfo($"Re-applied {string.Join(", ", drifted.Select(Describe))}");
        SetStatus($"Re-applied {drifted.Length} settings at {DateTime.Now:HH:mm}", failures.Length > 0);
    }

    private static string Describe(WebcamSetting setting)
    {
        return setting.Auto ? $"{setting.Property} auto" : $"{setting.Property} {setting.Value}";
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect)
        {
            LogInfo($"Checking after {e.Reason}");
            _ = _checkNow.Release();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            LogInfo("Checking after resume");
            _ = _checkNow.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _checkNow.Dispose();
        }

        base.Dispose(disposing);
    }
}
