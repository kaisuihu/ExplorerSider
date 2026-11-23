using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ExplorerSider.Models;
using ExplorerSider.Services;
using ExplorerSider.UI;

namespace ExplorerSider;

public partial class App : Application
{
    private static Mutex? _mutex;
    private BookmarkStore? _store;
    private BarWindow? _bar;
    private ManageWindow? _manage;
    private TrayIconService? _tray;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---- 命令行工具模式（不启动 UI）----
        var args = e.Args;
        int gi = Array.IndexOf(args, "--genicon");
        if (gi >= 0)
        {
            var path = gi + 1 < args.Length ? args[gi + 1] : Path.Combine(AppContext.BaseDirectory, "app.ico");
            CloverIcon.WriteIco(path, new[] { 16, 24, 32, 48, 64, 128, 256 });
            AttachConsole(-1);
            Console.WriteLine($"icon written: {Path.GetFullPath(path)}");
            Shutdown(0);
            return;
        }

        int st = Array.IndexOf(args, "--selftest");
        if (st >= 0)
        {
            int code = SelfTest.Run(args.Length > st + 1 ? args[st + 1] : null);
            Shutdown(code);
            return;
        }

        // ---- 单实例 ----
        _mutex = new Mutex(true, "ExplorerSider_SingleInstance_Mutex", out var isNew);
        if (!isNew)
        {
            MessageBox.Show(L.AlreadyRunning, "ExplorerSider",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AutoStartHelper.RefreshPathIfEnabled();

        _store = BookmarkStore.Load();
        L.Set(_store.Settings.Language);
        AppLog.Write($"==== 启动 v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version} Bar=({_store.Settings.BarX:F0},{_store.Settings.BarY:F0}) Dock={_store.Settings.DockMode} Follow={_store.Settings.FollowMouse} ShowBar={_store.Settings.ShowBar}");
        _bar = new BarWindow(_store);
        _bar.Rebuild();
        if (_store.Settings.ShowBar) _bar.Show();

        _tray = new TrayIconService { Menu = BuildTrayMenu() };
        _tray.LeftClick += () => SetBarVisible(!(_bar?.IsVisible ?? false));
        _tray.LeftDoubleClick += ShowManage;
        _tray.RightClick += () => Dispatcher.Invoke(() =>
        {
            _tray.Menu = BuildTrayMenu(); // 刷新动态文本
            _tray.ShowContextMenu();
        });
        _tray.Init();

        if (_store.Settings.FirstRun)
        {
            _store.Settings.FirstRun = false;
            _store.SaveSettings();
            ShowManage();
        }

        Exit += (s, _) =>
        {
            SaveBarPosition();
            _tray?.Dispose();
            _mutex?.ReleaseMutex();
        };
    }

    public void ShowManage()
    {
        if (_store == null) return;
        if (_manage == null)
        {
            _manage = new ManageWindow(_store);
            _manage.Closed += (s, e) => _manage = null;
            _manage.Show();
        }
        else
        {
            _manage.Activate();
        }
    }

    public void SetBarVisible(bool visible)
    {
        if (_bar == null || _store == null) return;
        // 显示走救援路径：即使窗口停在屏外/状态失真死角，也强制带回鼠标所在屏显示位
        if (visible) _bar.RescueShow(); else _bar.Hide();
        _store.Settings.ShowBar = visible;
        _store.SaveSettings();
        Dispatcher.Invoke(() => _tray!.Menu = BuildTrayMenu());
    }

    public void ShutdownApp()
    {
        SaveBarPosition();
        Shutdown();
    }

    private void SaveBarPosition()
    {
        if (_store == null) return;
        // 只有本次会话拖动过把手才更新记忆——SavedX/SavedY 初值为 0，
        // 未拖动时覆写会把无意义的 (0,0) 写进设置，导致重启后丢失屏幕归属记忆
        if (_bar is { HasSaved: true })
        {
            _store.Settings.BarX = _bar.SavedX;
            _store.Settings.BarY = _bar.SavedY;
        }
        _store.SaveSettings();
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        var store = _store!;

        var toggleText = store.Settings.ShowBar ? L.TrayHideBar : L.TrayShowBar;
        menu.Items.Add(TrayItem(toggleText, () => SetBarVisible(!store.Settings.ShowBar)));
        menu.Items.Add(TrayItem(L.TrayManage, ShowManage));
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(L.TrayImport, () => ImportBookmarks(_manage)));
        menu.Items.Add(TrayItem(L.TrayExport, () => ExportBookmarks(_manage)));
        menu.Items.Add(new Separator());

        var follow = new MenuItem { Header = L.TrayFollowMouse, IsCheckable = true, IsChecked = store.Settings.FollowMouse };
        follow.Click += (s, e) =>
        {
            store.Settings.FollowMouse = follow.IsChecked; // IsCheckable 点击后自动翻转
            store.SaveSettings();
        };
        menu.Items.Add(follow);

        var auto = new MenuItem { Header = L.TrayAutoStart, IsCheckable = true, IsChecked = AutoStartHelper.IsEnabled() };
        auto.Click += (s, e) =>
        {
            var enable = !AutoStartHelper.IsEnabled();
            AutoStartHelper.SetEnabled(enable);
            auto.IsChecked = enable;
        };
        menu.Items.Add(auto);

        // 语言切换（English 默认；切换后托盘菜单重建，悬浮栏/管理窗菜单在下次打开/右键时自然生效）
        var lang = new MenuItem { Header = L.LanguageMenu };
        var en = new MenuItem { Header = "English", IsCheckable = true, IsChecked = !L.IsZh };
        var zh = new MenuItem { Header = "简体中文", IsCheckable = true, IsChecked = L.IsZh };
        en.Click += (s, e) => SwitchLanguage("en-US");
        zh.Click += (s, e) => SwitchLanguage("zh-CN");
        lang.Items.Add(en);
        lang.Items.Add(zh);
        menu.Items.Add(lang);

        var hk = new MenuItem { Header = L.TrayHotkey(store.Settings.HotkeyShowBar), IsEnabled = false };
        menu.Items.Add(hk);
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(L.TrayExit, ShutdownApp));
        return menu;
    }

    private void SwitchLanguage(string lang)
    {
        if (L.Current == lang) return;
        L.Set(lang);
        _store!.Settings.Language = lang;
        _store.SaveSettings();
        AppLog.Write($"Language switched to {lang}");
        _tray!.Menu = BuildTrayMenu(); // 立即重建托盘菜单
        _manage?.ApplyLanguage();      // 管理窗口开着则重刷静态文本
        _bar?.InvalidateMenus();       // 悬浮栏：重建把手菜单与各按钮缓存菜单
    }

    private static MenuItem TrayItem(string header, Action act)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (s, e) => act();
        return mi;
    }

    #region 导入/导出（托盘与管理窗口共用）

    public static void ImportBookmarks(Window? owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.ImportDialogTitle,
            Filter = L.ImportFilter
        };
        if (dlg.ShowDialog(owner) != true) return;

        var app = (App)Current;
        var store = app._store!;
        try
        {
            var html = File.ReadAllText(dlg.FileName);
            var imported = NetscapeHtml.Import(html);
            int n = imported.CountBookmarks();

            var choice = MessageBox.Show(owner,
                L.ImportSummary(n, imported.CountFolders()),
                L.ImportModeTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (choice == MessageBoxResult.Yes)
            {
                store.ReplaceRoot(imported);
            }
            else if (choice == MessageBoxResult.No)
            {
                imported.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
                store.Root.Children.Add(imported);
                store.Save();
            }
            else return;

            MessageBox.Show(owner, L.ImportDone(n), "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, L.ImportFailed + ex.Message, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void ExportBookmarks(Window? owner)
    {
        var app = (App)Current;
        var store = app._store!;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.ExportDialogTitle,
            Filter = L.ExportFilter,
            FileName = $"{L.ExportFileName}{DateTime.Now:yyyy.MM.dd}.html"
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, NetscapeHtml.Export(store.Root), new UTF8Encoding(true));
            MessageBox.Show(owner, L.ExportDone(store.Root.CountBookmarks(), dlg.FileName), L.ExportDoneTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Export failed: " + ex.Message, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion
}
