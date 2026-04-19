using System.Windows;
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

    private OverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = new SettingsStore();
        Subtitles = new SubtitleBus();
        FakeFeed = new FakeSubtitleFeed();
        Debug = new DebugLog();
        Audio = new AudioCaptureService();
        Voice = new DeepLVoiceService(Debug);
        VoiceOut = new VoiceOutPlayer();

        // Capture → DeepL Voice (transcribe + translate in one session) → publish snapshots.
        Audio.FrameAvailable += (_, frame) => Voice.AddFrame(frame);
        Voice.SourceUpdated += (_, snap) => Subtitles.PublishSource(snap);
        Voice.TargetUpdated += (_, snap) => Subtitles.PublishTarget(snap);
        // Translated voice bytes stream straight into the output player's ring buffer.
        Voice.TargetMediaChunk += (_, pcm) => VoiceOut.AddPcmChunk(pcm);

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        _overlay = new OverlayWindow();
        _overlay.Show();

        Tray = new TrayService(main);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        VoiceOut?.Dispose();
        Voice?.Dispose();
        Audio?.Dispose();
        _overlay?.Close();
        base.OnExit(e);
    }

    /// <summary>
    /// Starts audio capture + (if configured) a DeepL Voice session. Callable from any
    /// surface (tray, main window, future hotkey) so start/stop stays in one place.
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

        // No session at all if both output modes are disabled — we'd be paying for a
        // transcript stream nobody reads.
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
}
