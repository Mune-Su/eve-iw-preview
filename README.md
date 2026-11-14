# AUTZ IronWorks Preview

A lightweight Windows desktop utility for permanent live previews of running EVE Online clients and instant client switching.

## v0.1.0 MVP

- Detects visible EVE client windows automatically
- Creates permanent, always-on-top live DWM thumbnails
- Click a preview to restore and activate that EVE client
- Highlights the active EVE client
- Draggable preview strip
- Horizontal or vertical layout
- Configurable preview width, refresh interval and active-border colour
- Optional character labels parsed from window titles
- System tray controls
- Saves settings and overlay position locally
- Does not broadcast input, read process memory, crop the client, or automate gameplay

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK to build
- EVE Online clients running in Fixed Window or Windowed mode are recommended

## Build

Open PowerShell in the repository root:

```powershell
dotnet restore
dotnet build -c Release
```

## Run

```powershell
dotnet run --project .\src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj
```

## Publish a self-contained Windows executable

```powershell
dotnet publish .\src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

The executable will be in `publish\AUTZ.IronWorks.Preview.exe`.

## Controls

- Left-click a live preview: activate that EVE client
- Drag the header: move the preview strip
- Gear button: open settings
- Tray icon: show/hide, refresh clients, settings, exit

## Configuration

Settings are stored at:

`%LOCALAPPDATA%\AUTZ IronWorks Preview\settings.json`

## Safety boundary

AUTZ IronWorks Preview only uses documented Windows window-management and DWM thumbnail APIs. It does not transmit input to EVE, duplicate keystrokes, inspect EVE memory, or automate gameplay. Users remain responsible for compliance with CCP's current EULA and policies.

## Roadmap

- Per-character sizing and ordering
- Global hotkeys and cycle groups
- Multiple profiles
- Layout locking
- EVE log-based attention alerts
- Optional sound and text-to-speech notifications
- Installer and signed releases
