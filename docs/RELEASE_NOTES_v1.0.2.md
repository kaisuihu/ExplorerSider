# ExplorerSider v1.0.2

---

## 中文

**打开行为精修 + 双语界面版本。** 相比 v1.0.1 的全部变化：

### ✨ 新增

- **双语界面（English / 简体中文）**：默认 **English**，托盘右键菜单 **Language / 语言** 即切即用（无需重启），覆盖所有右键菜单、弹窗、编辑对话框、错误提示与管理窗口

### 🐛 修复

- **"在当前窗口跳转"现在跳转最后活动的窗口**：v1.0.1 会跳到最早打开的窗口（COM 枚举顺序问题）；同时，停在"主页"/搜索页的窗口此前被完全忽略（路径为空被过滤），导致手动新开的窗口即使置顶也不被选中——两者均已修复
- **裸服务器根可打开**：`\\192.168.210.200` 这类不带共享名的地址此前直接报"文件夹不存在或无法访问"（.NET 路径层拒绝该形式），现在与资源管理器行为一致——打开后显示该主机的共享列表
- **切换语言后悬浮栏菜单立即刷新**：此前把手菜单与各按钮右键菜单缓存于构建时，切换后需重启才变

### ⚙️ 改进

- **新标签页模式不再静默失败**：回车后验证导航结果，键序被系统吞掉时自动降级为新开窗口（此前表现为点击无反应）；连续失败 2 次后本会话内直接走新开窗口，不再白等
- 菜单标注修正：真正默认是"在当前窗口跳转（默认，推荐）"，"新标签页打开"标注为试验性（v1.0.1 曾误标默认）

### 📥 下载与升级

| 文件 | 说明 |
|---|---|
| `ExplorerSider.exe` | 单文件自包含（约 140 MB，无需安装 .NET） |

- **从 v1.0.x 升级**：托盘右键退出程序 → 用新 exe 覆盖旧文件 → 双击运行。书签与设置全部保留（`%APPDATA%\ExplorerSider` 不动）
- 注意：本版起 UI 默认 **English**；老用户可在托盘菜单 **Language / 语言** 切回简体中文（设置会保存）
- 完整功能说明见[使用手册（中文）](USER_MANUAL_zh-CN.md)

---

## English

**Open-behavior refinements + bilingual UI.** Everything changed since v1.0.1:

### ✨ Added

- **Bilingual UI (English / 简体中文)**: defaults to **English**, switchable in the tray menu under **Language / 语言** — effective immediately, no restart; covers all context menus, dialogs, the edit dialog, error messages and the manager window

### 🐛 Fixed

- **"Navigate current window" now targets the most recently active window**: v1.0.1 jumped to the oldest open window (COM enumeration order); also, windows parked on **Home** or **Search** were ignored entirely (empty path filtered out), so manually opened windows were never picked even when focused — both fixed
- **Bare server roots now open**: addresses like `\\192.168.210.200` (no share name) used to fail with "folder not found" (.NET path APIs reject the form); they now behave like Explorer — showing the host's share list
- **Bar menus refresh immediately on language switch**: the handle menu and per-button menus were cached at build time and kept the old language until restart

### ⚙️ Improved

- **New-tab mode no longer fails silently**: navigation is verified after Enter; when keystrokes are swallowed by the system it automatically falls back to opening a new window (previously clicks appeared dead); after 2 failures in a session it goes straight to new windows without waiting
- Menu labeling corrected: the real default is "Navigate current window (default, recommended)"; "New tab" is now labeled experimental (v1.0.1 mislabeled it as the default)

### 📥 Download & Upgrade

| File | Notes |
|---|---|
| `ExplorerSider.exe` | Single-file, self-contained (~140 MB, no .NET install needed) |

- **Upgrading from v1.0.x**: exit from the tray → overwrite the old exe → run. Bookmarks and settings are untouched (`%APPDATA%\ExplorerSider`)
- Note: the UI now defaults to **English**; existing users can switch back to 简体中文 via the tray menu **Language / 语言** (persisted)
- Full details: [User Manual (English)](USER_MANUAL_en-US.md)
