using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>
/// Click-through overlay that sits on top of a thumbnail window and draws the character label, the
/// optional active-client highlight and the resize grip. It is a separate window because DWM thumbnails
/// always paint above anything drawn inside the window that hosts them.
/// </summary>
public sealed class LabelWindow : Window
{
    private readonly Border _frame = new();
    private readonly Border _labelBox = new();
    private readonly TextBlock _text = new();
    private readonly Polygon _grip = new();

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

        _text.FontWeight = FontWeights.SemiBold;
        _text.FontSize = 13;
        _text.TextTrimming = TextTrimming.CharacterEllipsis;

        _labelBox.Background = new SolidColorBrush(Color.FromArgb(170, 10, 15, 22));
        _labelBox.Padding = new Thickness(6, 2, 6, 2);
        _labelBox.Margin = new Thickness(4);
        _labelBox.HorizontalAlignment = HorizontalAlignment.Left;
        _labelBox.VerticalAlignment = VerticalAlignment.Top;
        _labelBox.Child = _text;

        // little triangle in the bottom-right corner, only shown while hovering
        _grip.Points = new PointCollection { new Point(0, 14), new Point(14, 0), new Point(14, 14) };
        _grip.Fill = new SolidColorBrush(Color.FromArgb(200, 245, 158, 11));
        _grip.Stroke = new SolidColorBrush(Color.FromArgb(160, 10, 15, 22));
        _grip.StrokeThickness = 1;
        _grip.HorizontalAlignment = HorizontalAlignment.Right;
        _grip.VerticalAlignment = VerticalAlignment.Bottom;
        _grip.Margin = new Thickness(0, 0, 3, 3);
        _grip.Visibility = Visibility.Collapsed;

        var root = new Grid();
        root.Children.Add(_labelBox);
        root.Children.Add(_grip);

        _frame.BorderThickness = new Thickness(0);
        _frame.Child = root;
        Content = _frame;

        SourceInitialized += (_, _) => MakeClickThrough();
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

    public void SetLabel(string text, Brush brush, bool show)
    {
        _text.Text = text;
        _text.Foreground = brush;
        _labelBox.Visibility = show && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetHighlight(Brush? brush)
    {
        _frame.BorderBrush = brush;
        _frame.BorderThickness = new Thickness(brush is null ? 0 : 3);
    }

    public void SetGripVisible(bool visible) =>
        _grip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
}
