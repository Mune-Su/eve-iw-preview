namespace AUTZ.IronWorks.Preview.Services;

internal sealed record ChangelogEntry(string Version, string Title, string[] Items);

/// <summary>What changed in each version. The newest entry comes first and is what the start-up pop-up shows.</summary>
internal static class Changelog
{
    public static readonly ChangelogEntry[] Entries =
    {
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
            "New: Settings > About > Diagnostics can write one continuous debug.log, or start a new debug-DATE_TIME.log every time detailed logging is turned on (the newest 20 are kept). Open debug log opens the newest."
        }),
        new("0.9.9", "Steady highlight while holding the hotkey", new[]
        {
            "Fixed: while holding Next / Previous, the active-client highlight could briefly jump back to a client you had already passed. Windows reports focus changes a little late when they come this fast, and those late reports were being treated as current.",
            "For 250 ms after each switch, late focus reports from earlier clients are now ignored. The client scan still re-checks the real front window every 1.5 seconds, so a genuine click on another client is picked up straight after."
        }),
        new("0.9.8", "Fast held-key cycling", new[]
        {
            "Holding the Next or Previous hotkey now repeats continuously at the Windows keyboard repeat rate, like EVE-APM, EVE-Maj and EVE-O Preview. Releasing the key stops it.",
            "Removed the no-repeat flag from Windows-registered hotkeys. A normal tap still switches exactly once.",
            "An accepted direct switch now returns immediately instead of waiting up to 60 ms for the foreground event; the existing pending-switch tracking keeps the cycle order correct while Windows finishes each switch."
        }),
        new("0.9.7", "Hotkey switching allowed by Windows", new[]
        {
            "Fixed: switching from the last client back to the first with the Next hotkey kept bouncing back, so the hotkey looked stuck.",
            "Cause (found with the debug log): Windows only lets a program bring a window to the front if it received the last keypress. The hotkey key was swallowed before it reached the app, so Windows refused every switch and it had to be forced, which didn't always stick.",
            "A hotkey that is a plain key (optionally with Ctrl, Alt, Shift or Win) is now registered with Windows, the same way EVE-O Preview does it, so every switch is allowed straight away. Mouse buttons, key chords, left/right-only modifiers and modifier-only hotkeys still use the old method, as do all hotkeys while \"pass the key through\" or \"only while EVE is in front\" is on.",
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
            "New: Detailed Logging. Turn it on from the tray menu or Settings > About > Diagnostics, reproduce a problem, then use Open Debug Log.",
            "debug.log records every step with millisecond timestamps: each hotkey press and how long it took to run, each focus attempt and which method Windows accepted (including when an Alt tap or the last-resort switch was needed), every minimise, restore and maximise sent to a client and why, thumbnails shown, hidden, created and closed and why, every focus change Windows reports, the client list when it changes, and the settings that affect switching.",
            "It is written by a background thread, so it doesn't slow switching down, and it rolls over to debug.old.log at about 4 MB. preview.log is unchanged."
        }),
        new("0.9.3", "Website link", new[]
        {
            "\"by AUTZ IronWorks · From planets to profit\" in the Settings sidebar and on the About page is now a link to the AUTZ IronWorks website.",
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
        new("0.7.0", "Settings in the EVE-Maj style", new[]
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




