using System.IO;
using Microsoft.Win32;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Keeps the tray icon on the taskbar instead of under the ^ arrow (Windows 11). Does nothing on older Windows.</summary>
internal static class TrayPinService
{
    private const string KeyPath = @"Control Panel\NotifyIconSettings";

    /// <summary>Returns true once this app's icon has been found in the list (whether or not it needed changing).</summary>
    public static bool Promote()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            using var root = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (root is null) return false;                                    // not Windows 11 (22H2 or later)

            bool found = false;
            foreach (string name in root.GetSubKeyNames())
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath + "\\" + name, writable: true);
                if (key?.GetValue("ExecutablePath") is not string path || !Matches(path, exe)) continue;

                found = true;
                if (key.GetValue("IsPromoted") is int current && current == 1) continue;
                key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                Log.Debug("TRAY", $"tray icon set to show on the taskbar ({name})");
            }
            return found;
        }
        catch (Exception ex)
        {
            Log.Debug("TRAY", "couldn't set the tray icon to show on the taskbar: " + ex.Message);
            return false;
        }
    }

    /// <summary>Windows writes the path in full, or with a known-folder id in front ("{GUID}\Tools\app.exe").</summary>
    private static bool Matches(string registered, string exe)
    {
        try
        {
            if (registered.Equals(exe, StringComparison.OrdinalIgnoreCase)) return true;
            if (registered.StartsWith('{'))
                return Path.GetFileName(registered).Equals(Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return false;
    }
}

