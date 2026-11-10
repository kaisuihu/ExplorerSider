# ExplorerSider v1.0.0

---

## 中文

**首个正式版本。** 一个 Windows 10/11 分层文件夹书签小工具，Clover 书签功能的独立替代品：悬浮书签栏 + 系统托盘，不注入资源管理器、免安装、数据全本地。

### ✨ 核心功能

- 📂 **分层书签**：根 → 文件夹 → 书签，增删改、重命名、排序、跨文件夹移动
- 🧲 **悬浮书签栏**：置顶小条停靠屏幕顶部或四角（角落是鼠标"天然陷阱"），贴边自动隐藏仅露 2px；文件夹悬停展开多级子列表
- ⌨️ **全局热键** `Ctrl+Alt+B`：在**鼠标所在显示器**呼出/收起（多显示器各自独立）
- 🖱️ **拖拽一切**：
  - 从资源管理器拖入文件/文件夹 → 一键收藏（拖到文件夹上=放入该文件夹）
  - 悬浮栏/管理窗口内部拖动 → 排序（指示线）+ 移入文件夹（悬停 0.4s 自动展开）
- 🚀 **点击直达**：本地目录、局域网共享/NAS（`\\192.168.x.x\share`）、控制面板等虚拟位置（`::{CLSID}`）、注册表路径（regedit 定位）全覆盖
- 🛡️ **管理员权限目录**：自动检测无权访问的目录，弹 UAC 提权打开；也可右键"以管理员身份打开"
- 🔁 **Clover 互导**：Netscape Bookmark HTML 导入/导出，与 Clover/Chrome/Edge 兼容，图标与时间戳完整保留（含往返一致性自检 `--selftest`）
- 🍀 **零素材**：托盘/exe 四叶草图标纯代码绘制；导入的书签保留 Clover 原图标，新建书签自动抓取系统图标
- 🗂️ **数据安全**：`%APPDATA%\ExplorerSider\bookmarks.json`（人可读 JSON）+ 每次保存滚动备份 3 份

### 📥 下载与安装

| 文件 | 说明 |
|---|---|
| `ExplorerSider.exe` | 单文件自包含（约 140 MB，**无需安装 .NET**），下载后放到任意目录双击运行 |

- 系统要求：Windows 10 / 11 x64
- 建议先看 [使用手册（中文）](docs/USER_MANUAL_zh-CN.md) 的"快速上手"
- 开机自启：托盘右键菜单勾选即可

### ⚠️ 已知限制

- 悬浮栏为独立窗口，不内嵌资源管理器（Win11 无公开标签页 API）
- 仅支持一层文件夹（设计约束）
- `NewTab`（新标签页打开）模式在 Win11 24H2 上受系统限制，不可靠时自动回退

### 🙏 从 Clover 迁移

1. 在 Clover 中导出书签 HTML（或用你已有的备份文件）
2. ExplorerSider 托盘右键 → **导入书签** → 选择该 HTML → 替换或合并
3. 完成。导出的 HTML 亦可随时带回 Clover/Chrome/Edge

---

## English

**First stable release.** A hierarchical folder-bookmark utility for Windows 10/11 — a standalone replacement for Clover's bookmarks: floating bar + system tray, no Explorer injection, no installer, fully local data.

### ✨ Highlights

- 📂 **Hierarchical bookmarks**: Root → Folder → Bookmark, with full add/edit/delete, rename, reorder and cross-folder move
- 🧲 **Floating bar**: docks top-center or at any screen corner (corners are natural mouse traps), auto-hides to a 2px sliver; folders expand multi-level sub-lists on hover
- ⌨️ **Global hotkey** `Ctrl+Alt+B`: toggles the bar on **whichever monitor the mouse is on** (independent per display)
- 🖱️ **Drag everything**:
  - Drop files/folders from File Explorer → instant bookmark (drop on a folder = into that folder)
  - Internal drag on bar/manager → reorder (with indicator) + move into folders (auto-expands after 0.4s hover)
- 🚀 **One click to jump**: local folders, LAN shares/NAS (`\\192.168.x.x\share`), virtual locations (`::{CLSID}`), and registry paths (located in regedit) all supported
- 🛡️ **Admin-protected folders**: access-denied directories detected automatically and opened via UAC elevation; "Open as administrator" also on the right-click menu
- 🔁 **Clover interop**: Netscape Bookmark HTML import/export, compatible with Clover/Chrome/Edge, icons and timestamps fully preserved (round-trip self-test via `--selftest`)
- 🍀 **Zero assets**: tray/exe clover icon drawn purely in code; imported bookmarks keep their original Clover icons, new ones fetch system icons automatically
- 🗂️ **Safe data**: `%APPDATA%\ExplorerSider\bookmarks.json` (human-readable JSON) + 3 rolling backups on every save

### 📥 Download & Install

| File | Notes |
|---|---|
| `ExplorerSider.exe` | Single-file, self-contained (~140 MB, **no .NET install needed**) — put it anywhere and run |

- Requirements: Windows 10 / 11 x64
- See the [User Manual (English)](docs/USER_MANUAL_en-US.md) "Quick Start" section
- Start with Windows: tick it in the tray menu

### ⚠️ Known Limitations

- The bar is a standalone window, not embedded in File Explorer (Win11 exposes no tab API)
- One level of folders only (by design)
- `NewTab` open mode is unreliable on Win11 24H2 due to OS restrictions; it falls back automatically

### 🙏 Migrating from Clover

1. Export your bookmarks from Clover as HTML (or use an existing backup)
2. ExplorerSider tray right-click → **Import** → pick the HTML → replace or merge
3. Done. Exports work back into Clover/Chrome/Edge at any time
