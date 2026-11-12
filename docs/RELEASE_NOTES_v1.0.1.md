# ExplorerSider v1.0.1

---

## 中文

**修复与体验优化版本。** 相比 v1.0.0 的全部变化：

### 🐛 修复

- **悬浮栏不再出现在 Alt+Tab**：v1.0.0 中按 `Alt+Tab` 能看到「ExplorerSider 书签栏」窗口，现已从任务切换器中隐藏（不影响任务栏、托盘与热键）
- **点击书签后资源管理器偶尔不弹到前台**：规避 Windows 前台锁定策略——点击瞬间让渡置顶权（`AllowSetForegroundWindow`），激活失败时追加强制置顶兜底，explorer 不再藏在后台
- **偶发"悬浮栏无法呼出"**：修复退出时位置记忆被污染为 (0,0)、重启后丢失停靠屏归属的问题；托盘菜单「显示悬浮书签栏」/托盘左键现在是**强力恢复出口**——无论窗口停在什么异常状态，都会强制回到鼠标所在屏显示

### ✨ 新增

- **「跟随鼠标切换显示器」设置**（托盘菜单勾选切换，默认开启=保持 v1.0.0 行为）：悬浮栏停靠在屏幕**顶部**时常被弹出的条挡住点击，关闭后仅悬浮栏**所在屏**响应贴边呼出，跨屏切换只认热键 `Ctrl+Alt+B`；停靠在**底部/角落**时跟随依旧好用，两种用法自选
- **诊断日志** `%APPDATA%\ExplorerSider\log.txt`：记录启动、呼出/收起、热键判定等关键事件（64KB 自动截断），偶发问题不再靠猜

### 📥 下载与升级

| 文件 | 说明 |
|---|---|
| `ExplorerSider.exe` | 单文件自包含（约 140 MB，无需安装 .NET） |

- **从 v1.0.0 升级**：托盘右键退出程序 → 用新 exe 覆盖旧文件 → 双击运行。书签与设置全部保留（`%APPDATA%\ExplorerSider` 不动）
- 全新安装与完整功能说明见[使用手册（中文）](USER_MANUAL_zh-CN.md)

---

## English

**Bug fixes and refinements.** Everything changed since v1.0.0:

### 🐛 Fixed

- **Floating bar no longer appears in Alt+Tab**: v1.0.0 showed an "ExplorerSider 书签栏" window in the task switcher; it is now hidden from Alt+Tab (taskbar, tray and hotkey unaffected)
- **Explorer sometimes stayed in the background after clicking a bookmark**: worked around the Windows foreground-lock policy — foreground rights are granted at click time (`AllowSetForegroundWindow`) plus a force-activation fallback, so the Explorer window reliably comes to front
- **Bar occasionally failed to reappear**: fixed the position memory being polluted with (0,0) on exit (which lost the docked monitor after restart); tray "Show bar" / tray left-click is now a **rescue path** — whatever state the window is stuck in, it is forced back onto the monitor under the mouse

### ✨ Added

- **"Follow the mouse across monitors" setting** (tray menu toggle, on by default = v1.0.0 behavior): when docked at the **top**, the sliding bar can block your clicks — turn it off and only the bar's own monitor responds to the edge trigger, with `Ctrl+Alt+B` as the only way across screens; keep it on for **bottom/corner** docking where following is handy
- **Diagnostic log** at `%APPDATA%\ExplorerSider\log.txt`: records startup, slide in/out and hotkey decisions (auto-truncated at 64KB) — no more guessing at one-off glitches

### 📥 Download & Upgrade

| File | Notes |
|---|---|
| `ExplorerSider.exe` | Single-file, self-contained (~140 MB, no .NET install needed) |

- **Upgrading from v1.0.0**: exit from the tray → overwrite the old exe → run. Bookmarks and settings are untouched (`%APPDATA%\ExplorerSider`)
- Fresh installs and full details: [User Manual (English)](USER_MANUAL_en-US.md)
