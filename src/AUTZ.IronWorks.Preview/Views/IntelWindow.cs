using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class IntelWindow : Window
{
    private static readonly string SettingsFile = Path.Combine(AppInfo.DataFolder, "intel-channels.json");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly List<string> _channels = new();
    private readonly StackPanel _channelRows = new();
    private readonly ListBox _feed = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray };
    private readonly TextBox _folder = new() { MinWidth = 350 };
    private readonly TextBox _entry = new() { Width = 260, Margin = new Thickness(0,0,8,0) };
    public IntelWindow()
    {
        Title = "Intel - Local Chat Logs"; Width = 850; Height = 650;
        Background = new SolidColorBrush(Color.FromRgb(13,15,22)); Foreground = Brushes.White;
        try { if (File.Exists(SettingsFile)) _channels.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SettingsFile)) ?? new()); } catch { }
        var root = new DockPanel { Margin = new Thickness(12) }; var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "CHAT LOG FOLDER", Foreground = Brushes.Orange });
        _folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs", "Chatlogs");
        top.Children.Add(_folder);
        top.Children.Add(new TextBlock { Text = "INTEL CHANNELS", Foreground = Brushes.Orange, Margin = new Thickness(0,8,0,4) });
        var addRow = new StackPanel { Orientation = Orientation.Horizontal };
        addRow.Children.Add(_entry);
        var add = new Button { Content = "Add Channel" }; add.Click += (_,_) => AddChannel(); addRow.Children.Add(add);
        _entry.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { AddChannel(); e.Handled = true; } };
        top.Children.Add(addRow); top.Children.Add(_channelRows);
        top.Children.Add(_status); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _feed.Foreground = Brushes.White; _feed.Background = new SolidColorBrush(Color.FromRgb(20,23,32));
        root.Children.Add(_feed); Content = root;
                  Loaded += (_,_) => { if (Tag as string == "iron-chrome") return; Tag = "iron-chrome"; IronWindowChrome.Attach(this, "Intel"); };
          PopoutLayout.Track("Intel", this);
DrawChannels(); _timer.Tick += (_,_) => Poll();
        Loaded += (_,_) => { Poll(); _timer.Start(); }; Closed += (_,_) => _timer.Stop();
    }
    private void SaveChannels()
    {
        try { Directory.CreateDirectory(AppInfo.DataFolder); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(_channels)); }
        catch { Log.Debug("INTEL", "channel settings save failed"); }
    }
    private void AddChannel()
    {
        string channel = _entry.Text.Trim();
        if (channel.Length is < 1 or > 100 || channel.Contains(Path.DirectorySeparatorChar) || channel.Contains(Path.AltDirectorySeparatorChar))
        { _status.Text = "Enter a valid channel name"; return; }
        if (!_channels.Contains(channel, StringComparer.OrdinalIgnoreCase)) { _channels.Add(channel); SaveChannels(); }
        _entry.Clear(); DrawChannels();
    }
    private void DrawChannels()
    {
        _channelRows.Children.Clear();
        foreach (string channel in _channels.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0,0,0,4) };
            var remove = new Button { Content = "×", Padding = new Thickness(8,2,8,2) };
            remove.Click += (_,_) => { _channels.Remove(channel); SaveChannels(); DrawChannels(); };
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            row.Children.Add(new TextBlock { Text = channel, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray });
            _channelRows.Children.Add(row);
        }
    }
    private bool Selected(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        return _channels.Any(c => stem.Equals(c, StringComparison.OrdinalIgnoreCase) || stem.StartsWith(c + "_", StringComparison.OrdinalIgnoreCase));
    }
    private void Poll()
    {
        try
        {
            string folder = _folder.Text.Trim();
            if (!Directory.Exists(folder)) { _status.Text = "Chat log folder not found"; return; }
            if (_channels.Count == 0) { _status.Text = "Add an intel channel to monitor"; return; }
            var files = Directory.EnumerateFiles(folder, "*.txt").Select(x => new FileInfo(x))
                .Where(x => x.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-2) && Selected(x.Name))
                .OrderByDescending(x => x.LastWriteTimeUtc).Take(250);
            int count = 0;
            foreach (var file in files)
            {
                if (!_offsets.TryGetValue(file.FullName, out long offset)) { _offsets[file.FullName] = file.Length; continue; }
                if (file.Length < offset) offset = 0;
                long available = file.Length - offset;
                if (available < 2) continue;
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(offset, SeekOrigin.Begin);
                int readSize = (int)Math.Min(available, 256 * 1024);
                if (readSize % 2 != 0) readSize--;
                if (readSize <= 0) continue;
                byte[] bytes = new byte[readSize]; int read = stream.Read(bytes,0,readSize); read -= read % 2;
                string text = Encoding.Unicode.GetString(bytes,0,read);
                int end = text.LastIndexOf('\n');
                if (end < 0) continue;
                string complete = text[..(end+1)];
                _offsets[file.FullName] = offset + Encoding.Unicode.GetByteCount(complete);
                string channel = _channels.First(c => Path.GetFileNameWithoutExtension(file.Name).Equals(c,StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(file.Name).StartsWith(c+"_",StringComparison.OrdinalIgnoreCase));
                foreach (string raw in complete.Split('\n'))
                {
                    string line = raw.Trim(); if (!line.StartsWith('[') || !line.Contains('>')) continue;
                    // The same broadcast may be written into several clients' channel logs.
                    // Deduplicate by channel + original EVE timestamp + sender + message, not by arrival time.
                    string key = channel.ToLowerInvariant() + "|" + line;
                    if (!_seen.Add(key)) { Log.Debug("INTEL", "duplicate log line skipped"); continue; }
                    _seenOrder.Enqueue(key);
                    while (_seenOrder.Count > 10000) _seen.Remove(_seenOrder.Dequeue());
                    _feed.Items.Insert(0, channel + "  " + line);
                    if (_feed.Items.Count > 500) _feed.Items.RemoveAt(_feed.Items.Count - 1);
                    count++;
                }
            }
            _status.Text = "Monitoring " + _channels.Count + " selected channel(s)";
            if (count > 0) Log.Debug("INTEL", "new unique chat lines=" + count);
        }
        catch (Exception ex) { _status.Text = "Unable to read chat logs"; Log.Debug("INTEL", "poll failed: " + ex.GetType().Name); }
    }
}
