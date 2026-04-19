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

    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
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
