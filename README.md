# AUTZ IronWorks Preview

A lightweight Windows utility for permanent live previews of your running EVE Online clients and instant client switching.

**Run it:** double-click `AUTZ.IronWorks.Preview.exe` in this folder (needs the .NET 8 Desktop Runtime, already installed if you have the .NET SDK). It runs in the **system tray only** (no taskbar button): right-click the tray icon for the menu, double-click it for Settings.

## v0.6.0
- **Profiles:** save whole layouts (positions, sizes, look, hotkeys, per-character settings) and switch between them from Settings > Profiles or with hotkeys (per profile, next, previous). Your existing settings become the "Default" profile.
- **Positions saved by character or by client** (Settings > General). By client, the 1st client to open always uses the 1st position, the 2nd the 2nd, and so on, login screens included.
- **Keep all thumbnails the same size** (Settings > General). Resize one and they all follow. Untick it to size each one separately.
- **Which thumbnails to show** is in Settings > Thumbnails (not the tray menu), plus **hide the active client's thumbnail** and **hide all thumbnails while EVE isn't in front** (with a delay so it doesn't flicker).
- **Borders:** a border on the active client and, optionally, on the others, each with its own colour, width and style (solid, dashed, dotted, dash-dot, double, corner brackets).
- **Text label:** font, size, bold, colour, background colour and opacity, one of 9 positions and a pixel offset (Settings > Appearance).
- **Skip clients in the next / previous hotkeys** (Settings > Hotkeys), shown with a red X on the thumbnail. Login screens can be included or left out.
- **Auto-minimise** the other clients when you switch to one, with a delay and a never-minimise list (Settings > Clients).
- **New characters take the position and size of the login screen they replace.**
- **Faster switching:** focus changes (highlight, hide, login-to-character swap) are picked up the instant they happen instead of by polling, and the focus call itself is no longer slowed down by fallback tricks unless Windows refuses.
- **Next / previous hotkey fix:** it now carries on to the next client if Windows refuses to focus one, instead of bouncing between two. Every press is written to `%LOCALAPPDATA%\AUTZ IronWorks Preview\preview.log`.
- The tray menu and the thumbnail right-click menu stay short: everything else is in Settings.

## v0.5.0
- No control window and no taskbar button: tray icon only.
- Settings is split into pages down the left side.
- Choose which clients get a thumbnail; clients (and login screens) are kept in the order they were opened.
- A login screen sits where the character that usually logs in on that client appears.
- Move a thumbnail with **Shift + left-click and drag**. Right-click opens the menu.
- Option to make thumbnails click-proof (switch with hotkeys only). Default opacity is 60%.

## v0.4.x
- AUTZ IronWorks emblem for the exe, tray and windows; drag the bottom-right corner to resize; colour pickers; updates never touch a running app.

## Features
- One borderless, always-on-top live thumbnail per EVE client (choose which ones)
- Clients kept in the order they were opened, login screens included
- Positions remembered per character or per client
- Global hotkeys: next / previous client, plus a jump-to hotkey per character
- Per-character label text, label colour, width and height
- Profiles, borders, text styling, auto-minimise, hide rules
- System tray icon with a short menu; only one copy can run at a time

## Using it
- **Switch:** left-click a thumbnail (if "Thumbnails clickable" is on), or use the hotkeys.
- **Move:** hold **Shift** and drag a thumbnail with the left button.
- **Resize:** drag the small triangle in the bottom-right corner of a thumbnail.
- **Hotkeys:** defaults are `Ctrl+Alt+Right` (next) and `Ctrl+Alt+Left` (previous), in the order the clients were opened. Change them in Settings > Hotkeys by clicking the box and pressing the keys. A hotkey with no modifier (like `` ` ``) is grabbed system-wide, so you won't be able to type that key in other apps.
- Settings are stored in `%LOCALAPPDATA%\AUTZ IronWorks Preview\` (`profiles\NAME.json` per profile, `app.json` for the active profile).

## Building
```powershell
dotnet build -c Release
.\publish-exe.ps1      # builds the single-file exe into the repo root
```

## Icon
The AUTZ IronWorks emblem is built in: `src\AUTZ.IronWorks.Preview\Assets\app.ico` (exe, tray and title bars) and `logo.png` (shown inside the app windows). To change it, replace those two files and rebuild.

## Update workflow
`update-preview.ps1` replaces the repo with the newest `AUTZ-IronWorks-Preview-v*.zip`, keeps Git history, LICENSE and the Assets folder (icon + logo), builds, publishes the exe to the repo root, and stages the changes. It does not commit or push. If the app is open it is left running; Windows lets the running exe be renamed, so the new exe takes its place and is used the next time you open the app. Always launch the exe in the repo root (not one from a `bin` folder).
