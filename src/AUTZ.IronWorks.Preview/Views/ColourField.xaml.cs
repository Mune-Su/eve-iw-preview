using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>Hex box + live colour swatch + "Pick…" button (standard Windows colour dialog) + quick presets.</summary>
public partial class ColourField : UserControl
{
    private static readonly string[] PresetColours =
    {
        "#FFFFFF", "#F97316", "#EF4444", "#22C55E", "#38BDF8",
        "#A78BFA", "#F472B6", "#FACC15", "#9D9DAA", "#000000"
    };

    private string _fallbackHex = "#FFFFFF";

    /// <summary>Raised whenever the text changes.</summary>
    public event Action? HexChanged;

    /// <summary>Colour shown (faded) while the box is empty, i.e. "use the default".</summary>
    public string FallbackHex
    {
        get => _fallbackHex;
        set { _fallbackHex = value; UpdateSwatch(); }
    }

    /// <summary>Shows a ✕ button that clears the box.</summary>
    public bool AllowEmpty
    {
        get => ClearBtn.Visibility == Visibility.Visible;
        set => ClearBtn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Hex
    {
        get => HexBox.Text.Trim();
        set => HexBox.Text = value ?? "";
    }

    /// <summary>Empty counts as valid (it means "use the default").</summary>
    public bool IsValid => Hex.Length == 0 || TryParse(Hex, out _);

    public ColourField()
    {
        InitializeComponent();

        foreach (string hex in PresetColours)
        {
            string chosen = hex;
            TryParse(hex, out var colour);
            var chip = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(colour),
                BorderBrush = new SolidColorBrush(Color.FromRgb(111, 111, 125)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = hex
            };
            chip.MouseLeftButtonUp += (_, _) => Hex = chosen;
            Presets.Children.Add(chip);
        }

        UpdateSwatch();
    }

    public static bool TryParse(string? text, out Color colour)
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

    private void UpdateSwatch()
    {
        if (Swatch is null || HexBox is null) return;

        string text = Hex;
        var grey = new SolidColorBrush(Color.FromRgb(111, 111, 125));

        if (text.Length == 0)
        {
            Swatch.Background = TryParse(_fallbackHex, out var fallback) ? new SolidColorBrush(fallback) : Brushes.Transparent;
            Swatch.Opacity = 0.5;
            Swatch.BorderBrush = grey;
            Swatch.ToolTip = "Using the default colour. Click to pick one.";
        }
        else if (TryParse(text, out var colour))
        {
            Swatch.Background = new SolidColorBrush(colour);
            Swatch.Opacity = 1;
            Swatch.BorderBrush = grey;
            Swatch.ToolTip = "Click to pick a colour";
        }
        else
        {
            Swatch.Background = Brushes.Transparent;
            Swatch.Opacity = 1;
            Swatch.BorderBrush = Brushes.IndianRed;
            Swatch.ToolTip = "Not a valid colour. Use something like #F97316.";
        }
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSwatch();
        HexChanged?.Invoke();
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e) => PickColour();

    private void Pick_Click(object sender, RoutedEventArgs e) => PickColour();

    private void Clear_Click(object sender, RoutedEventArgs e) => Hex = "";

    private void PickColour()
    {
        string start = Hex.Length > 0 && TryParse(Hex, out _) ? Hex : _fallbackHex;
        TryParse(start, out var current);

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };

        // the settings window is Topmost, so the dialog must be owned by it or it opens behind
        var owner = Window.GetWindow(this);
        nint handle = owner is null ? 0 : new WindowInteropHelper(owner).Handle;

        if (dialog.ShowDialog(new Win32Owner(handle)) == System.Windows.Forms.DialogResult.OK)
            Hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }
}

internal sealed class Win32Owner : System.Windows.Forms.IWin32Window
{
    public Win32Owner(nint handle) => Handle = handle;
    public nint Handle { get; }
}




