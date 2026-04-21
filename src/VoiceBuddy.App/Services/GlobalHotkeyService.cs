using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;

namespace VoiceBuddy.Services;

/// <summary>
/// Registers a single Win32 global hotkey via RegisterHotKey and invokes
/// <see cref="Pressed"/> when it fires. Uses a message-only HwndSource to receive
/// WM_HOTKEY without owning a visible window. Call <see cref="SetBinding"/> to
/// change the combo at runtime; empty string disables the hotkey.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public event EventHandler? Pressed;

    private readonly HwndSource _source;
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 1;
    private bool _registered;

    public GlobalHotkeyService()
    {
        // HWND_MESSAGE = -3: message-only window, never shown, no taskbar entry, can
        // receive WM_HOTKEY. Runs on whatever thread this is constructed on (expected:
        // the WPF dispatcher thread, because we hook into WPF's message pump).
        var parameters = new HwndSourceParameters("VoiceBuddyHotkey")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public string CurrentBinding { get; private set; } = "";

    /// <summary>
    /// Parses the binding, unregisters any existing hotkey, and registers the new one.
    /// Silently no-ops on parse failure or registration conflict — caller checks
    /// <see cref="LastError"/> to report UX.
    /// </summary>
    public void SetBinding(string binding)
    {
        Unregister();
        CurrentBinding = binding ?? "";
        LastError = null;
        if (string.IsNullOrWhiteSpace(binding)) return;

        if (!HotkeyBinding.TryParse(binding, out var mods, out var vk))
        {
            LastError = $"Unrecognized hotkey: {binding}";
            return;
        }

        // MOD_NOREPEAT = 0x4000 — holding the key down fires once, not a storm of events.
        if (!RegisterHotKey(_source.Handle, HotkeyId, mods | 0x4000, vk))
        {
            var code = Marshal.GetLastWin32Error();
            LastError = $"Hotkey '{binding}' is in use by another app (Win32 {code}).";
        }
        else
        {
            _registered = true;
        }
    }

    public string? LastError { get; private set; }

    private void Unregister()
    {
        if (!_registered) return;
        try { UnregisterHotKey(_source.Handle, HotkeyId); } catch { }
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// Parses and formats hotkey strings like "Ctrl+Alt+V" or "Ctrl+Shift+F9". WPF's Key enum
/// handles the key-name side; we map modifiers ourselves to Win32 MOD_* flags.
/// </summary>
public static class HotkeyBinding
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    public static bool TryParse(string binding, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(binding)) return false;

        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        // Last token is the key; everything before is a modifier. Case-insensitive.
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var m = parts[i].ToLowerInvariant();
            switch (m)
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL; break;
                case "alt":
                    modifiers |= MOD_ALT; break;
                case "shift":
                    modifiers |= MOD_SHIFT; break;
                case "win":
                case "meta":
                    modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        var keyToken = parts[^1];
        if (!Enum.TryParse<Key>(keyToken, ignoreCase: true, out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    /// <summary>
    /// Canonical format: "Ctrl+Alt+Shift+Win+Key". Produced from current live keyboard
    /// state — used by the settings UI's hotkey-capture control.
    /// </summary>
    public static string Format(ModifierKeys mods, Key key)
    {
        var sb = new StringBuilder();
        if ((mods & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
        if ((mods & ModifierKeys.Alt) != 0) sb.Append("Alt+");
        if ((mods & ModifierKeys.Shift) != 0) sb.Append("Shift+");
        if ((mods & ModifierKeys.Windows) != 0) sb.Append("Win+");
        sb.Append(key);
        return sb.ToString();
    }
}
