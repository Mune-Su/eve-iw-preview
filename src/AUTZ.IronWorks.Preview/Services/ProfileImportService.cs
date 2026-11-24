using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

public sealed record ProfileImportResult(AppSettings Settings, string Source, string SuggestedName, int Characters, int Positions, int Groups);

/// <summary>Reads EVE-IW profiles, and .ini and .json profiles, into a new EVE-IW profile.</summary>
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
                var settings = ImportIni(text, out int positions, out int groups);
                result = Result(settings, ".ini", name, positions, groups);
                return true;
            }
            using var document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("thumbnail", out var thumb) && thumb.ValueKind == JsonValueKind.Object &&
                (root.TryGetProperty("characters", out _) || root.TryGetProperty("hotkeyGroups", out _)))
            {
                var settings = ImportNested(root, out int positions, out int groups);
                result = Result(settings, ".json", name, positions, groups);
                return true;
            }
            if (root.ValueKind == JsonValueKind.Object &&
                (root.TryGetProperty("FlatLayout", out _) || root.TryGetProperty("ClientHotkey", out _) || root.TryGetProperty("ThumbnailRefreshPeriod", out _)))
            {
                var settings = ImportFlat(root, out int positions, out int groups);
                result = Result(settings, ".json", name, positions, groups);
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
            error = "That file isn't a profile this app can read.";
        }
        catch (Exception ex) { error = "The profile couldn't be read: " + ex.Message; }
        return false;
    }

    private static ProfileImportResult Result(AppSettings s, string source, string name, int positions, int groups) =>
        new(s, source, name, s.AllGroupCharacters().Distinct(StringComparer.OrdinalIgnoreCase).Count(), positions, groups);

    private static AppSettings ImportIni(string text, out int positionCount, out int groupCount)
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
        ParseIniFont(Get("overlay", "characterNameFont", Get("overlay", "font")), s);
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
                if (parts.Length > 1) { string key = IniHotkey(parts[1]); if (key.Length > 0) s.CycleForwardHotkey = key; }
            }
        }
        if (groups.Count == 0) groups.Add(new ThumbnailGroup { Name = "Imported", Characters = s.Characters.Keys.Where(AppSettings.IsCharacterKey).ToList(), ShowOthers = true });
        s.ThumbnailGroups = groups; s.ActiveThumbnailGroup = groups[0].Name; s.RosterInitialised = true; s.EnsureGroups();
        groupCount = groups.Count;
        return s;
    }

    private static AppSettings ImportNested(JsonElement root, out int positionCount, out int groupCount)
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
                string key = VkHotkey(GetString(row, "forwardKey")); if (groups.Count == 1 && key.Length > 0) s.CycleForwardHotkey = key;
            }
        }
        if (groups.Count == 0) groups.Add(new ThumbnailGroup { Name = "Imported", Characters = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ShowOthers = true });
        s.ThumbnailGroups = groups; s.ActiveThumbnailGroup = groups[0].Name; s.RosterInitialised = true; s.EnsureGroups(); groupCount = groups.Count;
        return s;
    }

    /// <summary>A .json profile that keeps window positions in "FlatLayout" and hotkeys in "ClientHotkey" (keys are "EVE - name").</summary>
    private static AppSettings ImportFlat(JsonElement root, out int positionCount, out int groupCount)
    {
        var s = new AppSettings { ClickOptionsSplit = true, ThumbnailsClickable = true, LoginScreensClickable = true };
        if (TrySize(root, "ThumbnailSize", out int width, out int height))
        {
            s.PreviewWidth = Math.Clamp(width, 120, 1200);
            s.PreviewHeight = height <= 0 ? 0 : Math.Clamp(height, 45, 1200);
        }
        if (root.TryGetProperty("ThumbnailOpacity", out var opacity) && opacity.TryGetDouble(out double o))
            s.ThumbnailOpacityPercent = Math.Clamp((int)Math.Round(o <= 1 ? o * 100 : o), 10, 100);
        s.RefreshMilliseconds = Math.Clamp(GetInt(root, "ThumbnailRefreshPeriod", 1500), 500, 10000);
        s.AlwaysOnTop = GetBool(root, "ShowThumbnailsAlwaysOnTop", true);
        s.HideActiveThumbnail = GetBool(root, "HideActiveClientThumbnail", false);
        s.HideWhenEveNotFocused = GetBool(root, "HideThumbnailsOnLostFocus", false);
        s.AutoMinimiseInactive = GetBool(root, "MinimizeInactiveClients", false);
        s.ShowLabels = GetBool(root, "ShowThumbnailOverlays", true);
        s.HighlightActive = GetBool(root, "EnableActiveClientHighlight", false);
        s.ActiveBorderWidth = Math.Clamp(GetInt(root, "ActiveClientHighlightThickness", 3), 1, 20);
        s.ActiveBorderHex = NamedColour(GetString(root, "ActiveClientHighlightColor"), "#F97316");

        var names = new List<string>();
        if (root.TryGetProperty("FlatLayout", out var layout) && layout.ValueKind == JsonValueKind.Object)
        {
            foreach (var row in layout.EnumerateObject())
            {
                string character = StripEve(row.Name);
                if (character.Length == 0 || !TryXY(row.Value, out double x, out double y)) continue;
                names.Add(character);
                var c = s.GetOrAddCharacter(character);
                c.Left = x;
                c.Top = y;
            }
        }
        if (root.TryGetProperty("ClientHotkey", out var hotkeys) && hotkeys.ValueKind == JsonValueKind.Object)
        {
            foreach (var row in hotkeys.EnumerateObject())
            {
                string character = StripEve(row.Name);
                string key = FlatHotkey(row.Value.ValueKind == JsonValueKind.String ? row.Value.GetString() ?? "" : "");
                if (character.Length == 0 || key.Length == 0) continue;
                names.Add(character);
                s.GetOrAddCharacter(character).Hotkey = key;
            }
        }
        if (root.TryGetProperty("DisableThumbnail", out var hidden) && hidden.ValueKind == JsonValueKind.Object)
        {
            foreach (var row in hidden.EnumerateObject())
            {
                string character = StripEve(row.Name);
                if (character.Length > 0 && row.Value.ValueKind == JsonValueKind.True) s.GetOrAddCharacter(character).Hidden = true;
            }
        }
        if (root.TryGetProperty("CycleGroup1ForwardHotkeys", out var forward) && forward.ValueKind == JsonValueKind.Array)
        {
            string key = forward.EnumerateArray().Select(k => FlatHotkey(k.GetString() ?? "")).FirstOrDefault(k => k.Length > 0) ?? "";
            if (key.Length > 0) s.CycleForwardHotkey = key;
        }
        if (root.TryGetProperty("CycleGroup1BackwardHotkeys", out var back) && back.ValueKind == JsonValueKind.Array)
        {
            string key = back.EnumerateArray().Select(k => FlatHotkey(k.GetString() ?? "")).FirstOrDefault(k => k.Length > 0) ?? "";
            if (key.Length > 0) s.CycleBackHotkey = key;
        }
        if (root.TryGetProperty("CycleGroup1ClientsOrder", out var order) && order.ValueKind == JsonValueKind.Object)
        {
            var ordered = order.EnumerateObject()
                .Select(p => (Name: StripEve(p.Name), Rank: p.Value.TryGetInt32(out int r) ? r : int.MaxValue))
                .Where(p => p.Name.Length > 0).OrderBy(p => p.Rank).Select(p => p.Name).ToList();
            if (ordered.Count > 0)
            {
                s.ClientOrder = ordered.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                s.ClientOrderMode = AppSettings.OrderManual;
                names.AddRange(ordered);
            }
        }
        positionCount = s.Characters.Values.Count(c => c.Left.HasValue && c.Top.HasValue);
        var group = new ThumbnailGroup { Name = "Imported", Characters = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ShowOthers = true };
        s.ThumbnailGroups = new List<ThumbnailGroup> { group };
        s.ActiveThumbnailGroup = group.Name;
        s.RosterInitialised = true;
        s.EnsureGroups();
        groupCount = 1;
        return s;
    }

    private static string StripEve(string key) =>
        (key.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase) ? key[6..] : key).Trim();

    /// <summary>"100, 200" or {"X":100,"Y":200}.</summary>
    private static bool TryXY(JsonElement value, out double x, out double y)
    {
        x = y = 0;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var px = GetDouble(value, "X") ?? GetDouble(value, "x");
            var py = GetDouble(value, "Y") ?? GetDouble(value, "y");
            if (px is null || py is null) return false;
            x = px.Value;
            y = py.Value;
            return true;
        }
        if (value.ValueKind != JsonValueKind.String) return false;
        var parts = (value.GetString() ?? "").Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }

    private static bool TrySize(JsonElement root, string name, out int width, out int height)
    {
        width = height = 0;
        if (!root.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            width = GetInt(value, "Width", 0);
            height = GetInt(value, "Height", 0);
            return width > 0;
        }
        if (!TryXY(value, out double w, out double h)) return false;
        width = (int)w;
        height = (int)h;
        return width > 0;
    }

    /// <summary>"F1, Control" or "Control+F1" -> "Ctrl+F1". "" when it can't be read.</summary>
    private static string FlatHotkey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("None", StringComparison.OrdinalIgnoreCase)) return "";
        var mods = new List<string>();
        string key = "";
        foreach (string part in raw.Split(new[] { ',', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "control": case "ctrl": if (!mods.Contains("Ctrl")) mods.Add("Ctrl"); break;
                case "alt": if (!mods.Contains("Alt")) mods.Add("Alt"); break;
                case "shift": if (!mods.Contains("Shift")) mods.Add("Shift"); break;
                default: key = part; break;
            }
        }
        if (key.Length == 0) return "";
        string text = string.Join("+", mods.Append(key));
        return HotkeyParser.TryParse(text, out _) ? text : "";
    }

    private static string NamedColour(string text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        string hex = Colour(text, "");
        if (hex.Length > 0) return hex;
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(text.Trim()) is System.Windows.Media.Color c)
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch { }
        return fallback;
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
    private static void ParseIniFont(string text, AppSettings s) { var p = TrimQuotes(text).Split(','); if (p.Length > 0 && p[0].Trim().Length > 0) s.LabelFontName = p[0].Trim(); if (p.Length > 1 && int.TryParse(p[1], out int z)) s.LabelFontSize = Math.Clamp(z, 6, 72); if (p.Length > 4 && int.TryParse(p[4], out int w)) s.LabelBold = w >= 600; }
    private static string Position(int n) => n switch { 1 => "TopRight", 2 => "BottomLeft", 3 => "BottomRight", 4 => "TopCenter", 5 => "BottomCenter", 6 => "LeftCenter", 7 => "RightCenter", 8 => "Center", _ => "TopLeft" };
    private static string BorderStyle(int n) => n switch { 1 => "Dashed", 2 => "Dotted", 3 => "DashDot", _ => "Solid" };
    private static string IniHotkey(string raw) { var n = raw.Split(',', StringSplitOptions.TrimEntries); int at = n.Length > 1 && (n[0] == "0" || n[0] == "1") ? 1 : 0; return at < n.Length && int.TryParse(n[at], out int vk) ? VkHotkey("0x" + vk.ToString("X")) : ""; }
    private static string VkHotkey(string raw) { if (!raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !int.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vk)) return raw; return vk switch { 0xC0 => "`", >= 0x30 and <= 0x39 => ((char)vk).ToString(), >= 0x41 and <= 0x5A => ((char)vk).ToString(), 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", _ => "" }; }
    private static bool GetBool(JsonElement e, string n, bool f) => e.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : f;
    private static int GetInt(JsonElement e, string n, int f) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out int x) ? x : f;
    private static double? GetDouble(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetDouble(out double x) ? x : null;
    private static string GetString(JsonElement e, string n, string f = "") => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? f : f;
    private static string Colour(string text, string fallback) => Colour(text, fallback, out _);
    private static string Colour(string text, string fallback, out int alpha) { alpha = 100; text = text.Trim(); if (Regex.IsMatch(text, @"^0x[0-9A-Fa-f]{8}$")) { alpha = (int)Math.Round(Convert.ToInt32(text.Substring(2, 2), 16) * 100 / 255d); return "#" + text[4..].ToUpperInvariant(); } if (Regex.IsMatch(text, @"^#[0-9A-Fa-f]{8}$")) { alpha = (int)Math.Round(Convert.ToInt32(text.Substring(1, 2), 16) * 100 / 255d); return "#" + text[3..].ToUpperInvariant(); } return Regex.IsMatch(text, @"^#[0-9A-Fa-f]{6}$") ? text.ToUpperInvariant() : fallback; }
}

