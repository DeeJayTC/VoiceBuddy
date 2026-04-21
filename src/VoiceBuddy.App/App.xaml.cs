using System.Windows;
using VoiceBuddy.Models;
using VoiceBuddy.Services;
using VoiceBuddy.Views;

namespace VoiceBuddy;

public partial class App : Application
{
    public static SettingsStore Settings { get; private set; } = null!;
    public static SubtitleBus Subtitles { get; private set; } = null!;
    public static FakeSubtitleFeed FakeFeed { get; private set; } = null!;
    public static AudioCaptureService Audio { get; private set; } = null!;
    public static DeepLVoiceService Voice { get; private set; } = null!;
    public static VoiceOutPlayer VoiceOut { get; private set; } = null!;
    public static TrayService Tray { get; private set; } = null!;
    public static DebugLog Debug { get; private set; } = null!;
    public static DeepLLanguagesService Languages { get; private set; } = null!;
    public static DictationService Dictation { get; private set; } = null!;
    public static GlobalHotkeyService Hotkey { get; private set; } = null!;

    private OverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = new SettingsStore();

        // Initialize language from settings
        var langMgr = LanguageManager.Instance;
        langMgr.SetLanguage(Settings.Current.UILanguage);
        langMgr.ApplyToResources();

        Subtitles = new SubtitleBus();
        FakeFeed = new FakeSubtitleFeed();
        Debug = new DebugLog();
        Audio = new AudioCaptureService();
        Voice = new DeepLVoiceService(Debug);
        VoiceOut = new VoiceOutPlayer();
        Languages = new DeepLLanguagesService(Debug);
        Dictation = new DictationService(Settings.Current.Dictation);
        Hotkey = new GlobalHotkeyService();

        // Capture → DeepL Voice → mode-aware sink. In Captions mode, source+target feed
        // the overlay and voice-out. In Dictation mode, only concluded target segments
        // are pulled and typed into the active window; overlay/voice-out stay silent.
        Audio.FrameAvailable += (_, frame) => Voice.AddFrame(frame);
        Voice.SourceUpdated += (_, snap) =>
        {
            if (Settings.Current.Mode == AppMode.Captions) Subtitles.PublishSource(snap);
        };
        Voice.TargetUpdated += (_, snap) =>
        {
            if (Settings.Current.Mode == AppMode.Dictation) Dictation.Consume(snap);
            else Subtitles.PublishTarget(snap);
        };
        Voice.TargetMediaChunk += (_, pcm) => VoiceOut.AddPcmChunk(pcm);

        // Hotkey: toggles the capture session only while in Dictation mode. Registration
        // is refreshed on every settings change (binding string may have changed).
        Hotkey.Pressed += (_, _) =>
        {
            if (Settings.Current.Mode != AppMode.Dictation) return;
            if (Audio.IsRunning) StopCapture();
            else _ = StartCapture();
        };
        ApplyHotkeyBinding();
        Settings.Changed += (_, _) => ApplyHotkeyBinding();

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        _overlay = new OverlayWindow();
        _overlay.Show();

        Tray = new TrayService(main);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Hotkey?.Dispose();
        Tray?.Dispose();
        VoiceOut?.Dispose();
        Voice?.Dispose();
        Audio?.Dispose();
        _overlay?.Close();
        base.OnExit(e);
    }

    /// <summary>
    /// Starts audio capture + (if configured) a DeepL Voice session. Callable from any
    /// surface (tray, main window, hotkey) so start/stop stays in one place. Behavior
    /// diverges by mode: Captions keeps overlay + optional voice-out; Dictation forces
    /// voice-out off (we'd just be wasting bytes) and resets the typed-segment counter.
    /// </summary>
    public static async Task StartCapture()
    {
        var cfg = Settings.Current.Audio;
        if (string.IsNullOrEmpty(cfg.DeviceId))
        {
            Tray?.ShowBalloon("Pick a device", "Select an input source in the Overview tab first.");
            return;
        }
        Audio.Start(cfg.DeviceId, cfg.DeviceKind);

        var t = Settings.Current.Translation;
        if (string.IsNullOrWhiteSpace(t.DeepLApiKey)) return;

        var mode = Settings.Current.Mode;
        if (mode == AppMode.Dictation)
        {
            Dictation.ResetSession();
            await Voice.StartAsync(t.DeepLApiHost, t.DeepLApiKey, t.SourceLang, t.TargetLang,
                wantVoiceOut: false, voiceGender: "");
            return;
        }

        // Captions mode: skip the session entirely when both outputs are off — no point
        // paying for a transcript stream nobody reads.
        if (!t.CaptionsEnabled && !t.VoiceOutEnabled) return;

        if (t.VoiceOutEnabled)
            VoiceOut.Start(t.VoiceOutDeviceId);

        await Voice.StartAsync(t.DeepLApiHost, t.DeepLApiKey, t.SourceLang, t.TargetLang,
            wantVoiceOut: t.VoiceOutEnabled, voiceGender: t.VoiceGender);
    }

    public static void StopCapture()
    {
        Audio.Stop();
        Voice.Stop();
        VoiceOut.Stop();
    }

    private static void ApplyHotkeyBinding()
    {
        var binding = Settings.Current.Mode == AppMode.Dictation
            ? Settings.Current.Dictation.Hotkey
            : "";
        if (Hotkey.CurrentBinding == binding) return;
        Hotkey.SetBinding(binding);
    }

    public static void ClearOverlay()
    {
        if (Current is App a) a._overlay?.ClearCaptions();
    }
}
