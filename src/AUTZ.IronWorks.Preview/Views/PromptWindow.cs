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
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x15, 0x1C));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

        _box.Text = initial;
        _box.Padding = new Thickness(6);
        _box.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2A));
        _box.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
        _box.CaretBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
        _box.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
        panel.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            Value = _box.Text.Trim();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    /// <summary>Returns the typed text, or null if cancelled.</summary>
    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var window = new PromptWindow(title, label, initial) { Owner = owner };
        return window.ShowDialog() == true ? window.Value : null;
    }
}
