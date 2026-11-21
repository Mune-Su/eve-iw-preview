using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Two logs in %LOCALAPPDATA%\EVE-IronWorks-Preview:
///  - preview.log : the normal diagnostics log (errors, one line per hotkey switch). Always on, kept small.
///  - debug.log   : the ADMIN log. Only written while "Detailed logging" is on (Settings > About > Diagnostics, or
///                  the tray menu). One continuous file, or (Diagnostics setting) a new debug-DATE_TIME.log every
///                  time detailed logging is turned on. It records every step the app takes: hotkeys, each focus attempt and which method
///                  worked, every minimise / restore / maximise it sends to a client, thumbnails shown / hidden /
///                  created / closed, and every focus change Windows reports, all with millisecond timestamps.
/// Debug lines are queued and written by a background thread, so logging never slows down a switch or the hotkey hook.
///
/// PRIVACY: a log can be sent to someone, so nothing personal is written. Every line goes through Scrub() first:
///  - character names and profile names become a short code (Pilot-3FA9C1, Profile-B07E42): the same name always gives
///    the same code on this PC, but the name can't be worked out from it
///  - the title of any window that isn't an EVE client is never written (browser tabs and documents would show up there)
///  - your Windows user name, PC name and folder paths (Documents, AppData ...) are replaced with placeholders
///  - e-mail addresses and the EVE game-log file names (they hold the character's ID number) are removed
/// </summary>
internal static class Log
{
    private static readonly string Folder = AppInfo.DataFolder;
    private static readonly string FilePath = Path.Combine(Folder, "preview.log");
    public static readonly string DebugPath = Path.Combine(Folder, "debug.log");
    private static readonly string DebugOldPath = Path.Combine(Folder, "debug.old.log");
    private const long DebugMaxBytes = 4_000_000;      // then debug.log becomes debug.old.log and a new one starts
    private const long SessionMaxBytes = 25_000_000;   // a per-session file just stops growing here
    private const int SessionFilesKept = 20;

    // each queued line remembers which file it belongs to, so a switch between files can never mix them up
    private static readonly ConcurrentQueue<(string Path, string Line)> Queue = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Thread? _writer;
    private static volatile bool _verbose;
    private static volatile bool _perSession;
    private static volatile string _activePath = DebugPath;

    // ----- privacy: what gets replaced, and with what -----
    private static readonly object NameLock = new();
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase);    // name -> "Pilot" or "Profile"
    private static Regex? _nameRegex;
    private static bool _nameRegexValid;
    private static string _salt = Guid.NewGuid().ToString("N");     // a saved per-install value replaces this at start-up
    private static readonly Regex EmailRx = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}");
    private static readonly Regex GameLogFileRx = new(@"\b\d{8}_\d{6}(?:_\d+)?\.txt\b", RegexOptions.IgnoreCase);
    private static readonly (Regex Rx, string To)[] Environmental = BuildEnvironmental();

    /// <summary>True while detailed (admin) logging is on.</summary>
    public static bool Verbose
    {
        get => _verbose;
        set
        {
            if (_verbose == value) return;
            if (value)
            {
                _activePath = _perSession ? NewSessionPath() : DebugPath;
                _verbose = true;
                StartWriter();
                Debug("LOG", $"===== detailed logging ON  ({AppInfo.Name} v{AppInfo.Version}, Windows {Environment.OSVersion.Version}, pid {Environment.ProcessId}) =====");
            }
            else
            {
                Queue.Enqueue((_activePath, Stamp("LOG", "===== detailed logging OFF =====")));
                Signal.Set();
                _verbose = false;
            }
        }
    }

    /// <summary>
    /// True = every time detailed logging is turned on it starts a new file (debug-DATE_TIME.log).
    /// False = one continuous debug.log. Changing it while logging is on moves to the other kind of file at once.
    /// </summary>
    public static bool PerSessionFiles
    {
        get => _perSession;
        set
        {
            if (_perSession == value) return;
            _perSession = value;
            if (!_verbose) return;

            string old = _activePath;
            string next = value ? NewSessionPath() : DebugPath;
            Queue.Enqueue((old, Stamp("LOG", "===== log mode changed: carrying on in " + Path.GetFileName(next) + " =====")));
            _activePath = next;
            Debug("LOG", $"===== carried on from {Path.GetFileName(old)} ({AppInfo.Name} v{AppInfo.Version}, pid {Environment.ProcessId}) =====");
        }
    }

    /// <summary>The debug log to open: the one being written, else the newest one on disk. Null if there is none.</summary>
    public static string? LatestDebugPath()
    {
        try
        {
            if (_verbose && File.Exists(_activePath)) return _activePath;
            if (!Directory.Exists(Folder)) return null;
            return Directory.GetFiles(Folder, "debug-*.log")
                .Concat(File.Exists(DebugPath) ? new[] { DebugPath } : Array.Empty<string>())
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string NewSessionPath()
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string path = Path.Combine(Folder, $"debug-{stamp}.log");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"debug-{stamp}-{n}.log");
        PruneSessionFiles(path);
        return path;
    }

    /// <summary>Keeps the newest session logs; older ones are deleted so the folder doesn't fill up.</summary>
    private static void PruneSessionFiles(string keep)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            var old = Directory.GetFiles(Folder, "debug-*.log")
                .Where(f => !f.Equals(keep, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Skip(SessionFilesKept - 1);
            foreach (string f in old) File.Delete(f);
        }
        catch { }
    }

    public static void Write(string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 120_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + Scrub(text) + Environment.NewLine);
        }
        catch { }
        if (_verbose) Debug("INFO", text);
    }

    /// <summary>One line in debug.log. Does nothing (and costs nothing) while detailed logging is off.</summary>
    public static void Debug(string area, string text)
    {
        if (!_verbose) return;
        Queue.Enqueue((_activePath, Stamp(area, text)));
        Signal.Set();
    }

    /// <summary>
    /// Same, for Log.Debug($"..."): while detailed logging is off the text is never built and nothing inside the braces
    /// is evaluated (Log.Win and the other window lookups would otherwise run on every focus change and every switch).
    /// </summary>
    public static void Debug(string area, DebugLine line)
    {
        if (!_verbose) return;
        Queue.Enqueue((_activePath, Stamp(area, line.Text)));
        Signal.Set();
    }

    /// <summary>Milliseconds since the app started: handy for timing a sequence of steps.</summary>
    public static long Ms => Clock.ElapsedMilliseconds;

    /// <summary>High-volume operational trace. Completely inert while Detailed Logging is off.</summary>
    public static void Trace(string area, string text)
    {
      if (!_verbose) return;
      Queue.Enqueue((_activePath, Stamp(area, text)));
      Signal.Set();
    }

    public static void TraceException(string source, Exception exception)
    {
      if (_verbose) Trace("EXCEPT", source + ": " + exception);
    }

    private static string Stamp(string area, string text) =>
        $"{DateTime.Now:HH:mm:ss.fff}  +{Clock.ElapsedMilliseconds,9}ms  t{Environment.CurrentManagedThreadId,-3} {area,-8} {text}";

    /// <summary>
    /// A window handle for the log: "0x1A2B3C 'EVE - Pilot-3FA9C1'" for an EVE client, "[this app]" for our own windows and
    /// "[other window]" for anything else. The title of a window that isn't an EVE client is never written.
    /// </summary>
    public static string Win(nint hwnd)
    {
        if (hwnd == 0) return "0x0 (none)";
        try
        {
            if (!NativeMethods.IsWindow(hwnd)) return $"0x{hwnd:X} (gone)";
            if (NativeMethods.IsHungAppWindow(hwnd)) return $"0x{hwnd:X} (NOT RESPONDING)";
            string state = NativeMethods.IsIconic(hwnd) ? " min" : NativeMethods.IsZoomed(hwnd) ? " max" : "";

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return $"0x{hwnd:X} [this app]{state}";

            int len = NativeMethods.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(Math.Max(1, len + 1));
            if (len > 0) NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString().Trim();

            if (title.Equals("EVE", StringComparison.OrdinalIgnoreCase)) return $"0x{hwnd:X} 'EVE'{state}";
            if (title.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase))
            {
                string name = title[6..].Trim();
                return $"0x{hwnd:X} 'EVE - {Pilot(name)}'{state}";
            }
            return $"0x{hwnd:X} [other window]{state}";
        }
        catch
        {
            return $"0x{hwnd:X}";
        }
    }

    // ---------- privacy ----------

    /// <summary>A random value kept with the app's settings, mixed into every name code so the codes can't be matched against a list of names.</summary>
    public static string Salt
    {
        set
        {
            lock (NameLock)
            {
                _salt = string.IsNullOrEmpty(value) ? _salt : value;
                foreach (string name in Aliases.Keys.ToList()) Aliases[name] = CodeFor(name, Kinds.TryGetValue(name, out var kind) ? kind : "Pilot");
                _nameRegexValid = false;
            }
        }
    }

    /// <summary>Tell the log that this is a character name, so it is replaced by a code wherever it turns up.</summary>
    public static void RegisterPilot(string? name) => Register(name, "Pilot");

    /// <summary>Same for a profile name (the name "Default" is left alone).</summary>
    public static void RegisterProfile(string? name)
    {
        if (name is not null && name.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase)) return;
        Register(name, "Profile");
    }

    /// <summary>The code a character name is shown as in the logs.</summary>
    public static string Pilot(string name)
    {
        RegisterPilot(name);
        lock (NameLock) return Aliases.TryGetValue(name.Trim(), out var code) ? code : "Pilot-?";
    }

    private static void Register(string? name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Length < 2 || name.StartsWith('[')) return;           // "[client 2]" style keys aren't names
        lock (NameLock)
        {
            if (Aliases.ContainsKey(name)) return;
            Kinds[name] = kind;
            Aliases[name] = CodeFor(name, kind);
            _nameRegexValid = false;
        }
    }

    /// <summary>The code a command-line value of an EVE client is shown as in the detailed log (salted, so the value can't be worked out).</summary>
    public static string ArgCode(string value) => CodeFor(value, "Arg");

    private static string CodeFor(string name, string kind)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(_salt + "|" + kind + "|" + name.ToLowerInvariant()));
        return kind + "-" + Convert.ToHexString(hash, 0, 3);
    }

    private static (Regex, string)[] BuildEnvironmental()
    {
        var list = new List<(string Text, string To, bool Word)>();

        void AddPath(Environment.SpecialFolder folder, string token)
        {
            try
            {
                string path = Environment.GetFolderPath(folder).TrimEnd('\\', '/');
                if (path.Length >= 4) list.Add((path, token, false));
            }
            catch { }
        }
        void AddWord(string? text, string token)
        {
            if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 3) list.Add((text.Trim(), token, true));
        }

        AddPath(Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%");
        AddPath(Environment.SpecialFolder.ApplicationData, "%APPDATA%");
        AddPath(Environment.SpecialFolder.MyDocuments, "%DOCUMENTS%");
        AddPath(Environment.SpecialFolder.UserProfile, "%USERPROFILE%");
        AddWord(Environment.UserName, "<user>");
        AddWord(Environment.MachineName, "<pc>");
        AddWord(Environment.UserDomainName, "<domain>");

        // folders first (longest first, so "...\\Documents" wins over "...\\sierr"), then the bare names
        return list
            .OrderBy(x => x.Word ? 1 : 0)
            .ThenByDescending(x => x.Text.Length)
            .GroupBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(x => (
                new Regex(x.Word ? @"(?<![\p{L}\p{N}_])" + Regex.Escape(x.Text) + @"(?![\p{L}\p{N}_])" : Regex.Escape(x.Text),
                          RegexOptions.IgnoreCase),
                x.To))
            .ToArray();
    }

    private static Regex? NameRegex()
    {
        lock (NameLock)
        {
            if (_nameRegexValid) return _nameRegex;
            _nameRegexValid = true;
            if (Aliases.Count == 0) return _nameRegex = null;
            string pattern = string.Join("|", Aliases.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
            return _nameRegex = new Regex(@"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase);
        }
    }

    /// <summary>Takes anything personal out of a line before it is written to a log file.</summary>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            foreach (var (rx, to) in Environmental) text = rx.Replace(text, to);
            text = EmailRx.Replace(text, "<email>");
            text = GameLogFileRx.Replace(text, "<game-log-file>");
            var names = NameRegex();
            if (names is not null)
            {
                text = names.Replace(text, m =>
                {
                    lock (NameLock) return Aliases.TryGetValue(m.Value, out var code) ? code : m.Value;
                });
            }
            return text;
        }
        catch
        {
            return "(line withheld: it couldn't be checked for personal information)";
        }
    }

    /// <summary>Deletes every log written by versions before names were removed from them.</summary>
    public static void DeleteOldLogs()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (string f in Directory.GetFiles(Folder, "debug*.log")) File.Delete(f);
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch { }
    }

    public static void ClearDebug()
    {
        try
        {
            if (File.Exists(DebugPath)) File.Delete(DebugPath);
            if (File.Exists(DebugOldPath)) File.Delete(DebugOldPath);
            if (Directory.Exists(Folder))
            {
                foreach (string f in Directory.GetFiles(Folder, "debug-*.log")) File.Delete(f);
            }
        }
        catch { }
    }

    private static void StartWriter()
    {
        if (_writer is not null) return;
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "debug-log", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    private static void WriterLoop()
    {
        var batch = new StringBuilder();
        while (true)
        {
            Signal.WaitOne(500);

            // write the queued lines in order; a run of lines for the same file goes out in one write
            string? path = null;
            batch.Clear();
            while (Queue.TryDequeue(out var item))
            {
                if (path is not null && !item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    Flush(path, batch.ToString());
                    batch.Clear();
                }
                path = item.Path;
                batch.Append(Scrub(item.Line)).Append(Environment.NewLine);
            }
            if (path is not null && batch.Length > 0) Flush(path, batch.ToString());
        }
    }

    private static void Flush(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (path.Equals(DebugPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(DebugPath) && new FileInfo(DebugPath).Length > DebugMaxBytes)
                {
                    if (File.Exists(DebugOldPath)) File.Delete(DebugOldPath);
                    File.Move(DebugPath, DebugOldPath);
                }
            }
            else if (File.Exists(path) && new FileInfo(path).Length > SessionMaxBytes)
            {
                return;                                   // a very long session: stop here rather than fill the disk
            }
            File.AppendAllText(path, text);
        }
        catch { }
    }
}

/// <summary>The part of Log.Debug($"...") that skips all the work while detailed logging is off.</summary>
[InterpolatedStringHandler]
internal ref struct DebugLine
{
    private readonly StringBuilder? _text;

    public DebugLine(int literalLength, int formattedCount, out bool isEnabled)
    {
        isEnabled = Log.Verbose;
        _text = isEnabled ? new StringBuilder(literalLength + formattedCount * 12) : null;
    }

    public void AppendLiteral(string value) => _text?.Append(value);

    public void AppendFormatted<T>(T value) => _text?.Append(value);

    public void AppendFormatted<T>(T value, string? format)
    {
        if (_text is null) return;
        if (value is IFormattable formattable) _text.Append(formattable.ToString(format, null));
        else _text.Append(value);
    }

    public void AppendFormatted<T>(T value, int alignment)
    {
        if (_text is null) return;
        string s = value?.ToString() ?? "";
        _text.Append(alignment < 0 ? s.PadRight(-alignment) : s.PadLeft(alignment));
    }

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        if (_text is null) return;
        string s = value is IFormattable formattable ? formattable.ToString(format, null) : value?.ToString() ?? "";
        _text.Append(alignment < 0 ? s.PadRight(-alignment) : s.PadLeft(alignment));
    }

    internal string Text => _text?.ToString() ?? "";
}

