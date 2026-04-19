using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceBuddy.Models;

namespace VoiceBuddy.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private Settings _current;

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceBuddy");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        _current = Load();
    }

    public Settings Current => _current;

    public event EventHandler<Settings>? Changed;

    public void Save(Settings next)
    {
        _current = next;
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(next, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
        Changed?.Invoke(this, next);
    }

    public void NotifyChanged() => Changed?.Invoke(this, _current);

    private Settings Load()
    {
        if (!File.Exists(_path)) return new Settings();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }
}
