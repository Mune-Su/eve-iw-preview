// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
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
    bool CrystalBroke,
    bool FleetFollow,
    bool MiningCompression,
    bool ConversationInvite,
    bool Decloak,
    bool FleetRegroup,
    bool WarpScram,
    bool WarpBubble,
    bool CloakFailed,
    bool FleetInvite,
    int MiningIdleSeconds,
    string[] Keywords,
    bool SystemName,
    bool MiningManualStop);

/// <summary>Kind is one of LogMonitorService.Kinds, or "keyword".</summary>
public sealed record LogEvent(string Character, string Kind, string Message);

/// <summary>
/// Reads EVE's own log files (Documents\EVE\logs) for alerts and the current solar system.
/// Read only. Works on a background timer and hands results to the UI thread.
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
    private readonly Dictionary<string, DateTime> _miningInstantAt = new(StringComparer.OrdinalIgnoreCase);   // when the game itself said mining stopped
    private readonly Dictionary<string, DateTime> _offNoted = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex MinerRx = new(@"\bminers?\b", RegexOptions.IgnoreCase);
    private readonly HashSet<string> _olderLogs = new(StringComparer.OrdinalIgnoreCase);   // old logs of a character that has a newer one: not watched
    private const int MaxLogFiles = 80;                                                    // per kind (game / chat)
    private readonly Dictionary<string, Queue<double>> _miningGaps = new(StringComparer.OrdinalIgnoreCase);   // recent seconds between mining lines, per character
    private Timer? _timer;
    private LogConfig _config = new(false, "", "", 1000, true, false, false, false, false, false, false, false, false, false, false, false, 60, Array.Empty<string>(), false, true);

    /// <summary>Every alert kind, in the order they are listed in Configuration.</summary>
    public static readonly string[] Kinds =
    {
        "attack", "warp-scram", "warp-bubble", "decloak", "cloak-failed", "crystal", "mining", "compression",
        "fleet-follow", "regroup", "fleet-invite", "conversation", "keyword"
    };
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
            _miningInstantAt.Clear();
            _miningGaps.Clear();
            _olderLogs.Clear();
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

            if (DateTime.UtcNow - _lastScan > TimeSpan.FromSeconds(1))
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
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // characters whose newest log is already taken
        foreach (string path in RecentFiles(folder, since, chat))              // newest first, so a new login is never the one skipped
        {
            if (_tails.TryGetValue(path, out var existing))
            {
                if (!covered.Add(existing.Listener))
                {
                    _tails.Remove(path);                                       // a newer log of the same character exists now
                    _olderLogs.Add(path);
                    Log.Debug("LOGMON", $"stopped watching an older {(chat ? "chat" : "game")} log of '{existing.Listener}' (a newer one exists)");
                }
                continue;
            }
            if (_olderLogs.Contains(path)) continue;
            if (_tails.Values.Count(t => t.IsChat == chat) >= MaxLogFiles)
            {
                Log.Debug("LOGMON", $"file limit reached ({MaxLogFiles} {(chat ? "chat" : "game")} logs); remaining files skipped");
                return;
            }
            var tail = OpenTail(new FileInfo(path), chat);
            if (tail is null) continue;
            if (!covered.Add(tail.Listener)) { _olderLogs.Add(path); continue; }   // only the newest log of each character is needed
            _tails[path] = tail;
            Log.Debug("LOGMON", $"watching {(chat ? "chat" : "game")} log for '{tail.Listener}', starting at byte {tail.Position}");
            Log.Trace("LOGMON", $"  file '{path}'");
        }

        // drop files that haven't been written to for a long while
        foreach (var stale in _tails.Values.Where(t => t.IsChat == chat && File.Exists(t.Path) &&
                     File.GetLastWriteTime(t.Path) < since.AddHours(-12)).ToList())
            _tails.Remove(stale.Path);
    }

    /// <summary>The log files changed since the given time. Only recent files are looked at, so years of old logs cost nothing.</summary>
    private static List<string> RecentFiles(string folder, DateTime sinceLocal, bool chat)
    {
        DateTime sinceUtc = sinceLocal.ToUniversalTime();
        var query = new FileSystemEnumerable<(string Path, DateTime Written)>(folder, (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.LastWriteTimeUtc.UtcDateTime))
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory &&
                entry.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                (!chat || entry.FileName.StartsWith("Local_", StringComparison.OrdinalIgnoreCase)) &&
                entry.LastWriteTimeUtc.UtcDateTime >= sinceUtc
        };
        return query.OrderByDescending(f => f.Written).Select(f => f.Path).ToList();
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
        Log.Trace("EVELOG", $"{(tail.IsChat ? "chat" : "game")} '{tail.Listener}': {line}");
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
            var now = DateTime.UtcNow;
            bool justAnnounced = _miningInstantAt.TryGetValue(tail.Listener, out var announced) && (now - announced).TotalSeconds < 3;
            if (!_lastMining.ContainsKey(tail.Listener)) Log.Debug("ALERT", $"mining line seen for '{tail.Listener}': watching for it to stop");
            if (_lastMining.TryGetValue(tail.Listener, out var before) && !_miningFired.Contains(tail.Listener))
            {
                // learn the mining cycle: the longest gap between the last few mining lines (lasers on the same ship can be staggered)
                if (!_miningGaps.TryGetValue(tail.Listener, out var gaps)) _miningGaps[tail.Listener] = gaps = new Queue<double>();
                gaps.Enqueue((now - before).TotalSeconds);
                while (gaps.Count > 8) gaps.Dequeue();
                if (gaps.Count == 2) Log.Debug("ALERT", $"mining cycle learned for '{tail.Listener}': about {gaps.Max():0} s");
            }
            else if (_miningFired.Contains(tail.Listener) && !justAnnounced)
            {
                _miningGaps.Remove(tail.Listener);       // mining started again after a stop: learn the cycle afresh
            }
            _lastMining[tail.Listener] = now;
            if (!justAnnounced) _miningFired.Remove(tail.Listener);   // a last line of the cycle the game just announced must not re-arm
        }

        // The game says outright when a miner stops: the hold is full, or the rock is gone. Alert at once, no waiting.
        if (kind == "notify" && MiningStopReason(text) is string stopReason)
        {
            if (config.MiningIdle) MiningStopNow(tail.Listener, stopReason, text);
            else NoteOff("mining", tail.Listener);
        }

        if (config.Attack && kind == "combat" &&
            (IncomingRx.IsMatch(text) || text.Contains(" misses you", StringComparison.OrdinalIgnoreCase)))
        {
            Post(tail, "attack", text);
        }

        // The game log only holds messages from the game itself (never chat), so player names like "Crystal" can't trigger these.
        // The text to match comes from real game-log lines.
        if (config.CrystalBroke && text.Contains("deactivates due to the destruction", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("crystal", StringComparison.OrdinalIgnoreCase))
            Post(tail, "crystal", text);
        if (config.FleetFollow && text.StartsWith("Following ", StringComparison.OrdinalIgnoreCase) &&
            text.Contains(" in warp", StringComparison.OrdinalIgnoreCase))
            Post(tail, "fleet-follow", text);
        if (text.StartsWith("Successfully compressed", StringComparison.OrdinalIgnoreCase))
        {
            if (config.MiningCompression) Post(tail, "compression", text);
            else NoteOff("compression", tail.Listener);
        }
        if (config.ConversationInvite && text.Contains("is inviting you to a conversation", StringComparison.OrdinalIgnoreCase))
            Post(tail, "conversation", text);
        if (config.Decloak && ContainsAny(text, "cloak deactivates", "cloaking device deactivates", "decloaked"))
            Post(tail, "decloak", text);
        if (config.FleetRegroup && text.StartsWith("Regrouping", StringComparison.OrdinalIgnoreCase))
            Post(tail, "regroup", text);
        if (config.WarpScram && ContainsAny(text, "warp scramble attempt", "warp disruption attempt") &&
            text.Contains("to you", StringComparison.OrdinalIgnoreCase))
            Post(tail, "warp-scram", text);
        if (config.WarpBubble && text.Contains("warp disruption zone", StringComparison.OrdinalIgnoreCase))
            Post(tail, "warp-bubble", text);
        if (config.CloakFailed && ContainsAny(text, "cloaking systems are unable to activate", "can't cloak", "cannot cloak"))
            Post(tail, "cloak-failed", text);
        if (config.FleetInvite && (text.Contains("wants you to join their fleet", StringComparison.OrdinalIgnoreCase) ||
            (text.Contains("fleet", StringComparison.OrdinalIgnoreCase) && text.Contains("invit", StringComparison.OrdinalIgnoreCase))))
            Post(tail, "fleet-invite", text);

        foreach (string keyword in config.Keywords)
        {
            if (keyword.Length > 0 && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                Post(tail, "keyword", text);
                break;
            }
        }
    }

    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private const double MiningAutoMargin = 3;      // seconds of slack on top of the learned mining cycle

    private void CheckMiningIdle(LogConfig config)
    {
        if (!config.MiningIdle || !config.MiningManualStop) return;
        foreach (var pair in _lastMining.ToList())
        {
            if (_miningFired.Contains(pair.Key)) continue;
            double limit;
            if (config.MiningIdleSeconds > 0) limit = config.MiningIdleSeconds;
            else if (_miningGaps.TryGetValue(pair.Key, out var gaps) && gaps.Count >= 2) limit = Math.Clamp(gaps.Max(), 3, 600) + MiningAutoMargin;   // 0 = automatic: the learned cycle plus a few seconds
            else limit = 90;                                                                                                                             // cycle not learned yet
            if ((DateTime.UtcNow - pair.Value).TotalSeconds < limit) continue;
            _miningFired.Add(pair.Key);
            Log.Debug("ALERT", $"mining stopped: no mining line for {(DateTime.UtcNow - pair.Value).TotalSeconds:0} s (limit {limit:0} s{(config.MiningIdleSeconds > 0 ? "" : ", automatic")})");
            Post(new LogEvent(pair.Key, "mining", "has stopped mining"));
        }
    }

    /// <summary>The text of a game line that ends mining, or null.</summary>
    private static string? MiningStopReason(string text)
    {
        if (text.Contains("cargo hold is full", StringComparison.OrdinalIgnoreCase)) return "hold full";
        if (text.Contains("deactivates", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("destruction", StringComparison.OrdinalIgnoreCase) &&      // a broken crystal has its own alert
            MinerRx.IsMatch(text)) return "module deactivated";
        return null;
    }

    /// <summary>Mining stopped, straight from the game. One alert per stop: the silence timer and the other module stay quiet.</summary>
    private void MiningStopNow(string listener, string reason, string text)
    {
        if (_miningFired.Contains(listener))
        {
            Log.Debug("ALERT", $"mining stopped ({reason}) for '{listener}': already alerted");
            return;
        }
        _miningFired.Add(listener);
        _miningInstantAt[listener] = DateTime.UtcNow;
        Log.Debug("ALERT", $"mining stopped ({reason}) for '{listener}': {text}");
        Post(new LogEvent(listener, "mining", "has stopped mining"));
    }

    /// <summary>A game line for an alert that is switched off: said once a minute in the normal log, so a missing alert isn't a mystery.</summary>
    private void NoteOff(string kind, string listener)
    {
        string key = kind + "|" + listener;
        if (_offNoted.TryGetValue(key, out var at) && (DateTime.UtcNow - at).TotalSeconds < 60) return;
        _offNoted[key] = DateTime.UtcNow;
        Log.Write($"A '{kind}' line was read for '{listener}' but that alert is off (tick it under Events To Watch).");
    }

    private void Post(Tail tail, string kind, string text)
    {
        Log.Debug("ALERT", $"'{tail.Listener}' game-log line matched '{kind}': {text}");
        Post(new LogEvent(tail.Listener, kind, text));
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




