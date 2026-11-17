using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>A tiny "type a name" dialog in the app's dark style.</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _box = new();

    public string Value { get; private set; } = "";

    private PromptWindow(string title, string label, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Topmost = true;
        Background = (Brush)FindResource("BrushBg");
        Foreground = (Brush)FindResource("BrushText");
        FontFamily = new FontFamily("Inter, Segoe UI");
        FontSize = 13;

        var root = new StackPanel();
        root.Children.Add(new Border { Height = 3, Background = (Brush)FindResource("BrushAccent") });

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = (Brush)FindResource("BrushAccent"),
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

        _box.Text = initial;
        _box.Margin = new Thickness(0);
        panel.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var ok = new Button { Content = "OK", MinWidth = 92, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("PrimaryButton") };
        ok.Click += (_, _) =>
        {
            Value = _box.Text.Trim();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 92, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        root.Children.Add(panel);
        Content = root;
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    /// <summary>Returns the typed text, or null if cancelled.</summary>
    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var window = new PromptWindow(title, label, initial) { Owner = owner, Topmost = false };
        return window.ShowDialog() == true ? window.Value : null;
    }
}




