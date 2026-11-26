// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.IO;
using System.Reflection;
using AUTZ.IronWorks.Preview.Core;
using Microsoft.Win32;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>One entry in a sound drop-down. Id is what gets saved.</summary>
public sealed record SoundChoice(string Id, string Text, string Tip)
{
    public override string ToString() => Text;
}

/// <summary>
/// Alert sounds: built in, Windows, and your own.
/// Ids: "iw:Chime", "win:Exclamation", "custom:file.mp3", "none".
/// </summary>
internal static class SoundLibrary
{
    public const long MaxBytes = 5L * 1024 * 1024;
    public const int MaxSeconds = 10;
    public const string DefaultId = "iw:Chime";
    public const string NoneId = "none";

    public static readonly string[] Extensions = { ".wav", ".mp3", ".m4a", ".wma", ".aac" };

    // name in the exe, text in the list, hover note
    private static readonly (string Name, string Text, string Tip)[] BuiltIn =
    {
        ("Chime", "Chime", "Two soft bells."),
        ("Ping", "Ping", "One high bell."),
        ("DoubleBeep", "Double Beep", "Two short beeps."),
        ("Klaxon", "Klaxon", "Loud siren. Hard to miss."),
        ("Rising", "Rising", "A tone that goes up."),
        ("Falling", "Falling", "A tone that goes down."),
        ("Alarm", "Alarm", "Fast two-tone alarm."),
        ("Blip", "Blip", "Very short blip.")
    };

    public static readonly string[] WindowsSounds = { "Exclamation", "Asterisk", "Beep", "Hand", "Question" };

    public static string Folder => Path.Combine(AppInfo.DataFolder, "sounds");
    public static string CustomFolder => Path.Combine(Folder, "custom");
    private static string BuiltInFolder => Path.Combine(Folder, "built-in");

    /// <summary>Turns an old saved value (a plain Windows sound name) into a sound id.</summary>
    public static string Normalise(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return DefaultId;
        if (id.Contains(':') || id == NoneId) return id;
        var match = WindowsSounds.FirstOrDefault(w => w.Equals(id, StringComparison.OrdinalIgnoreCase));
        return match is null ? DefaultId : "win:" + match;
    }

    /// <summary>The list for a sound drop-down. sameAsMain adds "Same as main sound" and "No sound" at the top (for one alert).</summary>
    public static List<SoundChoice> Choices(bool sameAsMain)
    {
        var list = new List<SoundChoice>();
        if (sameAsMain)
        {
            list.Add(new SoundChoice("", "Same as main sound", "Uses the sound set under Notifications."));
            list.Add(new SoundChoice(NoneId, "No sound", "This alert makes no sound."));
        }
        foreach (var b in BuiltIn) list.Add(new SoundChoice("iw:" + b.Name, b.Text, b.Tip));
        foreach (string w in WindowsSounds) list.Add(new SoundChoice("win:" + w, "Windows " + w, "Windows sound. Depends on your sound scheme."));
        foreach (string file in CustomFiles()) list.Add(new SoundChoice("custom:" + file, "Custom: " + Path.GetFileNameWithoutExtension(file), "Your sound: " + file));
        return list;
    }

    /// <summary>The sounds you have added (file names only).</summary>
    public static List<string> CustomFiles()
    {
        try
        {
            if (!Directory.Exists(CustomFolder)) return new List<string>();
            return Directory.GetFiles(CustomFolder)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>Checks a file and copies it into the sounds folder. Error is a short message for a pop-up.</summary>
    public static bool TryAdd(string source, out string id, out string error)
    {
        id = "";
        error = "";
        try
        {
            if (!Extensions.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
            {
                error = "File type not supported.";
                return false;
            }
            var info = new FileInfo(source);
            if (!info.Exists)
            {
                error = "File not found.";
                return false;
            }
            if (info.Length > MaxBytes)
            {
                error = "File too big. Max 5 MB.";
                return false;
            }
            Directory.CreateDirectory(CustomFolder);
            string name = Path.GetFileName(source);
            string target = Path.Combine(CustomFolder, name);
            if (File.Exists(target) && new FileInfo(target).Length == info.Length)
            {
                id = "custom:" + name;          // already added
                return true;
            }
            int n = 2;
            while (File.Exists(target))
            {
                name = Path.GetFileNameWithoutExtension(source) + $" ({n++})" + Path.GetExtension(source);
                target = Path.Combine(CustomFolder, name);
            }
            File.Copy(source, target);
            id = "custom:" + name;
            Log.Debug("ALERT", "custom sound added (" + Path.GetExtension(name) + ", " + (info.Length / 1024) + " KB)");
            return true;
        }
        catch (Exception ex)
        {
            error = "Couldn't add the sound: " + ex.Message;
            return false;
        }
    }

    /// <summary>The file to play for a sound id, or null if there isn't one.</summary>
    public static string? PathOf(string id)
    {
        try
        {
            if (id.StartsWith("iw:", StringComparison.OrdinalIgnoreCase)) return BuiltInFile(id[3..]);
            if (id.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
            {
                string file = Path.Combine(CustomFolder, Path.GetFileName(id[7..]));
                return File.Exists(file) ? file : null;
            }
            if (id.StartsWith("win:", StringComparison.OrdinalIgnoreCase)) return WindowsFile(id[4..]);
        }
        catch (Exception ex)
        {
            Log.Debug("ALERT", "sound lookup failed: " + ex.Message);
        }
        return null;
    }

    /// <summary>A built-in sound straight from the exe (used when another sound can't be played).</summary>
    public static Stream? OpenBuiltIn(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("IWSound." + name + ".wav");

    /// <summary>Copies a built-in sound out of the exe the first time it is needed (the player needs a file).</summary>
    private static string? BuiltInFile(string name)
    {
        if (!BuiltIn.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return null;
        string file = Path.Combine(BuiltInFolder, name + ".wav");
        if (File.Exists(file)) return file;
        using var stream = OpenBuiltIn(name);
        if (stream is null) return null;
        Directory.CreateDirectory(BuiltInFolder);
        using (var output = File.Create(file)) stream.CopyTo(output);
        return file;
    }

    /// <summary>The .wav Windows uses for one of its sounds, from the sound scheme in use.</summary>
    private static string? WindowsFile(string name)
    {
        string eventName = name.ToLowerInvariant() switch
        {
            "asterisk" => "SystemAsterisk",
            "hand" => "SystemHand",
            "question" => "SystemQuestion",
            "beep" => ".Default",
            _ => "SystemExclamation"
        };
        using var key = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\" + eventName + @"\.Current");
        string? value = key?.GetValue("") as string;
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = Environment.ExpandEnvironmentVariables(value);
        if (!Path.IsPathRooted(value)) value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", value);
        return File.Exists(value) ? value : null;
    }
}
