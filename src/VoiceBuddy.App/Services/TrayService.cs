using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using VoiceBuddy.Models;
using WpfApp = System.Windows.Application;

namespace VoiceBuddy.Services;

/// <summary>
/// System-tray icon + context menu. Built on Windows Forms NotifyIcon because WPF has no
/// native tray support. The menu is rebuilt on every open so the input-source list always
/// reflects the current devices.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly Window _mainWindow;

    public TrayService(Window mainWindow)
    {
        _mainWindow = mainWindow;

        _menu = new ContextMenuStrip();
        _menu.Opening += OnMenuOpening;

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "VoiceBuddy",
            Visible = true,
        };
        // Handle clicks manually. NotifyIcon + ContextMenuStrip has a long-standing
        // foreground-activation bug in WPF+WinForms hybrid apps: the menu appears but
        // doesn't claim focus, so clicking an item does nothing and the menu closes on
        // the next click. Showing it ourselves and nudging it foreground is the fix.
        _tray.MouseUp += OnTrayMouseUp;
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static Icon LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(path))
        {
            try { return new Icon(path); }
            catch { /* fall through */ }
        }
        return SystemIcons.Application;
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (!_mainWindow.IsVisible) ShowMainWindow();
            return;
        }
        if (e.Button != MouseButtons.Right) return;

        RebuildMenu();
        _menu.Show(System.Windows.Forms.Cursor.Position);
        // Force the menu window into the foreground so first click on an item registers.
        if (_menu.Handle != IntPtr.Zero) SetForegroundWindow(_menu.Handle);
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e) => RebuildMenu();

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        _menu.Items.Add("Show VoiceBuddy", null, (_, _) => ShowMainWindow());
        _menu.Items.Add(new ToolStripSeparator());

        var dictation = App.Settings.Current.Mode == AppMode.Dictation;
        var verb = dictation ? "dictation" : "capture";
        var captureItem = new ToolStripMenuItem((App.Audio.IsRunning ? "Stop " : "Start ") + verb);
        captureItem.Click += (_, _) =>
        {
            if (App.Audio.IsRunning) App.StopCapture();
            else _ = App.StartCapture();
        };
        _menu.Items.Add(captureItem);
        _menu.Items.Add(new ToolStripSeparator());

        // Input source submenu — rebuild every open so connecting/disconnecting a device
        // is reflected without needing to hit Refresh in the main UI.
        var inputRoot = new ToolStripMenuItem("Input source");
        var currentCfg = App.Settings.Current.Audio;
        foreach (var dev in App.Audio.EnumerateDevices())
        {
            var prefix = dev.Kind == AudioDeviceKind.Render ? "\U0001F50A " : "\U0001F3A4 ";
            var suffix = dev.IsDefault ? "  (default)" : "";
            var item = new ToolStripMenuItem(prefix + dev.FriendlyName + suffix)
            {
                Checked = dev.Id == currentCfg.DeviceId && dev.Kind == currentCfg.DeviceKind,
            };
            var captured = dev;
            item.Click += (_, _) => SelectDevice(captured);
            inputRoot.DropDownItems.Add(item);
        }
        _menu.Items.Add(inputRoot);
        _menu.Items.Add(new ToolStripSeparator());

        var layout = App.Settings.Current.OverlayLayout;
        var lockItem = new ToolStripMenuItem("Lock overlay (click-through)")
        {
            Checked = layout.Locked,
        };
        lockItem.Click += (_, _) => ToggleLock();
        _menu.Items.Add(lockItem);

        _menu.Items.Add("Unlock && reposition overlay", null, (_, _) => Reposition());
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add("Quit", null, (_, _) => WpfApp.Current.Shutdown());
    }

    private void ShowMainWindow()
    {
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false; // brief topmost-flash trick to force focus
        _mainWindow.Focus();
    }

    private static void SelectDevice(AudioDevice dev)
    {
        App.Settings.Current.Audio.DeviceId = dev.Id;
        App.Settings.Current.Audio.DeviceKind = dev.Kind;
        App.Settings.Save(App.Settings.Current);
        if (App.Audio.IsRunning) App.Audio.Start(dev.Id, dev.Kind);
    }

    private static void ToggleLock()
    {
        var s = App.Settings.Current;
        s.OverlayLayout.Locked = !s.OverlayLayout.Locked;
        App.Settings.Save(s);
    }

    private void Reposition()
    {
        var s = App.Settings.Current;
        s.OverlayLayout.Locked = false;
        App.Settings.Save(s);
        _tray.BalloonTipTitle = "Overlay unlocked";
        _tray.BalloonTipText = "Drag the overlay to reposition. Drag its edges to resize. Lock from the tray menu when done.";
        _tray.ShowBalloonTip(3000);
    }

    public void ShowBalloon(string title, string text, int ms = 2500)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(ms);
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
    }
}
