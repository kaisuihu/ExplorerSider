using System.Runtime.InteropServices;

namespace ExplorerSider.Services;

/// <summary>多屏停靠/热键所需的 Win32 API（物理像素坐标，规避 DIP 换算）。</summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public bool Contains(POINT p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;   // 整屏（含任务栏）
        public RECT rcWork;      // 工作区（不含任务栏）
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- SendInput（模拟 Ctrl+T 新标签等键盘操作）----

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;          // 1 = INPUT_KEYBOARD
        [FieldOffset(8)] public KEYBDINPUT ki;      // x64: union 起始偏移 8
    }

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_RETURN = 0x0D;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>发送单个按键（虚拟键码）</summary>
    public static void SendKey(ushort vk, bool up = false)
    {
        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 }
        };
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>发送 Unicode 字符（绕过输入法，适合注入含中文的路径）</summary>
    public static void SendChar(char c, bool up = false)
    {
        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) }
        };
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const int WM_HOTKEY = 0x0312;

    internal const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    /// <summary>获取鼠标当前所在显示器（物理像素矩形）</summary>
    public static bool GetMouseMonitor(ref MONITORINFO mi)
    {
        if (!GetCursorPos(out var p)) return false;
        var hMon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
        mi = default;
        mi.cbSize = Marshal.SizeOf<MONITORINFO>();
        return GetMonitorInfoW(hMon, ref mi);
    }

    /// <summary>取指定点的显示器信息</summary>
    public static bool GetMonitorAt(POINT p, ref MONITORINFO mi)
    {
        var hMon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
        mi = default;
        mi.cbSize = Marshal.SizeOf<MONITORINFO>();
        return GetMonitorInfoW(hMon, ref mi);
    }
}
