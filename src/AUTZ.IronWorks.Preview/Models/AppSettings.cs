// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Text.Json;

namespace AUTZ.IronWorks.Preview.Models;

/// <summary>Per-character overrides. Zero / empty values mean "use the default".</summary>
public sealed class CharacterSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Label { get; set; } = "";
    public string LabelColourHex { get; set; } = "";
    /// <summary>Text size of this character's label. 0 = use the default size.</summary>
    public int LabelSize { get; set; }
    /// <summary>How solid this character's label text is (10-100). 0 = use the default.</summary>
    public int LabelOpacityPercent { get; set; }
    public string Hotkey { get; set; } = "";

    /// <summary>True = no thumbnail is shown for this character.</summary>
    public bool Hidden { get; set; }

    /// <summary>True = the next / previous client hotkeys skip this character.</summary>
    public bool ExcludeFromCycling { get; set; }

    /// <summary>True = auto-minimise leaves this character's client alone.</summary>
    public bool NeverMinimise { get; set; }

    /// <summary>What happens to this client's window when you switch to it: None, Maximise or Restore.</summary>
    public string ActivateAction { get; set; } = "None";

    /// <summary>True = this client is minimised as soon as you switch away from it.</summary>
    public bool MinimiseOnLeave { get; set; }
}

/// <summary>Optional switching overrides for a numbered client. Null inherits the character setting.</summary>
public sealed class ClientSwitchSettings
{
    public string? ActivateAction { get; set; }
    public bool? MinimiseOnLeave { get; set; }
}

/// <summary>A named set of hotkeys and switching overrides for numbered clients.</summary>
public sealed class SlotHotkeyGroup
{
    public string Name { get; set; } = "Default";
    /// <summary>"1" = hotkey for the 1st client, "2" = the 2nd, and so on.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();
    public Dictionary<string, ClientSwitchSettings> Switching { get; set; } = new();
    /// <summary>How many client lines the group shows (at least 1).</summary>
    public int Rows { get; set; } = 1;
}

/// <summary>One thumbnail's saved spot and size inside a saved layout.</summary>
public sealed class LayoutBox
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>A saved arrangement: the client order and where every thumbnail sits. Load one to swap arrangements.</summary>
public sealed class SavedLayout
{
    public string Name { get; set; } = "Layout";
    public string ClientOrderMode { get; set; } = AppSettings.OrderAutomatic;
    public List<string> ClientOrder { get; set; } = new();
    public Dictionary<string, int> SavedAccountSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string PositionMode { get; set; } = AppSettings.ModeCharacter;
    /// <summary>Settings key (a character, or "[client 2]" / "[login 1]") -> its spot and size.</summary>
    public Dictionary<string, LayoutBox> Boxes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A named list of characters. The group in use decides which characters get a thumbnail.</summary>
public sealed class ThumbnailGroup
{
    public string Name { get; set; } = "Default";
    public List<string> Characters { get; set; } = new();
    /// <summary>True = logged-in characters that aren't in this group still get a thumbnail.</summary>
    public bool ShowOthers { get; set; } = false;
}

/// <summary>Everything in one profile (a "layout"): appearance, behaviour, positions, per-character settings.</summary>
public sealed class AppSettings
{
    public const string ModeCharacter = "Character";
    public const string ModeClient = "Client";

    public const string OrderAutomatic = "Automatic";
    public const string OrderSaved = "Saved";
    public const string OrderManual = "Manual";

    // ----- general -----
    /// <summary>"Character": a character's thumbnail follows them. "Client": the Nth client to open always uses the Nth position.</summary>
    public string PositionMode { get; set; } = ModeCharacter;
    public int PreviewWidth { get; set; } = 280;
    /// <summary>0 = automatic 16:9 from the width.</summary>
    public int PreviewHeight { get; set; } = 0;
    /// <summary>True = every thumbnail is the same size (resize one and they all follow).</summary>
    public bool SameSizeForAll { get; set; } = false;
    public int RefreshMilliseconds { get; set; } = 1500;
    public bool LockPositions { get; set; } = false;
    /// <summary>Character thumbnails (logged-in clients): false = clicking one does nothing, so you can only switch with the hotkeys.</summary>
    public bool ThumbnailsClickable { get; set; } = true;
    /// <summary>Login screen thumbnails (clients that aren't logged in yet): false = clicking one does nothing. Independent of the character option.</summary>
    public bool LoginScreensClickable { get; set; } = false;
    /// <summary>Set once old profiles have been moved to the two independent click options (login used to depend on the character option).</summary>
    public bool ClickOptionsSplit { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool SnapEnabled { get; set; } = true;
    public int SnapDistance { get; set; } = 15;
    /// <summary>A character with no saved spot takes the position (and size) of the login screen it replaces.</summary>
    public bool NewCharactersInheritPosition { get; set; } = true;
    /// <summary>Where thumbnails with no saved spot go: "Down" = down the left edge, "Across" = along the top.</summary>
    public string StackDirection { get; set; } = "Down";
    /// <summary>Saved arrangements of client order and thumbnail positions.</summary>
    public List<SavedLayout> Layouts { get; set; } = new();

    // ----- appearance -----
    /// <summary>How solid the thumbnails are (10-100). 60 = a bit see-through.</summary>
    public int ThumbnailOpacityPercent { get; set; } = 60;
    public bool ShowLabels { get; set; } = true;
    public string DefaultLabelColourHex { get; set; } = "#FFFFFF";
    public string LabelFontName { get; set; } = "Segoe UI";
    public int LabelFontSize { get; set; } = 13;
    /// <summary>How solid the label text is (10-100). (The box behind it has its own opacity.)</summary>
    public int LabelOpacityPercent { get; set; } = 60;
    public bool LabelBold { get; set; } = true;
    public string LabelBackgroundHex { get; set; } = "#0C0C0E";
    public int LabelBackgroundOpacityPercent { get; set; } = 60;
    /// <summary>TopLeft, TopCenter, TopRight, LeftCenter, Center, RightCenter, BottomLeft, BottomCenter, BottomRight.</summary>
    public string LabelPosition { get; set; } = "TopLeft";
    public int LabelOffsetX { get; set; } = 0;
    public int LabelOffsetY { get; set; } = 0;

    // ----- borders (Solid, Dashed, Dotted, DashDot, Double, CornerBrackets) -----
    public bool HighlightActive { get; set; } = false;
    public string ActiveBorderHex { get; set; } = "#F97316";
    public int ActiveBorderWidth { get; set; } = 3;
    public string ActiveBorderStyle { get; set; } = "Solid";
    public bool ShowInactiveBorder { get; set; } = false;
    public string InactiveBorderHex { get; set; } = "#6F6F7D";
    public int InactiveBorderWidth { get; set; } = 1;
    public string InactiveBorderStyle { get; set; } = "Solid";

    // ----- which thumbnails are shown -----
    /// <summary>Older versions: the characters listed on the Thumbnails page. Moved into the "Default" thumbnail group; kept only so old profiles still load.</summary>
    public List<string> Roster { get; set; } = new();
    /// <summary>The thumbnail groups (lists of characters). The one named by ActiveThumbnailGroup is in use.</summary>
    public List<ThumbnailGroup> ThumbnailGroups { get; set; } = new();
    public string ActiveThumbnailGroup { get; set; } = "Default";
    /// <summary>False until the list has been filled in from the characters already known.</summary>
    public bool RosterInitialised { get; set; } = false;
    /// <summary>Older versions: now a setting of each thumbnail group (ShowOthers). Only read when an old profile is moved over.</summary>
    public bool ShowUnlistedCharacters { get; set; } = true;
    /// <summary>Show a thumbnail for clients that are still on the login screen.</summary>
    public bool ShowLoginScreens { get; set; } = true;
    /// <summary>Hide the thumbnail of the client you're currently in.</summary>
    public bool HideActiveThumbnail { get; set; } = false;
    /// <summary>Hide every thumbnail while something other than EVE has focus.</summary>
    public bool HideWhenEveNotFocused { get; set; } = false;
    public int HideDelayMs { get; set; } = 500;

    // ----- hotkeys and cycling -----
    public string CycleForwardHotkey { get; set; } = "Ctrl+Alt+Right";
    public string CycleBackHotkey { get; set; } = "Ctrl+Alt+Left";
    /// <summary>Next / previous login screen. These only go to clients that aren't logged in yet.</summary>
    public string CycleLoginForwardHotkey { get; set; } = "";
    public string CycleLoginBackHotkey { get; set; } = "";
    public bool CycleIncludeLoginScreens { get; set; } = true;
    /// <summary>Older versions: the red X on skipped thumbnails was an option. It is always shown now; this value is ignored.</summary>
    public bool ShowExcludedOverlay { get; set; } = true;
    /// <summary>"Automatic" = opening order. "Saved" = account signatures return to the slots captured by Save current order.
    /// "Manual" = the character order selected in Configuration.</summary>
    public string ClientOrderMode { get; set; } = OrderAutomatic;
    /// <summary>Characters in client order, for "Saved" and "Manual". A character that isn't in the list sits after the ones that are.</summary>
    public List<string> ClientOrder { get; set; } = new();
    /// <summary>Saved order only: salted launcher account signature -> numbered client slot. This identifies a login screen before a character is selected.</summary>
    public Dictionary<string, int> SavedAccountSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>True after "Reset order": the saved order is cleared the next time no EVE client is open, then builds again in opening order.</summary>
    public bool ClientOrderResetPending { get; set; } = false;
    /// <summary>Older versions: the client hotkeys. Moved into the "Default" client-hotkey group; kept only so old profiles still load.</summary>
    public Dictionary<string, string> ClientSlotHotkeys { get; set; } = new();
    /// <summary>The client-hotkey groups. The one named by ActiveClientHotkeyGroup is in use.</summary>
    public List<SlotHotkeyGroup> ClientHotkeyGroups { get; set; } = new();
    public string ActiveClientHotkeyGroup { get; set; } = "Default";
    /// <summary>Hotkeys only work while an EVE client is the window in front.</summary>
    public bool HotkeysOnlyInEve { get; set; } = false;
    /// <summary>The key or button still reaches the program as well (it is not swallowed).</summary>
    public bool HotkeyPassThrough { get; set; } = false;
    /// <summary>True = the hotkeys keep working while the Configuration window is open (they still pause while a hotkey box is recording a key).</summary>
    public bool HotkeysInConfig { get; set; } = false;

    // ----- client behaviour -----
    public bool AutoMinimiseInactive { get; set; } = false;
    public int AutoMinimiseDelaySeconds { get; set; } = 2;
    /// <summary>When no EVE client is in front, keep the one you used last un-minimised.</summary>
    public bool AutoMinimiseKeepLastActive { get; set; } = true;
    /// <summary>Also minimise clients that haven't been in front yet (loading, or on the login screen).</summary>
    public bool AutoMinimiseOnStart { get; set; } = true;

    // ----- notifications -----
    public bool NotificationsEnabled { get; set; } = false;
    public bool NotifySkipActiveClient { get; set; } = true;
    /// <summary>The same alert on several clients inside this many seconds plays one sound. 0 = every alert plays its sound.</summary>
    public double AlertSoundGapSeconds { get; set; } = 1.5;
    /// <summary>Older versions: one cooldown for every alert. Copied into AlertCooldowns once if it had been changed.</summary>
    public int NotifyCooldownSeconds { get; set; } = 20;
    /// <summary>Alert kind -> its own cooldown in seconds (0 = every time). Missing = the default for that alert.</summary>
    public Dictionary<string, int> AlertCooldowns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>True once the old single cooldown has been moved into AlertCooldowns.</summary>
    public bool AlertCooldownsMigrated { get; set; } = false;

    /// <summary>Default cooldown for each alert, in seconds.</summary>
    public static int DefaultCooldown(string kind) => kind switch
    {
        "attack" or "warp-scram" => 10,
        "warp-bubble" or "keyword" => 30,
        "decloak" or "cloak-failed" or "fleet-follow" or "regroup" => 5,
        _ => 0
    };

    /// <summary>The cooldown in use for one alert.</summary>
    public int CooldownFor(string kind) =>
        AlertCooldowns.TryGetValue(kind, out int seconds) ? Math.Clamp(seconds, 0, 3600) : DefaultCooldown(kind);
    public bool NotifyFlashBorder { get; set; } = true;
    public string NotifyBorderHex { get; set; } = "#F87171";
    public int NotifyBorderSeconds { get; set; } = 6;
    public bool NotifyToast { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    /// <summary>The main alert sound: "iw:Chime" (built in), "win:Exclamation" (Windows), "custom:file.mp3" (added by you).
    /// Older profiles hold a plain Windows sound name, or "Custom" with NotifySoundFile.</summary>
    public string NotifySoundName { get; set; } = "iw:Chime";
    /// <summary>Older versions: the custom .wav path. Copied into the sounds folder when the profile is opened.</summary>
    public string NotifySoundFile { get; set; } = "";
    /// <summary>Volume of the main alert sound, 0 to 100.</summary>
    public int NotifyVolume { get; set; } = 80;
    /// <summary>Alert kind -> its own sound. Missing or "" = the main sound.</summary>
    public Dictionary<string, string> AlertSounds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Alert kind -> its own volume (0 to 100). Missing = the main volume.</summary>
    public Dictionary<string, int> AlertVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool NotifySpeak { get; set; } = false;

    // ----- log monitoring -----
    public bool LogMonitoringEnabled { get; set; } = false;
    /// <summary>Empty = Documents\EVE\logs\Gamelogs.</summary>
    public string GameLogFolder { get; set; } = "";
    /// <summary>Empty = Documents\EVE\logs\Chatlogs.</summary>
    public string ChatLogFolder { get; set; } = "";
    public int LogPollMilliseconds { get; set; } = 1000;
    public bool LogWatchAttack { get; set; } = true;
    public bool LogWatchMiningIdle { get; set; } = false;
    public bool LogWatchCrystalBroke { get; set; } = false;
    public bool LogWatchFleetFollow { get; set; } = false;
    public bool LogWatchMiningCompression { get; set; } = false;
    public bool LogWatchConversationInvite { get; set; } = false;
    public bool LogWatchDecloak { get; set; } = false;
    public bool LogWatchFleetRegroup { get; set; } = false;
    public bool LogWatchWarpScram { get; set; } = false;
    public bool LogWatchWarpBubble { get; set; } = false;
    public bool LogWatchCloakFailed { get; set; } = false;
    public bool LogWatchFleetInvite { get; set; } = false;
    public int LogMiningIdleSeconds { get; set; } = 60;
    /// <summary>Mining stopped also alerts when you switch a module off yourself (the game writes nothing for that, so it waits for silence).</summary>
    public bool LogMiningManualStop { get; set; } = true;
    /// <summary>One phrase per line. A game-log line containing one of them raises an alert.</summary>
    public string LogKeywords { get; set; } = "warp scramble\nwarp disrupt";
    /// <summary>Show the solar system next to the name on each thumbnail (read from the Local chat log).</summary>
    public bool ShowSystemName { get; set; } = false;

    /// <summary>Which character was last logged in on the Nth client to open ("1", "2", ...).</summary>
    public Dictionary<string, string> SlotCharacters { get; set; } = new();

    /// <summary>Per-character settings. Keys that start with "[" ("[client 2]", "[login 1]") only hold a saved position and size.</summary>
    public Dictionary<string, CharacterSettings> Characters { get; set; } = new();

    public static bool IsCharacterKey(string key) => !key.StartsWith('[');

    public CharacterSettings? TryGetCharacter(string name) =>
        Characters.TryGetValue(name, out var c) ? c : null;

    public CharacterSettings GetOrAddCharacter(string name)
    {
        if (!Characters.TryGetValue(name, out var c))
        {
            c = new CharacterSettings();
            Characters[name] = c;
        }
        return c;
    }

    // ---------- groups ----------

    public SlotHotkeyGroup? FindSlotGroup(string? name) =>
        ClientHotkeyGroups.FirstOrDefault(g => g.Name.Equals(name ?? "", StringComparison.OrdinalIgnoreCase));

    public ThumbnailGroup? FindThumbGroup(string? name) =>
        ThumbnailGroups.FirstOrDefault(g => g.Name.Equals(name ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>The client hotkeys of the group in use.</summary>
    private SlotHotkeyGroup? ActiveSlotGroup() =>
        FindSlotGroup(ActiveClientHotkeyGroup) ?? ClientHotkeyGroups.FirstOrDefault();

    public IReadOnlyDictionary<string, string> ActiveSlotHotkeys() => ActiveSlotGroup()?.Hotkeys ?? ClientSlotHotkeys;

    public ClientSwitchSettings? TryGetClientSwitchSettings(int slot)
    {
        var group = ActiveSlotGroup();
        return group is not null && group.Switching.TryGetValue(slot.ToString(), out var settings) ? settings : null;
    }

    /// <summary>The thumbnail group in use (null only for a profile that hasn't been through EnsureGroups yet).</summary>
    public ThumbnailGroup? ActiveThumbGroup() =>
        FindThumbGroup(ActiveThumbnailGroup) ?? ThumbnailGroups.FirstOrDefault();

    /// <summary>Every character that is in any thumbnail group.</summary>
    public IEnumerable<string> AllGroupCharacters() =>
        ThumbnailGroups.SelectMany(g => g.Characters).Concat(Roster);

    /// <summary>Makes sure there's at least one group of each kind. Old profiles get a "Default" group from what they had.</summary>
    public void EnsureGroups()
    {
        if (ClientHotkeyGroups.Count == 0)
        {
            var g = new SlotHotkeyGroup { Name = "Default" };
            foreach (var pair in ClientSlotHotkeys)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value)) g.Hotkeys[pair.Key] = pair.Value;
            }
            int highest = 0;
            foreach (string key in g.Hotkeys.Keys)
            {
                if (int.TryParse(key, out int n) && n > highest) highest = n;
            }
            g.Rows = Math.Max(1, highest);
            ClientHotkeyGroups.Add(g);
            ClientSlotHotkeys = new Dictionary<string, string>();
            ActiveClientHotkeyGroup = g.Name;
        }
        else if (FindSlotGroup(ActiveClientHotkeyGroup) is null)
        {
            ActiveClientHotkeyGroup = ClientHotkeyGroups[0].Name;
        }

        if (ThumbnailGroups.Count == 0)
        {
            var g = new ThumbnailGroup { Name = "Default", ShowOthers = ShowUnlistedCharacters };
            foreach (string name in Roster)
            {
                string trimmed = (name ?? "").Trim();
                if (trimmed.Length > 0 && !g.Characters.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) g.Characters.Add(trimmed);
            }
            ThumbnailGroups.Add(g);
            Roster = new List<string>();
            ActiveThumbnailGroup = g.Name;
        }
        else if (FindThumbGroup(ActiveThumbnailGroup) is null)
        {
            ActiveThumbnailGroup = ThumbnailGroups[0].Name;
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();

    /// <summary>Copies every setting from another instance into this one (this object stays the same instance).</summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite) property.SetValue(this, property.GetValue(other));
        }
    }
}

/// <summary>The memory cleaner (Settings > Memory). App-wide, not per profile. Everything is off until the user switches it on.</summary>
public sealed class MemoryCleanerSettings
{
    public bool TrimEveryMinutes { get; set; } = false;
    public int TrimMinutes { get; set; } = 5;
    public bool ClearCacheEvery { get; set; } = false;
    public int CacheMinutes { get; set; } = 5;
    public bool TrimAboveUsage { get; set; } = false;
    public int TrimUsagePercent { get; set; } = 80;
    public bool ClearCacheAboveUsage { get; set; } = false;
    public int CacheUsagePercent { get; set; } = 80;
    /// <summary>True = EVE clients are never trimmed (a trimmed background client stutters for a moment when you switch to it).</summary>
    public bool SkipEveClients { get; set; } = true;
    /// <summary>True = EVE clients in the background are trimmed once they have been idle for EveIdleMinutes (never the one in front). Overrides SkipEveClients.</summary>
    public bool EveIdleTrim { get; set; } = false;
    public int EveIdleMinutes { get; set; } = 10;
    /// <summary>Programs using less than this many megabytes are not trimmed (0 = trim everything).</summary>
    public int MinTrimMb { get; set; } = 30;
    /// <summary>True = a program that goes over its own limit (ProgramLimits) is trimmed.</summary>
    public bool UseProgramLimits { get; set; } = false;
    /// <summary>One "name=megabytes" per line, for example chrome=1500.</summary>
    public string ProgramLimits { get; set; } = "";
    /// <summary>Program names (without .exe) that are never trimmed, separated by commas.</summary>
    public string ExcludedPrograms { get; set; } = "";
    public bool ShowNotification { get; set; } = false;

    public bool AnyAutomatic => TrimEveryMinutes || ClearCacheEvery || TrimAboveUsage || ClearCacheAboveUsage ||
                                (UseProgramLimits && !string.IsNullOrWhiteSpace(ProgramLimits));

    public MemoryCleanerSettings Clone() =>
        JsonSerializer.Deserialize<MemoryCleanerSettings>(JsonSerializer.Serialize(this)) ?? new MemoryCleanerSettings();
}

/// <summary>Performance options (Settings > Performance). App-wide. Everything is off until switched on.</summary>
public sealed class PerformanceSettings
{
    /// <summary>The tray switch: false turns every option below off at once, without changing them.</summary>
    public bool Enabled { get; set; } = true;
    public bool HighPerformancePlan { get; set; } = false;
    public bool RaiseFrontClient { get; set; } = false;
    public bool LowerBackgroundClients { get; set; } = false;
    public bool EfficiencyBackground { get; set; } = false;
    /// <summary>The power plan to go back to (set while High Performance is in use, cleared once it is put back).</summary>
    public string RestorePowerPlan { get; set; } = "";
    /// <summary>True while client priorities may have been changed (so a crash is cleaned up on the next start).</summary>
    public bool PrioritiesChanged { get; set; } = false;

    public bool AnyOn => Enabled && (HighPerformancePlan || RaiseFrontClient || LowerBackgroundClients || EfficiencyBackground);

    public PerformanceSettings Clone() =>
        JsonSerializer.Deserialize<PerformanceSettings>(JsonSerializer.Serialize(this)) ?? new PerformanceSettings();
}

/// <summary>App-wide state that isn't part of a profile: active profile, profile hotkeys, startup and what's-new options.</summary>
public sealed class AppState
{
    public string ActiveProfile { get; set; } = "Default";
    public string NextProfileHotkey { get; set; } = "";
    public string PreviousProfileHotkey { get; set; } = "";
    public Dictionary<string, string> ProfileHotkeys { get; set; } = new();
    public bool RunOnStartup { get; set; } = false;
    /// <summary>Master switch for the client-switching half of the app. Memory cleaning continues when false.</summary>
    public bool SwitcherEnabled { get; set; } = true;
    /// <summary>Master switch for automatic and manual memory-cleaner actions. The switcher continues when false.</summary>
    public bool MemoryCleanerEnabled { get; set; } = true;
    /// <summary>"Update" = when the version changes, "Always" = every start, "Never".</summary>
    public string WhatsNewMode { get; set; } = "Update";
    public string LastSeenVersion { get; set; } = "";
    /// <summary>Detailed (admin) logging to debug.log. App-wide, not per profile.</summary>
    public bool DebugLogging { get; set; } = false;
    /// <summary>"Continuous" = one debug.log that carries on. "PerSession" = a new debug-DATE_TIME.log every time detailed logging is turned on.</summary>
    public string DebugLogMode { get; set; } = "Continuous";
    /// <summary>Keep the tray icon on the main tray instead of the hidden-icons overflow (Windows 11).</summary>
    public bool PinTrayIcon { get; set; } = true;
    /// <summary>Download new versions in the background and install them when the app next starts.</summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>True = the app always starts as administrator (through a scheduled task, so Windows doesn't ask each time). Set up once with "Restart as administrator".</summary>
    public bool AlwaysAdmin { get; set; } = false;
    /// <summary>A random value mixed into the codes that replace names in the logs (so the codes can't be matched against a list of names).</summary>
    public string LogSalt { get; set; } = "";
    /// <summary>True once the logs written before names were removed from them have been deleted (done once).</summary>
    public bool LogsScrubbed { get; set; } = false;
    /// <summary>The memory cleaner (Settings > Memory).</summary>
    public MemoryCleanerSettings Memory { get; set; } = new();

    // ----- other hotkeys (app-wide) -----
    /// <summary>Pauses every other hotkey so the keys type normally, and turns them back on. Works while paused.</summary>
    public string SuspendHotkey { get; set; } = "";
    /// <summary>Next / previous Windows virtual desktop (the same as Ctrl+Win+Right / Left).</summary>
    public string NextDesktopHotkey { get; set; } = "";
    public string PreviousDesktopHotkey { get; set; } = "";

    // ----- switcher per client -----
    /// <summary>Accounts (or characters) the switcher leaves alone: no thumbnail, no cycling, no hotkeys.</summary>
    public List<string> DisabledClients { get; set; } = new();
    /// <summary>Client key -> the name last seen for it, so the menu can list clients that are closed.</summary>
    public Dictionary<string, string> ClientLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Performance options. All off until switched on.</summary>
    public PerformanceSettings Performance { get; set; } = new();

    public AppState Clone() =>
        JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(this)) ?? new AppState();
}
