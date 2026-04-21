# VoiceBuddy Localization Guide

This guide explains how to add, manage, and deploy multilingual support for VoiceBuddy.

## Architecture

VoiceBuddy uses a JSON-based string resource system for localization:

- **Source of truth**: `src/VoiceBuddy.App/Strings/Strings.en.json` (English)
- **Translation files**: `src/VoiceBuddy.App/Strings/Strings.{lang}.json` (e.g., `Strings.es.json`, `Strings.de.json`)
- **Language manager**: `src/VoiceBuddy.App/Services/LanguageManager.cs` (loads and caches translations at runtime)
- **Build script**: `build/translate-strings.ps1` (automates translation via DeepL API with change detection)

## String Structure

The English strings file (`Strings.en.json`) is organized hierarchically:

```json
{
  "App": {
    "Title": "VoiceBuddy",
    "Tagline": "Live subtitles powered by DeepL"
  },
  "Overview": {
    "AudioSource": "Audio source",
    "Device": "Device",
    ...
  },
  "Settings": {
    "TabHeader": "Settings",
    ...
  }
}
```

Access strings in code using dot-separated keys: `"Overview.AudioSource"`

## Adding New Strings

When adding new UI text to VoiceBuddy:

1. **Add to English file**: Edit `src/VoiceBuddy.App/Strings/Strings.en.json`
   ```json
   "Settings": {
     "NewOption": "New option label"
   }
   ```

2. **Use the LanguageManager in code**:
   ```csharp
   var text = LanguageManager.Instance.GetString("Settings.NewOption");
   ```

3. **Commit changes** so the build script detects the update.

4. **Run the build script** (see below) to generate translations.

## Automatic Translation with DeepL

The build script `build/translate-strings.ps1` automates translation:

### Setup

1. Get a DeepL API key from [DeepL API Console](https://www.deepl.com/pro-api)
2. Set the environment variable:
   ```powershell
   $env:DEEPL_API_KEY = "your-api-key-here"
   ```

### Running the Build Script

```powershell
cd build
.\translate-strings.ps1 -Languages "de,fr,es,it,nl,pl,pt,ja,zh,ko"
```

Or, if `DEEPL_API_KEY` is set:
```powershell
.\translate-strings.ps1
```

### How It Works

The script:

1. **Computes a hash** of `Strings.en.json` and compares it to the previous hash stored in `.strings-hash`
2. **Skips translation** if the English file hasn't changed (no wasted API calls!)
3. **Batches requests** to DeepL API in groups of 50 strings for efficiency
4. **Generates language files** (e.g., `Strings.de.json`, `Strings.fr.json`)
5. **Updates the hash file** so future runs detect only new changes

### Supported Languages

Pass language codes to `-Languages` parameter. Common examples:

| Code | Language     |
|------|--------------|
| de   | German       |
| fr   | French       |
| es   | Spanish      |
| it   | Italian      |
| nl   | Dutch        |
| pl   | Polish       |
| pt   | Portuguese   |
| ja   | Japanese     |
| zh   | Chinese      |
| ko   | Korean       |

## Using Translations at Runtime

### In Code

```csharp
// Get a translated string
var label = LanguageManager.Instance.GetString("Settings.AppLanguage");
```

### Switching Languages

When the user changes the language in Settings:

```csharp
var langMgr = LanguageManager.Instance;
langMgr.SetLanguage("es");  // Switch to Spanish
// UI updates on next render or restart
```

### XAML Binding (Future Enhancement)

Currently, VoiceBuddy uses hardcoded English strings in XAML and switches labels in code-behind. Future versions could implement `IValueConverter` for dynamic XAML binding:

```xml
<Label Content="{Binding 'Settings.AppLanguage', Converter={StaticResource LanguageConverter}}" />
```

## Adding Support for a New Language

1. **Create a new translation file**:
   ```powershell
   # Add the language code to the build script call
   .\build\translate-strings.ps1 -Languages "de,fr,es,ja,my-new-lang"
   ```

2. **The script will generate**: `src/VoiceBuddy.App/Strings/Strings.my-new-lang.json`

3. **Users can select the language** from the Settings → Application language dropdown.

4. **Restart required**: App restart is needed for language changes to take full effect (XAML binding limitation; could be improved with dynamic resource refresh).

## Maintenance

### Updating an Existing Translation

To fix a translation manually:

1. Edit `src/VoiceBuddy.App/Strings/Strings.{lang}.json` directly
2. Change the value (keys must match English structure)
3. Save and rebuild

### Removing Unused Strings

1. Delete the key from `Strings.en.json`
2. Delete corresponding keys from all language files
3. Run the build script to regenerate (the deleted key won't reappear)

### Verifying Translations

1. Launch the app
2. Go to Settings → Application language
3. Select a language
4. Restart the app
5. Verify text appears in the selected language

## DeepL API Costs

- **Free tier**: 500,000 characters/month (sufficient for most development)
- **Pro tier**: Unlimited (needed for frequent updates)

The build script uses **change detection** to minimize translation API calls. Only new/modified strings are sent to DeepL, keeping costs low.

## File Structure After Build

```
src/VoiceBuddy.App/Strings/
  Strings.en.json      (English source)
  Strings.de.json      (German)
  Strings.es.json      (Spanish)
  Strings.fr.json      (French)
  ... (other languages)
  .strings-hash        (Change detection file; auto-generated)
```

All `.json` files are copied to `bin/Debug/net10.0-windows/Strings/` at build time (see `VoiceBuddy.App.csproj`).

## Troubleshooting

**"Language file not found"**
- Ensure `Strings.en.json` exists
- Run `dotnet build` to copy files to output
- Check that files are in `bin/Debug/net10.0-windows/Strings/`

**"Translation script fails with API error"**
- Verify `DEEPL_API_KEY` is set correctly
- Check API quota at [DeepL Console](https://www.deepl.com/pro-api)
- Ensure `Strings.en.json` is valid JSON

**"Language picker shows no languages"**
- Verify `LanguageManager.GetAvailableLanguages()` finds files in output directory
- Rebuild the project to copy string files
- Check `bin/Debug/net10.0-windows/Strings/` exists

**"UI doesn't change when language is switched"**
- Restart the application (current limitation due to XAML binding)
- Verify language was saved by checking `%AppData%\VoiceBuddy\settings.json` for `"UILanguage"` field

## Future Enhancements

- [ ] **Dynamic XAML binding**: Implement `IValueConverter` to update XAML labels without restart
- [ ] **Right-to-left (RTL) support**: Add RTL layout support for Arabic, Hebrew
- [ ] **Locale-specific formatting**: Date/time/number formatting per language
- [ ] **Community translations**: Allow community contributors to submit translations
- [ ] **Fallback mechanism**: Gracefully fall back to English if a language file is incomplete
