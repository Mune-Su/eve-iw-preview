using System.IO;
using System.IO.Enumeration;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace AUTZ.IronWorks.Preview.Services;

public sealed record LogConfig(
    bool Enabled,
    string GameFolder,
    string ChatFolder,
    int PollMs,
    bool Attack,
    bool MiningIdle,
    int MiningIdleSeconds,
    string[] Keywords,
    bool SystemName);

/// <summary>Kind is "attack", "mining" or "keyword".</summary>
public sealed record LogEvent(string Character, string Kind, string Message);

/// <summary>
/// Reads the log files the EVE client itself writes for the player (Documents\EVE\logs): game logs for combat, mining
/// and keyword alerts, and the Local chat log for the current solar system. It only ever reads; new lines are picked
/// up as they are written. File work happens on a background timer and results are handed to the UI thread.
/// </summary>
public sealed class LogMonitorService : IDisposable
{
    private sealed class Tail
    {
        public string Path = "";
        public string Listener = "";
        public long Position;
        public Encoding Encoding = Encoding.UTF8;
        public bool IsChat;
    }

    private static readonly Regex LineRx = new(@"^\[\s*[\d\.\s:]+\]\s*\((\w+)\)\s*(.*)$");
    private static readonly Regex TagRx = new("<[^>]*>");
    private static readonly Regex ListenerRx = new(@"Listener:\s*(.+)");
    private static readonly Regex SystemRx = new(@"Channel changed to Local\s*:\s*(.+?)\s*$");
    private static readonly Regex IncomingRx = new(@"^\d+\s+from\s");

    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<string, Tail> _tails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastMining = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _miningFired = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private LogConfig _config = new(false, "", "", 1000, true, false, 60, Array.Empty<string>(), false);
    private bool _initialScanDone;
    private DateTime _lastScan = DateTime.MinValue;

    public event Action<LogEvent>? EventRaised;

    /// <summary>Character name, solar system.</summary>
    public event Action<string, string>? SystemChanged;

    public string Status { get; private set; } = "Log monitoring is off.";

    public static string DefaultGameFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs", "Gamelogs");

    public static string DefaultChatFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs", "Chatlogs");

    public void Configure(LogConfig config)
    {
        lock (_gate)
        {
            bool restart = config.Enabled != _config.Enabled || config.PollMs != _config.PollMs ||
                           config.GameFolder != _config.GameFolder || config.ChatFolder != _config.ChatFolder ||
                           config.SystemName != _config.SystemName;
            _config = config;
            if (!restart) return;

            _timer?.Dispose();
            _timer = null;
            _tails.Clear();
            _lastMining.Clear();
            _miningFired.Clear();
            _initialScanDone = false;
            _lastScan = DateTime.MinValue;

            if (!config.Enabled)
            {
                Status = "Log monitoring is off.";
                return;
            }

            Status = "Starting...";
            int poll = Math.Clamp(config.PollMs, 250, 10000);
            _timer = new Timer(_ => Tick(), null, 300, poll);
        }
    }

    private void Tick()
    {
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            var config = _config;
            if (!config.Enabled) return;

            if (DateTime.UtcNow - _lastScan > TimeSpan.FromSeconds(5))
            {
                _lastScan = DateTime.UtcNow;
                ScanFolder(string.IsNullOrWhiteSpace(config.GameFolder) ? DefaultGameFolder : config.GameFolder, false);
                if (config.SystemName)
                    ScanFolder(string.IsNullOrWhiteSpace(config.ChatFolder) ? DefaultChatFolder : config.ChatFolder, true);
                _initialScanDone = true;

                int games = _tails.Values.Count(t => !t.IsChat);
                int chats = _tails.Values.Count(t => t.IsChat);
                Status = games == 0 && chats == 0
                    ? "No recent log files found. Check the folders below and that EVE is writing logs."
                    : $"Watching {games} game log(s)" + (config.SystemName ? $" and {chats} chat log(s)." : ".");
            }

            foreach (var tail in _tails.Values.ToList()) ReadNew(tail, config);
            CheckMiningIdle(config);
        }
        catch (Exception ex)
        {
            Log.Write("Log monitor: " + ex.Message);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    // ---------- finding files ----------

    private void ScanFolder(string folder, bool chat)
    {
        if (!Directory.Exists(folder)) return;

        var since = DateTime.Now.AddHours(-12);
        foreach (string path in RecentFiles(folder, since, chat))
        {
            if (_tails.ContainsKey(path)) continue;
            if (_tails.Count > 80) return;
            var tail = OpenTail(new FileInfo(path), chat);
            if (tail is not null) _tails[path] = tail;
        }

        // drop files that haven't been written to for a long while
        foreach (var stale in _tails.Values.Where(t => t.IsChat == chat && File.Exists(t.Path) &&
                     File.GetLastWriteTime(t.Path) < since.AddHours(-12)).ToList())
            _tails.Remove(stale.Path);
    }

    /// <summary>
    /// The log files written to since the given time. The game-log folder grows by a file per session for years, so the
    /// listing is filtered while the folder is read: only the few recent files are ever turned into objects.
    /// </summary>
    private static List<string> RecentFiles(string folder, DateTime sinceLocal, bool chat)
    {
        DateTime sinceUtc = sinceLocal.ToUniversalTime();
        var query = new FileSystemEnumerable<string>(folder, (ref FileSystemEntry entry) => entry.ToFullPath())
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory &&
                entry.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                (!chat || entry.FileName.StartsWith("Local_", StringComparison.OrdinalIgnoreCase)) &&
                entry.LastWriteTimeUtc.UtcDateTime >= sinceUtc
        };
        return query.ToList();
    }

    private Tail? OpenTail(FileInfo file, bool chat)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[2048];
            int read = stream.Read(head, 0, head.Length);
            var encoding = DetectEncoding(head, read, chat);
            string text = encoding.GetString(head, 0, read);

            var match = ListenerRx.Match(text);
            if (!match.Success) return null;

            var tail = new Tail
            {
                Path = file.FullName,
                Listener = match.Groups[1].Value.Trim(),
                Encoding = encoding,
                IsChat = chat,
                // files that already existed when monitoring started are followed from their end (no replay);
                // files created afterwards are read from the start
                Position = _initialScanDone ? 0 : stream.Length
            };

            if (chat && !_initialScanDone) AnnounceLastSystem(tail);
            return tail;
        }
        catch
        {
            return null;
        }
    }

    private static Encoding DetectEncoding(byte[] bytes, int count, bool chat)
    {
        if (count >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (count >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
        if (count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
        if (chat && count >= 2 && bytes[1] == 0) return Encoding.Unicode;
        return Encoding.UTF8;
    }

    /// <summary>Works out the system a character is in right now from the end of its Local chat log.</summary>
    private void AnnounceLastSystem(Tail tail)
    {
        try
        {
            using var stream = new FileStream(tail.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - 300_000);
            if (start % 2 == 1) start--;
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - start];
            int read = stream.Read(buffer, 0, buffer.Length);
            string text = tail.Encoding.GetString(buffer, 0, read);

            string? last = null;
            foreach (string line in text.Split('\n'))
            {
                var match = SystemRx.Match(line.TrimEnd('\r'));
                if (match.Success) last = match.Groups[1].Value;
            }
            if (last is not null) PostSystem(tail.Listener, last);
        }
        catch { }
    }

    // ---------- reading new lines ----------

    private void ReadNew(Tail tail, LogConfig config)
    {
        try
        {
            using var stream = new FileStream(tail.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            if (length < tail.Position) tail.Position = 0;     // the file was replaced or truncated
            if (length == tail.Position) return;

            int toRead = (int)Math.Min(length - tail.Position, 512 * 1024);
            if (tail.Encoding is UnicodeEncoding && toRead % 2 == 1) toRead--;
            if (toRead <= 0) return;

            stream.Seek(tail.Position, SeekOrigin.Begin);
            var buffer = new byte[toRead];
            int read = stream.Read(buffer, 0, toRead);
            string text = tail.Encoding.GetString(buffer, 0, read);

            int lastNewline = text.LastIndexOf('\n');
            if (lastNewline < 0) return;                          // wait for the rest of the line

            string complete = text.Substring(0, lastNewline + 1);
            tail.Position += tail.Encoding.GetByteCount(complete);

            foreach (string raw in complete.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length > 0) HandleLine(tail, line, config);
            }
        }
        catch { }
    }

    private void HandleLine(Tail tail, string line, LogConfig config)
    {
        if (tail.IsChat)
        {
            var system = SystemRx.Match(line);
            if (system.Success) PostSystem(tail.Listener, system.Groups[1].Value);
            return;
        }

        var match = LineRx.Match(line);
        if (!match.Success) return;

        string kind = match.Groups[1].Value.ToLowerInvariant();
        string text = WebUtility.HtmlDecode(TagRx.Replace(match.Groups[2].Value, "")).Trim();

        if (kind == "mining")
        {
            _lastMining[tail.Listener] = DateTime.UtcNow;
            _miningFired.Remove(tail.Listener);
        }

        if (config.Attack && kind == "combat" &&
            (IncomingRx.IsMatch(text) || text.Contains(" misses you", StringComparison.OrdinalIgnoreCase)))
        {
            Post(new LogEvent(tail.Listener, "attack", text));
        }

        foreach (string keyword in config.Keywords)
        {
            if (keyword.Length > 0 && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                Post(new LogEvent(tail.Listener, "keyword", text));
                break;
            }
        }
    }

    private void CheckMiningIdle(LogConfig config)
    {
        if (!config.MiningIdle) return;
        var limit = TimeSpan.FromSeconds(Math.Max(10, config.MiningIdleSeconds));
        foreach (var pair in _lastMining.ToList())
        {
            if (_miningFired.Contains(pair.Key) || DateTime.UtcNow - pair.Value < limit) continue;
            _miningFired.Add(pair.Key);
            Post(new LogEvent(pair.Key, "mining", "has stopped mining"));
        }
    }

    private void Post(LogEvent logEvent) =>
        _dispatcher.BeginInvoke(new Action(() => EventRaised?.Invoke(logEvent)));

    private void PostSystem(string character, string system) =>
        _dispatcher.BeginInvoke(new Action(() => SystemChanged?.Invoke(character, system)));

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}




