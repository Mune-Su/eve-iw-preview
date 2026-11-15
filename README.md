# AUTZ IronWorks Preview

A lightweight Windows utility for permanent live previews of your running EVE Online clients and instant client switching.

## v0.2.0

- One borderless, always-on-top live thumbnail per EVE client (no frame, no title bar)
- Drag any thumbnail anywhere; positions are remembered per character
- Click a thumbnail (without dragging) to jump to that client
- Global hotkeys: next / previous client, plus a jump-to hotkey per character
- Per-character label text, label colour, width and height
- Optional active-client highlight border (off by default)
- Single-file exe in the repo root: `AUTZ IronWorks Preview.exe`
- Tray icon: show/hide, refresh, settings, lock positions, reset positions, exit
- Only one copy can run at a time

## Using it

- **Move**: drag a thumbnail. **Switch**: click it. **Settings / lock / reset / exit**: right-click a thumbnail or use the tray icon.
- **Default hotkeys**: `Ctrl+Alt+Right` = next client, `Ctrl+Alt+Left` = previous client (alphabetical by character). Change them, and set per-character hotkeys, in Settings.
- Hotkey format: `Ctrl+Alt+1`, `Ctrl+Shift+F5`, `Win+Q` ... Leave empty to disable.
- Settings are stored in `%LOCALAPPDATA%\AUTZ IronWorks Preview\settings.json`.

## Building

```powershell
dotnet build -c Release
.\publish-exe.ps1      # builds the single-file exe into the repo root
```

The exe needs the .NET 8 Desktop Runtime (already there if the SDK is installed).

## Icon

Put the AUTZ IronWorks icon at `src\AUTZ.IronWorks.Preview\Assets\app.ico` and rebuild. It becomes the exe icon and the tray icon automatically.

## Update workflow

`update-preview.ps1` replaces the repo with the newest `AUTZ-IronWorks-Preview-v*.zip`, keeps Git history, LICENSE and the icon, builds, publishes the exe to the repo root, and stages the changes. It does not commit or push.
