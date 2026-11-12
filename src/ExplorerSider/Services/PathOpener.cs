using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using ExplorerSider.Models;

namespace ExplorerSider.Services;

public enum PathKind
{
    LocalDirectory,
    LocalFile,
    Unc,
    Shell,      // ::{CLSID} 控制面板等虚拟路径
    Registry,   // 计算机\HKEY_...
    Unknown
}

/// <summary>打开书签目标：资源管理器 / regedit，支持管理员权限提升（UAC）。</summary>
public static class PathOpener
{
    private static readonly Regex RxDrive = new(@"^[A-Za-z]:\\", RegexOptions.Compiled);

    public static PathKind Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return PathKind.Unknown;
        if (path.StartsWith("::")) return PathKind.Shell;
        if (path.StartsWith(@"\\") || path.StartsWith("//")) return PathKind.Unc;
        if (path.StartsWith(@"计算机\HKEY", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(@"HKEY_", StringComparison.OrdinalIgnoreCase))
            return PathKind.Registry;
        if (RxDrive.IsMatch(path))
        {
            try
            {
                if (File.Exists(path)) return PathKind.LocalFile;
            }
            catch { /* 非法字符等 */ }
            return PathKind.LocalDirectory;
        }
        return PathKind.Unknown;
    }

    /// <summary>路径是否存在/可达（UNC 离线返回 false）</summary>
    public static bool Exists(string? path)
    {
        var kind = Classify(path);
        try
        {
            return kind switch
            {
                PathKind.LocalDirectory => Directory.Exists(path),
                PathKind.LocalFile => File.Exists(path),
                PathKind.Unc => Directory.Exists(path!) || File.Exists(path),
                PathKind.Shell or PathKind.Registry => true, // 虚拟路径无法用 Exists 判断，视为有效
                _ => false
            };
        }
        catch { return false; }
    }

    /// <summary>
    /// 打开书签目标（异步，不阻塞 UI）。
    /// Shell COM 调用（枚举/激活资源管理器窗口）全部在后台线程——
    /// 刚打开的窗口初始化期间跨进程 COM 属性访问可能阻塞数秒，放 UI 线程会冻结界面并吞掉后续点击。
    /// 错误通过 onError 回调（UI 线程外调用，调用方需自行 Dispatch）。
    /// </summary>
    public static void OpenAsync(string? path, bool elevated = false, FolderOpenMode mode = FolderOpenMode.NewTab, Action<string?>? onError = null)
    {
        // 点击瞬间（UI 线程，本进程持有"最后输入"资格）让渡置顶权给任意进程：
        // 否则后台线程延迟 1~2s 后的 SetForegroundWindow 会被 Windows 前台锁定策略拒绝（只闪任务栏不置顶）
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        string? err;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) { onError?.Invoke("路径为空"); return; }
            var kind = Classify(path);

            switch (kind)
            {
                case PathKind.Registry:
                    // regedit /m 直接定位到键；regedit 自身会请求管理员
                    Process.Start(new ProcessStartInfo("regedit.exe", $"/m \"{path}\"")
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                    return;

                case PathKind.Shell:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                    return;

                case PathKind.LocalFile:
                    // 选中文件（不打开文件本身）
                    if (!File.Exists(path)) { onError?.Invoke("文件不存在：" + path); return; }
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    return;

                case PathKind.LocalDirectory:
                case PathKind.Unc:
                    if (elevated)
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
                        {
                            UseShellExecute = true,
                            Verb = "runas"
                        });
                        return;
                    }
                    // 目录打开全链路后台执行（含 COM 枚举/键盘模拟），带完整回退
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        var e = OpenDirectory(path, mode);
                        if (e != null) onError?.Invoke(e);
                    });
                    return;

                default:
                    onError?.Invoke("无法识别的路径：\n" + path);
                    return;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 1223 = 用户取消 UAC
            if (ex.NativeErrorCode == 1223) return;
            err = "打开失败：" + ex.Message;
        }
        catch (Exception ex)
        {
            err = "打开失败：" + ex.Message;
        }
        onError?.Invoke(err);
    }

    /// <summary>
    /// 带超时执行：Shell COM 调用（跨进程 marshaling）在资源管理器忙时可能挂起很久
    /// （典型：刚打开的窗口初始化期间访问其属性），超时则放弃该步骤走后续兜底，保证总能打开。
    /// </summary>
    private static T RunOr<T>(Func<T> f, T fallback, int ms = 1500)
    {
        try
        {
            var t = System.Threading.Tasks.Task.Run(f);
            return t.Wait(ms) ? t.Result : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>一次 COM 枚举所有资源管理器窗口（句柄+规范化本地路径），供各决策共用减少跨进程往返</summary>
    private static List<(IntPtr hwnd, string path)> EnumerateExplorerWindows()
    {
        var list = new List<(IntPtr hwnd, string path)>();
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return list;
        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (dynamic w in shell.Windows())
        {
            try
            {
                string loc = (string)w.LocationURL;
                if (string.IsNullOrEmpty(loc) || !loc.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    continue;
                var local = UrlToLocalPath(loc);
                if (local == null) continue;
                list.Add(((IntPtr)(long)(int)w.HWND, local));
            }
            catch
            {
                // 桌面等不支持 LocationURL 的窗口
            }
        }
        return list;
    }

    private static void ActivateHwnd(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, 9 /* SW_RESTORE */);
        if (NativeMethods.SetForegroundWindow(hwnd)) return;

        // 前台锁定兜底：把本线程挂到当前前台窗口线程的输入队列，借其资格强制置顶
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            var thisTid = NativeMethods.GetCurrentThreadId();
            var fgTid = NativeMethods.GetWindowThreadProcessId(fg, out _);
            if (fgTid == 0 || fgTid == thisTid) return;
            NativeMethods.AttachThreadInput(thisTid, fgTid, true);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.AttachThreadInput(thisTid, fgTid, false);
        }
        catch { }
    }

    /// <summary>目录打开链路（后台线程）：激活已有窗口 → 存在性/权限检查 → 按模式打开 → 兜底新窗口</summary>
    private static string? OpenDirectory(string path, FolderOpenMode mode)
    {
        try
        {
            if (!Directory.Exists(path))
                return "文件夹不存在或无法访问：\n" + path;

            if (!CanListDirectory(path))
            {
                // 无权限列出内容（如 systemprofile）：提升打开
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                return null;
            }

            // 单次 COM 枚举（限时 800ms，explorer 忙时快速失败走兜底）
            var wins = RunOr(EnumerateExplorerWindows, new List<(IntPtr hwnd, string path)>(), 800);

            // 1. 已有窗口打开该文件夹 → 激活跳转
            var existing = wins.FirstOrDefault(w =>
                string.Equals(w.path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != default && existing.hwnd != IntPtr.Zero)
            {
                ActivateHwnd(existing.hwnd);
                return null;
            }

            // 2. 按模式处理（有可用窗口时）
            if (wins.Count > 0)
            {
                var hwnd = wins[0].hwnd;
                if (mode == FolderOpenMode.SameWindow)
                {
                    var ok = RunOr(() => { NavigateHwnd(hwnd, path); return true; }, false, 800);
                    if (ok) return null;
                }
                else if (mode == FolderOpenMode.NewTab)
                {
                    OpenNewTabOrFallback(hwnd, path); // 键盘模拟，内部自带互斥/验证/回退
                    return null;
                }
            }

            // 3. 兜底：新窗口
            StartExplorer(path);
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223) return null;
            return "打开失败：" + ex.Message;
        }
        catch (Exception ex)
        {
            return "打开失败：" + ex.Message;
        }
    }

    private static void NavigateHwnd(IntPtr hwnd, string path)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (dynamic w in shell.Windows())
        {
            try
            {
                if ((IntPtr)(long)(int)w.HWND == hwnd)
                {
                    w.Navigate2(path);
                    ActivateHwnd(hwnd);
                    return;
                }
            }
            catch { }
        }
    }


    /// <summary>键盘模拟互斥标志：防止连续点击多个书签时键序交错互拆</summary>
    private static int _kbBusy;

    private static void StartExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    /// <summary>
    /// 键盘模拟新标签页：前台化验证 → Ctrl+T → Ctrl+L → 逐字符注入路径（Unicode）→ 回车。
    /// 每步验证前台窗口仍为目标资源管理器，任一步失败/已有模拟进行中 → 回退为直接新开窗口。
    /// Win11 无公开的新标签 COM API（Navigate2 的 navOpenInNewTab 标志被忽略），此为最优可行方案。
    /// </summary>
    public static void OpenNewTabOrFallback(IntPtr hwnd, string path)
    {
        if (Interlocked.Exchange(ref _kbBusy, 1) == 1)
        {
            StartExplorer(path); // 上一次模拟仍在进行：避免键序交错，直接新窗口
            return;
        }
        try
        {
            NativeMethods.ShowWindow(hwnd, 9);
            NativeMethods.SetForegroundWindow(hwnd);
            System.Threading.Thread.Sleep(400);
            if (NativeMethods.GetForegroundWindow() != hwnd) { StartExplorer(path); return; }

            // Ctrl+T：新标签页
            NativeMethods.SendKey(NativeMethods.VK_CONTROL);
            NativeMethods.SendKey(0x54 /* T */);
            NativeMethods.SendKey(0x54, up: true);
            NativeMethods.SendKey(NativeMethods.VK_CONTROL, up: true);
            System.Threading.Thread.Sleep(450);
            if (NativeMethods.GetForegroundWindow() != hwnd) { StartExplorer(path); return; }

            // Ctrl+L：聚焦地址栏
            NativeMethods.SendKey(NativeMethods.VK_CONTROL);
            NativeMethods.SendKey(0x4C /* L */);
            NativeMethods.SendKey(0x4C, up: true);
            NativeMethods.SendKey(NativeMethods.VK_CONTROL, up: true);
            System.Threading.Thread.Sleep(200);

            // 注入路径（Unicode 逐字符，不受输入法影响）
            foreach (var ch in path)
            {
                NativeMethods.SendChar(ch);
                NativeMethods.SendChar(ch, up: true);
                System.Threading.Thread.Sleep(3);
            }
            System.Threading.Thread.Sleep(80);

            NativeMethods.SendKey(NativeMethods.VK_RETURN);
            NativeMethods.SendKey(NativeMethods.VK_RETURN, up: true);
        }
        catch
        {
            StartExplorer(path); // 键盘模拟异常：兜底新窗口
        }
        finally
        {
            Interlocked.Exchange(ref _kbBusy, 0);
        }
    }


    private static string? UrlToLocalPath(string url)
    {
        try
        {
            return new Uri(url).LocalPath; // file:///E:/a%20b → E:\a b；file://host/share → \\host\share
        }
        catch
        {
            return null;
        }
    }

    /// <summary>探测目录是否可列举（区分"存在但无权限"，如 C:\Windows\System32\config\systemprofile）</summary>
    public static bool CanListDirectory(string path)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }   // UNC 不可达等
        catch { return false; }
    }
}
