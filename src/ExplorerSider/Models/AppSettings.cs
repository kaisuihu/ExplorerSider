namespace ExplorerSider.Models;

/// <summary>点击文件夹书签的打开方式</summary>
public enum FolderOpenMode
{
    /// <summary>已有资源管理器窗口时模拟 Ctrl+T 新开标签页（Win11）</summary>
    NewTab,
    /// <summary>已有资源管理器窗口时直接在当前标签页跳转（Navigate2）</summary>
    SameWindow,
    /// <summary>总是新开资源管理器窗口</summary>
    NewWindow
}

public enum DockMode
{
    /// <summary>自由悬浮，不自动隐藏</summary>
    Free,
    /// <summary>顶部居中：鼠标划到所在屏幕顶部条带滑出（默认）</summary>
    TopAutoHide,
    /// <summary>左上角停靠：鼠标指向屏幕左上角滑出（角落是天然鼠标陷阱，最易命中）</summary>
    TopLeftAutoHide,
    /// <summary>右上角停靠</summary>
    TopRightAutoHide,
    /// <summary>左下角停靠</summary>
    BottomLeftAutoHide,
    /// <summary>右下角停靠</summary>
    BottomRightAutoHide
}

public class AppSettings
{
    /// <summary>悬浮条位置（物理像素，支持负值/副屏；Free 模式下记忆）</summary>
    public double BarX { get; set; } = double.NaN;

    public double BarY { get; set; } = double.NaN;

    public DockMode DockMode { get; set; } = DockMode.TopAutoHide;

    /// <summary>悬浮条是否显示</summary>
    public bool ShowBar { get; set; } = true;

    /// <summary>悬浮栏是否跟随鼠标切换显示器：true=鼠标进入任一屏触发区即在该屏滑出；false=仅悬浮栏所在屏响应贴边呼出，跨屏用热键（Ctrl+Alt+B）切换</summary>
    public bool FollowMouse { get; set; } = true;

    /// <summary>呼出悬浮条的全局热键（默认 Ctrl+Alt+B；格式 "Ctrl+Alt+B"）</summary>
    public string HotkeyShowBar { get; set; } = "Ctrl+Alt+B";

    /// <summary>点击文件夹书签的打开方式（默认当前窗口跳转：Win11 无公开新标签 API，键盘模拟在部分环境不稳定）</summary>
    public FolderOpenMode FolderOpenMode { get; set; } = FolderOpenMode.SameWindow;

    /// <summary>设置版本号：用于一次性迁移（0=旧版）</summary>
    public int SettingsVersion { get; set; } = 0;

    /// <summary>首次运行（用于自动打开管理窗口引导导入）</summary>
    public bool FirstRun { get; set; } = true;
}
