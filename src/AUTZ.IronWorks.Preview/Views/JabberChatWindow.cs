using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class JabberChatWindow : Window
{
    private readonly JabberService _service;
    private readonly TreeView _list = new() { Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
    private readonly TextBox _history = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _message = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 30, MaxHeight = 85, Margin = new Thickness(0,0,6,0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, FontSize = 11 };
    private readonly TextBlock _heading = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
    private readonly Button _contacts = new() { Content = "Contacts" };
    private readonly Button _rooms = new() { Content = "Chat rooms" };
    private bool _showRooms;
    private string _selected = "";
    public JabberChatWindow(JabberService service)
    {
        _service = service; Title = "Jabber"; Width = 560; Height = 550; MinWidth = 400; MinHeight = 330;
        Background = (Brush)FindResource("BrushBg"); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(7,3,7,7) };
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0,0,7,0) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        _contacts.Padding = _rooms.Padding = new Thickness(7,4,7,4);
        _contacts.Click += (_,_) => { _showRooms = false; Refresh(); };
        _rooms.Click += (_,_) => { _showRooms = true; Refresh(); };
        tabs.Children.Add(_contacts); tabs.Children.Add(_rooms);
        DockPanel.SetDock(tabs, Dock.Top); left.Children.Add(tabs);
        var addRow = new DockPanel { Margin = new Thickness(0,5,0,0) };
        var newAddress = new TextBox { MinWidth = 90 };
        var add = new Button { Content = "+", ToolTip = "Add full Jabber address" };
        add.Click += async (_,_) =>
        {
            string jid = newAddress.Text.Trim(); if (jid.Length == 0) return;
            try { if (_showRooms) await _service.JoinRoom(jid); else _service.AddContact(jid); _selected = jid; newAddress.Clear(); Refresh(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        DockPanel.SetDock(add, Dock.Right); addRow.Children.Add(add); addRow.Children.Add(newAddress);
        DockPanel.SetDock(addRow, Dock.Bottom); left.Children.Add(addRow);
        _list.SelectedItemChanged += (_,_) =>
        {
            if (_list.SelectedItem is TreeViewItem { Tag: string jid }) { _selected = jid; ShowConversation(); }
        };
        left.Children.Add(_list); grid.Children.Add(left);
        var right = new DockPanel();
        _heading.Margin = new Thickness(0,0,0,5); DockPanel.SetDock(_heading, Dock.Top); right.Children.Add(_heading);
        var composer = new DockPanel { Margin = new Thickness(0,6,0,0) };
        var send = new Button { Content = "Send", MinWidth = 50 };
        async Task Send()
        {
            if (_selected.Length == 0) { _status.Text = "Select a contact or room."; return; }
            try
            {
                if (_showRooms) await _service.SendRoom(_selected, _message.Text);
                else await _service.SendChat(_selected, _message.Text);
                _message.Clear(); ShowConversation();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        send.Click += async (_,_) => await Send();
        _message.PreviewKeyDown += async (_,e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            { e.Handled = true; await Send(); }
        };
        DockPanel.SetDock(send, Dock.Right); composer.Children.Add(send); composer.Children.Add(_message);
        DockPanel.SetDock(composer, Dock.Bottom); right.Children.Add(composer);
        right.Children.Add(_history); Grid.SetColumn(right,1); grid.Children.Add(right);
        root.Children.Add(grid); Content = root;
        IronWindowChrome.Attach(this, "Jabber"); PopoutLayout.Track("Chat",this);
        _service.ChatChanged += RefreshSafe; _service.Changed += RefreshSafe;
        Closed += (_,_) => { _service.ChatChanged -= RefreshSafe; _service.Changed -= RefreshSafe; };
        Refresh();
    }
    private void RefreshSafe() => Dispatcher.BeginInvoke(new Action(Refresh));
    private void Refresh()
    {
        _status.Text = _service.Status;
        _contacts.Foreground = _showRooms ? Brushes.Gray : Brushes.White;
        _rooms.Foreground = _showRooms ? Brushes.White : Brushes.Gray;
        _list.Items.Clear();
        if (_showRooms)
        {
            foreach (string room in _service.Rooms.OrderBy(x => x))
            {
                var item = new TreeViewItem { Header = room.Split('@')[0], Tag = room,
                    Foreground = Brushes.White, IsSelected = room.Equals(_selected, StringComparison.OrdinalIgnoreCase) };
                _list.Items.Add(item);
            }
        }
        else
        {
            foreach (var group in _service.Contacts.GroupBy(x => x.Group).OrderBy(x => x.Key))
            {
                var parent = new TreeViewItem { Header = group.Key + " (" + group.Count(x => x.Online) + "/" + group.Count() + ")",
                    Foreground = Brushes.White, IsExpanded = true };
                foreach (var contact in group.OrderByDescending(x => x.Online).ThenBy(x => x.Name))
                {
                    var item = new TreeViewItem { Header = (contact.Online ? "● " : "○ ") + contact.Name,
                        Tag = contact.Jid, ToolTip = contact.Jid,
                        Foreground = contact.Online ? Brushes.LightGreen : Brushes.LightGray,
                        IsSelected = contact.Jid.Equals(_selected, StringComparison.OrdinalIgnoreCase) };
                    parent.Items.Add(item);
                }
                _list.Items.Add(parent);
            }
        }
        ShowConversation();
    }
    private void ShowConversation()
    {
        _heading.Text = _selected;
        _history.Text = string.Join("\n", _service.Chats.Where(x => x.Peer.Equals(_selected,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Time).Select(x => $"[{x.Time:HH:mm}] {(x.Outgoing ? "You" : x.Peer)}: {x.Body}"));
        _history.ScrollToEnd();
    }
}
