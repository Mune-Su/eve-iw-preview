using System.IO;
using System.Text.Json;
using System.Windows;
using AUTZ.IronWorks.Preview.Core;
namespace AUTZ.IronWorks.Preview.Services;
internal static class PopoutLayout
{
    public sealed class Position
    {
        public bool Open { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }
    private static readonly string FilePath = Path.Combine(AppInfo.DataFolder, "popout-layout.json");
    private static readonly Dictionary<string,Position> Saved = Load();
    private static readonly Dictionary<string,Window> Live = new();
    private static bool _shuttingDown;
    private static Dictionary<string,Position> Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Dictionary<string,Position>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataFolder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Saved));
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) { Log.Debug("WINDOW", "layout save failed: " + ex.GetType().Name); }
    }
    public static void Track(string key, Window window)
    {
        Live[key] = window;
        if (Saved.TryGetValue(key, out var saved))
        {
            if (saved.Width >= window.MinWidth && saved.Height >= window.MinHeight)
            { window.Width = saved.Width; window.Height = saved.Height; }
            var area = System.Windows.Forms.Screen.AllScreens.Select(s => s.WorkingArea).FirstOrDefault(a =>
                saved.Left + window.Width > a.Left + 60 && saved.Left < a.Right - 60 &&
                saved.Top + 40 < a.Bottom && saved.Top >= a.Top - 10);
            if (!area.IsEmpty)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = saved.Left; window.Top = saved.Top;
            }
            if (saved.Maximized) window.WindowState = WindowState.Maximized;
        }
        void Capture()
        {
            if (window.WindowState == WindowState.Minimized) return;
            var bounds = window.WindowState == WindowState.Maximized ? window.RestoreBounds : new Rect(window.Left,window.Top,window.Width,window.Height);
            if (bounds.Width < 100 || bounds.Height < 100) return;
            Saved[key] = new Position { Open = true, Left = bounds.Left, Top = bounds.Top,
                Width = bounds.Width, Height = bounds.Height, Maximized = window.WindowState == WindowState.Maximized };
            Save();
        }
        window.LocationChanged += (_,_) => Capture();
        window.SizeChanged += (_,_) => Capture();
        window.StateChanged += (_,_) => Capture();
        window.Closing += (_,e) =>
        {
            if (e.Cancel) return;
            Capture();
        };
        window.Closed += (_,_) =>
        {
            Live.Remove(key);
            if (Saved.TryGetValue(key,out var item)) { item.Open = _shuttingDown; Save(); }
        };
    }
    public static void Shutdown()
    {
        _shuttingDown = true;
        foreach (var (key,window) in Live.ToArray())
        {
            if (window.WindowState == WindowState.Minimized) continue;
            var bounds = window.WindowState == WindowState.Maximized ? window.RestoreBounds : new Rect(window.Left,window.Top,window.Width,window.Height);
            Saved[key] = new Position { Open = true, Left = bounds.Left, Top = bounds.Top,
                Width = bounds.Width, Height = bounds.Height, Maximized = window.WindowState == WindowState.Maximized };
        }
        Save();
    }
    public static bool WasOpen(string key) => Saved.TryGetValue(key,out var item) && item.Open;
}
