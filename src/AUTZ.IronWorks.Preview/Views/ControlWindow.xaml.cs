using System.Windows;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>Small control window. Its only real job is to give the app a taskbar icon; closing it exits the app.</summary>
public partial class ControlWindow : Window
{
    private readonly PreviewManager _manager;

    public ControlWindow(PreviewManager manager)
    {
        InitializeComponent();
        _manager = manager;
        _manager.ClientsChanged += UpdateStatus;
        Closed += (_, _) => _manager.ClientsChanged -= UpdateStatus;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var names = _manager.ClientNames;
        StatusText.Text = names.Count switch
        {
            0 => "Waiting for EVE clients…",
            1 => "1 EVE client detected",
            _ => names.Count + " EVE clients detected"
        };
        ClientList.Text = string.Join("\n", names);
        ToggleBtn.Content = _manager.ThumbnailsVisible ? "Hide thumbnails" : "Show thumbnails";
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => _manager.ToggleVisibility();
    private void Settings_Click(object sender, RoutedEventArgs e) => _manager.OpenSettings();
    private void Reset_Click(object sender, RoutedEventArgs e) => _manager.ResetPositions();
    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
