namespace AUTZ.IronWorks.Preview.Services;

internal sealed record ChangelogEntry(string Version, string Title, string[] Items);

/// <summary>What changed in each version. The newest entry comes first and is what the start-up pop-up shows.</summary>
internal static class Changelog
{
    public static readonly ChangelogEntry[] Entries =
    {
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


