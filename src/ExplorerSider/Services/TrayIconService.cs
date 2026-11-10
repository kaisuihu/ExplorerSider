using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ExplorerSider.Services;

/// <summary>
/// 系统托盘图标（Win32 Shell_NotifyIcon 自封装，零外部依赖）。
/// 通过 message-only 窗口接收回调；左键单击/双击/右键以事件抛出，菜单由外部提供。
/// </summary>
public sealed partial class TrayIconService : IDisposable
{
    private HwndSource? _msgWindow;
    private System.Drawing.Icon? _icon;
    private uint _id;
    private bool _added;

    private const uint WM_TRAYICON = 0x8000 + 0x100; // WM_APP+256
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    public event Action? LeftClick;
    public event Action? LeftDoubleClick;
    public event Action? RightClick;

    /// <summary>右键弹出的菜单（须已构建好 PlacementTarget/Placement）</summary>
    public System.Windows.Controls.ContextMenu? Menu { get; set; }

    public void Init(string tip = "ExplorerSider 书签")
    {
        // message-only 窗口接收托盘回调
        _msgWindow = new HwndSource(new HwndSourceParameters("ExplorerSiderTray")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0
        });
        _msgWindow.AddHook(WndProc);

        _icon = CloverIcon.ToIcon(32);
        _id = 1;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _msgWindow.Handle,
            uID = _id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _icon.Handle,
            szTip = tip
        };
        _added = Shell_NotifyIconW(NIM_ADD, ref nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON && (uint)wParam == _id)
        {
            var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (mouse)
            {
                case WM_LBUTTONUP: LeftClick?.Invoke(); handled = true; break;
                case WM_LBUTTONDBLCLK: LeftDoubleClick?.Invoke(); handled = true; break;
                case WM_RBUTTONUP: RightClick?.Invoke(); handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>在鼠标位置弹出菜单（托盘区，由 RightClick 触发）</summary>
    public void ShowContextMenu()
    {
        if (Menu == null) return;
        Menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        Menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_msgWindow != null && _added)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _msgWindow.Handle,
                uID = _id
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
        }
        _icon?.Dispose();
        _msgWindow?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
    }

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);
}
