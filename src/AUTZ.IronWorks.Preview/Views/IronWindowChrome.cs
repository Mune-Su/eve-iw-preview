using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using AUTZ.IronWorks.Preview.Core;
namespace AUTZ.IronWorks.Preview.Views;
internal static class IronWindowChrome
{
    public static void Attach(Window window, string title)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.CanResize;
        window.Background = Theme.Get("BrushBg", Brushes.Black);
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0), GlassFrameThickness = new Thickness(0)
        });
        var original = window.Content as UIElement;
        if (original is null) return;
        window.Content = null;
        var root = new DockPanel();
        var line = new Border { Height = 2, Background = Brushes.Transparent };
        DockPanel.SetDock(line, Dock.Top); root.Children.Add(line);
        var header = new Grid { Height = 27, Background = Theme.Get("BrushBg", Brushes.Black) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var _ in new int[3]) header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        var name = new TextBlock { Text = title, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9,0,0,0), TextTrimming = TextTrimming.CharacterEllipsis };
        header.Children.Add(name);
        header.MouseLeftButtonDown += (_,e) =>
        {
            if (e.OriginalSource is Button) return;
            if (e.ClickCount == 2 && window.ResizeMode == ResizeMode.CanResize)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
        Button Add(string text, int col, Action action)
        {
            var button = new Button { Content = text, FontSize = 12, Padding = new Thickness(0),
                BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Theme.Muted, Focusable = false };
            button.Click += (_,_) => action(); Grid.SetColumn(button,col); header.Children.Add(button);
            return button;
        }
        Add("−",1,() => window.WindowState = WindowState.Minimized);
        var maximize = Add("□",2,() => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        if (window.ResizeMode != ResizeMode.CanResize) maximize.Visibility = Visibility.Collapsed;
        Add("×",3,() =>
        {
            string? key = window switch
            {
                PingsWindow => "Pings", JabberChatWindow => "Chat", SettingsWindow => "Settings",
                IntelWindow => "Intel", NewEdenMapWindow => "Map", _ => null
            };
            if (key is not null) AUTZ.IronWorks.Preview.Services.PopoutLayout.MarkUserClosed(key);
            window.Close();
        });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(original); window.Content = root;
        void Update()
        {
            bool active = window.IsActive;
            line.Background = active ? Theme.Accent : Brushes.Transparent;
            name.Foreground = active ? Theme.Text : Theme.Muted;
        }
        window.Activated += (_,_) => Update();
        window.Deactivated += (_,_) => Update();
        Update();
    }
}
