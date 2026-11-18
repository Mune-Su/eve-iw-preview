using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AUTZ.IronWorks.Preview.Core;
using ShapePath = System.Windows.Shapes.Path;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>How the label on a thumbnail looks.</summary>
public sealed record LabelSpec(
    string Text,
    Brush Foreground,
    bool Show,
    string FontName,
    double FontSize,
    bool Bold,
    Brush Background,
    string Position,
    double OffsetX,
    double OffsetY);

/// <summary>A border drawn around a thumbnail. Style: Solid, Dashed, Dotted, DashDot, Double, CornerBrackets.</summary>
public sealed record BorderSpec(string Hex, int Width, string Style);

/// <summary>
/// Click-through overlay that sits on top of a thumbnail window and draws the label, the border, the
/// "skipped by hotkeys" X and the resize grip. It is a separate window because DWM thumbnails always
/// paint above anything drawn inside the window that hosts them.
/// </summary>
public sealed class LabelWindow : Window
{
    private readonly Grid _root = new();
    private readonly Grid _borderHost = new();
    private readonly Border _labelBox = new();
    private readonly TextBlock _text = new();
    private readonly Polygon _grip = new();
    private readonly ShapePath _excluded = new();
    private ShapePath? _brackets;
    private BorderSpec? _border;

    public LabelWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _text.TextTrimming = TextTrimming.CharacterEllipsis;

        _labelBox.Padding = new Thickness(6, 2, 6, 2);
        _labelBox.Margin = new Thickness(4);
        _labelBox.HorizontalAlignment = HorizontalAlignment.Left;
        _labelBox.VerticalAlignment = VerticalAlignment.Top;
        _labelBox.Child = _text;

        // red X across a thumbnail whose client the next / previous hotkeys skip
        _excluded.Data = Geometry.Parse("M0,0 L1,1 M0,1 L1,0");
        _excluded.Stretch = Stretch.Fill;
        _excluded.Stroke = new SolidColorBrush(Color.FromArgb(150, 239, 68, 68));
        _excluded.StrokeThickness = 4;
        _excluded.Visibility = Visibility.Collapsed;
        _excluded.IsHitTestVisible = false;

        // little triangle in the bottom-right corner, only shown while hovering
        _grip.Points = new PointCollection { new Point(0, 14), new Point(14, 0), new Point(14, 14) };
        _grip.Fill = new SolidColorBrush(Color.FromArgb(230, 249, 115, 22));
        _grip.Stroke = new SolidColorBrush(Color.FromArgb(170, 12, 12, 14));
        _grip.StrokeThickness = 1;
        _grip.HorizontalAlignment = HorizontalAlignment.Right;
        _grip.VerticalAlignment = VerticalAlignment.Bottom;
        _grip.Margin = new Thickness(0, 0, 3, 3);
        _grip.Visibility = Visibility.Collapsed;

        _root.Children.Add(_excluded);
        _root.Children.Add(_borderHost);
        _root.Children.Add(_labelBox);
        _root.Children.Add(_grip);
        Content = _root;

        SourceInitialized += (_, _) => MakeClickThrough();
        SizeChanged += (_, _) => UpdateBrackets();
    }

    private void MakeClickThrough()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    public void SyncToOwner()
    {
        if (Owner is null) return;
        Left = Owner.Left;
        Top = Owner.Top;
        Width = Owner.ActualWidth;
        Height = Owner.ActualHeight;
    }

    public void SetLabel(LabelSpec spec)
    {
        _text.Text = spec.Text;
        _text.Foreground = spec.Foreground;
        _text.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(spec.FontName) ? "Segoe UI" : spec.FontName);
        _text.FontSize = spec.FontSize;
        _text.FontWeight = spec.Bold ? FontWeights.SemiBold : FontWeights.Normal;
        _labelBox.Background = spec.Background;

        (HorizontalAlignment horizontal, VerticalAlignment vertical) = spec.Position switch
        {
            "TopCenter" => (HorizontalAlignment.Center, VerticalAlignment.Top),
            "TopRight" => (HorizontalAlignment.Right, VerticalAlignment.Top),
            "LeftCenter" => (HorizontalAlignment.Left, VerticalAlignment.Center),
            "Center" => (HorizontalAlignment.Center, VerticalAlignment.Center),
            "RightCenter" => (HorizontalAlignment.Right, VerticalAlignment.Center),
            "BottomLeft" => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
            "BottomCenter" => (HorizontalAlignment.Center, VerticalAlignment.Bottom),
            "BottomRight" => (HorizontalAlignment.Right, VerticalAlignment.Bottom),
            _ => (HorizontalAlignment.Left, VerticalAlignment.Top)
        };
        _labelBox.HorizontalAlignment = horizontal;
        _labelBox.VerticalAlignment = vertical;
        _labelBox.RenderTransform = new TranslateTransform(spec.OffsetX, spec.OffsetY);

        _labelBox.Visibility = spec.Show && spec.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetExcluded(bool excluded) =>
        _excluded.Visibility = excluded ? Visibility.Visible : Visibility.Collapsed;

    public void SetGripVisible(bool visible) =>
        _grip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    // ---------- border ----------

    public void SetBorder(BorderSpec? spec)
    {
        if (Equals(spec, _border)) return;
        _border = spec;
        _borderHost.Children.Clear();
        _brackets = null;
        if (spec is null) return;

        Brush brush = Ui.BrushFrom(spec.Hex, null, Brushes.Orange);
        double width = Math.Clamp(spec.Width, 1, 20);

        switch (spec.Style)
        {
            case "Dashed":
                AddRectangle(brush, width, 0, new DoubleCollection { 4, 2 });
                break;
            case "Dotted":
                AddRectangle(brush, width, 0, new DoubleCollection { 1, 2 });
                break;
            case "DashDot":
                AddRectangle(brush, width, 0, new DoubleCollection { 4, 2, 1, 2 });
                break;
            case "Double":
            {
                double line = Math.Max(1, Math.Round(width / 3));
                AddRectangle(brush, line, 0, null);
                AddRectangle(brush, line, line * 2, null);
                break;
            }
            case "CornerBrackets":
                _brackets = new ShapePath
                {
                    Stroke = brush,
                    StrokeThickness = width,
                    StrokeStartLineCap = PenLineCap.Flat,
                    StrokeEndLineCap = PenLineCap.Flat,
                    IsHitTestVisible = false
                };
                _borderHost.Children.Add(_brackets);
                UpdateBrackets();
                break;
            default:
                AddRectangle(brush, width, 0, null);
                break;
        }
    }

    private void AddRectangle(Brush brush, double thickness, double inset, DoubleCollection? dashes)
    {
        var rectangle = new Rectangle
        {
            Stroke = brush,
            StrokeThickness = thickness,
            Margin = new Thickness(thickness / 2 + inset),
            IsHitTestVisible = false
        };
        if (dashes is not null) rectangle.StrokeDashArray = dashes;
        _borderHost.Children.Add(rectangle);
    }

    private void UpdateBrackets()
    {
        if (_brackets is null || _border is null) return;
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double half = Math.Clamp(_border.Width, 1, 20) / 2;
        double len = Math.Max(10, Math.Min(w, h) * 0.22);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // top-left
            context.BeginFigure(new Point(half, len), false, false);
            context.LineTo(new Point(half, half), true, false);
            context.LineTo(new Point(len, half), true, false);
            // top-right
            context.BeginFigure(new Point(w - len, half), false, false);
            context.LineTo(new Point(w - half, half), true, false);
            context.LineTo(new Point(w - half, len), true, false);
            // bottom-right
            context.BeginFigure(new Point(w - half, h - len), false, false);
            context.LineTo(new Point(w - half, h - half), true, false);
            context.LineTo(new Point(w - len, h - half), true, false);
            // bottom-left
            context.BeginFigure(new Point(len, h - half), false, false);
            context.LineTo(new Point(half, h - half), true, false);
            context.LineTo(new Point(half, h - len), true, false);
        }
        geometry.Freeze();
        _brackets.Data = geometry;
    }
}
