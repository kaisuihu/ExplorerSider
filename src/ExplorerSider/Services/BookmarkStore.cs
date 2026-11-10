using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExplorerSider.Models;

namespace ExplorerSider.Services;

/// <summary>书签与设置的持久化：%APPDATA%\ExplorerSider\，JSON 存储 + 滚动备份。</summary>
public class BookmarkStore
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExplorerSider");

    private static readonly string BookmarksFile = Path.Combine(DataDir, "bookmarks.json");
    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // BarX/BarY 未设置时为 NaN，允许以 "NaN" 字面量序列化
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public BookmarkNode Root { get; private set; } = BookmarkNode.NewFolder("书签栏");

    public AppSettings Settings { get; private set; } = new();

    public event Action? Changed;

    private BookmarkStore() { }

    public static BookmarkStore Load()
    {
        var store = new BookmarkStore();
        Directory.CreateDirectory(DataDir);

        store.Root = LoadJson<BookmarkNode>(BookmarksFile)
            ?? LoadJson<BookmarkNode>(BookmarksFile + ".bak.1")
            ?? LoadJson<BookmarkNode>(BookmarksFile + ".bak.2")
            ?? LoadJson<BookmarkNode>(BookmarksFile + ".bak.3")
            ?? BookmarkNode.NewFolder("书签栏");

        store.Settings = LoadJson<AppSettings>(SettingsFile) ?? new AppSettings();
        // UI 已移除"自由悬浮"选项；历史设置规范化为顶部居中
        if (store.Settings.DockMode == DockMode.Free)
        {
            store.Settings.DockMode = DockMode.TopAutoHide;
            store.SaveSettings();
        }
        // v1 迁移：NewTab 键盘模拟在多数环境不可靠，默认改为当前窗口跳转（用户此后手动选择不受影响）
        if (store.Settings.SettingsVersion < 1)
        {
            if (store.Settings.FolderOpenMode == FolderOpenMode.NewTab)
                store.Settings.FolderOpenMode = FolderOpenMode.SameWindow;
            store.Settings.SettingsVersion = 1;
            store.SaveSettings();
        }
        return store;
    }

    private static T? LoadJson<T>(string file) where T : class
    {
        try
        {
            if (!File.Exists(file)) return null;
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            return null; // 损坏则回退到备份
        }
    }

    /// <summary>保存书签（滚动备份 bak.3<-bak.2<-bak.1<-当前）。原子写入防断电损坏。</summary>
    public void Save()
    {
        RotateBackup(BookmarksFile, 3);
        WriteJsonAtomic(BookmarksFile, Root);
        SaveSettings();
        Changed?.Invoke();
    }

    /// <summary>整体替换根节点（导入"替换全部"模式）</summary>
    public void ReplaceRoot(BookmarkNode newRoot)
    {
        Root = newRoot;
        Save();
    }

    public void SaveSettings()
    {
        WriteJsonAtomic(SettingsFile, Settings);
    }

    private static void RotateBackup(string file, int keep)
    {
        try
        {
            for (int i = keep; i >= 1; i--)
            {
                var src = i == 1 ? file : $"{file}.bak.{i - 1}";
                var dst = $"{file}.bak.{i}";
                if (File.Exists(src)) File.Copy(src, dst, true);
            }
        }
        catch
        {
            // 备份失败不阻塞保存
        }
    }

    private static void WriteJsonAtomic<T>(string file, T obj)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(obj, JsonOpts));
        if (File.Exists(file)) File.Replace(tmp, file, null);
        else File.Move(tmp, file);
    }
}
