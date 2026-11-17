using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace AUTZ.IronWorks.Preview.Core;

/// <summary>The AUTZ IronWorks website palette (css/style.css). The same values live in App.xaml.</summary>
internal static class Theme
{
    public static readonly Color AccentColor = Color.FromRgb(0xF9, 0x73, 0x16);

    public static Brush Get(string key, Brush fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        }
        catch { }
        return fallback;
    }

    public static Brush Accent => Get("BrushAccent", new SolidColorBrush(AccentColor));
    public static Brush AccentHi => Get("BrushAccentHover", new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)));
    public static Brush AccentLine => Get("BrushAccentLine", new SolidColorBrush(Color.FromRgb(0x9A, 0x45, 0x12)));
    public static Brush Card => Get("BrushCard", Brushes.Transparent);
    public static Brush Text => Get("BrushText", Brushes.White);
    public static Brush Muted => Get("BrushMuted", Brushes.Gray);
    public static Brush Dim => Get("BrushDim", Brushes.Gray);
    public static Brush Line => Get("BrushLine", Brushes.DimGray);
    public static Brush Hover => Get("BrushHover", Brushes.Transparent);
    public static Brush Selected => Get("BrushSelected", Brushes.Transparent);
    public static Brush Success => Get("BrushSuccess", Brushes.LimeGreen);
    public static Brush Danger => Get("BrushDanger", Brushes.IndianRed);
}

internal static class AppInfo
{
    /// <summary>The name shown inside the app: window titles, the tray, messages and the About page.</summary>
    public const string Name = "EVE-IronWorks-Preview";

    /// <summary>The short public / technical name: the exe file name, the GitHub repository, Discord and Reddit.</summary>
    public const string ShortName = "EVE-IW-Preview";

    public const string Credit = "by AUTZ IronWorks \u00B7 From planets to profit";
    /// <summary>Where the credit line links to.</summary>
    public const string Website = "https://autz-ironworks.vercel.app/#/home";

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Profiles, app.json and the log live here.</summary>
    public static readonly string DataFolder = System.IO.Path.Combine(LocalAppData, Name);

    /// <summary>The folder earlier versions used. Its contents are copied across once on the first start.</summary>
    public static readonly string OldDataFolder = System.IO.Path.Combine(LocalAppData, "AUTZ IronWorks Preview");

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}




