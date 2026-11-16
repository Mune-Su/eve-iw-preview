# EVE-IW-Preview (EVE-IronWorks-Preview)

*by AUTZ IronWorks · From planets to profit*

A lightweight Windows utility for permanent live previews of your running EVE Online clients and instant client switching.

**Run it:** double-click `EVE-IW-Preview.exe` in this folder (needs the .NET 8 Desktop Runtime, already installed if you have the .NET SDK). It runs in the **system tray only** (no taskbar button): right-click the tray icon for the menu, double-click it for Configuration.

## v0.9.4
- **Detailed (admin) logging:** tray menu **Detailed Logging** / **Open Debug Log**, or Settings > About > Diagnostics. Writes `%LOCALAPPDATA%\EVE-IronWorks-Preview\debug.log`: hotkeys, every focus attempt and the method that worked, every minimise / restore / maximise sent to a client, thumbnails shown / hidden / created / closed, focus changes and client-list changes, with millisecond timestamps. Background writer, rolls over at ~4 MB.

## v0.9.3
- "by AUTZ IronWorks · From planets to profit" (Settings sidebar and About) links to the AUTZ IronWorks website: https://autz-ironworks.vercel.app/#/home
- The tray menu item is now just **Configuration**.
- `update-preview.ps1` also looks for the versions folder inside `Documents\IronWorks`, and no longer copies itself into the versions folder. Run it from the repo: `.\update-preview.ps1`.

## v0.9.2
- `update-preview.ps1` finds your repo and your versions folder by itself (it looks for `EVE IronWorks Preview versions` in Documents, and a repo named `EVE-IronWorks-Preview`, `EVE-IW-Preview` or `AUTZ-IronWorks-Preview` in `Projects`), so renaming them no longer breaks it. It keeps a copy of itself in the versions folder.

## v0.9.1
- The name in the Settings sidebar and the what's-new window uses the IronWorks casing (EVE-IronWorks), and the tray icon tooltip says EVE-IW-Preview.

## v0.9.0
- **Renamed: EVE-IW-Preview (EVE-IronWorks-Preview).** EVE-IW-Preview is the short name used for the exe (`EVE-IW-Preview.exe`), the GitHub repository (`eve-iw-preview`), Discord and Reddit. EVE-IronWorks-Preview is the name shown in the windows, the tray and the About page, with "by AUTZ IronWorks · From planets to profit" under it.
- **Your settings carry over:** they now live in `%LOCALAPPDATA%\EVE-IronWorks-Preview`. Profiles, positions and hotkeys are copied across from the old `AUTZ IronWorks Preview` folder the first time it starts, and the old folder is left untouched. The Run on Startup entry moves to the new name too.
- **Shorter tray menu:** Configuration (Settings), Hide All Thumbnails, Refresh Clients, Reset All Positions, Exit. Lock positions and Recover off-screen thumbnails are in Settings > General.
- The project folder and C# namespace are still `AUTZ.IronWorks.Preview` (the author's name). The exe, titles, settings folder and startup entry use the new name.

## v0.8.3
- Fixes the build error in 0.8.2 (a helper class was defined twice). Everything from 0.8.0 to 0.8.2 is included.

## v0.8.2
- Fixes the build errors that stopped 0.8.0 and 0.8.1 from building (missing `using` lines in the start-up code, the log monitor and the notification sounds, and the folder-picker helper). Everything from 0.8.0 and 0.8.1 is included.

## v0.8.1
- **Settings now match the AUTZ IronWorks website:** the exact palette from `css/style.css` (near-black `#0C0C0E` page, `#121215` sidebar, `#18181C` panels, `#2C2C33` lines, orange `#F97316`), the website's sidebar layout (logo and name on top, menu underneath, orange-tinted active item with an orange bar) and its button, input, tick-box and heading styles, with sharper corners.
- Saved colours that were still the old defaults are moved to the new palette automatically.

## v0.8.0
- **New Settings window** with a left-hand menu (General, Appearance, Thumbnails, Characters, Hotkeys, Clients, Notifications, Log Monitoring, About).
- **APPLY / SAVE / RESET / CLOSE:** Apply puts your changes into the running session without saving. Save writes them to the config file. Reset puts the profile's settings back to their defaults (your character list and positions are kept). Close asks before throwing away unsaved changes. A status line confirms what happened.
- **Hotkeys with no restrictions:** any key, any mouse button (including the side buttons and the wheel), single keys with no modifier, combinations such as `A+S`, and modifier-only hotkeys. Right-side modifiers are saved as `RCtrl`, `RAlt`, `RShift`, `RWin`. Options: only while EVE is in front, and let the key through to the program.
- **Client hotkeys:** a hotkey for the 1st client to open, the 2nd, and so on (Settings > Hotkeys), as well as per-character hotkeys.
- **Cycling can't get stuck:** the next / previous hotkeys skip a client that is not responding, and nothing ever waits on a frozen client (switching no longer blocks on it).
- **No right-click on thumbnails.** Thumbnails are non-activating overlays, so clicking one doesn't take focus from the game or pull the taskbar up.
- **Thumbnails page:** add characters by hand, remove them, or scan every running client and pick which ones to add.
- **Clients that aren't logged in can't be clicked** (switch it back on under General).
- **Auto-minimize also covers clients that haven't been in front yet** (loading or on the login screen), so a fresh start-up no longer wastes resources; optionally keep the last-used client visible.
- **Per client:** maximise or restore it when you switch to it, and minimise it when you switch away.
- **Recover off-screen thumbnails** (General, or the tray menu), which also runs by itself when your monitors change. Thumbnails can be turned off "always on top".
- **Run on Startup**, **Notifications** (flash the thumbnail border, a Windows notification, a sound, spoken alerts) and **Log Monitoring** (incoming damage, mining gone quiet, your own keywords, and the solar system on each thumbnail). The logs are read from `Documents\EVE\logs`; the app only reads them.
- **What's-new pop-up** on start-up and a full changelog under Settings > About.
- Tray menu: Settings, hide/show, refresh, recover off-screen thumbnails, suspend hotkeys, lock, reset positions, exit.

## v0.7.0
- **New Settings window in the EVE-Maj style, in AUTZ colours:** monospace text, a left menu with sub-sections you can click to jump to, boxed section cards (`# POSITIONS`, `# SIZE`, ...), square check boxes with an X, drop-downs, sliders for the opacity values, and colour swatches.
- **Profile bar across the top:** pick a profile, or NEW, COPY, RENAME, DELETE, RESET and IMPORT one. Pick another profile and press SAVE to switch to it.
- **Search box at the bottom:** type part of a setting's name or description and the matching sections are outlined and the first one is shown.
- **Pages:** General, Appearance, Thumbnails, Characters, Hotkeys, Clients and About. Profiles now live in the top bar and profile hotkeys are under Hotkeys.

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
- **Next / previous hotkey fix:** it now carries on to the next client if Windows refuses to focus one, instead of bouncing between two. Every press is written to `%LOCALAPPDATA%\EVE-IronWorks-Preview\preview.log`.
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
- **Hotkeys:** defaults are `Ctrl+Alt+Right` (next) and `Ctrl+Alt+Left` (previous), in the order the clients were opened. Change them in Settings > Hotkeys: click a box, then press any key, mouse button or combination. A hotkey with no modifier (like `T`) is taken over everywhere, so you can't type that key in other apps unless you tick "only while EVE is in front".
- Settings are stored in `%LOCALAPPDATA%\EVE-IronWorks-Preview\` (`profiles\NAME.json` per profile, `app.json` for the active profile).

## Building
```powershell
dotnet build -c Release
.\publish-exe.ps1      # builds the single-file exe into the repo root
```

## Icon
The AUTZ IronWorks emblem is built in: `src\AUTZ.IronWorks.Preview\Assets\app.ico` (exe, tray and title bars) and `logo.png` (shown inside the app windows). To change it, replace those two files and rebuild.

## Update workflow
`update-preview.ps1` replaces the repo with the newest `EVE-IW-Preview-v*.zip` (or `AUTZ-IronWorks-Preview-v*.zip`, the name used before the rename), finds the repo and the versions folder by itself, keeps Git history, LICENSE and the Assets folder (icon + logo), builds, publishes the exe to the repo root, and stages the changes. It does not commit or push. If the app is open it is left running; Windows lets the running exe be renamed, so the new exe takes its place and is used the next time you open the app. Always launch the exe in the repo root (not one from a `bin` folder).


