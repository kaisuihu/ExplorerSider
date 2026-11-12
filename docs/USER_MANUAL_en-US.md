# ExplorerSider User Manual (v1.0.1)

A hierarchical folder-bookmark utility — a standalone replacement for Clover's bookmarks.

Windows 11's "Quick Access" does not support folder hierarchy, and Clover is no longer maintained and unstable on Win11. ExplorerSider delivers the full hierarchical bookmark experience as a **floating bookmark bar + system tray icon**, without injecting into Explorer or touching the system. Portable, no installation.

---

## 1. Requirements & Installation

| Item | Requirement |
|---|---|
| OS | Windows 10 / Windows 11 (x64), multi-monitor supported |
| Runtime | **None needed** (.NET 8 runtime is embedded — single-file, self-contained) |
| Install | None: download `ExplorerSider.exe`, put it anywhere, double-click |
| Disk | ~140 MB (the exe) + a tiny config footprint |
| Privileges | Standard user; UAC elevation is requested on demand for protected folders |

Place the exe in a fixed location (e.g. `D:\Tools\ExplorerSider\`) before enabling auto-start — the Run entry tracks the exe's current path.

## 2. Quick Start (5 steps)

1. Double-click `ExplorerSider.exe` — a floating bookmark bar appears at the top-center of the screen and a four-leaf-clover tray icon appears
2. Tray right-click → **Bookmark Manager** (or simply drag a Clover backup HTML into the manager window)
3. Menu `File → Import Bookmarks`, pick your Clover-exported HTML, choose **Replace all** or **Merge** — bookmarks, folders, icons and timestamps are all preserved
4. **Click a bookmark** on the bar to jump straight to its folder; **hover** a folder button to expand its sub-list
5. Enable **Start with Windows** from the tray menu

## 3. The Floating Bookmark Bar

### Show & hide
- Docks at **top-center** by default; auto-hides to the screen edge when the mouse leaves, leaving a **2px** sliver (nearly invisible)
- Move the mouse near the exposed edge to slide it back out
- Global hotkey **`Ctrl+Alt+B`**: toggle show/hide on **whichever monitor the mouse is on** (each monitor docks independently)
- **Follow mouse across monitors** is on by default: pointing at another monitor's trigger edge slides the bar over there. If docking at the top keeps blocking your clicks, untick "Follow the mouse across monitors" in the tray menu — then only the bar's own monitor responds to the edge trigger, and the hotkey is the only way to move it across screens

### Dock position
Right-click the **clover handle** on the left of the bar: top-center (default), top-left / top-right / bottom-left / bottom-right. Screen corners are natural mouse "traps" — easiest to hit.

### Dragging
Hold the clover handle to drag the bar to a temporary position (the chosen dock position takes precedence).

## 4. Bookmarks & Folders

### Basics
| Action | How |
|---|---|
| Open | Left-click a bookmark |
| Expand folder | Hover the folder button to pop its sub-list; it collapses once the mouse leaves the parent button, the sub-list and any open context menu |
| Right-click any item | Open / **Open as administrator** (UAC) / Edit / Delete / Refresh icon |
| Create new | Click the **＋** on the right of the bar: New bookmark… / New folder… |
| Right-click a folder | Additionally offers "New bookmark inside" |

### Hierarchy rule (v1.0.0 design constraint)
- Structure is **Root → Folder → Bookmark**, with **one level of folders only**
- Folders cannot be dropped into other folders; no "new sub-folder" entry

### Supported path types
| Type | Example | Behavior |
|---|---|---|
| Local directory | `E:\Projects\Demo` | Opens in File Explorer |
| LAN share / NAS | `\\192.168.1.10\share`, `\\NAS\media` | Opens in File Explorer ("not found" notice while offline) |
| Virtual locations | `::{CLSID}` (Control Panel, This PC, …) | Opens in File Explorer |
| Registry | `Computer\HKEY_LOCAL_MACHINE\...` | Opens regedit positioned at the key |
| Access-denied dirs | e.g. `systemprofile` | Detected automatically; opens elevated via UAC |

### Open mode (`FolderOpenMode` in `settings.json`)
| Value | Behavior |
|---|---|
| `SameWindow` (default) | Navigates the **currently active** Explorer window; if the path is already open, its window is brought to front instead |
| `NewWindow` | Opens a new window every time |
| `NewTab` | Attempts a new tab in the current window (Win11 24H2 restricts third-party tabs; falls back to SameWindow behavior on failure) |

## 5. Drag & Drop

### Drop from File Explorer (create bookmarks)
Drag files/folders onto:
- **The bar's empty area / between bookmarks** → creates a bookmark at root
- **A folder button** → drops it into that folder (with highlight feedback)
- **The manager window's tree / folder** → same as above

### Internal dragging on the bar (reorder & move)
- Drag a bookmark/folder left-right to **reorder** (a vertical insertion line appears)
- Drop a bookmark on a **folder button's middle** = **move into** the folder; on its **left/right edges (~14px)** = reorder next to it
- Hover over a folder button while dragging (~0.4s) auto-expands its sub-list so you can drop inside
- Folders can only be reordered at root level (never moved into another folder)

### Internal dragging in the manager window
Same capabilities: a **blue indicator line** above/below the target decides the drop point; dropping on a folder item's middle = move into.

## 6. Manager Window (tray → Bookmark Manager)

- Tree view of all bookmarks/folders; double-click to edit; right-click for shortcuts
- Toolbar: New bookmark / New folder / Edit / Delete / Move up / Move down / Check invalid paths / Refresh all icons
- **Multi-select batch operations**: `Ctrl+Click` to add, `Ctrl+A` select all, `Esc` to clear; deleting also removes the single-clicked selection
- **Check invalid paths**: background scan; dead bookmarks shown in red (unreachable LAN paths are handled off the UI thread — no freezing)
- **Refresh all icons**: re-fetch system icons (also backgrounded)
- `File` menu: Import/Export HTML, Open data folder, Start-with-Windows toggle, Exit

## 7. System Tray

The four-leaf-clover icon (drawn purely in code, no image assets):
- Left-click / double-click: show/hide the floating bar
- Right-click menu: Show bar, Bookmark manager, Import, Export, Start with Windows (checkable), Exit

## 8. Clover Interoperability

- **Import**: Netscape Bookmark HTML (the format shared by Clover / Chrome / Edge), including folder hierarchy, `ICON` (base64 icons preserved verbatim — pixel-identical to Clover) and `ADD_DATE` timestamps; choose **Replace all** or **Merge** (as a new folder under root) on import
- **Export**: produces the same format with UTF-8 BOM + CRLF, directly re-importable into Clover/Chrome/Edge
- Round-trip consistency can be verified via the CLI self-test (see §10)

## 9. Data & Backup

| Content | Location |
|---|---|
| Bookmark data | `%APPDATA%\ExplorerSider\bookmarks.json` (human-readable JSON) |
| Settings | `%APPDATA%\ExplorerSider\settings.json` |
| Auto backups | `bookmarks.json.bak.1 / .bak.2 / .bak.3` in the same folder (rotated on every save) |
| Auto-start | Registry `HKCU\...\Run` (the path is refreshed automatically if the exe moves) |

**Migrating to a new PC**: copy `bookmarks.json`, or export the HTML.

## 10. Command Line

```
ExplorerSider.exe --selftest <bookmarks.html>   # Self-test: import→export→re-import, asserts tree equality
ExplorerSider.exe --genicon [output.ico]        # Exports the code-drawn clover icon as .ico
```

## 11. Settings Reference (settings.json)

| Key | Default | Description |
|---|---|---|
| `HotkeyShowBar` | `Ctrl+Alt+B` | Toggle hotkey (restart the app after changing) |
| `DockMode` | `1` | 1=top-center, 2–5=corners (0=free-floating is deprecated; normalized to 1 on load) |
| `ShowBar` | `true` | Whether the bar is shown |
| `FollowMouse` | `true` | Follow the mouse across monitors (toggle in tray menu); `false` = only the bar's own monitor responds to the edge trigger, hotkey moves it across screens |
| `FolderOpenMode` | `SameWindow` | See §4 |

## 12. Troubleshooting

| Symptom | Fix |
|---|---|
| Hotkey fails with a notice | `Ctrl+Alt+B` is taken by another app; change `HotkeyShowBar` in `settings.json` and restart |
| Bar is nowhere to be found | Press `Ctrl+Alt+B` with the mouse on the target monitor; or left-click the tray; check for the 2px edge sliver |
| Clicking a bookmark does nothing | For LAN paths, make sure the host is online; offline paths raise a "not found" notice |
| `NewTab` mode still reuses/opens a window | Win11 24H2 limitation; keyboard-simulation fallback kicks in by design |
| Protected folder seems unresponsive | A UAC prompt follows shortly; or right-click → "Open as administrator" |
| Icons missing | Right-click → Refresh icon; or manager window → "Refresh all icons" |

## 13. Known Limitations

- The bar is a standalone window, not embedded in File Explorer (Win11 exposes no tab API; embedding would require injecting into explorer — risky, not done)
- One level of folders only (deliberate design for simplicity)
- No duplicate-name check; the insertion indicator on the bar jitters slightly (drop accuracy unaffected)

---

*ExplorerSider v1.0.1 · WPF (.NET 8) · zero NuGet dependencies, zero image assets (all icons drawn in code)*
