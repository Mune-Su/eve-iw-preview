using System.IO;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Tiny diagnostics log: %LOCALAPPDATA%\EVE-IronWorks-Preview\preview.log (kept small).</summary>
internal static class Log
{
    private static readonly string Folder = AppInfo.DataFolder;
    private static readonly string FilePath = Path.Combine(Folder, "preview.log");

    public static void Write(string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 120_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text + Environment.NewLine);
        }
        catch { }
    }
}
