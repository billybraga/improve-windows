using System.IO;
using System.Reflection;
using NAudio.Wave;

namespace ImproveWindows.Ui.Audio;

internal interface ILoopAudioPlayer : IDisposable
{
    void TryDispose();
}

internal sealed class WaveOutLoopAudioPlayer : ILoopAudioPlayer
{
    private WaveOut? _soundPlayer;
    private bool _isDisposed;
    private readonly Stream _stream;
    private readonly int _deviceId;
    private LoopStream? _loopStream;

    public WaveOutLoopAudioPlayer(Stream stream, int deviceId)
    {
        _stream = stream;
        _deviceId = deviceId;
        Start();
    }

    private void Start()
    {
        _stream.Position = 0;
        var reader = new WaveFileReader(_stream);
        _loopStream = new LoopStream(reader);
        _soundPlayer = new WaveOut { DeviceNumber = _deviceId };
        _soundPlayer.PlaybackStopped += SoundPlayer_PlaybackStopped;
        _soundPlayer.Init(_loopStream);
        _soundPlayer.Play();
    }

    private void Stop()
    {
        if (_soundPlayer != null)
        {
            _soundPlayer.PlaybackStopped -= SoundPlayer_PlaybackStopped;
            _soundPlayer.Stop();
            _soundPlayer.Dispose();
            _soundPlayer = null;
            _loopStream?.Dispose();
            _loopStream = null;
            _stream.Dispose();
        }
    }

    private void SoundPlayer_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!_isDisposed)
        {
            try
            {
                Stop();
            }
            catch
            {
                //Do nothing
            }

            Start();
        }
    }

    public void TryDispose()
    {
        try
        {
            Dispose();
        }
        catch
        {
            //Do nothing.
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        Stop();
    }
}

internal sealed class AudioControl
{
    private static readonly Lazy<AudioControl> InstanceLazy = new(() => new AudioControl());
    public static AudioControl Instance => InstanceLazy.Value;

    public bool IsRunning { get; private set; }
    private List<ILoopAudioPlayer> _audioPlayers = new();
    private Stream? _sound;

    /// <summary>
    /// Start the audio playback which will keep the SPDIF link alive.
    /// </summary>
    public void Start()
    {
        foreach (var player in _audioPlayers)
        {
            player.TryDispose();
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.Resources.inaudible.wav";

        _sound = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not get {resourceName}");

        _audioPlayers = PlaySoundAsync(_sound);

        IsRunning = true;
    }

    private static List<ILoopAudioPlayer> PlaySoundAsync(Stream sound)
    {
        var deviceIds = new HashSet<int>();
        const string deviceName = "Audio Driver for Dis";
        for (var deviceId = -1; deviceId < WaveOut.DeviceCount; deviceId++)
        {
            var capabilities = WaveOut.GetCapabilities(deviceId);
            if (capabilities.ProductName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
            {
                deviceIds.Add(deviceId);
            }
        }

        var players = new List<ILoopAudioPlayer>(deviceIds.Count);
        foreach (var deviceId in deviceIds)
        {
            players.Add(new WaveOutLoopAudioPlayer(sound, deviceId: deviceId));
        }

        return players;
    }

    /// <summary>
    /// Stop the audio playback which will stop the SPDIF link.
    /// </summary>
    public void Stop()
    {
        foreach (var player in _audioPlayers)
        {
            player.TryDispose();
        }

        _audioPlayers.Clear();
        IsRunning = false;
    }
}