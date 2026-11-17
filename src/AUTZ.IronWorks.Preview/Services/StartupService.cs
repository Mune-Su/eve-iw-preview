using Microsoft.Win32;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Run on Startup: a value under HKCU\Software\Microsoft\Windows\CurrentVersion\Run (no admin rights needed).</summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EVE-IronWorks-Preview";
    private const string OldValueName = "AUTZ IronWorks Preview";      // earlier versions

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null || key?.GetValue(OldValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(OldValueName, false);
            if (enabled)
            {
                string? path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path)) key.SetValue(ValueName, "\"" + path + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Run on Startup: " + ex.Message);
        }
    }

    /// <summary>Keeps the registered path pointing at the exe that is running now, and moves an entry from the old name to the new one.</summary>
    public static void Refresh(bool enabled)
    {
        if (enabled || IsEnabled()) Set(true);
    }
}




