# AUTZ IronWorks Preview

A lightweight Windows utility for permanent live previews of your running EVE Online clients and instant client switching.

**Run it:** double-click `AUTZ.IronWorks.Preview.exe` in this folder (needs the .NET 8 Desktop Runtime, already installed if you have the .NET SDK).

## v0.3.0

- Right-click and drag moves a thumbnail; left-click switches to that client
- Thumbnails snap together (edge to edge and aligned) when dragged close; snap distance is adjustable
- Taskbar icon while the app is running (control window, starts minimized; closing it exits the app)
- Hotkey boxes record the keys you press, so any key works, including backtick (`)
- `AUTZ.IronWorks.Preview.exe` is built into the repo root next to this README

## Features

- One borderless, always-on-top live thumbnail per EVE client
- Positions remembered per character
- Global hotkeys: next / previous client, plus a jump-to hotkey per character
- Per-character label text, label colour, width and height
- Optional active-client highlight border (off by default)
- Tray icon: control window, show/hide, refresh, settings, lock positions, reset positions, exit
- Only one copy can run at a time

## Using it

- **Switch:** left-click a thumbnail.
- **Move:** hold the right mouse button on a thumbnail and drag. Right-click without dragging opens the menu (settings, lock, reset, exit).
- **Hotkeys:** defaults are `Ctrl+Alt+Right` (next) and `Ctrl+Alt+Left` (previous), alphabetical by character. Change them in Settings by clicking the box and pressing the keys. A hotkey with no modifier (like `` ` ``) is grabbed system-wide, so you won't be able to type that key in other apps.
- Settings are stored in `%LOCALAPPDATA%\AUTZ IronWorks Preview\settings.json`.

## Building

```powershell
dotnet build -c Release
.\publish-exe.ps1      # builds the single-file exe into the repo root
```

## Icon

Put the AUTZ IronWorks icon at `src\AUTZ.IronWorks.Preview\Assets\app.ico` and rebuild. It becomes the exe, taskbar and tray icon.

## Update workflow

`update-preview.ps1` replaces the repo with the newest `AUTZ-IronWorks-Preview-v*.zip`, keeps Git history, LICENSE and the icon, builds, publishes the exe to the repo root, and stages the changes. It does not commit or push.
