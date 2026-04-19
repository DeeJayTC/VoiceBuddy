using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceBuddy.Models;
using VoiceBuddy.Services;

namespace VoiceBuddy.Views;

public partial class MainWindow : Window
{
    // Starts true so XAML-parse-time ValueChanged events (fired when slider Min/Max coerce
    // the default Value) are ignored before our named fields exist. Cleared at end of first
    // BindFromSettings() on Loaded.
    private bool _binding = true;
    private float _smoothedLevel;

    private bool _balloonShown;

    public MainWindow()
    {
        InitializeComponent();
        App.Settings.Changed += (_, __) => BindFromSettings();
        App.Audio.LevelChanged += OnLevelChanged;
        App.Audio.Failed += OnAudioFailed;
        App.Audio.StateChanged += OnCaptureStateChanged;
        App.Voice.StatusChanged += OnVoiceStatus;
        App.Debug.EntryAdded += OnDebugEntry;
        App.Debug.Cleared += OnDebugCleared;
        StateChanged += OnStateChanged;
        Loaded += (_, __) =>
        {
            RefreshDevices();
            RefreshVoiceOutDevices();
            BindFromSettings();
            TryLoadLogo();
            AutoStartIfConfigured();
        };
        Closed += (_, __) =>
        {
            App.Audio.LevelChanged -= OnLevelChanged;
            App.Audio.Failed -= OnAudioFailed;
            App.Audio.StateChanged -= OnCaptureStateChanged;
            App.Voice.StatusChanged -= OnVoiceStatus;
            App.Debug.EntryAdded -= OnDebugEntry;
            App.Debug.Cleared -= OnDebugCleared;
            StateChanged -= OnStateChanged;
        };
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) return;

        // Hide to tray rather than living in the taskbar while minimized. Audio capture,
        // overlay, and the DeepL Voice session keep running unaffected.
        ShowInTaskbar = false;
        Hide();

        if (!_balloonShown)
        {
            _balloonShown = true;
            App.Tray.ShowBalloon(
                "VoiceBuddy is running",
                "Right-click the tray icon for quick controls. Double-click to reopen.");
        }
    }

    private void BindFromSettings()
    {
        _binding = true;
        try
        {
            var s = App.Settings.Current;

            FontFamilyBox.Text = s.OverlayStyle.FontFamily;
            FontSizeSlider.Value = s.OverlayStyle.FontSize;
            FontSizeValue.Text = s.OverlayStyle.FontSize.ToString(CultureInfo.InvariantCulture);

            SelectComboByContent(WeightBox, s.OverlayStyle.FontWeight.ToString());
            SelectComboByContent(AlignBox, s.OverlayStyle.TextAlign.ToString());

            TextColorBox.Text = s.OverlayStyle.TextColor;
            TextColorSwatch.Background = BrushFromHex(s.OverlayStyle.TextColor);

            LineHeightSlider.Value = s.OverlayStyle.LineHeight;
            LineHeightValue.Text = s.OverlayStyle.LineHeight.ToString("0.00", CultureInfo.InvariantCulture);

            OutlineColorBox.Text = s.OverlayStyle.OutlineColor;
            OutlineColorSwatch.Background = BrushFromHex(s.OverlayStyle.OutlineColor);
            OutlineWidthSlider.Value = s.OverlayStyle.OutlineWidth;
            OutlineWidthValue.Text = s.OverlayStyle.OutlineWidth.ToString(CultureInfo.InvariantCulture);

            BgColorBox.Text = s.OverlayStyle.BackgroundColor;
            BgColorSwatch.Background = BrushFromHex(s.OverlayStyle.BackgroundColor);
            BgOpacitySlider.Value = s.OverlayStyle.BackgroundOpacity;
            BgOpacityValue.Text = s.OverlayStyle.BackgroundOpacity.ToString("0.00", CultureInfo.InvariantCulture);

            PadXSlider.Value = s.OverlayStyle.PaddingX;
            PadXValue.Text = s.OverlayStyle.PaddingX.ToString(CultureInfo.InvariantCulture);
            PadYSlider.Value = s.OverlayStyle.PaddingY;
            PadYValue.Text = s.OverlayStyle.PaddingY.ToString(CultureInfo.InvariantCulture);
            RadiusSlider.Value = s.OverlayStyle.BorderRadius;
            RadiusValue.Text = s.OverlayStyle.BorderRadius.ToString(CultureInfo.InvariantCulture);
            MaxLinesSlider.Value = s.OverlayStyle.MaxLines;
            MaxLinesValue.Text = s.OverlayStyle.MaxLines.ToString(CultureInfo.InvariantCulture);
            MaxSentencesSlider.Value = s.OverlayStyle.MaxVisibleSentences;
            MaxSentencesValue.Text = s.OverlayStyle.MaxVisibleSentences.ToString(CultureInfo.InvariantCulture);

            PanelBgColorBox.Text = s.OverlayStyle.PanelBackgroundColor;
            PanelBgColorSwatch.Background = BrushFromHex(s.OverlayStyle.PanelBackgroundColor);
            PanelBgOpacitySlider.Value = s.OverlayStyle.PanelBackgroundOpacity;
            PanelBgOpacityValue.Text = s.OverlayStyle.PanelBackgroundOpacity.ToString("0.00", CultureInfo.InvariantCulture);

            SelectComboByContent(AnchorBox, s.OverlayLayout.Anchor.ToString());
            OffsetXSlider.Value = s.OverlayLayout.OffsetX;
            OffsetXValue.Text = s.OverlayLayout.OffsetX.ToString(CultureInfo.InvariantCulture);
            OffsetYSlider.Value = s.OverlayLayout.OffsetY;
            OffsetYValue.Text = s.OverlayLayout.OffsetY.ToString(CultureInfo.InvariantCulture);
            WidthSlider.Value = s.OverlayLayout.Width;
            WidthValue.Text = s.OverlayLayout.Width.ToString(CultureInfo.InvariantCulture);
            HeightSlider.Value = s.OverlayLayout.Height;
            HeightValue.Text = s.OverlayLayout.Height.ToString(CultureInfo.InvariantCulture);

            ModeReadout.Text = s.OverlayLayout.Mode == OverlayMode.Free
                ? $"Free ({(int)s.OverlayLayout.FreeLeft}, {(int)s.OverlayLayout.FreeTop})"
                : "Anchored";
            ResetAnchorButton.IsEnabled = s.OverlayLayout.Mode == OverlayMode.Free;

            TargetLangBox.Text = s.Translation.TargetLang;
            SourceLangBox.Text = s.Translation.SourceLang;
            SelectComboByContent(HostBox, s.Translation.DeepLApiHost);
            ApiKeyBox.Password = s.Translation.DeepLApiKey;
            ShowOriginalCheck.IsChecked = s.ShowOriginalText;

            CaptionsEnabledCheck.IsChecked = s.Translation.CaptionsEnabled;
            VoiceEnabledCheck.IsChecked = s.Translation.VoiceOutEnabled;
            VoiceOutConfig.Visibility = s.Translation.VoiceOutEnabled ? Visibility.Visible : Visibility.Collapsed;
            SelectComboByTag(VoiceGenderBox, s.Translation.VoiceGender);
            SyncVoiceOutDeviceSelection();

            LockButton.Content = s.OverlayLayout.Locked ? "Unlock overlay" : "Lock overlay (click-through)";

            UpdatePreview();
        }
        finally { _binding = false; }
    }

    private static void SelectComboByContent(ComboBox box, string value)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((item.Content as string) == value) { box.SelectedItem = item; return; }
        }
    }

    private static Brush BrushFromHex(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!); }
        catch { return Brushes.Transparent; }
    }

    private void Commit()
    {
        if (_binding) return;
        App.Settings.Save(App.Settings.Current);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var s = App.Settings.Current.OverlayStyle;
        var textBrush = BrushFromHex(s.TextColor);
        var outlineBrush = BrushFromHex(s.OutlineColor);
        var bgHex = s.BackgroundColor;
        var bg = (Color)ColorConverter.ConvertFromString(bgHex)!;
        var bgBrush = new SolidColorBrush(Color.FromArgb((byte)(s.BackgroundOpacity * 255), bg.R, bg.G, bg.B));

        foreach (var tb in new[] { PreviewText, PreviewOutline })
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
        }

        PreviewText.Foreground = textBrush;
        PreviewOutline.Foreground = outlineBrush;
        PreviewOutline.Margin = new Thickness(0);
        PreviewOutline.Effect = s.OutlineWidth > 0
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ((SolidColorBrush)outlineBrush).Color,
                BlurRadius = 0,
                ShadowDepth = 0,
                Opacity = 1.0,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
            }
            : null;

        PreviewBubble.Background = bgBrush;
        PreviewBubble.Padding = new Thickness(s.PaddingX, s.PaddingY, s.PaddingX, s.PaddingY);
        PreviewBubble.CornerRadius = new CornerRadius(s.BorderRadius);
    }

    // --- event handlers ---

    private void FontFamilyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.FontFamily = FontFamilyBox.Text;
        Commit();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayStyle.FontSize = v;
        FontSizeValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void WeightBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        if (WeightBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out var w))
        {
            App.Settings.Current.OverlayStyle.FontWeight = w;
            Commit();
        }
    }

    private void AlignBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        if (AlignBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<TextAlignmentMode>(item.Content?.ToString(), out var mode))
        {
            App.Settings.Current.OverlayStyle.TextAlign = mode;
            Commit();
        }
    }

    private void TextColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.TextColor = TextColorBox.Text;
        TextColorSwatch.Background = BrushFromHex(TextColorBox.Text);
        Commit();
    }

    private void LineHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.LineHeight = e.NewValue;
        LineHeightValue.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        Commit();
    }

    private void OutlineColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.OutlineColor = OutlineColorBox.Text;
        OutlineColorSwatch.Background = BrushFromHex(OutlineColorBox.Text);
        Commit();
    }

    private void OutlineWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayStyle.OutlineWidth = v;
        OutlineWidthValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void BgColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.BackgroundColor = BgColorBox.Text;
        BgColorSwatch.Background = BrushFromHex(BgColorBox.Text);
        Commit();
    }

    private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.BackgroundOpacity = e.NewValue;
        BgOpacityValue.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        Commit();
    }

    private void PadXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayStyle.PaddingX = v;
        PadXValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void PadYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayStyle.PaddingY = v;
        PadYValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayStyle.BorderRadius = v;
        RadiusValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void MaxLinesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayStyle.MaxLines = v;
        MaxLinesValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void MaxSentencesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayStyle.MaxVisibleSentences = v;
        MaxSentencesValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void PanelBgColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.PanelBackgroundColor = PanelBgColorBox.Text;
        PanelBgColorSwatch.Background = BrushFromHex(PanelBgColorBox.Text);
        Commit();
    }

    private void PanelBgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        App.Settings.Current.OverlayStyle.PanelBackgroundOpacity = e.NewValue;
        PanelBgOpacityValue.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        Commit();
    }

    private void AnchorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        if (AnchorBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<Anchor>(item.Content?.ToString(), out var a))
        {
            App.Settings.Current.OverlayLayout.Anchor = a;
            App.Settings.Current.OverlayLayout.Mode = OverlayMode.Anchored;
            Commit();
        }
    }

    private void OffsetXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayLayout.OffsetX = v;
        App.Settings.Current.OverlayLayout.Mode = OverlayMode.Anchored;
        OffsetXValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void OffsetYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayLayout.OffsetY = v;
        App.Settings.Current.OverlayLayout.Mode = OverlayMode.Anchored;
        OffsetYValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayLayout.Width = v;
        // Width tweaks alone shouldn't teleport the overlay back to the anchor — leave Mode alone.
        WidthValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_binding) return;
        var v = (int)e.NewValue;
        App.Settings.Current.OverlayLayout.Height = v;
        HeightValue.Text = v.ToString(CultureInfo.InvariantCulture);
        Commit();
    }

    private void ResetAnchorButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings.Current;
        s.OverlayLayout.Mode = OverlayMode.Anchored;
        App.Settings.Save(s);
    }

    // --- debug panel ---

    private int _debugCount;

    private void DebugEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        App.Debug.Enabled = DebugEnabledCheck.IsChecked == true;
    }

    private void DebugChunksCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        App.Debug.LogAudioChunks = DebugChunksCheck.IsChecked == true;
    }

    private void DebugClearButton_Click(object sender, RoutedEventArgs e)
    {
        App.Debug.Clear();
    }

    private void OnDebugEntry(object? sender, LogEntry entry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var arrow = entry.Direction == LogDirection.Out ? "\u2192" : "\u2190";
            // Truncate oversized payloads so one monster base64 doesn't break scrolling.
            var payload = entry.Payload.Length > 2000
                ? entry.Payload[..2000] + $"  …(+{entry.Payload.Length - 2000} chars)"
                : entry.Payload;
            var line = $"{entry.Timestamp:HH:mm:ss.fff}  {arrow}  {entry.Kind}  {payload}\n";
            DebugText.AppendText(line);
            _debugCount++;
            DebugStats.Text = $"{_debugCount} entries";
            if (DebugAutoScrollCheck.IsChecked == true)
            {
                DebugText.CaretIndex = DebugText.Text.Length;
                DebugScroll.ScrollToEnd();
            }
        });
    }

    private void OnDebugCleared(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            DebugText.Clear();
            _debugCount = 0;
            DebugStats.Text = "0 entries";
        });
    }

    private void TryLoadLogo()
    {
        // Look first next to the exe (Content item copied by the build), then under %AppData%
        // so end users can drop a PNG without rebuilding.
        string?[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Assets", "deepl-logo.png"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "VoiceBuddy", "Assets", "deepl-logo.png"),
        ];

        foreach (var path in candidates)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                LogoImage.Source = bmp;
                LogoImage.Visibility = Visibility.Visible;
                LogoPlaceholder.Visibility = Visibility.Collapsed;

                // Use the same image for the title bar + taskbar. WPF will pick appropriate
                // sizes from the source bitmap; a high-res PNG looks fine scaled down.
                Icon = bmp;
                return;
            }
            catch
            {
                // fall through, try next candidate
            }
        }
    }

    private void TargetLangBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.Translation.TargetLang = TargetLangBox.Text.Trim().ToUpperInvariant();
        Commit();
    }

    private void SourceLangBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.Translation.SourceLang = SourceLangBox.Text.Trim();
        Commit();
    }

    private void HostBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        if (HostBox.SelectedItem is ComboBoxItem item && item.Content is string host)
        {
            App.Settings.Current.Translation.DeepLApiHost = host;
            Commit();
        }
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.Translation.DeepLApiKey = ApiKeyBox.Password;
        Commit();
    }

    private void ShowOriginalCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.ShowOriginalText = ShowOriginalCheck.IsChecked == true;
        Commit();
    }

    // --- translation output ---

    private void CaptionsEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        App.Settings.Current.Translation.CaptionsEnabled = CaptionsEnabledCheck.IsChecked == true;
        Commit();
    }

    private void VoiceEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_binding) return;
        var on = VoiceEnabledCheck.IsChecked == true;
        App.Settings.Current.Translation.VoiceOutEnabled = on;
        VoiceOutConfig.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Commit();
    }

    private void VoiceGenderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        if (VoiceGenderBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            App.Settings.Current.Translation.VoiceGender = tag;
            Commit();
        }
    }

    private void RefreshVoiceOutDevices()
    {
        var wasBinding = _binding;
        _binding = true;
        try
        {
            VoiceOutDeviceBox.Items.Clear();
            VoiceOutDeviceBox.Items.Add(new ComboBoxItem { Content = "Default output device", Tag = null });
            foreach (var dev in App.Audio.EnumerateDevices())
            {
                if (dev.Kind != AudioDeviceKind.Render) continue;
                var suffix = dev.IsDefault ? "  (default)" : "";
                VoiceOutDeviceBox.Items.Add(new ComboBoxItem
                {
                    Content = $"🔊  {dev.FriendlyName}{suffix}",
                    Tag = dev.Id,
                });
            }
            SyncVoiceOutDeviceSelection();
        }
        finally { _binding = wasBinding; }
    }

    private void SyncVoiceOutDeviceSelection()
    {
        var id = App.Settings.Current.Translation.VoiceOutDeviceId;
        foreach (ComboBoxItem item in VoiceOutDeviceBox.Items)
        {
            if ((item.Tag as string) == id) { VoiceOutDeviceBox.SelectedItem = item; return; }
        }
        VoiceOutDeviceBox.SelectedIndex = 0;
    }

    private void VoiceOutDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        if (VoiceOutDeviceBox.SelectedItem is ComboBoxItem item)
        {
            App.Settings.Current.Translation.VoiceOutDeviceId = item.Tag as string;
            Commit();
        }
    }

    private void RefreshVoiceOutDevicesButton_Click(object sender, RoutedEventArgs e) => RefreshVoiceOutDevices();

    private static void SelectComboByTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((item.Tag as string) == tag) { box.SelectedItem = item; return; }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private void TestFireButton_Click(object sender, RoutedEventArgs e)
    {
        var (src, dst) = App.FakeFeed.Next();
        App.Subtitles.PublishSource(src);
        App.Subtitles.PublishTarget(dst);
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings.Current;
        s.OverlayLayout.Locked = !s.OverlayLayout.Locked;
        App.Settings.Save(s);
        LockButton.Content = s.OverlayLayout.Locked ? "Unlock overlay" : "Lock overlay (click-through)";
    }

    // --- audio ---

    private void RefreshDevices()
    {
        var wasBinding = _binding;
        _binding = true;
        try
        {
            var devices = App.Audio.EnumerateDevices();
            DeviceBox.Items.Clear();
            foreach (var d in devices)
            {
                var prefix = d.Kind == AudioDeviceKind.Render ? "🔊" : "🎤";
                var suffix = d.IsDefault ? "  (default)" : "";
                var item = new ComboBoxItem
                {
                    Content = $"{prefix}  {d.FriendlyName}{suffix}",
                    Tag = d,
                };
                DeviceBox.Items.Add(item);
            }

            var cfg = App.Settings.Current.Audio;
            if (!string.IsNullOrEmpty(cfg.DeviceId))
            {
                foreach (ComboBoxItem item in DeviceBox.Items)
                {
                    if (item.Tag is AudioDevice d && d.Id == cfg.DeviceId && d.Kind == cfg.DeviceKind)
                    {
                        DeviceBox.SelectedItem = item;
                        break;
                    }
                }
            }
            if (DeviceBox.SelectedItem is null && DeviceBox.Items.Count > 0)
            {
                // Prefer default render device.
                foreach (ComboBoxItem item in DeviceBox.Items)
                {
                    if (item.Tag is AudioDevice d && d.Kind == AudioDeviceKind.Render && d.IsDefault)
                    {
                        DeviceBox.SelectedItem = item;
                        break;
                    }
                }
                DeviceBox.SelectedItem ??= DeviceBox.Items[0];
            }
        }
        finally { _binding = wasBinding; }
    }

    private void AutoStartIfConfigured()
    {
        var cfg = App.Settings.Current.Audio;
        if (string.IsNullOrEmpty(cfg.DeviceId)) return;
        _ = App.StartCapture();
    }

    private void OnCaptureStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (App.Audio.IsRunning)
            {
                var fmt = App.Audio.CurrentFormat;
                StatusText.Text = fmt is null
                    ? "Starting…"
                    : $"{fmt.SampleRate} Hz · {fmt.Channels} ch · {fmt.BitsPerSample}-bit {fmt.Encoding}";
                CaptureToggleButton.Content = "Stop capture";
            }
            else
            {
                StatusText.Text = "Idle";
                CaptureToggleButton.Content = "Start capture";
                _smoothedLevel = 0;
                VuBar.Width = 0;
            }
        });
    }

    private void OnVoiceStatus(object? sender, string msg)
    {
        Dispatcher.BeginInvoke(() =>
        {
            VoiceStatus.Text = msg;

            // Classify for the header chip: green = connected, red = error, muted = idle/transitional.
            var lower = msg.ToLowerInvariant();
            var connected = lower.Contains("connected") || lower.Contains("session") && lower.Contains("…");
            var failed = lower.Contains("error") || lower.Contains("failed");
            var dot = (SolidColorBrush)HeaderSessionDot.Fill;
            HeaderSessionDot.Fill = connected
                ? new SolidColorBrush(Color.FromRgb(0x3F, 0xC0, 0x7A))
                : failed
                    ? new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A))
                    : (Brush)FindResource("Muted");
            HeaderSessionText.Text = msg;
            HeaderSessionText.Foreground = connected || failed
                ? (Brush)FindResource("Text")
                : (Brush)FindResource("Muted");
        });
    }

    private void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding) return;
        if (DeviceBox.SelectedItem is not ComboBoxItem { Tag: AudioDevice dev }) return;

        App.Settings.Current.Audio.DeviceId = dev.Id;
        App.Settings.Current.Audio.DeviceKind = dev.Kind;
        App.Settings.Save(App.Settings.Current);

        if (App.Audio.IsRunning) _ = App.StartCapture();
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void CaptureToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Audio.IsRunning) App.StopCapture();
        else _ = App.StartCapture();
    }

    private void OnLevelChanged(object? sender, float rms)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Smooth with a fast rise / slow decay envelope so the bar feels like a VU meter.
            _smoothedLevel = rms > _smoothedLevel
                ? _smoothedLevel + (rms - _smoothedLevel) * 0.6f
                : _smoothedLevel + (rms - _smoothedLevel) * 0.12f;

            // Map RMS (~0..0.7 for loud speech) to 0..1 with mild log shaping.
            var shown = Math.Clamp(_smoothedLevel * 3.2f, 0f, 1f);
            const double FullWidth = 320;
            VuBar.Width = shown * FullWidth;
        });
    }

    private void OnAudioFailed(object? sender, string msg)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = $"Audio error: {msg}";
            CaptureToggleButton.Content = "Start capture";
        });
    }
}
