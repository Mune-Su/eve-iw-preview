using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class JabberChatWindow : Window
{
    private readonly JabberService _service;
    private readonly ListBox _peers = new();
    private readonly TextBox _body = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _to = new() { Width = 240, Margin = new Thickness(0,0,8,0) };
    private readonly TextBox _message = new() { MinWidth = 200, Margin = new Thickness(0,0,8,0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray };
    public JabberChatWindow(JabberService service)
    {
        _service = service; Title = "Jabber Chat"; Width = 850; Height = 600; MinWidth = 580; MinHeight = 350;
        Background = (Brush)FindResource("BrushBg"); Foreground = (Brush)FindResource("BrushText");
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,8,0,0) };
        bottom.Children.Add(_to); bottom.Children.Add(_message);
        var send = new Button { Content = "Send" };
                  send.Click += async (_,_) => { try { await _service.SendChat(_to.Text, _message.Text); _message.Clear(); Refresh(); } catch (Exception ex) { _status.Text = ex.Message; } };
bottom.Children.Add(send); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _peers.SelectionChanged += (_,_) => { if (_peers.SelectedItem is string peer) { _to.Text = peer; ShowPeer(peer); } };
        grid.Children.Add(_peers); Grid.SetColumn(_body,1); _body.Margin = new Thickness(8,0,0,0); grid.Children.Add(_body);
        root.Children.Add(grid); Content = root;
        IronWindowChrome.Attach(this, "Jabber Chat");
        PopoutLayout.Track("Chat", this);
        _service.ChatChanged += RefreshSafe; Closed += (_,_) => _service.ChatChanged -= RefreshSafe;
        Refresh();
    }
    private void RefreshSafe() => Dispatcher.BeginInvoke(new Action(Refresh));
    private void Refresh()
    {
        _status.Text = _service.Status;
        string? selected = _peers.SelectedItem as string;
        string[] peers = _service.Chats.Select(x => x.Peer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        _peers.ItemsSource = peers;
        if (selected is not null && peers.Contains(selected)) _peers.SelectedItem = selected;
        else if (peers.Length > 0) _peers.SelectedIndex = 0;
        if (_peers.SelectedItem is string peer) ShowPeer(peer);
    }
    private void ShowPeer(string peer)
    {
        _body.Text = string.Join("\n\n", _service.Chats.Where(x => x.Peer.Equals(peer,StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Time:t} {(x.Outgoing ? "You" : peer)}: {x.Body}"));
        _body.ScrollToEnd();
    }
}
