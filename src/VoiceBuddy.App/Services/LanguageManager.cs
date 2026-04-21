using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Windows;

namespace VoiceBuddy.Services;

/// <summary>
/// Manages application string localization. Loads translated JSON resource files,
/// provides key-based string lookup, and pushes all strings into Application.Resources
/// as "Loc_Section_Key" keys so DynamicResource bindings in XAML update live.
/// </summary>
public sealed class LanguageManager
{
    private static LanguageManager? _instance;
    public static LanguageManager Instance => _instance ??= new LanguageManager();

    private Dictionary<string, object> _currentStrings = new();
    private string _currentLanguage = "en";
    private readonly string _stringsBasePath = Path.Combine(AppContext.BaseDirectory, "Strings");

    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Gets list of available language codes by scanning Strings/*.json files.
    /// </summary>
    public List<string> GetAvailableLanguages()
    {
        var languages = new List<string> { "en" };
        if (!Directory.Exists(_stringsBasePath)) return languages;

        var jsonFiles = Directory.GetFiles(_stringsBasePath, "Strings.*.json");
        foreach (var file in jsonFiles)
        {
            var filename = Path.GetFileNameWithoutExtension(file);
            if (filename.StartsWith("Strings."))
            {
                var lang = filename.Substring("Strings.".Length);
                if (!languages.Contains(lang))
                    languages.Add(lang);
            }
        }
        return languages;
    }

    /// <summary>
    /// Loads the specified language. Falls back to English if the language file doesn't exist.
    /// </summary>
    public void SetLanguage(string languageCode)
    {
        var filePath = GetLanguageFilePath(languageCode);
        if (!File.Exists(filePath))
        {
            // Fall back to English if language file not found
            if (languageCode != "en")
            {
                _currentLanguage = "en";
                filePath = GetLanguageFilePath("en");
            }
        }
        else
        {
            _currentLanguage = languageCode;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            _currentStrings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
        }
        catch
        {
            // If parsing fails, use empty dictionary (GetString will return key as fallback)
            _currentStrings = new();
        }
    }

    /// <summary>
    /// Gets a localized string by dot-separated key path (e.g., "Overview.Device").
    /// Returns the key itself if not found.
    /// </summary>
    public string GetString(string keyPath)
    {
        if (string.IsNullOrEmpty(keyPath))
            return "";

        var parts = keyPath.Split('.');
        object? current = _currentStrings;

        foreach (var part in parts)
        {
            if (current is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(part, out var prop))
                    current = prop;
                else
                    return keyPath;
            }
            else if (current is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue(part, out var next))
                    current = next;
                else
                    return keyPath;
            }
            else
            {
                return keyPath;
            }
        }

        if (current is JsonElement finalElem && finalElem.ValueKind == JsonValueKind.String)
            return finalElem.GetString() ?? keyPath;

        if (current is string str)
            return str;

        return keyPath;
    }

    /// <summary>
    /// Flattens all loaded strings and writes them into Application.Resources
    /// with keys like "Loc_Overview_Device". DynamicResource bindings in XAML
    /// will update automatically when this is called on a language switch.
    /// </summary>
    public void ApplyToResources()
    {
        if (Application.Current == null) return;

        var flat = new Dictionary<string, string>();
        FlattenInto(_currentStrings, "", flat);

        // Also load English as fallback for any keys missing in the selected language
        if (_currentLanguage != "en")
        {
            var enPath = GetLanguageFilePath("en");
            if (File.Exists(enPath))
            {
                try
                {
                    var enJson = File.ReadAllText(enPath);
                    var enStrings = JsonSerializer.Deserialize<Dictionary<string, object>>(enJson) ?? new();
                    var enFlat = new Dictionary<string, string>();
                    FlattenInto(enStrings, "", enFlat);
                    foreach (var kv in enFlat)
                    {
                        flat.TryAdd(kv.Key, kv.Value); // don't overwrite translated values
                    }
                }
                catch { }
            }
        }

        var resources = Application.Current.Resources;
        foreach (var kv in flat)
        {
            var resourceKey = "Loc_" + kv.Key.Replace(".", "_");
            resources[resourceKey] = kv.Value;
        }
    }

    private static void FlattenInto(Dictionary<string, object> dict, string prefix, Dictionary<string, string> result)
    {
        foreach (var kv in dict)
        {
            var fullKey = prefix.Length > 0 ? prefix + "." + kv.Key : kv.Key;
            if (kv.Value is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.Object)
                {
                    var nested = JsonSerializer.Deserialize<Dictionary<string, object>>(elem.GetRawText()) ?? new();
                    FlattenInto(nested, fullKey, result);
                }
                else if (elem.ValueKind == JsonValueKind.String)
                {
                    result[fullKey] = elem.GetString() ?? "";
                }
            }
            else if (kv.Value is string s)
            {
                result[fullKey] = s;
            }
        }
    }

    private string GetLanguageFilePath(string languageCode)
    {
        return languageCode == "en"
            ? Path.Combine(_stringsBasePath, "Strings.en.json")
            : Path.Combine(_stringsBasePath, $"Strings.{languageCode}.json");
    }
}
