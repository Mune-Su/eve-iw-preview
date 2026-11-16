using Microsoft.Win32;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Run on Startup: a value under HKCU\Software\Microsoft\Windows\CurrentVersion\Run (no admin rights needed).</summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AUTZ IronWorks Preview";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
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

    /// <summary>Keeps the registered path pointing at the exe that is running now (it moves if you run a different copy).</summary>
    public static void Refresh(bool enabled)
    {
        if (enabled) Set(true);
    }
}
