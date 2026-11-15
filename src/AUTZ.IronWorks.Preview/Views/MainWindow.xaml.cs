using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly EveWindowService _windows = new();
    private DispatcherTimer _timer = new();
    private bool _closing;
    private string _signature = "";

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        // Keep the strip on-screen if a monitor was unplugged since last run.
        bool onScreen = settings.Left >= SystemParameters.VirtualScreenLeft - 50
                     && settings.Left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
                     && settings.Top >= SystemParameters.VirtualScreenTop - 10
                     && settings.Top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;
        Left = onScreen ? settings.Left : 24;
        Top = onScreen ? settings.Top : 24;

        ConfigureTimer();
        Loaded += (_, _) => RefreshClients();
        Closing += (_, e) =>
        {
            if (!_closing) { e.Cancel = true; Hide(); }
        };
    }

    private void ConfigureTimer()
    {
        _timer.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_settings.RefreshMilliseconds) };
        _timer.Tick += (_, _) => RefreshClients();
        _timer.Start();
    }

    public void RefreshClients()
    {
        var clients = _windows.FindClients();
        string sig = string.Join('|', clients.Select(c => $"{c.Handle}:{c.Title}"));
        if (sig == _signature) { UpdateActiveBorders(); return; }
        _signature = sig;
        Rebuild(clients);
    }

    private void Rebuild(IReadOnlyList<EveClientWindow> clients)
    {
        ClientPanel.Children.Clear();
        ClientPanel.Orientation = _settings.VerticalLayout ? Orientation.Vertical : Orientation.Horizontal;
        EmptyMessage.Visibility = clients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var c in clients)
        {
            var card = new PreviewCard(c)
            {
                Width = _settings.PreviewWidth,
                Height = Math.Round(_settings.PreviewWidth * 9d / 16d)
            };
            card.ClientClicked += h => _windows.Activate(h);

            var stack = new StackPanel();
            stack.Children.Add(card);

            // DWM thumbnails paint above WPF content, so the label sits under the card.
            if (_settings.ShowLabels)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = c.CharacterName,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Background = new SolidColorBrush(Color.FromRgb(24, 32, 42)),
                    Padding = new Thickness(7, 4, 7, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsHitTestVisible = false
                });
            }

            ClientPanel.Children.Add(new Border
            {
                Tag = c.Handle,
                BorderThickness = new Thickness(3),
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Child = stack
            });
        }

        UpdateActiveBorders();
    }

    private void UpdateActiveBorders()
    {
        nint active = NativeMethods.GetForegroundWindow();
        Brush activeBrush;
        try { activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.ActiveBorderHex)); }
        catch { activeBrush = Brushes.Orange; }

        foreach (var b in ClientPanel.Children.OfType<Border>())
            b.BorderBrush = b.Tag is nint h && h == active ? activeBrush : Brushes.Transparent;
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DragMove();      // blocks until the mouse is released
        SavePosition();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SettingsService.Save(_settings);
            ConfigureTimer();
            _signature = "";
            RefreshClients();
        }
    }

    public void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }

    private void SavePosition()
    {
        if (!IsLoaded) return;
        _settings.Left = Left;
        _settings.Top = Top;
        SettingsService.Save(_settings);
    }

    public void CloseForExit()
    {
        _closing = true;
        _timer.Stop();
        SettingsService.Save(_settings);
        Close();
    }
}
