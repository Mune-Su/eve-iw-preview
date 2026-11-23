using System.Windows.Media;

namespace AUTZ.IronWorks.Preview.Core;

/// <summary>Small colour helpers shared by the manager and the overlay windows.</summary>
internal static class Ui
{
    /// <summary>Check box text you can select and copy. Only the box itself ticks.</summary>
    public static System.Windows.Controls.TextBox SelectableLabel(string text) => new()
    {
        Text = text,
        Style = (System.Windows.Style)System.Windows.Application.Current.FindResource("SelectableText"),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left
    };

    public static bool TryColour(string? text, out Color colour)
    {
        colour = Colors.White;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(text.Trim()) is Color parsed)
            {
                colour = parsed;
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>First valid colour out of hex / fallbackHex, otherwise the ultimate fallback.</summary>
    public static Brush BrushFrom(string? hex, string? fallbackHex, Brush ultimate)
    {
        foreach (string? candidate in new[] { hex, fallbackHex })
        {
            if (TryColour(candidate, out var colour))
            {
                var brush = new SolidColorBrush(colour);
                brush.Freeze();
                return brush;
            }
        }
        return ultimate;
    }

    /// <summary>A colour with its transparency set from a 0-100 percent (100 = solid).</summary>
    public static Brush BrushWithOpacity(string? hex, int percent, Brush ultimate)
    {
        if (!TryColour(hex, out var colour)) return ultimate;
        byte alpha = (byte)Math.Clamp((int)Math.Round(percent * 255 / 100.0), 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }
}
