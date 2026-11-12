# ExplorerSider — Clover 书签替代工具

Windows 11 分层文件夹书签小工具：顶部悬浮书签栏 + 托盘，替代 Clover 的书签功能（Clover 已停止维护且不支持 Win11）。

📖 文档：[使用手册（中文）](docs/USER_MANUAL_zh-CN.md) · [User Manual (English)](docs/USER_MANUAL_en-US.md) · [Release Notes](docs/RELEASE_NOTES_v1.0.1.md)

## 功能

- **分层书签**：文件夹/书签任意层级，增删改、重命名、排序、跨文件夹移动
- **悬浮书签栏**：置顶小条，文件夹悬停弹出多级菜单；可停靠屏幕顶部或四个角落（角落是鼠标天然"陷阱"，最好命中）；贴边自动隐藏
- **全局热键** `Ctrl+Alt+B`：在**鼠标所在显示器**呼出/收起悬浮条（多显示器友好）
- **拖拽创建**：把文件/文件夹从资源管理器拖到悬浮条或管理窗口即可收藏（拖到文件夹按钮上=放入该文件夹）
- **点击直达**：书签单击即打开对应文件夹；支持本地路径、局域网共享（`\\192.168.x.x\share`、NAS）、控制面板等虚拟位置（`::{CLSID}`）、注册表路径（自动用 regedit 打开）
- **管理员权限目录**：自动检测无权访问的目录（如 systemprofile），弹 UAC 提权打开；也可右键"以管理员身份打开"
- **Clover 互导**：导入/导出 Netscape Bookmark HTML（与 Clover/Chrome 书签格式兼容，含图标与时间戳）
- **书签图标**：导入的书签保留 Clover 原图标（像素级一致）；新建的书签自动抓取系统图标，可"刷新图标"
- **系统托盘**：四叶草图标（代码原创绘制），显隐悬浮条 / 管理 / 导入导出 / 开机自启

## 使用

1. 运行 `publish\ExplorerSider.exe`（免安装，单文件）
2. 首次运行自动打开管理窗口 → 菜单 `文件 → 导入书签`，选择 Clover 备份 HTML（如 `2026.11.09备份-Clover资源管理器书签.html`），选"替换"或"合并"
3. 悬浮条默认停靠屏幕顶部居中；**按住左侧四叶草把手可拖动**，右键把手可切换停靠位置（顶部/四角/自由）
4. 建议在托盘菜单开启"开机自启"

## 数据与备份

- 书签数据：`%APPDATA%\ExplorerSider\bookmarks.json`（人可读 JSON）
- 每次保存自动滚动备份 3 份（`.bak.1/2/3`）
- 迁移到新电脑：复制该 json，或用导出 HTML

## 设置项（settings.json）

| 键 | 默认 | 说明 |
|---|---|---|
| HotkeyShowBar | Ctrl+Alt+B | 呼出热键（改后重启生效） |
| DockMode | 1 | 0=自由 1=顶部居中 2~5=四角 |
| ShowBar | true | 悬浮条是否显示 |
| FollowMouse | true | 悬浮栏是否跟随鼠标切换显示器（false=仅停靠屏响应贴边呼出，跨屏用热键；托盘菜单可切换） |

## 命令行

```
ExplorerSider.exe --selftest <bookmarks.html>   # 自检：导入→导出→再导入等价验证
ExplorerSider.exe --genicon [输出.ico]          # 生成四叶草图标文件
```

## 从源码构建

需求：.NET SDK 8+（Windows）

```
cd src\ExplorerSider
dotnet run -- --genicon app.ico   # 首次先生成图标（可选，无图标也能构建）
dotnet build -c Release
..\publish.bat                    # 发布单文件 exe
```

技术：WPF (.NET 8)，零 NuGet 依赖、零图片素材（图标全部代码绘制），托盘/热键/多屏为 Win32 自封装。

## 已知限制

- 书签为独立悬浮条，不在资源管理器窗口内嵌（Win11 无官方标签页 API，内嵌需注入 explorer，风险高）
- 热键若被其他程序占用会注册失败（弹提示），改 settings.json 的 HotkeyShowBar 即可
