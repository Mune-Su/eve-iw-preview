using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>Small themed message boxes (the first button is the highlighted one).</summary>
public static class Dialogs
{
    /// <summary>Shows a message with the given buttons and returns the index of the one that was clicked (-1 if closed).</summary>
    public static int Ask(Window? owner, string title, string message, params string[] buttons)
    {
        var window = new DialogWindow(title, message, buttons.Length == 0 ? new[] { "OK" } : buttons);
        if (owner is not null && owner.IsLoaded) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
        return window.Choice;
    }

    public static void Info(Window? owner, string message, string title = "EVE-IronWorks-Preview") =>
        Ask(owner, title, message, "OK");
}

internal sealed class DialogWindow : Window
{
    public int Choice { get; private set; } = -1;

    public DialogWindow(string title, string message, string[] buttons)
    {
        Title = title;
        Width = 460;
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

        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        body.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = (Brush)FindResource("BrushAccent"),
            Margin = new Thickness(0, 0, 0, 10)
        });
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 92,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = i == 0,
                IsCancel = i == buttons.Length - 1 && buttons.Length > 1
            };
            if (i == 0) button.Style = (Style)FindResource("PrimaryButton");
            button.Click += (_, _) =>
            {
                Choice = index;
                Close();
            };
            row.Children.Add(button);
        }
        body.Children.Add(row);
        root.Children.Add(body);
        Content = root;
    }
}



