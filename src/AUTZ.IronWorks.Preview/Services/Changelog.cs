// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
namespace AUTZ.IronWorks.Preview.Services;

internal sealed record ChangelogEntry(string Version, string Title, string[] Items);

/// <summary>What changed in each version. The newest entry comes first and is what the start-up pop-up shows.</summary>
internal static class Changelog
{
    public static readonly ChangelogEntry[] Entries =  
      {
        new("0.19.35", "Per-client switching", new[]
        {
            "Numbered clients now have their own switching options.",
            "Each option can inherit the character setting."
        }),
        new("0.19.34", "Credits", new[]
        {
            "Credit added for the instant-switching code. See About."
        }),
        new("0.19.33", "Open source", new[]
        {
            "Now free software under the GPL-3.0. See About > View license."
        }),
        new("0.19.32", "Switching that keeps up", new[]
        {
            "Hotkeys now run on their own thread. Keys and clicks never wait on the app.",
            "Mouse-button and combo hotkeys switch at once. No Alt tap, no waiting on Windows.",
            "A hotkey's key-up never reaches the client you switch to."
        }),
        new("0.19.31", "Minimised client timing, numpad keys", new[]
        {
            "Switching to a minimised client is now timed in the detailed log.",
            "Next / Previous wait until that client is back: at least 0.15 s, at most 1 s.",
            "Presses in that time are ignored, so clicks aren't lost on a half-restored client.",
            "Numpad arrows, Home, End, PgUp, PgDn, Ins, Del and Enter are now their own keys (NumLeft, NumEnter...).",
            "A hotkey set to the main arrow keys no longer fires from the numpad. Set it again if you want the numpad one.",
            "New Discord link."
        }),
        new("0.19.30", "Faster switching", new[]
        {
            "Faster switching: a client slowed down by Client Speed gets full speed back before it comes to the front, not after.",
            "Rapid cycling no longer waits on priority changes. The client you left is slowed down only once you stop switching.",
            "A switch Windows refuses now waits at most about 80 ms in total, not up to 240 ms.",
            "Mouse-button and key-combo hotkeys run ahead of everything else the app is doing.",
            "Less work per switch: client priorities are changed without listing every program on the PC."
        }),
        new("0.19.29", "New Discord invite", new[]
        {
            "The Discord link under About > Community and in the README has a new, working invite."
        }),
        new("0.19.28", "Alert sound gap", new[]
        {
            "New: Same Alert Sound Gap (Alerts page). The same alert on several clients inside this many seconds plays one sound.",
            "The default is 1.5 seconds, so quick compressing plays every sound. 0 = every alert plays its sound.",
            "Flashes and notifications are not affected."
        }),
        new("0.19.27", "Faster alerts, one Alerts switch", new[]
        {
            "Mining stopped now alerts the moment the hold is full or the rock runs out.",
            "New: Alert when I stop mining by hand. Off = only hold full and rock gone alert.",
            "Alerts and log monitoring are one switch now: Enable Alerts.",
            "Events To Watch now sits above the log files. The check interval box is gone.",
            "Faster: the game logs are checked 4 times a second, and new logs are found within a second.",
            "Add several sounds at once.",
            "An alert that isn't shown now says why in the normal log."
        }),
        new("0.19.26", "Fix: mining stopped alerts missing", new[]
        {
            "Fixed: mining stopped alerts could miss some characters when there were lots of EVE log files.",
            "Only the newest log of each character is watched, newest first.",
            "The detailed log now shows when mining is seen and the cycle learned."
        }),
        new("0.19.25", "Fix: Next Client stuck on the last client", new[]
        {
            "Fixed: Next / Previous Client could get stuck, pressing again and again on the same client.",
            "A switch now only counts once the client is really in front, and a client that is still in front is never minimised.",
            "If Windows refuses one client, the press now moves on to the next one."
        }),
        new("0.19.24", "Login screen hotkeys and Discord", new[]
        {
            "New: Next / Previous Login Screen hotkeys. They only go to clients that aren't logged in yet.",
            "Cycling now says which order it follows.",
            "Client Speed notes say when each option helps.",
            "About says the app is Windows only, and links to the Discord, website and GitHub."
        }),
        new("0.19.23", "Memory and Performance together", new[]
        {
            "Memory and Performance are now one page, called Performance. Everything from both is still there.",
            "The App Components card moved to the top of Performance and is now called Features.",
            "The Performance card is now called Client Speed.",
            "About is always the last menu."
        }),
        new("0.19.22", "One-start updates", new[]
        {
            "Updates now install in a single start. The app checks GitHub before it opens and installs a newer version straight away.",
            "A small window with the crucible shows the download. It only appears when there is an update.",
            "If there's no internet or GitHub is slow, the app opens as normal."
        }),
        new("0.19.21", "Smarter mining alert", new[]
        {
            "Mining stopped can be set to 0 (automatic). It alerts as soon as a mining cycle is missed.",
            "The app learns each character's mining cycle. A number above 0 still works as a fixed time."
        }),
        new("0.19.20", "Desktop icon option", new[]
        {
            "The installer now offers to create a desktop icon. It's unticked by default.",
            "Updates keep what you chose the first time.",
            "update-preview.ps1 now only checks the code builds."
        }),
        new("0.19.19", "Silent updates and alert cooldowns", new[]
        {
            "Updates install by themselves: a new version is downloaded in the background and installed silently the next time the app starts.",
            "Turn it off under About > Update automatically.",
            "Each alert has its own cooldown. Mining stopped keeps its own idle time.",
            "Simpler comments throughout the code."
        }),
        new("0.19.18", "Alerts, sounds, layouts and performance", new[]
        {
            "Fixed: the app didn't open from the Start menu when 'Always start as administrator' was on.",
            "Fixed: a File Explorer window for a folder called EVE showed up as a thumbnail.",
            "Alerts match EVE's real game-log text. New: warp scrambled / disrupted, warp bubble, cloak failed and fleet invite.",
            "Each alert has its own sound, volume and Test button. New built-in sounds, and you can add your own (.wav, .mp3, .m4a, .wma, .aac).",
            "Disable Switcher can now turn the switcher off for one client at a time.",
            "New hotkeys: Pause / Resume Hotkeys (so you can type), and Next / Previous Desktop.",
            "Saved layouts: save the client order and thumbnail positions, then swap between them.",
            "New thumbnails can stack down the left edge or along the top.",
            "New Performance page: High Performance power plan, client priority and efficiency mode. All off by default.",
            "Import reads more profile types.",
            "About and Diagnostics are now separate pages. Hover notes on drop-down options. Shorter notes everywhere.",
            "Detailed logging now records settings changes, tray clicks, thumbnail moves, alerts and more."
        }),
        new("0.19.17", "Proper installer", new[]
        {
            "EVE-IW-Preview now installs properly: a per-user Setup adds it to Start and to Programs and Features with its version number, includes an uninstaller, and upgrades in place. Your settings and profiles are kept.",
            "The portable exe is gone from the repo and from the GitHub release, which now ships EVE-IW-Preview-Setup.exe and its checksum.",
            "update-preview.ps1 builds the installer and opens it, still taking the newest ZIP from Downloads.",
            "The GitHub pipelines use the Node 24 versions of their actions, so the Node.js 20 warning is gone."
        }),
        new("0.19.16", "Lighter detailed logging and a working updater", new[]
        {
            "Detailed Logging costs nothing again while it is off: the keyboard and mouse hooks no longer build a log line on every key press and click.",
            "update-preview.ps1 accepts complete source packages as well as targeted overlay packages, checks the ZIP version matches the project before building, and moves the applied ZIP from Downloads into the versions folder."
        }),
        new("0.19.15", "Simplified automatic cache cleaning", new[]
        {
            "Removed the 'only while free memory is below' option from Memory settings.",
            "Scheduled system-cache cleaning now runs according to its enabled interval without that extra condition."
        }),
        new("0.19.14", "GitHub production release", new[]
        {
            "Published through the automated GitHub release pipeline for production use.",
            "Includes the complete named Events To Watch alert update and preserves all existing project files."
        }),
        new("0.19.14-rc.1", "Release candidate", new[]
        {
            "First GitHub prerelease produced by the automated release pipeline.",
            "Includes the complete v0.19.13 project and named Events To Watch alert changes."
        }),
        new("0.19.13", "Complete update package", new[]
        {
            "Reissued the named event alerts update as a complete project package.",
            "All existing project files are preserved, including Core/MemoryTrim.cs."
        }),
        new("0.19.11", "Full detailed logging", new[]
        {
            "Detailed Logging now records the complete operational trace while it is enabled, rather than only selected switching events.",
            "The trace includes every client scan, Windows focus and title event, raw hotkey input handled by the app, log-monitor file discovery and every new EVE game/chat log line read, memory-cleaner decisions, settings changes, start-up, shutdown and exceptions.",
            "Privacy scrubbing still applies to every line. Character and profile names, personal paths, email addresses, other-window titles and EVE log filenames remain protected.",
            "Full tracing is still written on the background log thread and stops immediately when Detailed Logging is turned off."
        }),
        new("0.19.10", "Group component controls in the tray", new[]
        {
            "Disable Switcher and Disable Memory Cleaner now appear together in the same tray-menu section.",
            "The separator now sits below both component controls and above the manual memory actions."
        }),
        new("0.19.9", "Component controls in Configuration", new[]
        {
            "The General page now has an App Components card with Enable or Disable Switcher and Enable or Disable Memory Cleaner controls.",
            "The configuration controls use the same saved state as the tray menu, so both locations stay consistent."
        }),
        new("0.19.8", "Enable or disable the memory cleaner independently", new[]
        {
            "Disable Memory Cleaner stops automatic memory cleaning and disables the manual Trim Programs and Clear System Cache commands while leaving the switcher running.",
            "The tray command changes to Enable Memory Cleaner and the choice is remembered after restart.",
            "No notification is shown when the memory cleaner is enabled or disabled."
        }),
        new("0.19.7", "Fix switcher-disable publish error", new[]
        {
            "Fixed the build failure caused by calling Clear on the read-only client list.",
            "Disabling the switcher now replaces the client snapshot with an empty read-only list while leaving the memory cleaner running."
        }),
        new("0.19.6", "Run as a memory cleaner without the switcher", new[]
        {
            "Disable Switcher turns off the complete switching side of the app while leaving the memory cleaner running.",
            "The choice is remembered after restart. The tray command changes to Enable Switcher when switching is disabled.",
            "No notification is shown when the switcher is enabled or disabled."
        }),  
          new("0.19.5", "Fully close thumbnails while switching is paused", new[]
          {
              "Hide All Thumbnails is now Pause Switching. It hides every thumbnail and disables client cycling, client-slot hotkeys, per-character jump hotkeys, profile hotkeys and tray profile switching until Resume Switching is selected.",
              "Pause Switching now fully closes every thumbnail window, including its border and label. Pause/resume and Detailed Logging no longer show tray notifications."
          }),  
        new("0.19.3", "Import profiles", new[]
        {
            "Configuration > Import now reads .ini and .json profiles, as well as EVE-IW profiles.",
            "Supported thumbnail appearance, character groups, saved positions, snapping, hiding, auto-minimise, log folders and hotkeys are converted into a new EVE-IW profile.",
            "A summary is shown before import. The source file and existing EVE-IW profiles are never changed.",
            "Settings with no genuine EVE-IW equivalent are safely ignored."
        }),
        new("0.18.7", "Saved login slots by EVE account", new[]
        {
            "Save current order stores each open EVE account's private launcher signature against its numbered slot. In Saved mode, the login screen returns to that slot immediately, even when accounts are reopened in a different order.",
            "The mapping belongs to the EVE account, not the character. A second or third character on the same account keeps the same client slot.",
            "Automatic mode is unchanged: clients remain in the order they were opened.",
            "The text beside radio buttons can now be selected and copied; only the selection circle changes the option.",
            "Configuration wording, capitalisation and punctuation have been cleaned up."
        }),
        new("0.18.6", "Client start-up details in the detailed log", new[]
        {
            "New: when Detailed logging is on, debug.log now has CLIENTINFO lines. For every EVE client it records which window and process it is, when it was started, which program started it (for example a launcher), and the start-up arguments it was given. It also records the moment a login screen turns into a character (window, process, and the old and new title).",
            "Why: a client on the login screen has no character name yet, so the app can't tell which saved character it will become. If something in the start-up arguments stays the same for each account, it could be used to match login screens automatically.",
            "Private: argument names are written as they are, but every argument value is only a short code (Arg-XXXXXX) and its length. The code is salted per install: the same value gives the same code every time on this PC, so you can compare two starts, but the value itself (a token, a name) can't be worked out from it.",
            "If a line says the arguments are not available with error 5, the client is running as administrator and this app isn't: use Restart as administrator.",
            "Nothing else changed."
        }),
        new("0.18.4", "Reset order works straight away", new[]
        {
            "Fixed: Reset order did nothing while your clients were running. It only set a flag and waited until every EVE client was closed. It now clears the saved order at once. The clients that are open right now are put back in the order they were opened (that is how the saved list always builds), and any character that isn't open is forgotten. The list on screen updates straight away.",
            "The Cancel reset button is gone, since there is no waiting any more. A reset that an older version left waiting is done the next time the app starts.",
            "To set the order yourself instead, open the clients in the order you want and press Save current order."
        }),
        new("0.18.3", "Save the client order", new[]
        {
            "New: a Save current order button (Grouping > Client Order And Positions, shown for Automatic and Saved). Open your clients in the order you want, press it, and that order is stored for the Saved option. It is saved straight away, and the card shows what is stored. Then choose Saved and press Apply or Save to use it.",
            "It stores the logged-in clients in the order they are in now (clients still on the login screen have no name yet, so log them in first). It replaces what was stored before, and cancels a pending Reset order."
        }),
        new("0.18.2", "Thumbnails work while Configuration is open", new[]
        {
            "Fixed: clicking a thumbnail did nothing while the Configuration window was open. The window was a modal dialog, and Windows disables every other window of the app while a modal dialog is open, thumbnails included. It is a normal window now, so thumbnails can be clicked to switch (and moved with Shift + right-drag) while you change settings. The Configuration window is still one-at-a-time, and it stays in the taskbar if EVE covers it.",
            "A click that is ignored because clicking is switched off for that kind of thumbnail now says so in the detailed log (Settings > Diagnostics > Logs)."
        }),
        new("0.18.1", "Resize corner only with Shift", new[]
        {
            "Changed: the orange resize triangle in a thumbnail's bottom-right corner now only shows, and only works, while Shift is held. Without Shift it is hidden and a click there switches clients like anywhere else on the thumbnail. So moving (Shift + right-drag) and resizing (Shift, then drag the triangle with the left button) both need Shift, and nothing moves or resizes by accident.",
            "The triangle appears as soon as you press Shift over a thumbnail, even if the mouse isn't moving, and disappears when you let go. The cursor changes to match. The check runs only while the mouse is over a thumbnail, so it costs nothing the rest of the time.",
            "Lock thumbnail positions and sizes still hides the triangle and blocks both."
        }),
        new("0.18.0", "Click to switch, Shift + right-drag to move", new[]
        {
            "Changed: the left mouse button only switches clients now. It can no longer move a thumbnail (a Shift + left-drag used to). To move a thumbnail, hold Shift and drag with the right mouse button. A plain right-click does nothing. The small triangle in the bottom-right corner still resizes with the left button.",
            "Changed: character thumbnails and login screen thumbnails now have two separate, independent options under Interaction: \"Character thumbnails are clickable\" and \"Login screen thumbnails are clickable\". Before, the login option only worked while the character option was on. Your existing profiles keep exactly the behaviour they had.",
            "Lock thumbnail positions and sizes still stops both moving and resizing."
        }),
        new("0.17.3", "Hotkeys while Configuration is open", new[]
        {
            "New: Hotkey Options > \"Keep the hotkeys working while this Configuration window is open\". Off by default (the hotkeys are paused while the window is open, as before). Tick it and your hotkeys keep switching clients while you change settings.",
            "While you are recording a key in one of the hotkey boxes the hotkeys still pause, so pressing a hotkey there records it instead of firing it. They come back the moment you click out of the box, and they stay on when you click over to EVE.",
            "With the window open the hotkeys in use are the ones you last applied or saved, not unsaved edits. \"Hotkeys only work while an EVE client is the window in front\" still applies, and the Configuration window is not an EVE client."
        }),
        new("0.17.2", "Check box text can be selected", new[]
        {
            "Fixed: the text beside a check box can be highlighted and copied again. Clicking the text selects it and does not tick the box; only the box itself ticks it. This covers every check box in Settings, including the character lists.",
            "The radio buttons (for example the EVE client choices on the Memory page) are unchanged."
        }),
        new("0.17.1", "Hotkeys when EVE isn't in front", new[]
        {
            "Fixed: Next / Previous did nothing while no EVE client was in front if the client you used last was the only one it could switch to (for example one client open, or every other client left out of cycling). It now brings that client back. When you are in another program the last client you used is no longer skipped.",
            "A press that switches nothing now says why in preview.log, and a press that is ignored because \"Hotkeys only work while an EVE client is the window in front\" is on says so too (at most once every few seconds).",
            "The Hotkey Options text now says hotkeys work from any window while that option is unticked, and that they are paused while the Configuration window is open."
        }),
        new("0.17.0", "Always start as administrator", new[]
        {
            "New: Restart as administrator now asks \"Always\" or \"Just this once\". Choose Always and the app is set to start as administrator every time after that, without a Windows prompt each time, so you only do it once.",
            "How it works: Windows only skips its prompt for a task that was set up once with administrator rights. The app creates a scheduled task for itself (it has no trigger and only runs when the app starts it). After that, a normal start of the app (the exe, Run on Startup, the update script) hands over to that task and closes itself, and the task's copy carries on as administrator.",
            "It is a setting: Settings > General > Startup And Tray Icon > Always start as administrator. It can be changed while the app is running as administrator, and Save sets up or removes the task. If the task can't be started for any reason, the app simply starts normally.",
            "The task is re-pointed at the current exe every time the app starts as administrator, so moving the app or updating it keeps working."
        }),
        new("0.16.2", "Estimates under the memory buttons", new[]
        {
            "New: a small grey line under Trim Programs, Clear system cache and Clear clipboard says roughly what pressing it would free. It refreshes while the Memory page is open.",
            "Trim Programs shows the memory held by the programs it would trim right now (your smallest-size rule, the never-trim list, the EVE client setting and the program in front all count), as \"up to\" that much. After you have trimmed once it switches to \"about\", using how much of the trimmed memory really came back last time, and gets closer with each trim.",
            "Clear system cache shows the cached files it can drop and the unsaved data it will write to disk (administrator rights needed). Windows already counts cached files as available memory, so the Memory in use figure barely moves when they go.",
            "Clear clipboard shows the size of the text or picture on the clipboard, and says so when it is empty.",
            "The result lines under the buttons take no room while they are empty, which closes the gap on the Memory page."
        }),
        new("0.16.1", "Client order and positions together", new[]
        {
            "Client Order and Positions are now one card, Client Order And Positions, on the Grouping page. It starts with a short explanation of what the order is used for, then two numbered steps: 1. How Clients Are Ordered (Automatic, Saved or Manual) and 2. Where Thumbnail Positions Are Remembered (by character or by client), with the Recover and Reset buttons. The wording of every option is clearer, and the warning about Automatic order and positions saved by client sits right under the position choice.",
            "On a wide window the settings no longer stretch edge to edge: the page content stops at a comfortable width and is centred, so text lines stay readable and there is no long empty strip inside each card. The window can still be resized freely.",
            "The Hotkeys page points to the new place for the client order. Nothing was removed and your saved settings are unchanged."
        }),
        new("0.16.0", "Tighter, simpler Settings", new[]
        {
            "Less empty space: every box, field, drop-down, button, tick box and slider is smaller, and the section cards have less padding, so a lot more fits on screen.",
            "Pages are now General, Thumbnails, Grouping, Hotkeys, Alerts, Memory and About. Appearance and Thumbnails are one page called Thumbnails, and Clients and Characters are gone as pages.",
            "Thumbnails holds everything about how a thumbnail looks and behaves: Size And Opacity, Text Label (the text background is part of it now), Borders (active, inactive and the alert flash colour), Per-Character Overrides, Positions, and Interaction And Hiding. Every colour setting is on this page.",
            "Text Label: Font, Font Size and Position are on one line, Show names and Bold share a line, and the text and background colours, opacities and offsets sit side by side.",
            "Grouping holds the character groups and Find Running Clients.",
            "General is Startup And Tray Icon and Clients (scan interval, auto-minimize and the never-minimize list).",
            "The per-character jump-to hotkey, and what happens when you switch to or away from a client, are on the Hotkeys page.",
            "Alerts is Notifications, Log Monitoring and Events To Watch. The alert flash colour moved to Thumbnails > Borders.",
            "Memory is now in the order Memory, Settings (automatic cleaning), Top Programs and Exclusions (what it leaves alone). Nothing was removed and your saved settings are unchanged."
        }),
        new("0.15.0", "Tidier Settings menus", new[]
        {
            "Appearance now holds every colour, size and opacity option in one place: Size and Opacity (thumbnail width, height and opacity), Text Label (font, size, colour, opacity, position), Text Background, Borders (active and inactive together) and Per-Character Overrides (each character's own label, colour, size, opacity and thumbnail size). The thumbnail size options moved here from General, and the per-character look options moved here from Characters.",
            "Thumbnails is now about how thumbnails behave: Positions, Interaction (clicking, locking, snapping, always on top) and Hiding.",
            "Characters is now about who gets a thumbnail: the character groups, Find Running Clients, and each character's jump-to hotkey and switch behaviour.",
            "General is down to the app itself: Startup, Tray Icon and Scanning.",
            "Notifications and Log Monitoring are now one page called Alerts. Hotkeys, Clients, Memory and About are unchanged.",
            "The two character pickers (Appearance and Characters) always show the same character, and a bad value takes you to the page that holds it. Nothing was removed and your saved settings are unchanged."
        }),
        new("0.14.2", "Check box and opacity tweaks", new[]
        {
            "Check boxes: only the box itself is clickable now. The text beside it, and the empty space across the window, no longer tick or untick it.",
            "The option under Interaction is now just \"Thumbnails of clients that aren't logged in yet are clickable\".",
            "Every opacity now starts at 60%: thumbnail opacity, text opacity and the text background opacity. This is the default for new profiles and for Reset; values you have already saved are kept."
        }),
        new("0.14.1", "Label text: colour, size and opacity", new[]
        {
            "New: Text Opacity (Settings > Appearance > Text Label). It sets how solid the text on the thumbnails is, from 10 to 100 percent. The box behind the text keeps its own opacity under Text Background.",
            "New: each character can have its own label size and label opacity on the Characters page, next to the label colour that was already there. Leave a value at 0 to use the default. The preview on that page shows the colour, size and opacity the thumbnail will get.",
            "The default text colour and size are still in Settings > Appearance > Text Label."
        }),
        new("0.14.0", "Memory cleaner upgrades", new[]
        {
            "New: Restart Windows Explorer, a button on the Memory page and an item in the tray menu. It asks first, then closes Explorer (the taskbar, the desktop and any File Explorer windows) and starts it again. Your programs keep running.",
            "The tray menu is shorter: Refresh Clients and Reset All Positions are gone. Reset all positions is still in Settings > General.",
            "\"Trim programs' working set\" is now called Trim Programs.",
            "New: a Top Programs card on the Memory page. It lists the 10 programs using the most memory (all the processes of a program added together), refreshed every 10 seconds while the page is open, and nothing is read while the page is closed or the window is minimised. Each program has a Trim button (trim just that one now) and a Never trim / Allow button (adds it to, or takes it off, the never-trim list). An arrow compares with about a minute ago, and a yellow \"growing\" tag marks a program whose memory has kept rising for several minutes, which can mean a leak. The trends build up only while the page is open.",
            "New: Smallest program to trim (default 30 MB). Programs using less are skipped because trimming them gains almost nothing; 0 trims everything. It doesn't apply to the Trim button next to a program.",
            "New: EVE clients have three choices: never trim them (the default), trim background clients once they have been idle for the time you set (the client you are in is never trimmed), or trim them like any other program.",
            "New: per-program limits. Add lines like chrome=1500 and a program using more than that is trimmed (at most once every 3 minutes each). The never-trim list and the program in front still win.",
            "New: scheduled cache clearing can wait until free memory is low (default: below 20%), so it doesn't run when there is plenty of free memory. The usage-limit option is separate.",
            "Stronger Clear system cache: as well as flushing Windows' file cache it now writes modified pages to disk and drops the low-priority cached pages (needs administrator rights, like before).",
            "Every clean now shows the available memory before and after, and the Memory page keeps a running total of what has been returned since the app started.",
            "Lower impact: cleaning runs on its own thread at below-normal priority so it doesn't compete with EVE, and the Windows audio engine is never trimmed. The log still records only counts and sizes, never program names."
        }),
        new("0.13.0", "Memory cleaner", new[]
        {
            "New: a Memory page in Configuration. It shows how much memory your PC is using (a bar, the committed memory, and the average, highest and lowest readings) and has the buttons Trim programs' working set, Clear system cache and Clear clipboard.",
            "New: automatic cleaning, all off until you switch it on: trim every N minutes, clear the system cache every N minutes, and either one when memory use goes over a limit you choose. When a limit is on, memory use is checked every 30 seconds and it cleans at most once every 3 minutes. Nothing runs in the background while all of it is off. Saved with the app, not with a profile.",
            "It never trims the program you are using at that moment, Windows' own core processes, or this app. EVE clients are left alone by default (a trimmed background client stutters for a moment when you switch to it); untick that to include them. A list lets you add programs that must never be trimmed.",
            "Trimming asks Windows to take back memory a program isn't actively using (the program gets it back on demand), and works for your own programs without administrator rights. Clearing the system cache flushes Windows' file cache and needs administrator rights: without them those controls are greyed out, and a Restart as administrator button restarts the app with them (Windows asks first).",
            "The log only records counts and sizes, never program names."
        }),
        new("0.12.0", "Lighter on resources", new[]
        {
            "The keyboard hook is now only installed when a hotkey really needs it. Plain-key hotkeys (like ` for Next client) are delivered by Windows itself, so with those the app no longer sees every key you press in every program, and typing anywhere is no longer routed through it. The mouse hook is likewise only installed for mouse hotkeys, and neither is installed while hotkeys are suspended.",
            "Title changes are now watched only inside the EVE client processes. Before, the app was told about every name change of every window and control on the PC (browser tabs, the clock, chat apps), which kept it waking up all the time.",
            "Detailed logging costs nothing while it is off: the text of each log line is no longer built, and the window lookups inside it no longer run, on every focus change and every switch.",
            "preview.log no longer gets a line on every successful hotkey press (holding the key wrote about 30 lines a second): it now records failures and problems. Successful switches are in the detailed log.",
            "The client scan skips almost every window without making a string for it, does nothing more when no client has opened, closed or changed, and slows to every 3 seconds while no EVE client is open.",
            "Log monitoring reads the EVE log folders much more cheaply (only recent files are turned into objects, so years of old game logs no longer cost anything), and the log regexes are no longer compiled at start-up.",
            "Memory is handed back to Windows ten seconds after start-up and after the Configuration window is closed. The list of installed fonts is only built when the font box is first opened, and the runtime no longer starts a background garbage-collection thread.",
            "Profile and settings files are written to a temporary file and swapped in, so an interrupted save can't leave a half-written file."
        }),
        new("0.11.3", "Collapsible sections", new[]
        {
            "Every section in the Configuration window (the cards with an orange heading) can now be collapsed: click its heading, or press Space on it, to fold it up or open it again. A small arrow on the right shows which way it is.",
            "All sections start open. A section opens by itself when a search matches it, or when a setting in it needs fixing, so nothing you're looking for is hidden."
        }),
        new("0.11.2", "Build fix for 0.11.1", new[]
        {
            "Fixed: 0.11.1 didn't build (a number type mismatch in the client-order code). Nothing else changed: everything in 0.11.1 is in this version."
        }),
        new("0.11.1", "Positions follow the client order; Configuration no longer always on top", new[]
        {
            "Positions saved by client now follow the client order. With Saved or Manual order, a client's numbered place belongs to its character: if you close the 3rd client, the ones after it stay where they are and place 3 stays empty, and when it reopens it takes place 3 again with its thumbnail position. The client hotkeys count the same places, so \"Client 3\" is the client in place 3.",
            "With positions saved by client and Automatic order, a client you close and reopen goes to the end and the ones after it move up a place. Settings > Hotkeys > Client order now says so, and points to Saved or Manual.",
            "The Configuration window is no longer always on top: other windows can come in front of it. It now has a taskbar button, and clicking Configuration in the tray menu brings it back to the front. The dialogs it opens follow the same rule.",
            "Thumbnails that are set to \"always on top\" now sit over the Configuration window where they overlap, like over any other window."
        }),
        new("0.11.0", "Groups for client hotkeys and thumbnails, selectable text", new[]
        {
            "Client hotkeys (Settings > Hotkeys) now show just the first line, \"Client 1\". Press + to add a line for another client (and - to take the last one off).",
            "Client hotkeys come in groups: keep several sets, and pick the group in use from the drop-down. New, Rename and Delete manage the groups. Your existing client hotkeys become the \"Default\" group.",
            "The characters that get a thumbnail (Settings > Thumbnails) use the same design: groups, one line per character (tick it to show its thumbnail), and + to add another line. The group in use decides which thumbnails show; tick \"Also show thumbnails for logged-in characters that aren't in this group\" to show the others too. Your existing character list becomes the \"Default\" group, and Add selected under Find running clients adds to the group in use.",
            "The red X on thumbnails that the next / previous hotkeys skip is no longer an option: it is always shown, and the Skipped clients card just says so.",
            "Text in the Configuration window can now be selected and copied (descriptions, notes, the folder paths, the status line and the version). The labels next to tick boxes and radio buttons still toggle when clicked, so they can't be selected."
        }),
        new("0.10.2", "Build fix for 0.10.1", new[]
        {
            "Fixed: 0.10.1 didn't build (an out parameter was used inside a local function in the client-order code). Nothing else changed: everything in 0.10.1 is in this version."
        }),
        new("0.10.1", "Reset order, private logs, tray profile switch", new[]
        {
            "New: Reset order (Settings > Hotkeys > Client order, in Saved mode). It clears the saved order the next time all your EVE clients are closed (or none are open), then the list builds up again in the order the clients are opened, like the first time. You can cancel it before it happens.",
            "The saved order now keeps the opening order while it builds: if the 3rd client you opened logs in first, it still goes 3rd.",
            "Private logs: debug.log and preview.log no longer contain anything personal, so a log can be sent to someone. Character and profile names show as a short code (for example Pilot-3FA9C1); the same name always gives the same code on this PC, but the name can't be worked out from it. The title of any window that isn't an EVE client is never written (browser tabs, documents), and your Windows user name, PC name, folder paths, e-mail addresses and game-log file names (they hold the character ID) are removed.",
            "Logs written by older versions did contain names, so they are deleted once, the first time this version starts.",
            "The tray Profiles menu now switches profile even while the Configuration window is open, and the window shows the new profile straight away."
        }),
        new("0.10.0", "Client order, tray profiles and per-run debug logs", new[]
        {
            "New: CLIENT ORDER (Settings > Hotkeys). Automatic is the order the clients were opened, as before. Saved remembers the order, so closing and reopening a client puts it back in the same place; new characters are added to the end by themselves. Manual lets you set the order with the arrows, and a new character shows up unticked and only counts once you tick it.",
            "A client still on the login screen takes the first free place in the saved or manual list, so it is already in position when the character logs in.",
            "Next / Previous, the numbered thumbnail positions and the client hotkeys all follow the client order. The order is saved per profile.",
            "New: the tray menu has a Profiles menu. Pick a profile to switch to it; the one in use is ticked.",
            "New: the tray icon is kept on the taskbar instead of the hidden-icons arrow (Windows 11). Settings > General > Tray icon turns this off, and has a shortcut to Windows' own taskbar settings.",
            "New: Settings > Diagnostics > Logs can write one continuous debug.log, or start a new debug-DATE_TIME.log every time detailed logging is turned on (the newest 20 are kept). Open debug log opens the newest."
        }),
        new("0.9.9", "Steady highlight while holding the hotkey", new[]
        {
            "Fixed: while holding Next / Previous, the active-client highlight could briefly jump back to a client you had already passed. Windows reports focus changes a little late when they come this fast, and those late reports were being treated as current.",
            "For 250 ms after each switch, late focus reports from earlier clients are now ignored. The client scan still re-checks the real front window every 1.5 seconds, so a genuine click on another client is picked up straight after."
        }),
        new("0.9.8", "Fast held-key cycling", new[]
        {
            "Holding the Next or Previous hotkey now repeats continuously at the Windows keyboard repeat rate. Releasing the key stops it.",
            "Removed the no-repeat flag from Windows-registered hotkeys. A normal tap still switches exactly once.",
            "An accepted direct switch now returns immediately instead of waiting up to 60 ms for the foreground event; the existing pending-switch tracking keeps the cycle order correct while Windows finishes each switch."
        }),
        new("0.9.7", "Hotkey switching allowed by Windows", new[]
        {
            "Fixed: switching from the last client back to the first with the Next hotkey kept bouncing back, so the hotkey looked stuck.",
            "Cause (found with the debug log): Windows only lets a program bring a window to the front if it received the last keypress. The hotkey key was swallowed before it reached the app, so Windows refused every switch and it had to be forced, which didn't always stick.",
            "A hotkey that is a plain key (optionally with Ctrl, Alt, Shift or Win) is now registered with Windows, so every switch is allowed straight away. Mouse buttons, key chords, left/right-only modifiers and modifier-only hotkeys still use the old method, as do all hotkeys while \"pass the key through\" or \"only while EVE is in front\" is on.",
            "Switching no longer freezes keyboard and mouse input for up to 0.2 seconds while it waits for Windows."
        }),
        new("0.9.6", "Hotkey switching lands on the right client", new[]
        {
            "Fixed: the Next / Previous client hotkey could get stuck on one client, or jump back to the client you had just left.",
            "Cause (found with the debug log): a switch takes a few milliseconds to land, and the app was reading the window in front before Windows had updated it. That stale answer was then used as \"where you are\", so the next press picked the same client again, and the client scan put the old client back as active every 1.5 seconds.",
            "Now the client a switch is heading to counts as the active one until Windows confirms it (or 400 ms pass). Late focus reports from the client you left are ignored, and the client scan no longer overwrites a switch in progress.",
            "One press = one switch: if Windows refuses a switch, the hotkey stops there instead of trying every other client.",
            "Auto-minimise never minimises the client a switch is heading to, and \"Hotkeys only work while EVE is in front\" keeps working during a switch."
        }),
        new("0.9.5", "Smooth hotkey switching", new[]
        {
            "Fixed: the Next / Previous client hotkey flashed through every other client and landed on the wrong one.",
            "Cause (found with the debug log): Windows accepts the switch but for a few milliseconds nothing is in front. The app read that gap as a refusal and tried the next client, then the next, so every client got activated in turn.",
            "Now an accepted switch counts as done (the app waits up to 40 ms for Windows to finish), so one press makes exactly one switch and no Alt keypress is sent to EVE."
        }),
        new("0.9.4", "Detailed (admin) logging", new[]
        {
            "New: Detailed Logging. Turn it on from the tray menu or Settings > Diagnostics > Logs, reproduce a problem, then use Open Debug Log.",
            "debug.log records every step with millisecond timestamps: each hotkey press and how long it took to run, each focus attempt and which method Windows accepted (including when an Alt tap or the last-resort switch was needed), every minimise, restore and maximise sent to a client and why, thumbnails shown, hidden, created and closed and why, every focus change Windows reports, the client list when it changes, and the settings that affect switching.",
            "It is written by a background thread, so it doesn't slow switching down, and it rolls over to debug.old.log at about 4 MB. preview.log is unchanged."
        }),
        new("0.9.3", "Website link", new[]
        {
            "\"by AUTZ IronWorks Â· From planets to profit\" in the Settings sidebar and on the About page is now a link to the AUTZ IronWorks website.",
            "The tray menu item is now just \"Configuration\".",
            "The update script also finds a versions folder inside Documents\\IronWorks, and it no longer keeps a second copy of itself in the versions folder (run it from the repo)."
        }),
        new("0.9.2", "Update script finds its own folders", new[]
        {
            "The update script now finds your repo and your versions folder by itself, so renaming either folder no longer breaks it.",
            "It keeps a copy of itself in the versions folder, so you can run it from there. Everything from 0.9.1 is included."
        }),
        new("0.9.1", "Name tweaks", new[]
        {
            "The name in the Settings sidebar and the what's-new window now uses the IronWorks casing: EVE-IronWorks.",
            "The tray icon tooltip now says EVE-IW-Preview."
        }),
        new("0.9.0", "Now EVE-IW-Preview (EVE-IronWorks-Preview)", new[]
        {
            "Renamed. EVE-IW-Preview is the short name for the exe file, the GitHub repository, Discord and Reddit. EVE-IronWorks-Preview is the name shown in the windows, the tray and About. Made by AUTZ IronWorks: from planets to profit.",
            "The program file is now EVE-IW-Preview.exe.",
            "Your settings move to %LOCALAPPDATA%\\EVE-IronWorks-Preview. Profiles, positions and hotkeys are copied across automatically the first time it starts, and the old folder is left untouched.",
            "The Run on Startup entry is renamed too. If you had it on, it carries over.",
            "The tray menu is down to five items: Configuration (Settings), Hide All Thumbnails, Refresh Clients, Reset All Positions and Exit. Lock positions and Recover off-screen thumbnails are in Settings > General."
        }),
        new("0.8.3", "Build fix", new[]
        {
            "Fixes the build error in 0.8.2 (a helper class was defined twice).",
            "Everything from 0.8.0, 0.8.1 and 0.8.2 is included."
        }),
        new("0.8.2", "Build fix", new[]
        {
            "Fixes the build errors in 0.8.0 and 0.8.1: missing references in the start-up code, the log monitor and the notification sounds, and the folder-picker helper used by the Log Monitoring page.",
            "Everything from 0.8.0 and 0.8.1 is included."
        }),
        new("0.8.1", "Settings now match the AUTZ IronWorks website", new[]
        {
            "The Settings window uses the exact colours of the AUTZ IronWorks website: near-black background, charcoal panels and the orange accent.",
            "Same menu style as the website: a sidebar with the logo and name on the left, orange-tinted active item with an orange bar, and a page heading with the profile controls on the right.",
            "Buttons, boxes, tick boxes, drop-downs and sliders follow the website's styling, with sharper corners.",
            "Your saved colours that were still the old defaults move over to the new palette automatically."
        }),
        new("0.8.0", "Fresh look, full hotkeys, alerts and log monitoring", new[]
        {
            "New Settings window with a left menu, section cards and a profile bar.",
            "Apply puts your changes into the running session without saving. Save writes them to the config file. Reset puts the profile's settings back to their defaults. Close asks before throwing away unsaved changes.",
            "Hotkeys have no restrictions now: any key, any mouse button (including the side buttons and the wheel), single keys with no modifier, and combinations. Right-side modifiers are saved as RCtrl, RAlt and so on.",
            "Assign a hotkey to a client slot (client 1, client 2, ...) as well as to a character.",
            "Cycle with the next / previous hotkeys never gets stuck: a client that is not responding is skipped, and nothing waits on it any more.",
            "Right-click on thumbnails is gone. Thumbnails are now non-activating overlays, so clicking one no longer pulls the taskbar up over a full-screen game.",
            "Thumbnails page: add characters by hand, remove them, or scan the running clients and pick which ones to add.",
            "Thumbnails of clients that aren't logged in can't be clicked (turn it back on in Settings > General).",
            "Auto-minimise now also covers clients that haven't been in front yet, so a fresh start-up no longer wastes resources. Optionally keep the last-used client visible.",
            "Per client: maximise or restore it when you switch to it, and minimise it when you switch away.",
            "Recover thumbnails that ended up off-screen (Settings > General, or the tray menu). It also runs by itself when your monitors change.",
            "Thumbnails can be turned off 'always on top'.",
            "Run on Startup.",
            "Notifications: flash the thumbnail border, a Windows notification, a sound, or a spoken alert.",
            "Log Monitoring (reads the EVE logs): alerts for incoming damage, mining that has gone quiet, and your own keywords, plus the solar system shown on each thumbnail.",
            "This what's-new window, and a way to open the full changelog from Settings > About."
        }),
        new("0.7.0", "New Settings window", new[]
        {
            "New Settings window with a profile bar, section cards, sliders and a search box.",
            "Pages: General, Appearance, Thumbnails, Characters, Hotkeys, Clients and About."
        }),
        new("0.6.0", "Profiles, borders and hiding", new[]
        {
            "Profiles: save whole layouts and switch with hotkeys.",
            "Positions saved by character or by client, and thumbnails kept the same size if you want.",
            "Borders for the active and inactive clients, text label styling, hide rules and auto-minimise.",
            "Skip chosen clients in next / previous, and new characters take over their login screen's position."
        }),
        new("0.5.0", "Tray-only app", new[]
        {
            "No control window and no taskbar button: tray icon only.",
            "Settings split into pages; choose which clients get a thumbnail.",
            "Shift + left-drag moves a thumbnail; thumbnails can be made unclickable; default opacity 60%."
        }),
        new("0.4.x", "Icons and resizing", new[]
        {
            "AUTZ IronWorks emblem for the exe, tray and windows.",
            "Drag the bottom-right corner to resize, colour pickers, and updates that never touch a running app."
        })
    };
}
