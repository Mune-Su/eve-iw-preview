using System.Windows;
using AUTZ.IronWorks.Preview.Views;
namespace AUTZ.IronWorks.Preview.Services;
internal static class JabberHub
{
    private static readonly Lazy<JabberService> Service = new(() => new JabberService());
    public static JabberService Instance => Service.Value;
    private static PingsWindow? _window;
    private static JabberChatWindow? _chat;
    public static void OpenChat(bool activate = true)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_chat is null || !_chat.IsLoaded)
            {
                _chat = new JabberChatWindow(Instance);
                _chat.Show();
            }
            if (_chat.WindowState == WindowState.Minimized) _chat.WindowState = WindowState.Normal;
            if (activate) _chat.Activate();
        });
    }
    public static void OpenWindow(bool activate = true)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is null || !_window.IsLoaded)
            {
                try { _window = new PingsWindow(Instance); _window.Show(); if (activate) _window.Activate(); }
                catch (Exception ex) { AUTZ.IronWorks.Preview.Views.Dialogs.Info(null, "Couldn't open Pings: " + ex.Message); }
            }
            else { if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal; if (activate) _window.Activate(); }
        });
    }
}
