using System.IO;
using System.Text.Json;

namespace ImproveWindows.Ui.Webcam;

internal sealed class WebcamSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public WebcamSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "webcam-settings.json");
    }

    public string FilePath => _filePath;

    public List<WebcamSetting>? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<WebcamSetting>>(json);
    }

    public void Save(IReadOnlyCollection<WebcamSetting> settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
