# AUTZ IronWorks Preview

A lightweight Windows utility for permanent live previews of your running EVE Online clients and instant client switching.

**Run it:** double-click `AUTZ.IronWorks.Preview.exe` in this folder (needs the .NET 8 Desktop Runtime, already installed if you have the .NET SDK). It runs in the **system tray only** (no taskbar button): right-click the tray icon for the menu.

## v0.5.0
- Move a thumbnail with **Shift + left-click and drag** (right-click now just opens the menu).
- New option **Thumbnails clickable** (Settings > General, tray menu, or the thumbnail menu). Untick it and thumbnails ignore clicks, so you switch only with the hotkeys.
- Default thumbnail opacity is now 60% (Settings > Appearance, 10-100).
- Next / previous hotkeys no longer get stuck: they go through every client in the order the clients were opened, and carry on even if Windows refuses to focus one.
- The control window is gone. Right-click the system tray icon for everything: Settings, hide/show all thumbnails, which thumbnails to show, refresh, lock, reset positions and exit. Double-click the icon to open Settings.
- No taskbar button while the app is running; it lives in the system tray.
- Settings is split into pages down the left side: General, Appearance, Thumbnails, Hotkeys and Characters.
- Choose which clients get a thumbnail: tick / untick each character (and login screens) in Settings > Thumbnails, or from the tray menu > Thumbnails to show.
- Clients are kept in the order they were opened (login screens included): default stacking, Reset all positions and the next / previous hotkeys all follow that order. Login screens are labelled "Login screen 1", "Login screen 2", and so on.
- A login screen is placed where the character that usually logs in on that client appears. A character with no saved position takes over the spot its login screen was in.

## v0.4.2
- Uses the AUTZ IronWorks emblem: exe icon, system tray, window title bars, and shown in the settings window

## v0.4.1
- Updates never touch a running app: `publish-exe.ps1` swaps the exe in the background and the new version starts the next time you open the app

## v0.4.0
- Drag the bottom-right corner of a thumbnail (left button) to resize it; the shape is kept and the size is remembered per character
- Colour pickers in Settings: live swatch, "Pick..." (standard Windows colour dialog), quick presets and a live label preview
- `set-icon.ps1` copies the AUTZ IronWorks website icon into the app (exe and tray icon)
- Lock option now covers both moving and resizing

## v0.3.0
- Right-click and drag moves a thumbnail; left-click switches to that client
- Thumbnails snap together (edge to edge and aligned) when dragged close; snap distance is adjustable
- Hotkey boxes record the keys you press, so any key works, including backtick (`)
- `AUTZ.IronWorks.Preview.exe` is built into the repo root next to this README

## Features
- One borderless, always-on-top live thumbnail per EVE client (choose which ones)
- Clients kept in the order they were opened, login screens included
- Positions remembered per character
- Global hotkeys: next / previous client, plus a jump-to hotkey per character
- Per-character label text, label colour, width and height
- Optional active-client highlight border (off by default)
- Thumbnail opacity setting (default 60%) and a clickable on/off switch
- System tray icon with the full menu
- Only one copy can run at a time

## Using it
- **Switch:** left-click a thumbnail (if "Thumbnails clickable" is on), or use the hotkeys.
- **Resize:** drag the small triangle in the bottom-right corner with the left button. The thumbnail keeps its shape.
- **Move:** hold **Shift** and drag a thumbnail with the left button. Right-click opens the menu (settings, clickable, lock, reset, exit).
- **Choose thumbnails:** right-click the tray icon > Thumbnails to show, or Settings > Thumbnails.
- **Hotkeys:** defaults are `Ctrl+Alt+Right` (next) and `Ctrl+Alt+Left` (previous), in the order the clients were opened. Change them in Settings > Hotkeys by clicking the box and pressing the keys. A hotkey with no modifier (like `` ` ``) is grabbed system-wide, so you won't be able to type that key in other apps.
- Settings are stored in `%LOCALAPPDATA%\AUTZ IronWorks Preview\settings.json`.

## Building
```powershell
dotnet build -c Release
.\publish-exe.ps1      # builds the single-file exe into the repo root
```

## Icon
The AUTZ IronWorks emblem is built in: `src\AUTZ.IronWorks.Preview\Assets\app.ico` (exe, tray and title bars) and `logo.png` (shown inside the app windows). To change it, replace those two files and rebuild.

## Update workflow
`update-preview.ps1` replaces the repo with the newest `AUTZ-IronWorks-Preview-v*.zip`, keeps Git history, LICENSE and the icon, builds, publishes the exe to the repo root, and stages the changes. It does not commit or push. If the app is open it is left running; Windows lets the running exe be renamed, so the new exe takes its place and is used the next time you open the app. Always launch the exe in the repo root (not one from a `bin` folder).
