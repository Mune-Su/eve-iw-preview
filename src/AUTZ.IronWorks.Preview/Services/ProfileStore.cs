using System.IO;
using System.Text.Json;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Profiles are separate files in %LOCALAPPDATA%\AUTZ IronWorks Preview\profiles\NAME.json, and app.json
/// remembers which one is active. The old single settings.json becomes the "Default" profile the first time.
/// </summary>
public static class ProfileStore
{
    public const string DefaultName = "Default";

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AUTZ IronWorks Preview");
    private static readonly string ProfilesFolder = Path.Combine(Folder, "profiles");
    private static readonly string StatePath = Path.Combine(Folder, "app.json");
    private static readonly string LegacyPath = Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static (AppSettings Settings, AppState State) LoadActive()
    {
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
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, Options));
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathFor(name))) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(string name, AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ProfilesFolder);
            File.WriteAllText(PathFor(name), JsonSerializer.Serialize(settings, Options));
        }
        catch { }
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
