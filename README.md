# EVE-IW-Preview (EVE-IronWorks-Preview)

*by AUTZ IronWorks · From planets to profit*

Live previews of all your EVE Online clients, with instant switching, alerts and a memory cleaner. It runs in the system tray.

**Windows only** (Windows 10 and 11, 64-bit). Not for macOS or Linux.

It only shows pictures of your clients and brings windows to the front. It never sends keys or mouse clicks to EVE.

## Install

1. Download **`EVE-IW-Preview-Setup.exe`** from the [latest release](https://github.com/Mune-Su/eve-iw-preview/releases/latest).
2. Run it. If Windows SmartScreen appears, choose **More info**, then **Run anyway** (the Setup isn't code-signed).
3. Tick **Create a desktop icon** if you want one.
4. Open **EVE-IW-Preview** from the Start menu. Its icon appears in the system tray.

- Installs for your Windows account only. No admin rights needed.
- Shows in Programs and Features, with an uninstaller.
- **Updates itself:** the app checks for a newer version each time it starts, and installs it straight away with no prompts. Turn it off under About > Update automatically.
- Your settings and profiles are kept on every update.
- Needs the .NET 8 Desktop Runtime. Setup tells you if it's missing.

**Tray icon:** right-click for the menu, double-click for Configuration.

## What it does

### Thumbnails
- A live, always-on-top thumbnail for each EVE client, including login screens.
- Click a thumbnail to switch to that client.
- **Move:** Shift + drag with the right mouse button. **Resize:** Shift + drag the orange corner.
- Size, opacity, borders, label font, colour and position. Per-character overrides.
- Hide the thumbnail of the client you're in, or hide all of them while EVE isn't in front.
- Shows the solar system next to the name (optional).
- New thumbnails stack down the left edge or along the top.

### Switching and hotkeys
- Next / previous client, a hotkey for the 1st, 2nd, 3rd client, and a jump-to key per character.
- Separate next / previous **login screen** hotkeys. They only go to clients that aren't logged in yet.
- Next / previous follow the client order (see Order, groups and layouts). Skipped clients are passed over.
- Any key, mouse button or combination.
- **Pause / Resume Hotkeys:** stops every other hotkey so you can type. A "HOTKEYS PAUSED" badge shows while paused.
- **Next / Previous Desktop:** switches Windows virtual desktops.
- Profile hotkeys: next, previous, or straight to one profile.
- Skip clients when cycling. Auto-minimise the clients you aren't using.

### Order, groups and layouts
- Client order: the order they were opened, saved per account (works on the login screen), or your own order.
- Thumbnail groups: choose which characters get a thumbnail.
- **Saved layouts:** save the client order and every thumbnail's position, then load another to swap.
- **Disable Switcher:** for all clients, or one client at a time (tray menu or General page).

### Alerts
Read from EVE's own game logs (read only). Each alert can flash the thumbnail border, show a Windows notification, play a sound and speak.

| Alert | What sets it off |
|---|---|
| Taking damage | Incoming damage or a miss in the combat log |
| Warp scrambled / disrupted | A scram or point attempt on you |
| In a warp bubble | You're inside a warp disruption field |
| Decloaked | Your cloak dropped |
| Cloak failed | Your cloak couldn't turn on |
| Mining crystal broke | A mining crystal was used up |
| Mining stopped | A missed mining cycle (automatic), or no mining for the time you set |
| Compression done | Ore was compressed |
| Warp follow | You're following a fleet warp |
| Fleet regroup | Your fleet is regrouping |
| Fleet invite | You were invited to a fleet |
| Conversation request | Someone wants to start a chat |
| Keywords | A game-log line matches one of your phrases |

- Each alert has its own sound, volume, cooldown and **Test** button.
- Built-in sounds: Chime, Ping, Double Beep, Klaxon, Rising, Falling, Alarm, Blip. The Windows sounds are listed too.
- **Add your own:** .wav, .mp3, .m4a, .wma or .aac. Max 5 MB, plays up to 10 seconds.

### Memory cleaner (Configuration > Performance)
- Trim programs, clear the system cache (admin), clear the clipboard, restart Windows Explorer.
- Automatic cleaning on a timer, when memory use is high, or when a program goes over its own limit.
- Top 10 programs by memory, with trends. A never-trim list.
- Shows the last automatic clean, or why it's waiting.

### Client speed (Configuration > Performance)
All off by default. Each one has its own switch, and the tray menu turns them all off or on at once.
- High Performance power plan while EVE is running. Switches back when the last client closes.
- Raise priority of the client in front (Above Normal).
- Lower priority of background clients (Below Normal).
- Efficiency mode for background clients. Uses less power, may lower their frame rate.

If the app closes unexpectedly, it puts everything back the next time it starts.

### Profiles and import
- As many profiles as you like. Switch from the tray, Configuration or a hotkey.
- **Import** reads EVE-IW profiles, and .json and .ini profiles.

### Diagnostics
- **Detailed logging** records everything the app does: switches, hotkeys, settings changes, tray clicks, alerts, memory and performance actions.
- Character and profile names show as codes (for example `Pilot-3FA9C1`), so a log is safe to share.
- Turn it on from the tray or Configuration > Diagnostics > Logs.

### Other
- Run on Startup. Always start as administrator (no prompt after the first time).
- Only one copy runs at a time.
- Settings live in `%LOCALAPPDATA%\EVE-IronWorks-Preview\`.

## Building from source

```powershell
dotnet build -c Release
.\installer\build-installer.ps1 -InstallInnoIfMissing   # makes installer-output\EVE-IW-Preview-Setup.exe
```

## Release pipeline

- Every push to `release` runs **Validate**: restore, build, and build the installer.
- Pushing a `vX.Y.Z` tag runs **Release**: builds the installer and publishes `EVE-IW-Preview-Setup.exe` and `SHA256SUMS.txt` as the GitHub release.
- Installed copies pick up the new **Latest** release the next time they start.

## Suggestions and bug reports
- [Join the Discord](https://discord.gg/8vBCG4WsTx) for suggestions, bug reports and help.
- Or open an issue on [GitHub](https://github.com/Mune-Su/eve-iw-preview/issues).
- For a bug, turn on Detailed logging (Configuration > Diagnostics > Logs), repeat the problem, and attach the log. Names show as codes, so it's safe to share.

## Version history

See [CHANGELOG.md](CHANGELOG.md).

