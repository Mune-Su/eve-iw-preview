using System.IO;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Tiny diagnostics log: %LOCALAPPDATA%\AUTZ IronWorks Preview\preview.log (kept small).</summary>
internal static class Log
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AUTZ IronWorks Preview");
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
