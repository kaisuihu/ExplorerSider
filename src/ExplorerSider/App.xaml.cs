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
            MessageBox.Show("ExplorerSider 已在运行，请查看系统托盘（四叶草图标）。", "ExplorerSider",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AutoStartHelper.RefreshPathIfEnabled();

        _store = BookmarkStore.Load();
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
        if (visible) _bar.Show(); else _bar.Hide();
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
        _store.Settings.BarX = _bar?.SavedX ?? _store.Settings.BarX;
        _store.Settings.BarY = _bar?.SavedY ?? _store.Settings.BarY;
        _store.SaveSettings();
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        var store = _store!;

        var toggleText = store.Settings.ShowBar ? "隐藏悬浮书签栏" : "显示悬浮书签栏";
        menu.Items.Add(TrayItem(toggleText, () => SetBarVisible(!store.Settings.ShowBar)));
        menu.Items.Add(TrayItem("书签管理…", ShowManage));
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem("导入书签 HTML…", () => ImportBookmarks(_manage)));
        menu.Items.Add(TrayItem("导出书签 HTML…", () => ExportBookmarks(_manage)));
        menu.Items.Add(new Separator());

        var auto = new MenuItem { Header = "开机自启", IsCheckable = true, IsChecked = AutoStartHelper.IsEnabled() };
        auto.Click += (s, e) =>
        {
            var enable = !AutoStartHelper.IsEnabled();
            AutoStartHelper.SetEnabled(enable);
            auto.IsChecked = enable;
        };
        menu.Items.Add(auto);

        var hk = new MenuItem { Header = $"呼出热键：{store.Settings.HotkeyShowBar}（鼠标所在屏）", IsEnabled = false };
        menu.Items.Add(hk);
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem("退出", ShutdownApp));
        return menu;
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
            Title = "导入 Netscape 书签 HTML（Clover / Chrome 导出）",
            Filter = "书签 HTML|*.html;*.htm|所有文件|*.*"
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
                $"文件包含 {n} 个书签、{imported.CountFolders()} 个文件夹。\n\n" +
                "【是】替换现有全部书签\n【否】合并为根目录下的新文件夹\n【取消】放弃导入",
                "导入方式", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

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

            MessageBox.Show(owner, $"导入完成：{n} 个书签", "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "导入失败：" + ex.Message, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void ExportBookmarks(Window? owner)
    {
        var app = (App)Current;
        var store = app._store!;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出书签（Netscape HTML，可被 Clover / Chrome 导入）",
            Filter = "书签 HTML|*.html",
            FileName = $"ExplorerSider书签_{DateTime.Now:yyyy.MM.dd}.html"
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, NetscapeHtml.Export(store.Root), new UTF8Encoding(true));
            MessageBox.Show(owner, $"已导出 {store.Root.CountBookmarks()} 个书签到：\n{dlg.FileName}", "导出完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "导出失败：" + ex.Message, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion
}
