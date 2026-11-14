using System.Text.Json;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;
public static class SettingsService
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AUTZ IronWorks Preview");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    public static AppSettings Load() { try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); } catch { } return new(); }
    public static void Save(AppSettings value) { Directory.CreateDirectory(Folder); File.WriteAllText(FilePath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); }
}
