using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoiceBuddy.Interop;

public static class WindowNative
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    /// <summary>
    /// Applies the overlay's always-on extended styles and toggles tool-window based on
    /// <paramref name="obsCaptureMode"/>. With OBS mode off the window is a tool window
    /// (hidden from Alt-Tab / OBS picker). With OBS mode on it appears as a normal window
    /// so OBS's Window Capture can list it. WS_EX_NOACTIVATE stays on in both modes so the
    /// overlay never steals focus from the app the user is actually interacting with.
    /// </summary>
    public static void ApplyOverlayStyles(Window window, bool obsCaptureMode)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_NOACTIVATE;
        if (obsCaptureMode) ex &= ~WS_EX_TOOLWINDOW;
        else ex |= WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    public static void SetClickThrough(Window window, bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (clickThrough)
            ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        else
            ex &= ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }
}
