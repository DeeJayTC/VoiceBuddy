using System.Runtime.InteropServices;

namespace VoiceBuddy.Services;

/// <summary>
/// Types Unicode text into whatever window currently has focus via SendInput with
/// KEYEVENTF_UNICODE. Works across nearly every input surface (edit controls, browsers,
/// Electron apps, terminals) because it bypasses keyboard layouts entirely — each WM_CHAR
/// is delivered as the literal codepoint. Supports surrogate pairs for characters outside
/// the BMP (emoji etc.).
/// </summary>
public static class TextInjector
{
    public static void Type(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Each char needs a KEYDOWN + KEYUP pair. Surrogate pairs (2 chars) become 2 pairs.
        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            ushort code = text[i];
            inputs[i * 2] = MakeUnicodeInput(code, keyUp: false);
            inputs[i * 2 + 1] = MakeUnicodeInput(code, keyUp: true);
        }

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeUnicodeInput(ushort scan, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = scan,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0u),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    // --- Win32 ---

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
