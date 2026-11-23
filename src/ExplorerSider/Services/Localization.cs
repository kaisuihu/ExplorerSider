namespace ExplorerSider.Services;

/// <summary>
/// 极简双语本地化（简体中文 / English，默认 English）。零资源文件：属性直取，编译期检查。
/// 菜单均在右键时动态构建 → 切换语言后下一个菜单自然生效；静态窗口（ManageWindow）
/// 通过 ApplyLanguage() 重刷。启动时由 App 按 settings.Language 调 Set。
/// </summary>
public static class L
{
    private static bool _zh; // false = English（默认）

    public static bool IsZh => _zh;

    public static string Current => _zh ? "zh-CN" : "en-US";

    public static void Set(string lang) => _zh = lang == "zh-CN";

    private static string T(string en, string zh) => _zh ? zh : en;

    // ---- 托盘菜单 ----
    public static string TrayHideBar => T("Hide bookmark bar", "隐藏悬浮书签栏");
    public static string TrayShowBar => T("Show bookmark bar", "显示悬浮书签栏");
    public static string TrayManage => T("Bookmark Manager…", "书签管理…");
    public static string TrayImport => T("Import Bookmarks (HTML)…", "导入书签 HTML…");
    public static string TrayExport => T("Export Bookmarks (HTML)…", "导出书签 HTML…");
    public static string TrayFollowMouse => T("Follow the mouse across monitors", "悬浮栏跟随鼠标切换显示器");
    public static string TrayAutoStart => T("Start with Windows", "开机自启");
    public static string TrayHotkey(string hk) => T($"Hotkey: {hk} (monitor under mouse)", $"呼出热键：{hk}（鼠标所在屏）");
    public static string TrayExit => T("Exit", "退出");
    public static string LanguageMenu => T("Language / 语言", "Language / 语言");

    // ---- 通用弹窗 ----
    public static string AlreadyRunning => T(
        "ExplorerSider is already running — check the system tray (clover icon).",
        "ExplorerSider 已在运行，请查看系统托盘（四叶草图标）。");
    public static string HotkeyFailed(string hk) => T(
        $"Failed to register global hotkey {hk} (possibly taken by another app).\nEdit HotkeyShowBar in %APPDATA%\\ExplorerSider\\settings.json and restart.",
        $"全局热键 {hk} 注册失败（可能已被其他程序占用），\n可修改 %APPDATA%\\ExplorerSider\\settings.json 中的 HotkeyShowBar 后重启。");

    // ---- 导入/导出 ----
    public static string ImportDialogTitle => T(
        "Import Netscape bookmark HTML (exported from Clover / Chrome)",
        "导入 Netscape 书签 HTML（Clover / Chrome 导出）");
    public static string ImportFilter => T("Bookmark HTML|*.html;*.htm|All files|*.*", "书签 HTML|*.html;*.htm|所有文件|*.*");
    public static string ImportSummary(int n, int f) => T(
        $"The file contains {n} bookmarks in {f} folders.\n\n[Yes] Replace all existing bookmarks\n[No] Merge as a new folder under root\n[Cancel] Abort",
        $"文件包含 {n} 个书签、{f} 个文件夹。\n\n【是】替换现有全部书签\n【否】合并为根目录下的新文件夹\n【取消】放弃导入");
    public static string ImportModeTitle => T("Import mode", "导入方式");
    public static string ImportDone(int n) => T($"Imported {n} bookmarks", $"导入完成：{n} 个书签");
    public static string ImportFailed => T("Import failed: ", "导入失败：");
    public static string ExportDialogTitle => T(
        "Export bookmarks (Netscape HTML, re-importable into Clover / Chrome)",
        "导出书签（Netscape HTML，可被 Clover / Chrome 导入）");
    public static string ExportFilter => T("Bookmark HTML|*.html", "书签 HTML|*.html");
    public static string ExportFileName => T("ExplorerSiderBookmarks_", "ExplorerSider书签_");
    public static string ExportDone(int n, string path) => T(
        $"Exported {n} bookmarks to:\n{path}", $"已导出 {n} 个书签到：\n{path}");
    public static string ExportDoneTitle => T("Export complete", "导出完成");

    // ---- 悬浮栏菜单 ----
    public static string EmptyItem => T("(empty)", "(空)");
    public static string MenuOpen => T("Open", "打开");
    public static string MenuOpenAdmin => T("Open as administrator", "以管理员身份打开");
    public static string MenuRefreshIcon => T("Refresh icon", "刷新图标");
    public static string MenuNewChildBookmark => T("New bookmark inside…", "新建子书签…");
    public static string MenuEdit => T("Edit…", "编辑…");
    public static string MenuDelete => T("Delete", "删除");
    public static string MenuNewBookmark => T("New bookmark…", "新建书签…");
    public static string MenuNewFolder => T("New folder…", "新建文件夹…");
    public static string DefaultFolderName => T("New folder", "新文件夹");
    public static string DefaultBookmarkName => T("New bookmark", "新书签");
    public static string DeleteFolderConfirm(string name, int n) => T(
        $"Delete folder \"{name}\" and its {n} bookmarks?", $"删除文件夹“{name}”及其 {n} 个书签？");
    public static string DeleteBookmarkConfirm(string name) => T(
        $"Delete bookmark \"{name}\"?", $"删除书签“{name}”？");
    public static string DeleteTitle => T("Confirm delete", "确认删除");
    public static string MenuDockPosition => T("Dock position", "停靠位置");
    public static string DockTop => T("Top-center", "屏幕顶部居中");
    public static string DockTopLeft => T("Top-left corner", "左上角");
    public static string DockTopRight => T("Top-right corner", "右上角");
    public static string DockBottomLeft => T("Bottom-left corner", "左下角");
    public static string DockBottomRight => T("Bottom-right corner", "右下角");
    public static string MenuOpenMode => T("Folder open mode", "文件夹打开方式");
    public static string OpenModeNewTab => T("New tab (experimental; keystrokes swallowed on some setups)", "新标签页打开（试验性，部分环境键序被吞）");
    public static string OpenModeSameWindow => T("Navigate current window (default, recommended)", "在当前窗口跳转（默认，推荐）");
    public static string OpenModeNewWindow => T("Always open a new window", "总是新开窗口");
    public static string MenuHideBar => T("Hide bar", "隐藏悬浮条");

    // ---- 悬浮栏 ToolTip ----
    public static string HandleTooltip => T(
        "Drag to move · Right-click for dock settings",
        "按住拖动定位 · 右键本区域设置停靠位置");
    public static string AddButtonTooltip => T("New bookmark", "新建书签");

    // ---- 编辑对话框 ----
    public static string EditNewBookmarkTitle => T("New bookmark", "新建书签");
    public static string EditNewFolderTitle => T("New folder", "新建文件夹");
    public static string EditBookmarkTitle => T("Edit bookmark", "编辑书签");
    public static string EditFolderTitle => T("Edit folder", "编辑文件夹");
    public static string LabelName => T("Name", "名称");
    public static string LabelPath => T("Path", "路径");
    public static string BtnBrowse => T("Browse…", "浏览文件夹…");
    public static string PathHint => T(
        "You can also paste directly: file / UNC (\\\\192.168.1.1\\share) / ::{CLSID}",
        "也可以直接粘贴：文件 / UNC（\\\\192.168.1.1\\share）/ ::{CLSID}");
    public static string BtnOk => T("OK", "确定");
    public static string BtnCancel => T("Cancel", "取消");
    public static string FolderPickerTitle => T("Select folder", "选择文件夹");
    public static string NameRequired => T("Name cannot be empty", "名称不能为空");
    public static string PathRequired => T("Path cannot be empty", "路径不能为空");

    // ---- 管理窗口 ----
    public static string ManageTitle(string dir) => T(
        $"ExplorerSider Bookmarks — {dir}", $"ExplorerSider 书签管理 — {dir}");
    public static string MwMenuFile => T("File", "文件");
    public static string MwMenuImport => T("Import bookmarks (Netscape HTML / Clover backup)…", "导入书签（Netscape HTML / Clover 备份）…");
    public static string MwMenuExport => T("Export bookmarks (Netscape HTML)…", "导出书签（Netscape HTML）…");
    public static string MwMenuDataDir => T("Open data folder", "打开数据目录");
    public static string MwMenuAutoStart => T("Start with Windows", "开机自启");
    public static string MwMenuExit => T("Exit", "退出程序");
    public static string MwMenuActions => T("Actions", "操作");
    public static string MwMenuCheckPaths => T("Check invalid paths", "检查失效路径");
    public static string MwMenuRefreshIcons => T("Refresh all icons", "刷新全部图标");
    public static string MwBtnUp => T("↑ Move up", "↑上移");
    public static string MwBtnDown => T("↓ Move down", "↓下移");
    public static string MwStatusHint => T(
        "Tip: drag files/folders from Explorer in to create bookmarks; drag nodes to reorder or move between folders",
        "提示：从资源管理器拖入文件/文件夹可快速创建书签；拖动节点可调整顺序与层级");
    public static string MwDeleteConfirm(int n) => T(
        $"Delete {n} selected item(s) (including all their content)?", $"确认删除选中的 {n} 项（含其中全部内容）？");
    public static string MwChecking => T(
        "Checking paths (network shares may be slow)…", "正在检查路径（网络共享可能较慢）…");
    public static string MwCheckAllValid => T("All bookmark paths are valid ✓", "全部书签路径有效 ✓");
    public static string MwCheckBad(int bad) => T(
        $"{bad} bookmark path(s) not found (highlighted red; network shares may be temporarily unreachable)",
        $"{bad} 个书签路径不存在（已标红，可能是网络共享暂不可达）");
    public static string MwCheckTitle => T("Check complete", "检查完成");
    public static string MwRefreshingIcons => T(
        "Refreshing icons (network shares may be slow)…", "正在刷新图标（网络共享可能较慢）…");
    public static string MwIconsRefreshed(int n) => T(
        $"Refreshed {n} bookmark icons (unreachable network paths keep original icons)",
        $"已刷新 {n} 个书签图标（不可达的网络路径保留原图标）");
    public static string MwRefreshTitle => T("Refresh icons", "刷新图标");

    // ---- 打开错误（PathOpener）----
    public static string ErrFolderNotFound(string path) => T(
        $"Folder not found or inaccessible:\n{path}", $"文件夹不存在或无法访问：\n{path}");
    public static string ErrOpenFailed => T("Open failed: ", "打开失败：");
    public static string ErrUnknownPath(string path) => T(
        $"Unrecognized path:\n{path}", $"无法识别的路径：\n{path}");
}
