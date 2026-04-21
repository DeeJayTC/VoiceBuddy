using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoiceBuddy.Models;

public enum Anchor
{
    TopLeft, TopCenter, TopRight,
    CenterLeft, Center, CenterRight,
    BottomLeft, BottomCenter, BottomRight,
}

public enum TextAlignmentMode { Left, Center, Right }

public enum OverlayMode { Anchored, Free }

public sealed class OverlayStyle : INotifyPropertyChanged
{
    private string _fontFamily = "Segoe UI";
    private int _fontSize = 28;
    private int _fontWeight = 600;
    private string _textColor = "#FFFFFF";
    private string _backgroundColor = "#000000";
    private double _backgroundOpacity = 0.55;
    private string _outlineColor = "#000000";
    private int _outlineWidth = 2;
    private TextAlignmentMode _textAlign = TextAlignmentMode.Center;
    private double _lineHeight = 1.25;
    private int _paddingX = 18;
    private int _paddingY = 10;
    private int _borderRadius = 8;
    private int _maxLines = 3;
    private int _maxVisibleSentences = 5;
    private string _panelBackgroundColor = "#000000";
    private double _panelBackgroundOpacity;
    private int _newSentenceAfterSeconds = 4;
    private int _clearAfterSeconds = 30;

    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public int FontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public int FontWeight { get => _fontWeight; set => Set(ref _fontWeight, value); }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value); }
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public double BackgroundOpacity { get => _backgroundOpacity; set => Set(ref _backgroundOpacity, value); }
    public string OutlineColor { get => _outlineColor; set => Set(ref _outlineColor, value); }
    public int OutlineWidth { get => _outlineWidth; set => Set(ref _outlineWidth, value); }
    public TextAlignmentMode TextAlign { get => _textAlign; set => Set(ref _textAlign, value); }
    public double LineHeight { get => _lineHeight; set => Set(ref _lineHeight, value); }
    public int PaddingX { get => _paddingX; set => Set(ref _paddingX, value); }
    public int PaddingY { get => _paddingY; set => Set(ref _paddingY, value); }
    public int BorderRadius { get => _borderRadius; set => Set(ref _borderRadius, value); }
    public int MaxLines { get => _maxLines; set => Set(ref _maxLines, value); }
    public int MaxVisibleSentences { get => _maxVisibleSentences; set => Set(ref _maxVisibleSentences, value); }
    public string PanelBackgroundColor { get => _panelBackgroundColor; set => Set(ref _panelBackgroundColor, value); }
    public double PanelBackgroundOpacity { get => _panelBackgroundOpacity; set => Set(ref _panelBackgroundOpacity, value); }

    // Split the current utterance into its own bubble when nothing new has arrived for this
    // many seconds. Keeps one continuous monologue from becoming one monster pending bubble.
    public int NewSentenceAfterSeconds { get => _newSentenceAfterSeconds; set => Set(ref _newSentenceAfterSeconds, value); }

    // Wipe the overlay entirely after this many seconds of silence.
    public int ClearAfterSeconds { get => _clearAfterSeconds; set => Set(ref _clearAfterSeconds, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class OverlayLayout : INotifyPropertyChanged
{
    private OverlayMode _mode = OverlayMode.Anchored;
    private Anchor _anchor = Anchor.BottomCenter;
    private int _offsetX;
    private int _offsetY = 80;
    private int _width = 960;
    private int _height = 220;
    private bool _locked;
    private double _freeLeft;
    private double _freeTop;
    private bool _obsCaptureMode;

    public OverlayMode Mode { get => _mode; set => Set(ref _mode, value); }
    public Anchor Anchor { get => _anchor; set => Set(ref _anchor, value); }
    public int OffsetX { get => _offsetX; set => Set(ref _offsetX, value); }
    public int OffsetY { get => _offsetY; set => Set(ref _offsetY, value); }
    public int Width { get => _width; set => Set(ref _width, value); }
    public int Height { get => _height; set => Set(ref _height, value); }
    public bool Locked { get => _locked; set => Set(ref _locked, value); }
    public double FreeLeft { get => _freeLeft; set => Set(ref _freeLeft, value); }
    public double FreeTop { get => _freeTop; set => Set(ref _freeTop, value); }

    // Opt-in "show up cleanly in OBS's Window Capture picker": clears the tool-window
    // extended style, enables a taskbar entry, and swaps the window title. Capture still
    // requires OBS's WGC capture method because the overlay is a layered (transparent) window.
    public bool ObsCaptureMode { get => _obsCaptureMode; set => Set(ref _obsCaptureMode, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class TranslationConfig : INotifyPropertyChanged
{
    private string _sourceLang = "auto";
    private string _targetLang = "EN-US";
    private string _deepLApiKey = "";
    private string _deepLApiHost = "api-free.deepl.com";
    private bool _captionsEnabled = true;
    private bool _voiceOutEnabled;
    private string? _voiceOutDeviceId;
    // Empty = let DeepL pick the default voice for the target language.
    // "male" / "female" override it.
    private string _voiceGender = "";

    public string SourceLang { get => _sourceLang; set => Set(ref _sourceLang, value); }
    public string TargetLang { get => _targetLang; set => Set(ref _targetLang, value); }
    public string DeepLApiKey { get => _deepLApiKey; set => Set(ref _deepLApiKey, value); }
    public string DeepLApiHost { get => _deepLApiHost; set => Set(ref _deepLApiHost, value); }
    public bool CaptionsEnabled { get => _captionsEnabled; set => Set(ref _captionsEnabled, value); }
    public bool VoiceOutEnabled { get => _voiceOutEnabled; set => Set(ref _voiceOutEnabled, value); }
    public string? VoiceOutDeviceId { get => _voiceOutDeviceId; set => Set(ref _voiceOutDeviceId, value); }
    public string VoiceGender { get => _voiceGender; set => Set(ref _voiceGender, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class AudioConfig : INotifyPropertyChanged
{
    private string? _deviceId;
    private AudioDeviceKind _deviceKind = AudioDeviceKind.Render;

    public string? DeviceId { get => _deviceId; set => Set(ref _deviceId, value); }
    public AudioDeviceKind DeviceKind { get => _deviceKind; set => Set(ref _deviceKind, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public enum AppMode { Captions, Dictation }

public sealed class DictationConfig : INotifyPropertyChanged
{
    // Stored in the canonical "Ctrl+Alt+V" form produced by HotkeyBinding.Format().
    // Empty string disables the global hotkey (button-only).
    private string _hotkey = "Ctrl+Alt+V";
    private bool _appendSpace = true;

    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    public bool AppendSpace { get => _appendSpace; set => Set(ref _appendSpace, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class Settings : INotifyPropertyChanged
{
    public OverlayStyle OverlayStyle { get; init; } = new();
    public OverlayLayout OverlayLayout { get; init; } = new();
    public TranslationConfig Translation { get; init; } = new();
    public AudioConfig Audio { get; init; } = new();
    public DictationConfig Dictation { get; init; } = new();

    private AppMode _mode = AppMode.Captions;
    public AppMode Mode { get => _mode; set { _mode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode))); } }

    private bool _showOriginalText;
    public bool ShowOriginalText { get => _showOriginalText; set { _showOriginalText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowOriginalText))); } }

    private string _uiLanguage = "en";
    public string UILanguage { get => _uiLanguage; set { _uiLanguage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UILanguage))); } }

    public event PropertyChangedEventHandler? PropertyChanged;
}
