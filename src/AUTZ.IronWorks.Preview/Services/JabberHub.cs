using System.Windows;
using AUTZ.IronWorks.Preview.Views;
namespace AUTZ.IronWorks.Preview.Services;
internal static class JabberHub
{
    private static readonly Lazy<JabberService> Service = new(() => new JabberService());
    public static JabberService Instance => Service.Value;
    private static PingsWindow? _window;
    public static void OpenWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is null || !_window.IsLoaded)
            {
                try { _window = new PingsWindow(Instance); _window.Show(); _window.Activate(); }
                catch (Exception ex) { AUTZ.IronWorks.Preview.Views.Dialogs.Info(null, "Couldn't open Pings: " + ex.Message); }
            }
            else { if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal; _window.Activate(); }
        });
    }
}

