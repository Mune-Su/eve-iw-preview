// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.IO;
using System.Text.Json;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Each profile is a file in %LOCALAPPDATA%\EVE-IronWorks-Preview\profiles. app.json remembers which one is in use.</summary>
public static class ProfileStore
{
    public const string DefaultName = "Default";

    private static readonly string Folder = AppInfo.DataFolder;
    private static readonly string ProfilesFolder = Path.Combine(Folder, "profiles");
    private static readonly string StatePath = Path.Combine(Folder, "app.json");
    private static readonly string LegacyPath = Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static (AppSettings Settings, AppState State) LoadActive()
    {
        MigrateOldFolder();
        Directory.CreateDirectory(ProfilesFolder);
        var state = LoadState();

        if (List().Count == 0)
        {
            var first = new AppSettings();
            try
            {
                if (File.Exists(LegacyPath))
                    first = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(LegacyPath)) ?? new AppSettings();
            }
            catch { }
            Save(DefaultName, first);
            state.ActiveProfile = DefaultName;
            SaveState(state);
        }

        if (!Exists(state.ActiveProfile))
        {
            state.ActiveProfile = List()[0];
            SaveState(state);
        }
        return (Load(state.ActiveProfile), state);
    }

    /// <summary>The data folder used to be "AUTZ IronWorks Preview". It is copied across once, so profiles, positions and hotkeys carry over.</summary>
    private static void MigrateOldFolder()
    {
        try
        {
            string old = AppInfo.OldDataFolder;
            if (!Directory.Exists(old)) return;
            if (Directory.Exists(ProfilesFolder) && Directory.EnumerateFiles(ProfilesFolder, "*.json").Any()) return;   // it already has its own profiles

            CopyFolder(old, Folder);
            Log.Write("Copied your settings from " + old);
        }
        catch (Exception ex)
        {
            Log.Write("Couldn't copy the old settings folder: " + ex.Message);
        }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            string target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
        foreach (string dir in Directory.GetDirectories(from))
            CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    public static AppState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath)) ?? new AppState();
        }
        catch { }
        return new AppState();
    }

    public static void SaveState(AppState state)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            WriteFile(StatePath, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    public static IReadOnlyList<string> List()
    {
        try
        {
            if (!Directory.Exists(ProfilesFolder)) return Array.Empty<string>();
            return Directory.GetFiles(ProfilesFolder, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f) ?? "")
                .Where(n => n.Length > 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool Exists(string name) => File.Exists(PathFor(name));

    public static AppSettings Load(string name)
    {
        try
        {
            if (File.Exists(PathFor(name)))
                return Migrate(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathFor(name))) ?? new AppSettings());
        }
        catch { }
        return Migrate(new AppSettings());
    }

    /// <summary>Moves colours that were still an older default (navy / amber, or the charcoal / burnt-orange of v0.8.0) over to the website's palette.</summary>
    private static AppSettings Migrate(AppSettings s)
    {
        static bool Is(string value, params string[] olds) => olds.Any(o => value.Equals(o, StringComparison.OrdinalIgnoreCase));
        s.EnsureGroups();      // profiles from before the groups existed get a "Default" group built from what they had
        if (!s.ClickOptionsSplit)
        {
            // login screens used to be clickable only while character thumbnails were too: keep that behaviour for old profiles
            if (!s.ThumbnailsClickable) s.LoginScreensClickable = false;
            s.ClickOptionsSplit = true;
        }

        if (Is(s.ActiveBorderHex, "#F59E0B", "#E4600C")) s.ActiveBorderHex = "#F97316";
        if (Is(s.LabelBackgroundHex, "#0A0F16", "#141414")) s.LabelBackgroundHex = "#0C0C0E";
        if (Is(s.InactiveBorderHex, "#606060")) s.InactiveBorderHex = "#6F6F7D";
        if (Is(s.NotifyBorderHex, "#EF4444")) s.NotifyBorderHex = "#F87171";
        if (!s.AlertCooldownsMigrated)
        {
            // one shared cooldown became one per alert: a value you had changed is copied to every alert, once
            if (s.NotifyCooldownSeconds != 20 && s.AlertCooldowns.Count == 0)
                foreach (string kind in LogMonitorService.Kinds)
                    if (kind != "mining") s.AlertCooldowns[kind] = Math.Clamp(s.NotifyCooldownSeconds, 0, 3600);
            s.AlertCooldownsMigrated = true;
        }
        return s;
    }

    public static void Save(string name, AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ProfilesFolder);
            WriteFile(PathFor(name), JsonSerializer.Serialize(settings, Options));
        }
        catch { }
    }

    /// <summary>Writes to a temporary file and swaps it in, so a crash or power cut in the middle of a save can never leave a half-written profile.</summary>
    private static void WriteFile(string path, string text)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete(string name)
    {
        try { File.Delete(PathFor(name)); } catch { }
    }

    /// <summary>Renames a profile and keeps the active-profile name and profile hotkeys in step.</summary>
    public static bool Rename(string oldName, string newName)
    {
        try
        {
            if (!Exists(oldName) || Exists(newName)) return false;
            File.Move(PathFor(oldName), PathFor(newName));

            var state = LoadState();
            if (state.ActiveProfile.Equals(oldName, StringComparison.OrdinalIgnoreCase)) state.ActiveProfile = newName;
            string? key = state.ProfileHotkeys.Keys.FirstOrDefault(k => k.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
            {
                string hotkey = state.ProfileHotkeys[key];
                state.ProfileHotkeys.Remove(key);
                state.ProfileHotkeys[newName] = hotkey;
            }
            SaveState(state);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Strips characters that can't be in a file name.</summary>
    public static string CleanName(string? raw)
    {
        string text = raw ?? "";
        foreach (char bad in Path.GetInvalidFileNameChars()) text = text.Replace(bad.ToString(), "");
        return text.Trim().TrimEnd('.');
    }

    private static string PathFor(string name) => Path.Combine(ProfilesFolder, name + ".json");
}


