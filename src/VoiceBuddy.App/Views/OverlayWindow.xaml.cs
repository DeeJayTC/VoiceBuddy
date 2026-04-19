using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VoiceBuddy.Interop;
using VoiceBuddy.Models;

namespace VoiceBuddy.Views;

public partial class OverlayWindow : Window
{
    private static readonly TimeSpan IdleFadeAfter = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FadeInTime = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan FadeOutTime = TimeSpan.FromMilliseconds(700);

    // Per-side transcript state.
    //   _start        — index in snap.Concluded where the CURRENT utterance begins.
    //                   Segments before this index have already been finalized into _finalized.
    //   _finalized    — one entry per completed sentence (what the user sees as a concluded box).
    // A sentence is finalized when the server delivers an update whose Concluded has grown
    // (or has content beyond our current start) AND Tentative is empty — i.e. the
    // in-progress utterance has no more pending words.
    private int _tgtStart;
    private int _srcStart;
    private readonly List<string> _tgtFinalized = new();
    private readonly List<string> _srcFinalized = new();
    private TranscriptSnapshot _tgtSnap = TranscriptSnapshot.Empty("auto");
    private TranscriptSnapshot _srcSnap = TranscriptSnapshot.Empty("auto");

    // UI — one BoxView per visible bubble. When we need more, we create; when we need fewer,
    // we drop the oldest (index 0), which is exactly the "use the oldest slot again" behavior.
    private readonly List<BoxView> _views = new();
    private DispatcherTimer? _idleTimer;

    private sealed class BoxView
    {
        public required Border Border;
        public required TextBlock TargetTb;
        public required TextBlock SourceTb;
    }

    public OverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, __) =>
        {
            WindowNative.MakeToolWindow(this);
            ApplyLayout();
            ApplyClickThrough();
            ApplyPanelBackground();
        };

        App.Settings.Changed += OnSettingsChanged;
        App.Subtitles.SourceUpdated += OnSourceUpdated;
        App.Subtitles.TargetUpdated += OnTargetUpdated;
        Closed += (_, __) =>
        {
            App.Settings.Changed -= OnSettingsChanged;
            App.Subtitles.SourceUpdated -= OnSourceUpdated;
            App.Subtitles.TargetUpdated -= OnTargetUpdated;
            _idleTimer?.Stop();
        };
    }

    // ---------- layout / click-through / drag / resize ----------

    private void OnSettingsChanged(object? sender, Settings s)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLayout();
            ApplyClickThrough();
            ApplyPanelBackground();
            RenderTranscript();
        });
    }

    private void ApplyPanelBackground()
    {
        var style = App.Settings.Current.OverlayStyle;
        var c = ColorFromHex(style.PanelBackgroundColor);
        var a = (byte)Math.Clamp(style.PanelBackgroundOpacity * 255, 0, 255);
        PanelBackground.Background = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
    }

    private void ApplyLayout()
    {
        var layout = App.Settings.Current.OverlayLayout;
        Width = Math.Max(320, layout.Width);
        Height = Math.Max(80, layout.Height);

        if (layout.Mode == OverlayMode.Free)
        {
            Left = layout.FreeLeft;
            Top = layout.FreeTop;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            var dw = work.Width;
            var dh = work.Height;

            double x, y;
            var anchor = layout.Anchor.ToString();
            if (anchor.EndsWith("Left")) x = layout.OffsetX;
            else if (anchor.EndsWith("Right")) x = dw - Width - layout.OffsetX;
            else x = (dw - Width) / 2 + layout.OffsetX;

            if (anchor.StartsWith("Top")) y = layout.OffsetY;
            else if (anchor.StartsWith("Bottom")) y = dh - Height - layout.OffsetY;
            else y = (dh - Height) / 2 + layout.OffsetY;

            Left = x;
            Top = y;
        }

        var anc = layout.Anchor.ToString();
        var horizontal = anc.EndsWith("Left") ? HorizontalAlignment.Left
            : anc.EndsWith("Right") ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;
        var vertical = anc.StartsWith("Top") ? VerticalAlignment.Top
            : anc.StartsWith("Bottom") ? VerticalAlignment.Bottom
            : VerticalAlignment.Center;
        BubbleStack.HorizontalAlignment = layout.Mode == OverlayMode.Free ? HorizontalAlignment.Center : horizontal;
        BubbleStack.VerticalAlignment = layout.Mode == OverlayMode.Free ? VerticalAlignment.Bottom : vertical;
    }

    private void ApplyClickThrough()
    {
        var locked = App.Settings.Current.OverlayLayout.Locked;
        WindowNative.SetClickThrough(this, locked);
        var vis = locked ? Visibility.Collapsed : Visibility.Visible;
        DragAffordance.Visibility = vis;
        ResizeGrips.Visibility = vis;
    }

    private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (App.Settings.Current.OverlayLayout.Locked) return;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }

        var s = App.Settings.Current;
        s.OverlayLayout.Mode = OverlayMode.Free;
        s.OverlayLayout.FreeLeft = Left;
        s.OverlayLayout.FreeTop = Top;
        App.Settings.Save(s);
    }

    private string? _resizeDir;
    private Point _resizeStartScreen;
    private double _resizeStartW;
    private double _resizeStartH;

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (App.Settings.Current.OverlayLayout.Locked) return;
        if (sender is not FrameworkElement grip || grip.Tag is not string dir) return;

        _resizeDir = dir;
        _resizeStartScreen = PointToScreen(e.GetPosition(this));
        _resizeStartW = Width;
        _resizeStartH = Height;

        grip.CaptureMouse();
        grip.MouseMove += Grip_MouseMove;
        grip.MouseLeftButtonUp += Grip_MouseLeftButtonUp;
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeDir is null) return;
        var current = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (current.X - _resizeStartScreen.X) / dpi.DpiScaleX;
        var dy = (current.Y - _resizeStartScreen.Y) / dpi.DpiScaleY;

        if (_resizeDir is "Right" or "Corner")
            Width = Math.Max(MinWidth, _resizeStartW + dx);
        if (_resizeDir is "Bottom" or "Corner")
            Height = Math.Max(MinHeight, _resizeStartH + dy);
    }

    private void Grip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement grip) return;
        grip.MouseMove -= Grip_MouseMove;
        grip.MouseLeftButtonUp -= Grip_MouseLeftButtonUp;
        grip.ReleaseMouseCapture();
        _resizeDir = null;

        var s = App.Settings.Current;
        s.OverlayLayout.Width = (int)Math.Round(Width);
        s.OverlayLayout.Height = (int)Math.Round(Height);
        App.Settings.Save(s);
    }

    private void OverlayLockButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var s = App.Settings.Current;
        s.OverlayLayout.Locked = true;
        App.Settings.Save(s);
    }

    // ---------- transcript state ----------

    private void OnTargetUpdated(object? sender, TranscriptSnapshot snap)
    {
        Dispatcher.Invoke(() =>
        {
            _tgtSnap = snap;
            IngestSide(ref _tgtStart, _tgtFinalized, snap);
            RenderTranscript();
            KickIdleTimer();
        });
    }

    private void OnSourceUpdated(object? sender, TranscriptSnapshot snap)
    {
        Dispatcher.Invoke(() =>
        {
            _srcSnap = snap;
            IngestSide(ref _srcStart, _srcFinalized, snap);
            RenderTranscript();
            KickIdleTimer();
        });
    }

    /// <summary>
    /// Advances per-side state with a new snapshot. Finalizes the current utterance when
    /// tentative is empty and concluded has any unfinalized segments. Detects session
    /// resets (concluded count dropped below our bookmark) and rewinds.
    /// </summary>
    private static void IngestSide(ref int start, List<string> finalized, TranscriptSnapshot snap)
    {
        // Session reset: the service cleared state and is publishing a fresh empty snapshot.
        if (snap.Concluded.Count < start)
        {
            finalized.Clear();
            start = 0;
        }

        var isEmpty = snap.Tentative.Count == 0;
        var hasUnfinalizedConcluded = snap.Concluded.Count > start;

        if (isEmpty && hasUnfinalizedConcluded)
        {
            var text = string.Concat(snap.Concluded.Skip(start).Select(s => s.Text)).Trim();
            if (!string.IsNullOrEmpty(text))
                finalized.Add(text);
            start = snap.Concluded.Count;
        }
    }

    /// <summary>
    /// Builds the "current utterance" text for a side: any concluded segments past our
    /// bookmark joined with the current tentative. Empty when nothing is in progress.
    /// </summary>
    private static string ComposeCurrent(TranscriptSnapshot snap, int start)
    {
        if (snap.Concluded.Count == start && snap.Tentative.Count == 0) return "";
        var conc = string.Concat(snap.Concluded.Skip(start).Select(s => s.Text));
        var tent = string.Concat(snap.Tentative.Select(s => s.Text));
        return (conc + tent).Trim();
    }

    // ---------- rendering ----------

    private void RenderTranscript()
    {
        var s = App.Settings.Current;
        var style = s.OverlayStyle;
        var max = Math.Max(1, style.MaxVisibleSentences);
        var showOriginal = s.ShowOriginalText;

        var tgtCurrent = ComposeCurrent(_tgtSnap, _tgtStart);
        var srcCurrent = ComposeCurrent(_srcSnap, _srcStart);

        // Build the desired slot list: finalized target sentences (each pairs with the
        // finalized source sentence at the same index if ShowOriginal is on) + one trailing
        // pending slot while a current utterance is being dictated.
        var desired = new List<(string Tgt, string? Src, bool Pending)>();
        for (int i = 0; i < _tgtFinalized.Count; i++)
        {
            var src = showOriginal && i < _srcFinalized.Count ? _srcFinalized[i] : null;
            desired.Add((_tgtFinalized[i], src, false));
        }
        var hasPending = !string.IsNullOrEmpty(tgtCurrent) ||
                         (showOriginal && !string.IsNullOrEmpty(srcCurrent));
        if (hasPending)
        {
            var src = showOriginal && !string.IsNullOrEmpty(srcCurrent) ? srcCurrent : null;
            desired.Add((tgtCurrent, src, true));
        }

        // Cap at max: drop the oldest. Trim the internal finalized list too so it doesn't
        // grow forever on long sessions.
        while (desired.Count > max) desired.RemoveAt(0);
        while (_tgtFinalized.Count > max) _tgtFinalized.RemoveAt(0);
        while (_srcFinalized.Count > max) _srcFinalized.RemoveAt(0);

        SyncViews(desired, style);

        if (_views.Count > 0 && BubbleStack.Opacity < 1)
            BubbleStack.BeginAnimation(OpacityProperty, new DoubleAnimation(1, new Duration(FadeInTime)));
    }

    private void SyncViews(List<(string Tgt, string? Src, bool Pending)> desired, OverlayStyle style)
    {
        // Grow: create missing boxes and append.
        while (_views.Count < desired.Count)
        {
            var view = CreateBox();
            _views.Add(view);
            BubbleStack.Children.Add(view.Border);
        }
        // Shrink: drop the oldest (top of the stack) so newer content stays put.
        while (_views.Count > desired.Count)
        {
            BubbleStack.Children.Remove(_views[0].Border);
            _views.RemoveAt(0);
        }
        // Update existing Border/TextBlock content in place to avoid flicker on each
        // tentative word.
        for (int i = 0; i < desired.Count; i++)
        {
            var (tgt, src, pending) = desired[i];
            UpdateBox(_views[i], tgt, src, pending, style);
        }
    }

    private static BoxView CreateBox()
    {
        var stack = new StackPanel();
        var tgt = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var src = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(tgt);
        stack.Children.Add(src);
        var border = new Border
        {
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = stack,
        };
        return new BoxView { Border = border, TargetTb = tgt, SourceTb = src };
    }

    private static void UpdateBox(BoxView v, string tgt, string? src, bool pending, OverlayStyle style)
    {
        v.TargetTb.Text = tgt;
        ApplyTextBlockStyle(v.TargetTb, style);
        v.TargetTb.Foreground = new SolidColorBrush(ColorFromHex(style.TextColor))
        {
            Opacity = pending ? 0.55 : 1.0,
        };

        if (!string.IsNullOrEmpty(src))
        {
            v.SourceTb.Visibility = Visibility.Visible;
            v.SourceTb.Text = src;
            ApplyTextBlockStyle(v.SourceTb, style);
            v.SourceTb.FontSize = Math.Max(10, (int)(style.FontSize * 0.62));
            v.SourceTb.LineHeight = v.SourceTb.FontSize * style.LineHeight;
            v.SourceTb.Foreground = new SolidColorBrush(ColorFromHex(style.TextColor))
            {
                Opacity = pending ? 0.40 : 0.72,
            };
        }
        else
        {
            v.SourceTb.Visibility = Visibility.Collapsed;
            v.SourceTb.Text = string.Empty;
        }

        v.Border.Background = BgBrush(style, pending ? 0.65 : 1.0);
        v.Border.CornerRadius = new CornerRadius(style.BorderRadius);
        v.Border.Padding = new Thickness(style.PaddingX, style.PaddingY, style.PaddingX, style.PaddingY);
    }

    // ---------- styling helpers ----------

    private static Brush BgBrush(OverlayStyle s, double opacityMultiplier = 1.0)
    {
        var c = ColorFromHex(s.BackgroundColor);
        var a = (byte)Math.Clamp(s.BackgroundOpacity * opacityMultiplier * 255, 0, 255);
        return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
    }

    private static Color ColorFromHex(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return Colors.White; }
    }

    private static void ApplyTextBlockStyle(TextBlock tb, OverlayStyle s)
    {
        tb.FontFamily = new FontFamily(s.FontFamily);
        tb.FontSize = s.FontSize;
        tb.FontWeight = FontWeight.FromOpenTypeWeight(s.FontWeight);
        tb.TextAlignment = s.TextAlign switch
        {
            TextAlignmentMode.Left => TextAlignment.Left,
            TextAlignmentMode.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
        tb.LineHeight = s.FontSize * s.LineHeight;
        tb.TextWrapping = TextWrapping.Wrap;
        tb.Effect = s.OutlineWidth > 0
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ColorFromHex(s.OutlineColor),
                BlurRadius = Math.Max(1, s.OutlineWidth * 2),
                ShadowDepth = 0,
                Opacity = 1.0,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
            }
            : null;
    }

    // ---------- idle fade ----------

    private void KickIdleTimer()
    {
        _idleTimer?.Stop();
        _idleTimer = new DispatcherTimer { Interval = IdleFadeAfter };
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer?.Stop();
            FadeBubbleOut();
        };
        _idleTimer.Start();
    }

    private void FadeBubbleOut()
    {
        if (BubbleStack.Opacity <= 0) return;
        var anim = new DoubleAnimation(0, new Duration(FadeOutTime));
        anim.Completed += (_, _) =>
        {
            foreach (var v in _views) BubbleStack.Children.Remove(v.Border);
            _views.Clear();
            _tgtFinalized.Clear();
            _srcFinalized.Clear();
            _tgtStart = 0;
            _srcStart = 0;
        };
        BubbleStack.BeginAnimation(OpacityProperty, anim);
    }
}
