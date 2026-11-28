using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class IntelWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListBox _feed = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray };
    private readonly TextBox _folder = new() { MinWidth = 350 };
    public IntelWindow()
    {
        Title = "Intel - Local Chat Logs"; Width = 850; Height = 600; Background = new SolidColorBrush(Color.FromRgb(13,15,22)); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(12) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "CHAT LOG FOLDER", Foreground = Brushes.Orange });
        _folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs", "Chatlogs");
        top.Children.Add(_folder);
        top.Children.Add(new TextBlock { Text = "New chat lines appear here. Local files only; no SSO.", Foreground = Brushes.LightGray, Margin = new Thickness(0, 6, 0, 6) });
        top.Children.Add(_status); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _feed.Foreground = Brushes.White; _feed.Background = new SolidColorBrush(Color.FromRgb(20,23,32));
        root.Children.Add(_feed); Content = root;
        _timer.Tick += (_, _) => Poll();
        Loaded += (_, _) => { Poll(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }
    private void Poll()
    {
        try
        {
            string folder = _folder.Text.Trim();
            if (!Directory.Exists(folder)) { _status.Text = "Chat log folder not found"; return; }
            var files = Directory.EnumerateFiles(folder, "*.txt").Select(x => new FileInfo(x))
                .Where(x => x.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-2)).OrderByDescending(x => x.LastWriteTimeUtc).Take(100);
            int count = 0;
            foreach (var file in files)
            {
                if (!_offsets.TryGetValue(file.FullName, out long offset)) { _offsets[file.FullName] = file.Length; continue; }
                if (file.Length < offset) offset = 0;
                if (file.Length == offset) continue;
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.Unicode, detectEncodingFromByteOrderMarks: offset == 0, bufferSize: 4096, leaveOpen: true);
                string text = reader.ReadToEnd();
                _offsets[file.FullName] = stream.Position;
                foreach (string line in text.Split('\n'))
                {
                    string entry = line.Trim();
                    if (entry.Length == 0 || !entry.StartsWith('[')) continue;
                    _feed.Items.Insert(0, $"{Path.GetFileNameWithoutExtension(file.Name).Split('_')[0]}  {entry}");
                    if (_feed.Items.Count > 500) _feed.Items.RemoveAt(_feed.Items.Count - 1);
                    count++;
                }
            }
            _status.Text = "Monitoring local chat logs";
            if (count > 0) Log.Debug("INTEL", $"new chat lines={count}");
        }
        catch (Exception ex) { _status.Text = "Unable to read chat logs"; Log.Debug("INTEL", ex.GetType().Name); }
    }
}
