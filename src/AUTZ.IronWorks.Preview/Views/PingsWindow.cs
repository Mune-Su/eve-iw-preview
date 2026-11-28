using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class PingsWindow : Window
{
    private readonly JabberService _service;
    private readonly StackPanel _feed = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private int _lastPingCount = -1;
    private DateTime _lastNewestPing = DateTime.MinValue;
    private bool _firstDisplay = true;
    private readonly TextBlock[] _papValues = new TextBlock[3];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private static readonly Regex Urls = new(@"https?://[^\s<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Field = new(@"^\s*(?<name>FC Name|Formup Location|Current System|Doctrine|PAP Type|PAP|Comms)\s*:\s*(?<value>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Footer = new(@"(?i)^\s*[~=-]*\s*this was an? .*broadcast from\b", RegexOptions.Compiled);
    public PingsWindow(JabberService service)
    {
        _service = service; Title = "Pings - " + AppInfo.Name;
        Width = 520; Height = 650; MinWidth = 370; MinHeight = 300;
        Background = (Brush)FindResource("BrushBg"); Foreground = (Brush)FindResource("BrushText");
        var root = new DockPanel { Margin = new Thickness(8,3,8,8) };
        var participation = new Grid { Margin = new Thickness(0,0,0,8) };
        for (int i = 0; i < 3; i++) participation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
        {
            string label = i == 0 ? "STRAT" : i == 1 ? "PCT" : "LAST PAP";
            var cell = new StackPanel { Margin = new Thickness(8,3,5,3) };
            var value = new TextBlock { Text = "--", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            _papValues[i] = value;
            cell.Children.Add(value);
            cell.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = (Brush)FindResource("BrushMuted") });
            var border = new Border { BorderBrush = (Brush)FindResource("BrushAccentLine"), BorderThickness = new Thickness(2,0,0,0), Child = cell };
            Grid.SetColumn(border, i); participation.Children.Add(border);
        }
        var papRow = new DockPanel { Margin = new Thickness(0,0,0,5) };
        var refresh = new Button { Content = "↻", Width = 25, Height = 24, FontSize = 16,
            Padding = new Thickness(0), ToolTip = "Refresh PAPs", HorizontalAlignment = HorizontalAlignment.Right };
        refresh.Click += (_,_) =>
        {
            _service.RefreshParticipation();
            refresh.ToolTip = _service.Participation is null ? "No Account Info received yet" : "Latest saved Account Info";
        };
        DockPanel.SetDock(refresh, Dock.Right); papRow.Children.Add(refresh);
        papRow.Children.Add(participation);
        DockPanel.SetDock(papRow, Dock.Top); root.Children.Add(papRow);
        _scroll.Content = _feed;
        root.Children.Add(_scroll);
        Content = root;
        _service.Changed += RefreshSafe;
                  _clock.Tick += (_,_) => Refresh();
          IronWindowChrome.Attach(this, "Pings");
          PopoutLayout.Track("Pings", this);
Loaded += (_,_) => _clock.Start();
        Closed += (_,_) => { _clock.Stop(); _service.Changed -= RefreshSafe; };
        Refresh();
    }
    private void RefreshSafe() => Dispatcher.BeginInvoke(new Action(Refresh));
    private void Refresh()
    {
        var pap = _service.Participation;
        _papValues[0].Text = pap?.Strat.ToString() ?? "--";
        _papValues[1].Text = pap?.Pct.ToString() ?? "--";
        _papValues[2].Text = pap?.LastPap ?? "--";
        var history = _service.History;
        double oldOffset = _scroll.VerticalOffset;
        bool nearBottom = _scroll.ScrollableHeight - oldOffset <= 36;
        var newest = history.Count == 0 ? DateTime.MinValue : history.Max(x => x.Time);
        bool newPing = history.Count > _lastPingCount && newest > _lastNewestPing;
        _lastPingCount = history.Count;
        _lastNewestPing = newest;
        _feed.Children.Clear();
        if (history.Count == 0)
        {
            _feed.Children.Add(new TextBlock { Text = "No pings received yet.", Foreground = (Brush)FindResource("BrushMuted") });
            return;
        }
        foreach (var ping in history.OrderBy(x => x.Time)) _feed.Children.Add(Card(ping));
        bool scrollToNewest = _firstDisplay || (newPing && nearBottom);
        _firstDisplay = false;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (scrollToNewest) _scroll.ScrollToEnd();
            else _scroll.ScrollToVerticalOffset(oldOffset);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private static string Kind(string body, Dictionary<string,string> fields)
    {
        if (Regex.IsMatch(body, @"(?i)\b(strategic|strat)\b") ||
            fields.TryGetValue("PAP Type", out var pap) && pap.Contains("Strategic", StringComparison.OrdinalIgnoreCase)) return "STRAT";
        if (Regex.IsMatch(body, @"(?i)\b(peacetime|pct)\b")) return "PCT";
        var match = Regex.Match(body, @"(?i)\bthis was an?\s+([\w-]+)\s+broadcast from\b");
        if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
        return "BROADCAST";
    }
    private static string When(DateTime time)
    {
        var age = DateTime.Now - time;
        if (age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(30))
        {
            int mins = Math.Max(1, (int)age.TotalMinutes);
            return mins + (mins == 1 ? " min ago" : " mins ago");
        }
        return time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("dd MMM, HH:mm");
    }
    private UIElement Card(JabberPing ping)
    {
        var fields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var prose = new List<string>();
        foreach (string raw in ping.Body.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || Footer.IsMatch(line) || line.StartsWith("~~~")) continue;
            var match = Field.Match(line);
            if (match.Success) fields[match.Groups["name"].Value] = match.Groups["value"].Value;
            else prose.Add(line);
        }
        string kind = Kind(ping.Body, fields);
        Color accent = kind switch
        {
            "STRAT" => Color.FromRgb(249,115,22), "PCT" => Color.FromRgb(184,113,65),
            "GICE" => Color.FromRgb(80,161,173), "BROADCAST" => Color.FromRgb(106,121,137),
            _ => Color.FromRgb(183,129,78)
        };
        var frame = new Border { Margin = new Thickness(0,0,0,6), BorderThickness = new Thickness(ping.Read ? 2 : 3,0,0,0),
            BorderBrush = new SolidColorBrush(accent), Background = new SolidColorBrush(Color.FromRgb(17,23,29)) };
        var backdrop = new Grid();
        // Existing AUTZ lava-furnace emblem, deliberately faint; variations follow broadcast category.
        try
        {
            var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
                Width = 155, Height = 155, Stretch = Stretch.Uniform, Opacity = kind switch
                { "STRAT" => .085, "PCT" => .065, "GICE" => .075, _ => .055 },
                HorizontalAlignment = kind == "PCT" ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            backdrop.Children.Add(logo);
        }
        catch { }
        var content = new StackPanel { Margin = new Thickness(10,7,10,7) };
        if (!fields.ContainsKey("FC Name"))
        {
            var announcement = Regex.Match(ping.Body, @"(?im)^\s*this was an?\s+.+?broadcast from\s+(?<sender>[^\r\n]+)");
            if (announcement.Success)
            {
                var row = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
                row.Inlines.Add(new Run("Announcement from: ") { Foreground = (Brush)FindResource("BrushMuted") });
                row.Inlines.Add(new Run(announcement.Groups["sender"].Value.Trim().TrimEnd('.', '~')) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
                content.Children.Add(row);
            }
        }
        if (fields.TryGetValue("FC Name", out var fc) && fc.Length > 0)
            {
            var row = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            row.Inlines.Add(new Run("Fleet Under: ") { Foreground = (Brush)FindResource("BrushMuted") });
            row.Inlines.Add(new Run(fc) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            content.Children.Add(row);
        }
        if (fields.TryGetValue("Formup Location", out var system) || fields.TryGetValue("Current System", out system))
            {
            var row = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            row.Inlines.Add(new Run("System: ") { Foreground = (Brush)FindResource("BrushMuted") });
            row.Inlines.Add(new Run(system) { Foreground = new SolidColorBrush(Color.FromRgb(250,183,137)), FontWeight = FontWeights.SemiBold });
            content.Children.Add(row);
        }
        if (prose.Count > 0)
        {
            var title = new TextBlock { Text = prose[0], FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,2) };
            content.Children.Add(title);
            foreach (string line in prose.Skip(1).Take(8))
            {
                var row = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
                AddLinkedText(row, line); content.Children.Add(row);
            }
        }
        if (fields.TryGetValue("Comms", out var comms) && comms.Length > 0)
        {
            var row = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,3,0,0) };
            row.Inlines.Add(new Run("Comms: ") { Foreground = (Brush)FindResource("BrushMuted") });
            AddLinkedText(row, comms);
            content.Children.Add(row);
        }
        if (fields.TryGetValue("Doctrine", out var doctrine) && doctrine.Length > 0)
        {
            content.Children.Add(Line("Doctrine:", 10, (Brush)FindResource("BrushMuted")));
            content.Children.Add(Line(doctrine, 11, Brushes.White));
        }
        var footer = new Border { Margin = new Thickness(-10,6,-10,-7), Padding = new Thickness(10,5,10,5),
            Background = new SolidColorBrush(Color.FromArgb(145,31,37,43)) };
        footer.Child = Line(kind + "  ·  " + When(ping.Time), 10.5, (Brush)FindResource("BrushMuted"));
        content.Children.Add(footer);
        backdrop.Children.Add(content); frame.Child = backdrop;
        return frame;
    }
    private static TextBlock Line(string text, double size, Brush color) => new()
    { Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap };
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
