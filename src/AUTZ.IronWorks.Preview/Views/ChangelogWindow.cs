using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>The what's-new pop-up: the newest version up top, then every earlier version underneath.</summary>
public sealed class ChangelogWindow : Window
{
    public ChangelogWindow(string version)
    {
        Title = "What's new - " + AppInfo.Name;
        Width = 640;
        Height = 620;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Background = (Brush)FindResource("BrushBg");
        Foreground = (Brush)FindResource("BrushText");
        FontFamily = new FontFamily("Inter, Segoe UI");
        FontSize = 13;
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")); }
        catch { }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // header: logo, name and version
        var header = new Border
        {
            Background = (Brush)FindResource("BrushPanel"),
            BorderBrush = (Brush)FindResource("BrushLine"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 16, 20, 16)
        };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        try
        {
            head.Children.Add(new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
                Width = 44,
                Height = 44
            });
        }
        catch { }
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = AppInfo.Name, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        titles.Children.Add(new TextBlock { Text = "VERSION " + version, FontSize = 11, Foreground = (Brush)FindResource("BrushAccent"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
        head.Children.Add(titles);
        header.Child = head;
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // body
        var list = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        bool first = true;
        foreach (var entry in Changelog.Entries)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("BrushCard"),
                BorderBrush = first ? (Brush)FindResource("BrushAccent") : (Brush)FindResource("BrushLine"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(16, 12, 16, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var inner = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            title.Children.Add(new TextBlock
            {
                Text = "v" + entry.Version,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushAccent"),
                Margin = new Thickness(0, 0, 10, 0)
            });
            title.Children.Add(new TextBlock { Text = entry.Title, FontWeight = FontWeights.SemiBold });
            if (first)
            {
                title.Children.Add(new TextBlock
                {
                    Text = "  LATEST",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("BrushSuccess"),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            inner.Children.Add(title);

            foreach (string item in entry.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = "•", Foreground = (Brush)FindResource("BrushAccent") });
                var text = new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap, Foreground = first ? (Brush)FindResource("BrushText") : (Brush)FindResource("BrushMuted") };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                inner.Children.Add(row);
            }
            card.Child = inner;
            list.Children.Add(card);
            first = false;
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        // footer
        var footer = new Border
        {
            Background = (Brush)FindResource("BrushPanel"),
            BorderBrush = (Brush)FindResource("BrushLine"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 12)
        };
        var close = new Button { Content = "Close", MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true, Style = (Style)FindResource("PrimaryButton") };
        close.Click += (_, _) => Close();
        footer.Child = close;
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        Content = grid;
    }
}
