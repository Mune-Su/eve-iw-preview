using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class PingsWindow : Window
{
    private readonly JabberService _service;
    private readonly ListBox _list = new();
    private readonly TextBox _body = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new();
    public PingsWindow(JabberService service)
    {
        _service = service;
        Title = "Pings - " + AppInfo.Name; Width = 900; Height = 620; MinWidth = 550; MinHeight = 350;
        Background = (Brush)FindResource("BrushBg"); Foreground = (Brush)FindResource("BrushText");
        var root = new DockPanel { Margin = new Thickness(14) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "PINGS", FontSize = 18, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushAccent"), Margin = new Thickness(0, 0, 20, 0) });
        top.Children.Add(_status); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0) };
        void Add(string label, Action click) { var button = new Button { Content = label, Margin = new Thickness(6, 0, 0, 0) }; button.Click += (_, _) => click(); bottom.Children.Add(button); }
        Add("Copy", () => { if (_body.Text.Length > 0) Clipboard.SetText(_body.Text); });
        Add("Mark Read", () => { if (_list.SelectedIndex >= 0) _service.MarkRead(false, _list.SelectedIndex); });
        Add("Mark All Read", () => _service.MarkRead(true));
        Add("Clear History", () => { if (Dialogs.Ask(this, "Clear Pings", "Delete saved ping history?", "Clear", "Cancel") == 0) _service.Clear(); });
        Add("Close", () => Close());
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is JabberPing p) _body.Text = $"{p.Time:g}  {p.Sender}\n{p.Channel}\n\n{p.Body}"; };
        Grid.SetColumn(_body, 1); _body.Margin = new Thickness(10, 0, 0, 0);
        columns.Children.Add(_list); columns.Children.Add(_body); root.Children.Add(columns);
        Content = root;
        _service.Changed += RefreshSafe;
        Closed += (_, _) => _service.Changed -= RefreshSafe;
        Refresh();
    }
    private void RefreshSafe() => Dispatcher.BeginInvoke(new Action(Refresh));
    private void Refresh()
    {
        _status.Text = _service.Status + "  |  Unread: " + _service.History.Count(p => !p.Read);
        int selected = _list.SelectedIndex;
        _list.Items.Clear();
        foreach (var p in _service.History)
            _list.Items.Add(p);
        _list.DisplayMemberPath = "Body";
        if (selected >= 0 && selected < _list.Items.Count) _list.SelectedIndex = selected;
        else if (_list.Items.Count == 0) _body.Text = "No pings received yet.";
    }
}

