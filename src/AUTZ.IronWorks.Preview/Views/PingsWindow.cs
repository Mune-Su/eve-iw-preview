using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class PingsWindow : Window
{
    private readonly JabberService _service;
    private readonly StackPanel _feed = new();
    private readonly TextBlock _status = new();
    private readonly Brush _accent;
    private readonly Brush _muted;
    private static readonly Regex Urls = new(@"https?://[^\s<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Field = new(@"^\s*(?:[-*>]\s*)?(?<name>FC Name|Formup Location|Current System|Doctrine|PAP Type|PAP|Comms)\s*:\s*(?<value>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public PingsWindow(JabberService service)
    {
        _service = service;
        _accent = (Brush)FindResource("BrushAccent");
        _muted = (Brush)FindResource("BrushMuted");
        Title = "Pings - " + AppInfo.Name;
        Width = 650; Height = 720; MinWidth = 420; MinHeight = 350;
        Background = (Brush)FindResource("BrushBg"); Foreground = (Brush)FindResource("BrushText");
        var root = new DockPanel { Margin = new Thickness(14) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) };
        top.Children.Add(new TextBlock { Text = "PINGS", FontSize = 18, FontWeight = FontWeights.Bold,
            Foreground = _accent, Margin = new Thickness(0,0,16,0) });
        _status.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(_status);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0,10,0,0) };
        AddButton(bottom, "Mark All Read", () => _service.MarkRead(true));
        AddButton(bottom, "Clear History", () =>
        {
            if (Dialogs.Ask(this, "Clear Pings", "Delete saved ping history?", "Clear", "Cancel") == 0) _service.Clear();
        });
        AddButton(bottom, "Close", Close);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var scroll = new ScrollViewer { Content = _feed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(scroll); Content = root;
        _service.Changed += RefreshSafe;
        Closed += (_, _) => _service.Changed -= RefreshSafe;
        Refresh();
    }
    private static void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(6,0,0,0) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private void RefreshSafe() => Dispatcher.BeginInvoke(new Action(Refresh));
    private void Refresh()
    {
        var history = _service.History;
        _status.Text = _service.Status + "  |  Unread: " + history.Count(p => !p.Read);
        _feed.Children.Clear();
        if (history.Count == 0)
        {
            _feed.Children.Add(new TextBlock { Text = "No pings received yet.", Foreground = _muted,
                Margin = new Thickness(6,12,0,0) });
            return;
        }
        for (int i = 0; i < history.Count; i++) _feed.Children.Add(Card(history[i], i));
    }
    private UIElement Card(JabberPing ping, int index)
    {
        var lines = ping.Body.Replace("\r", "").Split('\n');
        var fields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var prose = new List<string>();
        foreach (string raw in lines)
        {
            string line = raw.Trim(); if (line.Length == 0) continue;
            var match = Field.Match(line);
            if (match.Success) fields[match.Groups["name"].Value] = match.Groups["value"].Value;
            else if (!line.StartsWith("~~~") && !line.StartsWith("====")) prose.Add(line);
        }
        string headline = prose.FirstOrDefault() ?? "Fleet broadcast";
        // The title and fields are all visible. There is no collapsed or expanded state.
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18,27,36)),
            BorderBrush = ping.Read ? new SolidColorBrush(Color.FromRgb(42,62,77)) : _accent,
            BorderThickness = new Thickness(3,0,0,0),
            CornerRadius = new CornerRadius(3), Margin = new Thickness(0,0,0,9), Padding = new Thickness(12)
        };
        var content = new StackPanel();
        var heading = new TextBlock { Text = headline, FontSize = 15, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("BrushText") };
        content.Children.Add(heading);
        foreach (string key in new[] { "FC Name", "Formup Location", "Current System", "Doctrine", "PAP Type", "PAP", "Comms" })
        {
            if (!fields.TryGetValue(key, out var value)) continue;
            var row = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,0) };
            row.Inlines.Add(new Run(key + ": ") { Foreground = _muted });
            AddLinkedText(row, value);
            content.Children.Add(row);
        }
        // Include other useful broadcast text without duplicating parsed fields.
        foreach (string line in prose.Skip(1).Where(x => !Field.IsMatch(x)).Take(5))
        {
            var row = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,0) };
            AddLinkedText(row, line); content.Children.Add(row);
        }
        var footer = new DockPanel { Margin = new Thickness(0,10,0,0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        AddButton(buttons, "Copy", () => Clipboard.SetText(ping.Body));
        if (!ping.Read) AddButton(buttons, "Mark Read", () => _service.MarkRead(false, index));
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        footer.Children.Add(new TextBlock { Text = ping.Time.ToString("dd MMM yyyy  HH:mm") + "  •  " + ping.Sender,
            Foreground = _muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(footer); card.Child = content;
        return card;
    }
    private static void AddLinkedText(TextBlock target, string text)
    {
        int cursor = 0;
        foreach (Match match in Urls.Matches(text))
        {
            if (match.Index > cursor) target.Inlines.Add(new Run(text[cursor..match.Index]));
            string url = match.Value.TrimEnd('.', ',', ')');
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            {
                var link = new Hyperlink(new Run(url)) { NavigateUri = uri, Foreground = Brushes.DeepSkyBlue };
                link.RequestNavigate += (_, e) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch { }
                    e.Handled = true;
                };
                target.Inlines.Add(link);
            }
            else target.Inlines.Add(new Run(match.Value));
            cursor = match.Index + match.Length;
        }
        if (cursor < text.Length) target.Inlines.Add(new Run(text[cursor..]));
    }
}
