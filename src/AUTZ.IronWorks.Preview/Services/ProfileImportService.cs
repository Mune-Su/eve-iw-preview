using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

public sealed record ProfileImportResult(AppSettings Settings, string Source, string SuggestedName, int Characters, int Positions, int Groups);

/// <summary>Reads native EVE-IW profiles and migration profiles made by EVE-APM Preview or EVE-Maj Preview.</summary>
public static class ProfileImportService
{
    public static bool TryImport(string path, out ProfileImportResult? result, out string error)
    {
        result = null;
        error = "";
        try
        {
            string text = File.ReadAllText(path);
            string name = ProfileStore.CleanName(Path.GetFileNameWithoutExtension(path));
            if (Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase))
            {
                var settings = ImportApm(text, out int positions, out int groups);
                result = Result(settings, "EVE-APM Preview", name, positions, groups);
                return true;
            }
            using var document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("app", out var app) && app.ValueKind == JsonValueKind.String &&
                app.GetString()!.Equals("eve-maj-preview", StringComparison.OrdinalIgnoreCase))
            {
                var settings = ImportMaj(root, out int positions, out int groups);
                result = Result(settings, "EVE-Maj Preview", name, positions, groups);
                return true;
            }
            if (root.TryGetProperty("PreviewWidth", out _))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(text) ?? new AppSettings();
                settings.EnsureGroups();
                result = Result(settings, AppInfo.Name, name,
                    settings.Characters.Values.Count(c => c.Left.HasValue && c.Top.HasValue), settings.ThumbnailGroups.Count);
                return true;
            }
            error = "That JSON file isn't an EVE-IW Preview or EVE-Maj Preview profile.";
        }
        catch (Exception ex) { error = "The profile couldn't be read: " + ex.Message; }
        return false;
    }

    private static ProfileImportResult Result(AppSettings s, string source, string name, int positions, int groups) =>
        new(s, source, name, s.AllGroupCharacters().Distinct(StringComparer.OrdinalIgnoreCase).Count(), positions, groups);

    private static AppSettings ImportApm(string text, out int positionCount, out int groupCount)
    {
        var ini = ParseIni(text);
        string Get(string section, string key, string fallback = "") => ini.TryGetValue(section, out var values) && values.TryGetValue(key, out string? value) ? value : fallback;
        bool Bool(string section, string key, bool fallback) => bool.TryParse(Get(section, key), out bool value) ? value : fallback;
        int Int(string section, string key, int fallback) => int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        var s = new AppSettings
        {
            PreviewWidth = Math.Clamp(Int("thumbnail", "width", 280), 120, 1200),
            PreviewHeight = Math.Clamp(Int("thumbnail", "height", 0), 0, 1200),
            ThumbnailOpacityPercent = Math.Clamp(Int("thumbnail", "opacity", 60), 10, 100),
            ShowLoginScreens = Bool("thumbnail", "showNotLoggedInClients", true),
            HighlightActive = Bool("ui", "highlightActiveWindow", false),
            ActiveBorderHex = Colour(Get("ui", "highlightColor"), "#F97316"),
            ActiveBorderWidth = Math.Clamp(Int("ui", "highlightBorderWidth", 3), 1, 20),
            ActiveBorderStyle = BorderStyle(Int("ui", "activeBorderStyle", 0)),
            ShowInactiveBorder = Bool("ui", "showInactiveBorders", false),
            InactiveBorderHex = Colour(Get("ui", "inactiveBorderColor"), "#6F6F7D"),
            InactiveBorderWidth = Math.Clamp(Int("ui", "inactiveBorderWidth", 1), 1, 20),
            InactiveBorderStyle = BorderStyle(Int("ui", "inactiveBorderStyle", 0)),
            HideActiveThumbnail = Bool("ui", "hideActiveClientThumbnail", false),
            HideWhenEveNotFocused = Bool("ui", "hideThumbnailsWhenEVENotFocused", false),
            AlwaysOnTop = Bool("window", "alwaysOnTop", true),
            AutoMinimiseInactive = Bool("window", "minimizeInactiveClients", false),
            AutoMinimiseDelaySeconds = Math.Clamp((int)Math.Round(Int("window", "minimizeDelay", 2000) / 1000d), 0, 600),
            LockPositions = Bool("position", "lockPositions", false),
            SnapEnabled = Bool("position", "enableSnapping", true),
            SnapDistance = Math.Clamp(Int("position", "snapDistance", 10), 1, 100),
            ShowLabels = Bool("overlay", "showCharacterName", true),
            DefaultLabelColourHex = Colour(Get("overlay", "characterNameColor"), "#FFFFFF"),
            LabelPosition = Position(Int("overlay", "characterNamePosition", 0)),
            LabelOffsetX = Math.Clamp(Int("overlay", "characterNameOffsetX", 0), -300, 300),
            LabelOffsetY = Math.Clamp(Int("overlay", "characterNameOffsetY", 0), -300, 300),
            LabelBackgroundHex = Colour(Get("overlay", "backgroundColor"), "#0C0C0E"),
            LabelBackgroundOpacityPercent = Math.Clamp(Int("overlay", "backgroundOpacity", 60), 0, 100),
            ShowSystemName = Bool("overlay", "showSystemName", false),
            HotkeysOnlyInEve = Bool("hotkey", "onlyWhenEVEFocused", false),
            LogMonitoringEnabled = Bool("chatlog", "enableMonitoring", false) || Bool("gamelog", "enableMonitoring", false),
            ChatLogFolder = Get("chatlog", "directory"), GameLogFolder = Get("gamelog", "directory"),
            ThumbnailsClickable = true, LoginScreensClickable = true, ClickOptionsSplit = true
        };
        ParseApmFont(Get("overlay", "characterNameFont", Get("overlay", "font")), s);
        if (ini.TryGetValue("thumbnailPositions", out var positions))
        {
            foreach (var pair in positions)
            {
                string character = Uri.UnescapeDataString(pair.Key.Replace("+", " "));
                if (!TryPoint(pair.Value, out double x, out double y)) continue;
                var c = s.GetOrAddCharacter(character); c.Left = x; c.Top = y;
            }
        }
        positionCount = s.Characters.Values.Count(c => c.Left.HasValue && c.Top.HasValue);
        var groups = new List<ThumbnailGroup>();
        if (ini.TryGetValue("cycleGroups", out var rawGroups))
        {
            foreach (var pair in rawGroups)
            {
                string groupName = Uri.UnescapeDataString(pair.Key.Replace("+", " "));
                string[] parts = TrimQuotes(pair.Value).Split('|');
                var names = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(n => !n.Equals("EVE", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (names.Count == 0) continue;
                groups.Add(new ThumbnailGroup { Name = groupName, Characters = names, ShowOthers = false });
                if (parts.Length > 1) { string key = ApmHotkey(parts[1]); if (key.Length > 0) s.CycleForwardHotkey = key; }
            }
        }
        if (groups.Count == 0) groups.Add(new ThumbnailGroup { Name = "Imported", Characters = s.Characters.Keys.Where(AppSettings.IsCharacterKey).ToList(), ShowOthers = true });
        s.ThumbnailGroups = groups; s.ActiveThumbnailGroup = groups[0].Name; s.RosterInitialised = true; s.EnsureGroups();
        groupCount = groups.Count;
        return s;
    }

    private static AppSettings ImportMaj(JsonElement root, out int positionCount, out int groupCount)
    {
        var s = new AppSettings { ClickOptionsSplit = true, ThumbnailsClickable = true, LoginScreensClickable = true };
        if (root.TryGetProperty("thumbnail", out var t))
        {
            s.PreviewWidth = Math.Clamp(GetInt(t, "width", 280), 120, 1200);
            s.PreviewHeight = Math.Clamp(GetInt(t, "height", 0), 0, 1200);
            s.ThumbnailOpacityPercent = Math.Clamp((int)Math.Round(GetInt(t, "thumbnailOpacity", 153) * 100 / 255d), 10, 100);
            s.HighlightActive = GetBool(t, "showBorderWhenFocused", false); s.ActiveBorderWidth = Math.Clamp(GetInt(t, "borderWidth", 3), 1, 20);
            s.ActiveBorderHex = Colour(GetString(t, "borderColor"), "#F97316"); s.ActiveBorderStyle = GetString(t, "borderStyle", "Solid");
            s.ShowInactiveBorder = GetBool(t, "showBorderWhenInactive", false); s.InactiveBorderWidth = Math.Clamp(GetInt(t, "inactiveBorderWidth", 1), 1, 20);
            s.InactiveBorderHex = Colour(GetString(t, "inactiveBorderColor"), "#6F6F7D"); s.InactiveBorderStyle = GetString(t, "inactiveBorderStyle", "Solid");
            s.ShowLabels = GetBool(t, "showText", true) && GetBool(t, "showCharacterName", true);
            s.DefaultLabelColourHex = Colour(GetString(t, "characterNameColor"), "#FFFFFF");
            s.LabelBackgroundHex = Colour(GetString(t, "characterNameBgColor"), "#0C0C0E", out int alpha); s.LabelBackgroundOpacityPercent = alpha;
            s.LabelFontName = GetString(t, "characterNameFontName", "Segoe UI"); s.LabelFontSize = Math.Clamp(GetInt(t, "characterNameFontSize", 13), 6, 72);
            s.LabelBold = !GetString(t, "characterNameFontWeight", "Regular").Equals("Regular", StringComparison.OrdinalIgnoreCase);
            s.LabelPosition = GetString(t, "characterNamePosition", "TopLeft"); s.LabelOffsetX = Math.Clamp(GetInt(t, "characterNameOffsetX", 0), -300, 300); s.LabelOffsetY = Math.Clamp(GetInt(t, "characterNameOffsetY", 0), -300, 300);
            s.ShowSystemName = GetBool(t, "showSystemName", false); s.HideActiveThumbnail = GetBool(t, "activeThumbnailHidden", false);
            s.HideWhenEveNotFocused = GetBool(t, "hideWhenNoEveFocus", false); s.HideDelayMs = Math.Clamp(GetInt(t, "hideDebounceMs", 500), 0, 5000);
        }
        if (root.TryGetProperty("timer", out var timer)) s.RefreshMilliseconds = Math.Clamp(GetInt(timer, "scanIntervalMs", 1500), 500, 10000);
        if (root.TryGetProperty("snapping", out var snap)) { s.SnapEnabled = GetBool(snap, "enabled", true); s.SnapDistance = Math.Clamp(GetInt(snap, "threshold", 10), 1, 100); }
        if (root.TryGetProperty("autoMinimize", out var min)) { s.AutoMinimiseInactive = GetBool(min, "enabled", false); s.AutoMinimiseDelaySeconds = Math.Clamp((int)Math.Round(GetInt(min, "delayMs", 2000) / 1000d), 0, 600); s.AutoMinimiseKeepLastActive = GetBool(min, "exemptLastActiveOnFocusLoss", true); }
        if (root.TryGetProperty("chatlog", out var log)) { s.LogMonitoringEnabled = GetBool(log, "enabled", false); s.ChatLogFolder = GetString(log, "chatlogDir"); s.GameLogFolder = GetString(log, "gamelogDir"); s.LogPollMilliseconds = Math.Clamp(GetInt(log, "pollIntervalMs", 1000), 250, 10000); }
        if (root.TryGetProperty("hotkeys", out var hotkeys)) s.HotkeysOnlyInEve = GetBool(hotkeys, "requireEveFocus", false);
        var names = new List<string>();
        if (root.TryGetProperty("characters", out var characters) && characters.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in characters.EnumerateArray())
            {
                string character = GetString(row, "name").Trim(); if (character.Length == 0) continue;
                names.Add(character); var c = s.GetOrAddCharacter(character);
                if (row.TryGetProperty("position", out var pos)) { c.Left = GetDouble(pos, "x"); c.Top = GetDouble(pos, "y"); }
                c.Hidden = GetBool(row, "hideThumbnail", false); c.NeverMinimise = GetBool(row, "excludeFromMinimize", false);
            }
        }
        positionCount = s.Characters.Values.Count(c => c.Left.HasValue && c.Top.HasValue);
        var groups = new List<ThumbnailGroup>();
        if (root.TryGetProperty("hotkeyGroups", out var hotkeyGroups) && hotkeyGroups.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in hotkeyGroups.EnumerateArray())
            {
                string groupName = GetString(row, "name", "Imported");
                var members = row.TryGetProperty("characters", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
                groups.Add(new ThumbnailGroup { Name = groupName, Characters = members, ShowOthers = false });
                string key = MajHotkey(GetString(row, "forwardKey")); if (groups.Count == 1 && key.Length > 0) s.CycleForwardHotkey = key;
            }
        }
        if (groups.Count == 0) groups.Add(new ThumbnailGroup { Name = "Imported", Characters = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ShowOthers = true });
        s.ThumbnailGroups = groups; s.ActiveThumbnailGroup = groups[0].Name; s.RosterInitialised = true; s.EnsureGroups(); groupCount = groups.Count;
        return s;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase); string section = "";
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); result.TryAdd(section, new(StringComparer.OrdinalIgnoreCase)); continue; }
            int equals = line.IndexOf('='); if (equals <= 0) continue; result.TryAdd(section, new(StringComparer.OrdinalIgnoreCase)); result[section][line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return result;
    }
    private static string TrimQuotes(string s) => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    private static bool TryPoint(string value, out double x, out double y) { x = y = 0; var m = Regex.Match(value, @"@Point\(\s*(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s*\)"); return m.Success && double.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out x) && double.TryParse(m.Groups[2].Value, CultureInfo.InvariantCulture, out y); }
    private static void ParseApmFont(string text, AppSettings s) { var p = TrimQuotes(text).Split(','); if (p.Length > 0 && p[0].Trim().Length > 0) s.LabelFontName = p[0].Trim(); if (p.Length > 1 && int.TryParse(p[1], out int z)) s.LabelFontSize = Math.Clamp(z, 6, 72); if (p.Length > 4 && int.TryParse(p[4], out int w)) s.LabelBold = w >= 600; }
    private static string Position(int n) => n switch { 1 => "TopRight", 2 => "BottomLeft", 3 => "BottomRight", 4 => "TopCenter", 5 => "BottomCenter", 6 => "LeftCenter", 7 => "RightCenter", 8 => "Center", _ => "TopLeft" };
    private static string BorderStyle(int n) => n switch { 1 => "Dashed", 2 => "Dotted", 3 => "DashDot", _ => "Solid" };
    private static string ApmHotkey(string raw) { var n = raw.Split(',', StringSplitOptions.TrimEntries); int at = n.Length > 1 && (n[0] == "0" || n[0] == "1") ? 1 : 0; return at < n.Length && int.TryParse(n[at], out int vk) ? MajHotkey("0x" + vk.ToString("X")) : ""; }
    private static string MajHotkey(string raw) { if (!raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !int.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vk)) return raw; return vk switch { 0xC0 => "`", >= 0x30 and <= 0x39 => ((char)vk).ToString(), >= 0x41 and <= 0x5A => ((char)vk).ToString(), 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", _ => "" }; }
    private static bool GetBool(JsonElement e, string n, bool f) => e.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : f;
    private static int GetInt(JsonElement e, string n, int f) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out int x) ? x : f;
    private static double? GetDouble(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetDouble(out double x) ? x : null;
    private static string GetString(JsonElement e, string n, string f = "") => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? f : f;
    private static string Colour(string text, string fallback) => Colour(text, fallback, out _);
    private static string Colour(string text, string fallback, out int alpha) { alpha = 100; text = text.Trim(); if (Regex.IsMatch(text, @"^0x[0-9A-Fa-f]{8}$")) { alpha = (int)Math.Round(Convert.ToInt32(text.Substring(2, 2), 16) * 100 / 255d); return "#" + text[4..].ToUpperInvariant(); } if (Regex.IsMatch(text, @"^#[0-9A-Fa-f]{8}$")) { alpha = (int)Math.Round(Convert.ToInt32(text.Substring(1, 2), 16) * 100 / 255d); return "#" + text[3..].ToUpperInvariant(); } return Regex.IsMatch(text, @"^#[0-9A-Fa-f]{6}$") ? text.ToUpperInvariant() : fallback; }
}

