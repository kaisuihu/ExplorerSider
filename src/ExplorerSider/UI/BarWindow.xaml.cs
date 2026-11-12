using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ExplorerSider.Models;
using ExplorerSider.Services;

namespace ExplorerSider.UI;

/// <summary>
/// 顶部悬浮书签栏：无边框置顶小条。
/// 停靠模式：自由 / 顶部居中 / 四角停靠（鼠标指向对应角落滑出，角落是天然鼠标陷阱）。
/// 多屏：全部物理像素定位；热键 Ctrl+Alt+B 在鼠标所在屏呼出。
/// </summary>
public partial class BarWindow : Window
{
    private const int Edge = 2;      // 隐藏时露出的边缘像素
    private const int Margin2 = 2;   // 角落停靠时与屏幕边的间距
    private const int TriggerPx = 8; // 角落/边缘触发区大小

    private readonly BookmarkStore _store;
    private IntPtr _hwnd;
    private HwndSource? _source;

    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly DispatcherTimer _hoverTimer = new(); // 文件夹悬停延迟弹菜单

    private bool _shown = true;
    private bool _animBusy;
    private bool _dragging;
    private DateTime _lastActive = DateTime.Now;
    private DateTime _popupLastActive = DateTime.Now;

    private int _animFromY, _animToY, _animTargetX;
    private NativeMethods.MONITORINFO _animMon;

    private Popup? _activePopup;
    private Button? _activeFolderButton;
    private Button? _hoverFolderButton;
    /// <summary>子菜单项右键弹出的编辑菜单（打开期间父级子列表保持显示）</summary>
    private ContextMenu? _activeNodeMenu;
    /// <summary>模态操作进行中（编辑对话框/删除确认）：期间与结束后短时内整条链保持显示</summary>
    private volatile bool _modalOpen;

    private Button? _dropTargetBtn;
    private static readonly Brush DropHighlight = new SolidColorBrush(Color.FromRgb(0xE3, 0xF0, 0xFF));

    private int _hotkeyId = 0x5753;

    /// <summary>当前记忆的物理坐标（供退出时保存）</summary>
    public int SavedX { get; private set; }

    /// <summary>本次会话是否拖动过把手（未拖动则退出时不覆写位置记忆，防默认 0 污染 BarX/BarY）</summary>
    public bool HasSaved { get; private set; }

    public int SavedY { get; private set; }

    public BarWindow(BookmarkStore store)
    {
        _store = store;
        InitializeComponent();
        HandleIcon.Source = CloverIcon.Render(32);

        _dockTimer.Tick += DockTimer_Tick;
        _animTimer.Tick += AnimTimer_Tick;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(180);
        _hoverTimer.Tick += (s, e) => { _hoverTimer.Stop(); if (_hoverFolderButton != null) OpenFolderPopup(_hoverFolderButton); };
        _dropExpandTimer.Tick += (s, e) =>
        {
            _dropExpandTimer.Stop();
            if (_dropExpandItem != null) _dropExpandItem.IsSubmenuOpen = true; // 拖动悬停文件夹 → 展开子列表
        };
        _btnExpandTimer.Tick += (s, e) =>
        {
            _btnExpandTimer.Stop();
            if (_dragOverFolderBtn != null) OpenFolderPopup(_dragOverFolderBtn); // 书签拖到根级文件夹按钮悬停 → 展开子列表
        };

        AllowDrop = true;
        ContextMenu = BuildBarMenu();
        _store.Changed += () => Dispatcher.Invoke(() =>
        {
            Rebuild();
            RefreshActivePopup(); // 增删改后立即刷新打开中的子列表
        });

        SourceInitialized += (s, e) =>
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            // 工具窗口样式：从 Alt+Tab 中排除（ShowInTaskbar=False 只管任务栏，不管 Alt+Tab）
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
                NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE) | NativeMethods.WS_EX_TOOLWINDOW);
            _source = HwndSource.FromHwnd(_hwnd);
            _source.AddHook(WndProc);
            RegisterHotkey();
            RestorePosition();
            _dockTimer.Start();
            _lastActive = DateTime.Now.AddSeconds(2.5); // 启动后展示片刻再隐藏
        };
    }

    #region 消息与热键

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            HotkeyToggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void RegisterHotkey()
    {
        var (mods, vk, ok) = ParseHotkey(_store.Settings.HotkeyShowBar);
        if (!ok) return;
        if (!NativeMethods.RegisterHotKey(_hwnd, _hotkeyId, mods, vk))
        {
            MessageBox.Show(this, $"全局热键 {_store.Settings.HotkeyShowBar} 注册失败（可能已被其他程序占用），\n可修改 %APPDATA%\\ExplorerSider\\settings.json 中的 HotkeyShowBar 后重启。",
                "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>解析 "Ctrl+Alt+B" 形式热键</summary>
    internal static (uint mods, uint vk, bool ok) ParseHotkey(string s)
    {
        uint mods = 0, vk = 0;
        foreach (var part in s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": mods |= NativeMethods.MOD_CONTROL; break;
                case "ALT": mods |= NativeMethods.MOD_ALT; break;
                case "SHIFT": mods |= NativeMethods.MOD_SHIFT; break;
                case "WIN": mods |= NativeMethods.MOD_WIN; break;
                default:
                    if (part.Length == 1 && char.IsLetterOrDigit(part[0])) vk = char.ToUpper(part[0]);
                    else if (part.StartsWith('F') && int.TryParse(part[1..], out var f) && f is >= 1 and <= 12) vk = 0x6F + (uint)f;
                    break;
            }
        }
        return (mods, vk, vk != 0);
    }

    /// <summary>热键：鼠标所在屏呼出；该屏已呼出则收起</summary>
    private void HotkeyToggle()
    {
        Dispatcher.Invoke(() =>
        {
            var mi = new NativeMethods.MONITORINFO();
            if (!NativeMethods.GetMouseMonitor(ref mi)) return;

            if (_shown && WindowOnMonitor(mi))
            {
                AppLog.Write($"Hotkey: 收起（鼠标屏命中当前显示屏）");
                SlideOut(mi);
            }
            else
            {
                // 先瞬移到目标屏隐藏位（不可见），再滑入
                var (_, hide) = CalcPositions(mi);
                NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, hide.X, hide.Y, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                _shown = false;
                SlideIn(mi);
            }
        });
    }

    private bool WindowOnMonitor(NativeMethods.MONITORINFO mi)
    {
        NativeMethods.GetWindowRect(_hwnd, out var wr);
        var center = new NativeMethods.POINT { X = (wr.Left + wr.Right) / 2, Y = (wr.Top + wr.Bottom) / 2 };
        return mi.rcMonitor.Contains(center);
    }

    #endregion

    #region 停靠与滑动

    private (NativeMethods.POINT show, NativeMethods.POINT hide) CalcPositions(NativeMethods.MONITORINFO mi)
    {
        NativeMethods.GetWindowRect(_hwnd, out var wr);
        int w = wr.Width, h = wr.Height;
        var mon = mi.rcMonitor;
        var work = mi.rcWork;
        NativeMethods.POINT P(int x, int y) => new() { X = x, Y = y };

        switch (_store.Settings.DockMode)
        {
            case DockMode.TopAutoHide:
            {
                int cx = mon.Left + (mon.Width - w) / 2;
                return (P(cx, work.Top), P(cx, mon.Top - h + Edge));
            }
            case DockMode.TopLeftAutoHide:
                return (P(work.Left + Margin2, work.Top + Margin2), P(mon.Left + Margin2, mon.Top - h + Edge));
            case DockMode.TopRightAutoHide:
                return (P(work.Right - w - Margin2, work.Top + Margin2), P(mon.Right - w - Margin2, mon.Top - h + Edge));
            case DockMode.BottomLeftAutoHide:
                return (P(work.Left + Margin2, work.Bottom - h - Margin2), P(mon.Left + Margin2, mon.Bottom - Edge));
            case DockMode.BottomRightAutoHide:
                return (P(work.Right - w - Margin2, work.Bottom - h - Margin2), P(mon.Right - w - Margin2, mon.Bottom - Edge));
            default: // Free
                return (P((int)_store.Settings.BarX, (int)_store.Settings.BarY), P((int)_store.Settings.BarX, (int)_store.Settings.BarY));
        }
    }

    private bool InTriggerZone(NativeMethods.POINT p, NativeMethods.MONITORINFO mi)
    {
        var mon = mi.rcMonitor;
        return _store.Settings.DockMode switch
        {
            DockMode.TopAutoHide => p.X >= mon.Left && p.X < mon.Right && p.Y <= mon.Top + TriggerPx,
            DockMode.TopLeftAutoHide => p.X <= mon.Left + TriggerPx && p.Y <= mon.Top + TriggerPx,
            DockMode.TopRightAutoHide => p.X >= mon.Right - TriggerPx && p.Y <= mon.Top + TriggerPx,
            DockMode.BottomLeftAutoHide => p.X <= mon.Left + TriggerPx && p.Y >= mon.Bottom - TriggerPx,
            DockMode.BottomRightAutoHide => p.X >= mon.Right - TriggerPx && p.Y >= mon.Bottom - TriggerPx,
            _ => false
        };
    }

    private void DockTimer_Tick(object? sender, EventArgs e)
    {
        if (_animBusy || _dragging || !IsVisible || !IsLoaded) return;
        var mode = _store.Settings.DockMode;
        if (mode == DockMode.Free) { ClosePopupIfStale(); return; }

        if (!NativeMethods.GetCursorPos(out var p)) return;
        var mi = new NativeMethods.MONITORINFO();
        if (_store.Settings.FollowMouse)
        {
            // 跟随模式：鼠标所在屏即为响应屏
            if (!NativeMethods.GetMonitorAt(p, ref mi)) return;
        }
        else
        {
            // 固定模式：仅悬浮栏所在屏响应贴边呼出，鼠标到其他屏顶部不弹出不跟随（跨屏切换靠热键）
            NativeMethods.GetWindowRect(_hwnd, out var wrC);
            var center = new NativeMethods.POINT { X = (wrC.Left + wrC.Right) / 2, Y = (wrC.Top + wrC.Bottom) / 2 };
            if (!NativeMethods.GetMonitorAt(center, ref mi)) return;
        }
        NativeMethods.GetWindowRect(_hwnd, out var wr);

        bool mouseInWin = wr.Contains(p);
        bool popupHover = ClosePopupIfStale(); // 返回"菜单仍活跃"

        // Contains 防御：垂直并排的相邻屏边缘坐标可能落入本屏触发带（跟随模式下 mi 即鼠标所在屏，恒真）
        bool trigger = InTriggerZone(p, mi) && mi.rcMonitor.Contains(p);
        bool active = mouseInWin || trigger || popupHover || _modalOpen;
        if (active) _lastActive = DateTime.Now;

        if (!_shown && trigger && !_animBusy) SlideIn(mi);
        else if (_shown && !active && DateTime.Now - _lastActive > TimeSpan.FromMilliseconds(600)) SlideOut(mi);
    }

    /// <summary>
    /// 收起判定（每 100ms 检测）：鼠标离开父节点按钮、也不在其子列表内、
    /// 且没有从该子列表弹出的右键菜单 → 收起子列表。留 150ms 容错鼠标在按钮与子列表间滑动的间隙。
    /// 返回值 = 子列表是否仍在活跃使用中。
    /// </summary>
    private bool ClosePopupIfStale()
    {
        // 模态操作（编辑对话框/删除确认）期间整条链保持
        if (_modalOpen)
        {
            _popupLastActive = DateTime.Now;
            return true;
        }

        // 任何右键编辑菜单打开期间，父级链保持显示（向上透传，不逐级隐藏）
        if (_activeNodeMenu is { IsOpen: true })
        {
            _popupLastActive = DateTime.Now;
            return true;
        }
        _activeNodeMenu = null;

        if (_activePopup is not { IsOpen: true })
        {
            _activePopup = null;
            _activeFolderButton = null;
            return false;
        }

        // 几何判定鼠标是否在 子列表 Popup 或其父按钮 内。
        // 不用 IsMouseOver：右键菜单关闭会吞掉鼠标事件，WPF 的 over 状态可能过期。
        bool hover = MouseOverVisual(_activePopup.Child, 6) || MouseOverVisual(_activeFolderButton, 6);
        if (hover) _popupLastActive = DateTime.Now;
        if (DateTime.Now - _popupLastActive > TimeSpan.FromMilliseconds(150))
            CloseActivePopup();
        return hover;
    }

    /// <summary>鼠标当前是否位于指定控件的屏幕矩形内（含 pad 像素余量）</summary>
    private static bool MouseOverVisual(System.Windows.UIElement? el, int pad)
    {
        if (el == null || !el.IsVisible || el.RenderSize.Width <= 0) return false;
        try
        {
            if (!NativeMethods.GetCursorPos(out var p)) return false;
            var tl = el.PointToScreen(new Point(-pad, -pad));
            var br = el.PointToScreen(new Point(el.RenderSize.Width + pad, el.RenderSize.Height + pad));
            return p.X >= tl.X && p.X <= br.X && p.Y >= tl.Y && p.Y <= br.Y;
        }
        catch
        {
            return false;
        }
    }

    private void SlideIn(NativeMethods.MONITORINFO mi)
    {
        var (show, _) = CalcPositions(mi);
        StartAnim(show, mi);
        _shown = true;
        _lastActive = DateTime.Now;
        AppLog.Write($"SlideIn → ({show.X},{show.Y}) mon[{mi.rcMonitor.Left},{mi.rcMonitor.Top}~{mi.rcMonitor.Right},{mi.rcMonitor.Bottom}]");
    }

    private void SlideOut(NativeMethods.MONITORINFO mi)
    {
        CloseActivePopup();
        var (_, hide) = CalcPositions(mi);
        StartAnim(hide, mi);
        _shown = false;
        AppLog.Write($"SlideOut → ({hide.X},{hide.Y}) mon[{mi.rcMonitor.Left},{mi.rcMonitor.Top}~{mi.rcMonitor.Right},{mi.rcMonitor.Bottom}]");
    }

    /// <summary>
    /// 紧急呼出（托盘"显示悬浮书签栏"/托盘左键）：无视当前状态与位置，强制带到鼠标所在屏显示位。
    /// 用于悬浮栏因状态失真/坐标死角"显示不出来"时的保底出口。
    /// </summary>
    public void RescueShow()
    {
        if (_hwnd == IntPtr.Zero)
        {
            AppLog.Write("RescueShow: hwnd 未初始化，走普通 Show");
            Show(); // 从未显示过（ShowBar=false 启动），正常路径初始化
            return;
        }
        Show();
        var mi = new NativeMethods.MONITORINFO();
        if (!NativeMethods.GetMouseMonitor(ref mi))
        {
            AppLog.Write("RescueShow: 取鼠标屏失败");
            return;
        }
        NativeMethods.GetWindowRect(_hwnd, out var before);
        var (show, _) = CalcPositions(mi);
        _animTimer.Stop();
        _animBusy = false;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, show.X, show.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        _shown = true;
        _lastActive = DateTime.Now;
        AppLog.Write($"RescueShow: ({before.Left},{before.Top}) → ({show.X},{show.Y}) mon[{mi.rcMonitor.Left},{mi.rcMonitor.Top}~{mi.rcMonitor.Right},{mi.rcMonitor.Bottom}]");
    }

    private void StartAnim(NativeMethods.POINT target, NativeMethods.MONITORINFO mi)
    {
        _animTargetX = target.X;
        _animFromY = CurrentY();
        _animToY = target.Y;
        _animMon = mi;
        _animBusy = true;
        _animTimer.Start();
    }

    private void AnimTimer_Tick(object? sender, EventArgs e)
    {
        int y = CurrentY();
        int dist = _animToY - y;
        int step = Math.Max(4, Math.Abs(dist) / 4);
        int next = Math.Abs(dist) <= step ? _animToY : y + Math.Sign(dist) * step;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, _animTargetX, next, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        if (next == _animToY)
        {
            _animTimer.Stop();
            _animBusy = false;
        }
    }

    private int CurrentY()
    {
        NativeMethods.GetWindowRect(_hwnd, out var wr);
        return wr.Top;
    }

    private void RestorePosition()
    {
        var s = _store.Settings;
        if (!double.IsNaN(s.BarX) && !double.IsNaN(s.BarY))
        {
            // 恢复物理坐标（自由模式恢复原位；停靠模式启动先到隐藏位，由 dockTimer/热键滑出）
            var mi = new NativeMethods.MONITORINFO();
            var pt = new NativeMethods.POINT { X = (int)s.BarX, Y = (int)s.BarY };
            var target = s.DockMode == DockMode.Free
                ? new NativeMethods.POINT { X = (int)s.BarX, Y = (int)s.BarY }
                : (NativeMethods.GetMonitorAt(pt, ref mi) ? CalcPositions(mi).hide : default);
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, target.X, target.Y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            _shown = s.DockMode == DockMode.Free;
            if (s.DockMode != DockMode.Free)
            {
                // 启动时在记忆屏滑入展示片刻
                SlideIn(mi);
            }
            AppLog.Write($"启动恢复: 记忆({s.BarX:F0},{s.BarY:F0}) Dock={s.DockMode} Follow={s.FollowMouse} → hide({target.X},{target.Y})");
        }
        else
        {
            // 首次：主屏顶部居中滑入
            var mi = new NativeMethods.MONITORINFO();
            var screen = new NativeMethods.POINT { X = 0, Y = 0 };
            if (NativeMethods.GetMonitorAt(screen, ref mi)) SlideIn(mi);
            AppLog.Write("启动恢复: 无记忆，主屏滑入");
        }
    }

    #endregion

    #region 按钮条构建

    public void Rebuild()
    {
        BarItems.Items.Clear();
        foreach (var node in _store.Root.Children)
            BarItems.Items.Add(BuildItemButton(node));
    }

    private Button BuildItemButton(BookmarkNode node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var img = new Image
        {
            Source = NodeIcon(node),
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        panel.Children.Add(img);
        panel.Children.Add(new TextBlock
        {
            Text = node.Name + (node.IsFolder ? " ▾" : ""),
            VerticalAlignment = VerticalAlignment.Center
        });

        var btn = new Button
        {
            Content = panel,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = Cursors.Hand,
            Tag = node,
            Focusable = false
        };
        if (!node.IsFolder) btn.ToolTip = node.Path;

        btn.ContextMenu = BuildNodeMenu(node);

        // 拖动源：悬浮栏内排序（文件夹仅根级排序，不允许拖入其他文件夹）
        AttachDragSource(btn, node);

        // 放置目标：内部拖动=排序（竖线指示，左半前/右半后）；外部文件拖到文件夹按钮=创建子书签
        btn.AllowDrop = true;
        btn.DragOver += (s, e) => Btn_DragOver(e, btn, node);
        btn.DragLeave += (s, e) =>
        {
            btn.Background = Brushes.Transparent;
            RemoveBtnSep();
            if (ReferenceEquals(_dragOverFolderBtn, btn)) { _btnExpandTimer.Stop(); _dragOverFolderBtn = null; }
        };
        btn.Drop += (s, e) => Btn_Drop(e, btn, node);

        if (node.IsFolder)
        {
            btn.MouseEnter += (s, e) =>
            {
                _hoverFolderButton = btn;
                if (_activePopup is { IsOpen: true } && _activeFolderButton != btn)
                    OpenFolderPopup(btn); // 已有菜单打开时，同级悬停即切换
                else
                    _hoverTimer.Start();
            };
            btn.MouseLeave += (s, e) => { _hoverTimer.Stop(); _hoverFolderButton = null; };
            btn.Click += (s, e) => OpenFolderPopup(btn);
        }
        else
        {
            btn.Click += (s, e) => TryOpen(node);
        }
        return btn;
    }

    /// <summary>悬浮栏排序指示线（竖条）</summary>
    private readonly System.Windows.Controls.Border _btnDropSep = new()
    {
        Width = 2,
        Background = Brushes.DodgerBlue,
        Margin = new Thickness(0, 3, 0, 3),
        IsHitTestVisible = false
    };

    /// <summary>书签拖到文件夹按钮上悬停 400ms → 展开其子列表</summary>
    private readonly DispatcherTimer _btnExpandTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private Button? _dragOverFolderBtn;

    private void RemoveBtnSep()
    {
        if (_btnDropSep.Parent is ItemsControl ic) ic.Items.Remove(_btnDropSep);
    }

    /// <summary>文件夹按钮的排序边缘区宽度（px）：左右各此宽度内 = 排序，中间 = 移入</summary>
    private const double FolderEdgeZone = 14;

    private void Btn_DragOver(DragEventArgs e, Button btn, BookmarkNode node)
    {
        if (e.Data.GetData(typeof(BookmarkNode)) is BookmarkNode drag)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            double x = e.GetPosition(btn).X;
            double w = btn.RenderSize.Width;

            // 书签拖到文件夹按钮中部 = 移入该文件夹（高亮 + 悬停 400ms 展开其子列表）
            if (node.IsFolder && !drag.IsFolder && x >= FolderEdgeZone && x <= w - FolderEdgeZone)
            {
                RemoveBtnSep();
                btn.Background = DropHighlight;
                if (!ReferenceEquals(_dragOverFolderBtn, btn))
                {
                    _dragOverFolderBtn = btn;
                    _btnExpandTimer.Stop();
                    _btnExpandTimer.Start();
                }
                return;
            }

            // 排序（书签/文件夹均可与任何按钮排序）：左半=插前面，右半=插后面
            _dragOverFolderBtn = null;
            _btnExpandTimer.Stop();
            btn.Background = Brushes.Transparent;
            RemoveBtnSep();
            bool left = x < w / 2;
            int idx = BarItems.Items.IndexOf(btn);
            BarItems.Items.Insert(left ? idx : idx + 1, _btnDropSep);
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop) && node.IsFolder)
        {
            e.Effects = DragDropEffects.Copy;   // 外部文件拖到文件夹按钮 = 创建子书签
            e.Handled = true;
            btn.Background = DropHighlight;
            return;
        }
        e.Effects = DragDropEffects.None;
    }

    private void Btn_Drop(DragEventArgs e, Button btn, BookmarkNode node)
    {
        e.Handled = true;
        btn.Background = Brushes.Transparent;
        _dragOverFolderBtn = null;
        _btnExpandTimer.Stop();

        if (e.Data.GetData(typeof(BookmarkNode)) is BookmarkNode drag)
        {
            double x = e.GetPosition(btn).X;
            double w = btn.RenderSize.Width;

            // 书签在文件夹按钮中部松开 = 放入该文件夹子列表末尾
            if (node.IsFolder && !drag.IsFolder && !ReferenceEquals(drag, node)
                && x >= FolderEdgeZone && x <= w - FolderEdgeZone)
            {
                RemoveBtnSep();
                MoveNode(drag, node);
                return;
            }

            // 根级排序（书签/文件夹与任何按钮，含文件夹边缘区）
            int sepIdx = BarItems.Items.IndexOf(_btnDropSep);
            RemoveBtnSep();
            if (ReferenceEquals(drag, node)) return;

            var root = _store.Root;
            int target = sepIdx < 0 ? root.Children.Count : sepIdx;
            int cur = root.Children.IndexOf(drag);
            if (cur >= 0 && cur < target) target--; // 同集合内后移时索引修正
            root.Children.Remove(drag);
            root.Children.Insert(Math.Clamp(target, 0, root.Children.Count), drag);
            _store.Save();
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && node.IsFolder)
        {
            foreach (var p in paths)
            {
                var bm = BookmarkNode.NewBookmark(SafeName(p), p);
                bm.Icon = IconHelper.FetchIconDataUri(p);
                node.Children.Add(bm);
            }
            _store.Save();
        }
    }

    private ImageSource? NodeIcon(BookmarkNode node)
    {
        var src = IconHelper.FromDataUri(node.Icon);
        if (src == null && node.IsFolder) src = IconHelper.DefaultFolderIcon;
        return src;
    }

    private void TryOpen(BookmarkNode node)
    {
        PathOpener.OpenAsync(node.Path, mode: _store.Settings.FolderOpenMode, onError: err =>
            Dispatcher.Invoke(() => MessageBox.Show(this, err, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Warning)));
    }

    #endregion

    #region 多级弹出菜单

    private Menu BuildFolderMenu(BookmarkNode folder, HashSet<Guid>? keepExpanded = null)
    {
        // Menu 默认是水平菜单栏面板，改为纵向列表
        var verticalPanel = new FrameworkElementFactory(typeof(StackPanel));
        var menu = new Menu
        {
            Background = Brushes.White,
            VerticalAlignment = VerticalAlignment.Top,
            ItemsPanel = new ItemsPanelTemplate(verticalPanel),
            Tag = folder
        };
        bool has = false;
        foreach (var child in folder.Children)
        {
            menu.Items.Add(BuildMenuItem(child, keepExpanded));
            has = true;
        }
        if (!has)
            menu.Items.Add(new MenuItem { Header = "(空)", IsEnabled = false });

        // 列表空白处松开 = 放入该文件夹末尾
        menu.AllowDrop = true;
        menu.DragOver += (s, e) =>
        {
            if (e.Data.GetDataPresent(typeof(BookmarkNode))) { e.Effects = DragDropEffects.Move; e.Handled = true; }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
            else e.Effects = DragDropEffects.None;
        };
        menu.Drop += (s, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(typeof(BookmarkNode)) is BookmarkNode drag)
                MoveNode(drag, folder);
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] ps && ps.Length > 0)
            {
                foreach (var p in ps)
                {
                    var bm = BookmarkNode.NewBookmark(SafeName(p), p);
                    bm.Icon = IconHelper.FetchIconDataUri(p);
                    folder.Children.Add(bm);
                }
                _store.Save();
            }
        };
        return menu;
    }

    private void OpenFolderPopup(Button btn)
    {
        _hoverTimer.Stop();
        if (_activeFolderButton == btn && _activePopup is { IsOpen: true }) return;
        CloseActivePopup();

        if (btn.Tag is not BookmarkNode folder) return;
        var popup = new Popup
        {
            Child = BuildFolderMenu(folder),
            PlacementTarget = btn,
            Placement = PlacementMode.Bottom,
            // StaysOpen=false 会捕获鼠标：导致其他父节点收不到 MouseEnter（无法切换）、
            // 右键 mousedown 被判为点击外部而关闭父链。改为 true，由 ClosePopupIfStale 自行管理关闭。
            StaysOpen = true,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade
        };
        popup.Closed += (s, e) => { if (ReferenceEquals(_activePopup, popup)) { _activePopup = null; _activeFolderButton = null; } };
        popup.IsOpen = true;
        _activePopup = popup;
        _activeFolderButton = btn;
        _popupLastActive = DateTime.Now;
    }

    /// <summary>数据变更后刷新打开中的子列表内容（删除/改名立即生效），并保持原展开链</summary>
    private void RefreshActivePopup()
    {
        if (_activePopup is not { IsOpen: true }) return;
        if (_activeFolderButton?.Tag is not BookmarkNode oldFolder) return;
        var folder = _store.Root.FindById(oldFolder.Id);
        if (folder == null)
        {
            CloseActivePopup(); // 该文件夹本身被删
            return;
        }

        // 收集当前展开的子菜单（多层数据刷新后保持展开，新建子项立即可见）
        var expanded = new HashSet<Guid>();
        if (_activePopup.Child is Menu oldMenu) CollectExpanded(oldMenu.Items, expanded);

        // Rebuild() 已重建按钮实例：按 Id 找新按钮并重锚定 PlacementTarget
        // （旧按钮已移出视觉树，不重锚定会导致 Popup 重开时定位失败、跑到屏幕中间）
        Button? newBtn = null;
        foreach (var item in BarItems.Items)
            if (item is Button b && b.Tag is BookmarkNode bn && bn.Id == folder.Id)
                newBtn = b;
        if (newBtn == null)
        {
            CloseActivePopup(); // 该文件夹已不在根级（被移走）
            return;
        }

        var p = _activePopup;
        p.IsOpen = false;               // 触发 Closed（_activePopup 会被清空，稍后恢复）
        p.PlacementTarget = newBtn;     // 重锚定到新按钮
        p.Child = BuildFolderMenu(folder, expanded);
        p.IsOpen = true;
        _activePopup = p;
        _activeFolderButton = newBtn;
        _popupLastActive = DateTime.Now;
    }

    private static void CollectExpanded(ItemCollection items, HashSet<Guid> into)
    {
        foreach (var it in items.OfType<MenuItem>())
        {
            if (it.IsSubmenuOpen && it.Tag is BookmarkNode n) into.Add(n.Id);
            if (it.HasItems) CollectExpanded(it.Items, into);
        }
    }

    private MenuItem BuildMenuItem(BookmarkNode node, HashSet<Guid>? keepExpanded = null)
    {
        var icon = new Image { Source = NodeIcon(node), Width = 16, Height = 16 };
        MenuItem mi;
        if (node.IsFolder)
        {
            mi = new MenuItem { Header = node.Name, Icon = icon };
            if (node.Children.Count == 0)
                mi.Items.Add(new MenuItem { Header = "(空)", IsEnabled = false });
            else
                foreach (var c in node.Children) mi.Items.Add(BuildMenuItem(c, keepExpanded));
            if (keepExpanded != null && keepExpanded.Contains(node.Id))
                mi.IsSubmenuOpen = true; // 数据刷新后保持原展开链（新建子书签立即可见）
        }
        else
        {
            mi = new MenuItem { Header = node.Name, Icon = icon, ToolTip = node.Path };
            mi.Click += (s, e) =>
            {
                if (_suppressNextClick) { _suppressNextClick = false; return; }
                CloseActivePopup();
                TryOpen(node);
            };
        }
        mi.Tag = node;

        // 右键编辑（隧道事件：右键菜单打开状态下再次右键不被吞掉）
        mi.PreviewMouseRightButtonUp += (s, e) =>
        {
            e.Handled = true;
            ShowNodeMenuOnItem(s, node);
        };

        // 拖动源：按住左键拖出
        AttachDragSource(mi, node);

        // 放置目标：书签项=指示线插入；文件夹项=移入末尾（悬停 400ms 展开其子列表）
        mi.AllowDrop = true;
        mi.DragOver += (s, e) => Item_DragOver(e, mi, node);
        mi.DragLeave += (s, e) => Item_DragLeave(mi);
        mi.Drop += (s, e) => Item_Drop(e, mi, node);

        return mi;
    }

    #region 列表内拖动排序与移动

    private bool _suppressNextClick;
    private readonly DispatcherTimer _dropExpandTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private MenuItem? _dropExpandItem;

    /// <summary>拖动插入指示线（动态移动到插入位置）</summary>
    private readonly Separator _dropSep = new()
    {
        Height = 2,
        Background = Brushes.DodgerBlue,
        Focusable = false,
        IsHitTestVisible = false
    };

    private void AttachDragSource(UIElement el, BookmarkNode node)
    {
        Point? start = null;
        el.PreviewMouseLeftButtonDown += (s, e) => start = e.GetPosition(el);
        el.PreviewMouseLeftButtonUp += (s, e) => start = null;
        el.PreviewMouseMove += (s, e) =>
        {
            if (start is not Point p0) return;
            if (e.LeftButton != MouseButtonState.Pressed) { start = null; return; }
            var p = e.GetPosition(el);
            if (Math.Abs(p.X - p0.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - p0.Y) <= SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            _suppressNextClick = true;
            try { DragDrop.DoDragDrop(el, new DataObject(typeof(BookmarkNode), node), DragDropEffects.Move); }
            finally
            {
                RemoveDropSep();
                RemoveBtnSep();
                _btnExpandTimer.Stop();
                _dragOverFolderBtn = null;
            }
        };
    }

    private static Menu? MenuOf(MenuItem mi) => mi.Parent as Menu;

    private void RemoveDropSep()
    {
        if (_dropSep.Parent is Menu m) m.Items.Remove(_dropSep);
    }

    private void Item_DragOver(DragEventArgs e, MenuItem mi, BookmarkNode node)
    {
        if (e.Data.GetDataPresent(typeof(BookmarkNode)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            if (node.IsFolder)
            {
                // 不支持多层文件夹：文件夹不能拖入文件夹（仅书签可移入）
                if (e.Data.GetData(typeof(BookmarkNode)) is BookmarkNode d && d.IsFolder)
                {
                    e.Effects = DragDropEffects.None;
                    Item_DragLeave(mi);
                    RemoveDropSep();
                    return;
                }
                // 书签拖到文件夹项：整体高亮 = 移入末尾；悬停 400ms 展开其子列表
                RemoveDropSep();
                mi.Background = DropHighlight;
                if (_dropExpandItem != mi)
                {
                    _dropExpandItem = mi;
                    _dropExpandTimer.Stop();
                    _dropExpandTimer.Start();
                }
                return;
            }

            // 书签项：按上/下半区移动指示线（蓝色横线 = 松开后落点）
            mi.Background = Brushes.Transparent;
            var menu = MenuOf(mi);
            if (menu == null) return;
            RemoveDropSep();
            int idx = menu.Items.IndexOf(mi);
            bool upper = e.GetPosition(mi).Y < mi.RenderSize.Height / 2;
            menu.Items.Insert(upper ? idx : idx + 1, _dropSep);
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            mi.Background = DropHighlight;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Item_DragLeave(MenuItem mi)
    {
        mi.Background = Brushes.Transparent;
        if (ReferenceEquals(_dropExpandItem, mi))
        {
            _dropExpandTimer.Stop();
            _dropExpandItem = null;
        }
    }

    private void Item_Drop(DragEventArgs e, MenuItem mi, BookmarkNode node)
    {
        e.Handled = true;
        Item_DragLeave(mi);

        if (e.Data.GetData(typeof(BookmarkNode)) is BookmarkNode drag)
        {
            if (node.IsFolder)
            {
                MoveNode(drag, node); // 在文件夹上松开 = 放入其子列表末尾
                return;
            }

            // 书签项：按指示线位置插入
            var menu = MenuOf(mi);
            int sepIdx = menu?.Items.IndexOf(_dropSep) ?? -1;
            RemoveDropSep();
            var folder = menu?.Tag as BookmarkNode;
            if (folder == null)
            {
                MoveNode(drag, node); // 兜底：插到该项前
                return;
            }
            if (folder.Children.Count == 0)
            {
                folder.Children.Add(drag);
                _store.Save();
                return;
            }
            if (ReferenceEquals(_store.Root.FindParent(drag), folder))
            {
                int cur = folder.Children.IndexOf(drag);
                if (cur >= 0 && cur < sepIdx) sepIdx--; // 同文件夹内后移时索引修正
            }
            MoveNodeTo(drag, folder, sepIdx < 0 ? folder.Children.Count : sepIdx);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            var target = node.IsFolder ? node : _store.Root.FindParent(node) ?? _store.Root;
            foreach (var p in paths)
            {
                var bm = BookmarkNode.NewBookmark(SafeName(p), p);
                bm.Icon = IconHelper.FetchIconDataUri(p);
                target.Children.Add(bm);
            }
            _store.Save();
        }
    }

    /// <summary>移动节点：targetItem 为文件夹 → 移入末尾；为书签 → 插到它前面；null → 根级末尾</summary>
    private void MoveNode(BookmarkNode drag, BookmarkNode? targetItem)
    {
        if (ReferenceEquals(drag, targetItem)) return;
        if (targetItem != null && drag.ContainsSubtree(targetItem)) return; // 不能移入自己的子孙
        if (drag.IsFolder && targetItem != null) return;                    // 不支持多层：文件夹仅根级
        if (targetItem != null && targetItem.IsFolder)
        {
            MoveNodeTo(drag, targetItem, targetItem.Children.Count);
            return;
        }

        var parent = _store.Root.FindParent(drag) ?? _store.Root;
        parent.Children.Remove(drag);
        if (targetItem == null)
            _store.Root.Children.Add(drag);
        else
        {
            var tp = _store.Root.FindParent(targetItem) ?? _store.Root;
            tp.Children.Insert(Math.Max(0, tp.Children.IndexOf(targetItem)), drag);
        }
        _store.Save();
    }

    /// <summary>移动节点到指定文件夹的指定索引</summary>
    private void MoveNodeTo(BookmarkNode drag, BookmarkNode targetFolder, int index)
    {
        if (drag.ContainsSubtree(targetFolder)) return;
        if (drag.IsFolder && !ReferenceEquals(targetFolder, _store.Root)) return; // 不支持多层：文件夹仅根级
        var parent = _store.Root.FindParent(drag) ?? _store.Root;
        parent.Children.Remove(drag);
        if (ReferenceEquals(parent, targetFolder))
        {
            // 同文件夹内移动：移除后修正索引
            if (index > parent.Children.Count) index = parent.Children.Count;
        }
        targetFolder.Children.Insert(Math.Clamp(index, 0, targetFolder.Children.Count), drag);
        _store.Save();
    }

    private static string SafeName(string p)
    {
        try
        {
            var n = System.IO.Path.GetFileName(p.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(n) ? p : n;
        }
        catch { return p; }
    }

    #endregion

    /// <summary>
    /// 在子菜单项上弹出右键编辑菜单；打开期间父级子列表保持。
    /// 关闭后不再强制收起（交给 150ms 自然判定），避免"再次右键"时事件被旧菜单吞掉导致整链瞬收。
    /// </summary>
    private void ShowNodeMenuOnItem(object sender, BookmarkNode node)
    {
        var nodeMenu = BuildNodeMenu(node);
        nodeMenu.PlacementTarget = sender as UIElement;
        nodeMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        nodeMenu.Closed += (s, e) => _activeNodeMenu = null;
        nodeMenu.IsOpen = true;
        _activeNodeMenu = nodeMenu;
        _popupLastActive = DateTime.Now;
    }

    private void CloseActivePopup()
    {
        if (_activePopup != null)
        {
            var p = _activePopup;
            _activePopup = null;
            _activeFolderButton = null;
            p.IsOpen = false;
        }
    }

    #endregion

    #region 节点右键菜单（增删改）

    private ContextMenu BuildNodeMenu(BookmarkNode node)
    {
        var menu = new ContextMenu();
        if (!node.IsFolder)
        {
            menu.Items.Add(MItem("打开", () => TryOpen(node)));
            menu.Items.Add(MItem("以管理员身份打开", () =>
            {
                PathOpener.OpenAsync(node.Path, elevated: true, onError: err =>
                    Dispatcher.Invoke(() => MessageBox.Show(this, err, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Warning)));
            }));
            menu.Items.Add(MItem("刷新图标", () =>
            {
                node.Icon = IconHelper.FetchIconDataUri(node.Path);
                _store.Save();
            }));
            menu.Items.Add(new Separator());
        }
        else
        {
            menu.Items.Add(MItem("新建子书签…", () => EditItem(node, asFolder: false)));
            // 暂不支持多层文件夹：不提供"新建子文件夹"
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(MItem("编辑…", () => EditItem(node: node)));
        menu.Items.Add(MItem("删除", () => DeleteNode(node)));
        TrackMenu(menu);
        return menu;
    }

    /// <summary>统一跟踪右键菜单的打开/关闭：打开期间悬浮栏与子列表保持显示</summary>
    private void TrackMenu(ContextMenu menu)
    {
        menu.Opened += (s, e) =>
        {
            _activeNodeMenu = menu;
            _popupLastActive = DateTime.Now;
        };
        menu.Closed += (s, e) =>
        {
            if (ReferenceEquals(_activeNodeMenu, menu)) _activeNodeMenu = null;
        };
    }

    private BookmarkNode? FindParentOf(BookmarkNode node) => _store.Root.FindParent(node);

    private MenuItem MItem(string header, Action act)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (s, e) => act();
        return mi;
    }

    /// <summary>编辑/新建：asFolder=true 新建子文件夹；node=null 且 parentFolder=null → 根级新建书签</summary>
    private void EditItem(BookmarkNode? parentFolder = null, bool asFolder = false, BookmarkNode? node = null, object? replaceInParent = null)
    {
        BookmarkNode target;
        BookmarkNode parent;
        if (node != null)
        {
            target = node;
            parent = FindParentOf(node) ?? _store.Root;
        }
        else
        {
            parent = parentFolder ?? _store.Root;
            target = asFolder ? BookmarkNode.NewFolder("新文件夹") : BookmarkNode.NewBookmark("新书签", "");
        }

        _modalOpen = true;
        try
        {
            if (EditDialog.Show(this, target, isNew: node == null))
            {
                if (node == null) parent.Children.Add(target);
                if (!target.IsFolder && string.IsNullOrEmpty(target.Icon))
                    target.Icon = IconHelper.FetchIconDataUri(target.Path);
                _store.Save();
            }
        }
        finally
        {
            _modalOpen = false;
            _popupLastActive = DateTime.Now;          // 对话框关闭后给子列表一小段缓冲
            _lastActive = DateTime.Now.AddSeconds(3); // 悬浮栏保持 3 秒供用户看到变更结果
        }
    }

    private void DeleteNode(BookmarkNode node)
    {
        int n = node.IsFolder ? node.CountBookmarks() : 1;
        var msg = node.IsFolder
            ? $"删除文件夹“{node.Name}”及其 {n} 个书签？"
            : $"删除书签“{node.Name}”？";

        _modalOpen = true;
        try
        {
            if (MessageBox.Show(this, msg, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var parent = FindParentOf(node) ?? _store.Root;
                parent.Children.Remove(node);
                _store.Save();
            }
        }
        finally
        {
            _modalOpen = false;
            _popupLastActive = DateTime.Now;
            _lastActive = DateTime.Now.AddSeconds(3);
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var m = new ContextMenu();
        m.Items.Add(MItem("新建书签…", () => EditItem()));
        m.Items.Add(MItem("新建文件夹…", () => EditItem(asFolder: true)));
        m.PlacementTarget = AddButton;
        m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        TrackMenu(m);
        m.IsOpen = true;
    }

    #endregion

    #region 拖拽（外部文件拖入 + 内部节点拖动）

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(BookmarkNode)))
        {
            // 内部节点拖动：根级按钮上的排序由按钮级 DragOver/Drop 处理；窗口空白 = 移到根级末尾
            e.Effects = DragDropEffects.Move;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;

        // 外部文件：命中文件夹按钮 → 高亮（拖入该按钮 = 放入该文件夹）
        ClearDropHighlight();
        _dropTargetBtn = HitFolderButton(e.GetPosition(this));
        if (_dropTargetBtn != null) _dropTargetBtn.Background = DropHighlight;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        // 先取目标再清高亮（ClearDropHighlight 会同时清掉 _dropTargetBtn 引用）
        var targetFolder = _dropTargetBtn?.Tag as BookmarkNode;
        ClearDropHighlight();

        // 内部节点拖动：在窗口空白处松开 = 移到根级末尾（按钮上松开由按钮级处理）
        if (e.Data.GetData(typeof(BookmarkNode)) is BookmarkNode drag)
        {
            MoveNode(drag, null);
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        foreach (var p in paths)
        {
            var bm = BookmarkNode.NewBookmark(SafeName(p), p);
            bm.Icon = IconHelper.FetchIconDataUri(p);
            (targetFolder ?? _store.Root).Children.Add(bm);
        }
        _store.Save();
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => ClearDropHighlight();

    private void ClearDropHighlight()
    {
        if (_dropTargetBtn != null) _dropTargetBtn.Background = Brushes.Transparent;
        _dropTargetBtn = null;
    }

    /// <summary>鼠标位置命中根级文件夹按钮？</summary>
    private Button? HitFolderButton(Point ptInWindow)
    {
        foreach (var item in BarItems.Items)
        {
            if (item is Button b && b.Tag is BookmarkNode { IsFolder: true })
            {
                var pos = b.TransformToAncestor(this).Transform(new Point(0, 0));
                var r = new Rect(pos, b.RenderSize);
                if (r.Contains(ptInWindow)) return b;
            }
        }
        return null;
    }

    #endregion

    #region 拖动定位 / 停靠菜单

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        try { DragMove(); } catch { /* 极端时机下抛 InvalidOperationException，忽略 */ }
        _dragging = false;
        OnDragFinished();
        e.Handled = true;
    }

    private void Handle_MouseEnter(object sender, MouseEventArgs e) => RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private void Handle_MouseLeave(object sender, MouseEventArgs e) => RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xE4));

    private void OnDragFinished()
    {
        if (!NativeMethods.GetWindowRect(_hwnd, out var wr)) return;
        SavedX = wr.Left;
        SavedY = wr.Top;
        HasSaved = true;
        var center = new NativeMethods.POINT { X = (wr.Left + wr.Right) / 2, Y = (wr.Top + wr.Bottom) / 2 };
        var mi = new NativeMethods.MONITORINFO();
        if (!NativeMethods.GetMonitorAt(center, ref mi)) return;

        var s = _store.Settings;
        if (s.DockMode == DockMode.Free)
        {
            s.BarX = wr.Left;
            s.BarY = wr.Top;
            _store.SaveSettings();
        }
        else
        {
            // 停靠模式：吸附到拖动后所在屏的显示位
            var (show, _) = CalcPositions(mi);
            s.BarX = show.X; // 记忆屏幕归属
            s.BarY = show.Y;
            _store.SaveSettings();
            _animBusy = true;
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, show.X, show.Y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            _animBusy = false;
            _shown = true;
            _lastActive = DateTime.Now;
        }
    }

    private readonly List<MenuItem> _dockItems = new();

    private ContextMenu BuildBarMenu()
    {
        _dockItems.Clear();
        var menu = new ContextMenu();

        var dock = new MenuItem { Header = "停靠位置" };
        foreach (var (label, mode) in new[]
        {
            ("屏幕顶部居中", DockMode.TopAutoHide),
            ("左上角", DockMode.TopLeftAutoHide),
            ("右上角", DockMode.TopRightAutoHide),
            ("左下角", DockMode.BottomLeftAutoHide),
            ("右下角", DockMode.BottomRightAutoHide)
        })
        {
            var item = RadioMode(label, mode);
            _dockItems.Add(item);
            dock.Items.Add(item);
        }
        menu.Items.Add(dock);

        // 文件夹打开方式（三态）
        var open = new MenuItem { Header = "文件夹打开方式" };
        var openItems = new List<MenuItem>();
        foreach (var (label, m) in new[]
        {
            ("新标签页打开（默认）", FolderOpenMode.NewTab),
            ("在当前窗口跳转", FolderOpenMode.SameWindow),
            ("总是新开窗口", FolderOpenMode.NewWindow)
        })
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            item.IsChecked = _store.Settings.FolderOpenMode == m;
            var captured = m;
            item.Click += (s, e) =>
            {
                _store.Settings.FolderOpenMode = captured;
                _store.SaveSettings();
                foreach (var i in openItems) i.IsChecked = ReferenceEquals(i, item);
            };
            openItems.Add(item);
            open.Items.Add(item);
        }
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(MItem("书签管理…", () => ((App)Application.Current).ShowManage()));
        menu.Items.Add(MItem("隐藏悬浮条", () => ((App)Application.Current).SetBarVisible(false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MItem("退出", () => ((App)Application.Current).ShutdownApp()));
        TrackMenu(menu);
        return menu;
    }

    private MenuItem RadioMode(string name, DockMode mode)
    {
        var mi = new MenuItem { Header = name, IsCheckable = true };
        mi.IsChecked = _store.Settings.DockMode == mode;
        mi.Click += (s, e) =>
        {
            _store.Settings.DockMode = mode;
            _store.SaveSettings();
            foreach (var m in _dockItems) m.IsChecked = ReferenceEquals(m, mi);

            if (mode == DockMode.Free)
            {
                if (!NativeMethods.GetWindowRect(_hwnd, out var wr)) return;
                _store.Settings.BarX = wr.Left;
                _store.Settings.BarY = wr.Top;
                _store.SaveSettings();
                _shown = true;
            }
            else
            {
                var c = new NativeMethods.MONITORINFO();
                var center = default(NativeMethods.POINT);
                if (NativeMethods.GetWindowRect(_hwnd, out var wr))
                    center = new NativeMethods.POINT { X = (wr.Left + wr.Right) / 2, Y = (wr.Top + wr.Bottom) / 2 };
                if (NativeMethods.GetMonitorAt(center, ref c)) SlideIn(c);
            }
        };
        return mi;
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _dockTimer.Stop();
        _animTimer.Stop();
        if (_hwnd != IntPtr.Zero) NativeMethods.UnregisterHotKey(_hwnd, _hotkeyId);
        base.OnClosed(e);
    }
}
