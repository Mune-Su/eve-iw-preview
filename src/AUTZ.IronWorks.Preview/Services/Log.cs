using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Two logs in %LOCALAPPDATA%\EVE-IronWorks-Preview:
///  - preview.log : the normal diagnostics log (errors, one line per hotkey switch). Always on, kept small.
///  - debug.log   : the ADMIN log. Only written while "Detailed logging" is on (Settings > About > Diagnostics, or
///                  the tray menu). It records every step the app takes: hotkeys, each focus attempt and which method
///                  worked, every minimise / restore / maximise it sends to a client, thumbnails shown / hidden /
///                  created / closed, and every focus change Windows reports, all with millisecond timestamps.
/// Debug lines are queued and written by a background thread, so logging never slows down a switch or the hotkey hook.
/// </summary>
internal static class Log
{
    private static readonly string Folder = AppInfo.DataFolder;
    private static readonly string FilePath = Path.Combine(Folder, "preview.log");
    public static readonly string DebugPath = Path.Combine(Folder, "debug.log");
    private static readonly string DebugOldPath = Path.Combine(Folder, "debug.old.log");
    private const long DebugMaxBytes = 4_000_000;      // then debug.log becomes debug.old.log and a new one starts

    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Thread? _writer;
    private static volatile bool _verbose;

    /// <summary>True while detailed (admin) logging is on.</summary>
    public static bool Verbose
    {
        get => _verbose;
        set
        {
            if (_verbose == value) return;
            _verbose = value;
            if (value)
            {
                StartWriter();
                Debug("LOG", $"===== detailed logging ON  ({AppInfo.Name} v{AppInfo.Version}, Windows {Environment.OSVersion.Version}, pid {Environment.ProcessId}) =====");
            }
            else
            {
                Queue.Enqueue(Stamp("LOG", "===== detailed logging OFF ====="));
                Signal.Set();
            }
        }
    }

    public static void Write(string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 120_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text + Environment.NewLine);
        }
        catch { }
        if (_verbose) Debug("INFO", text);
    }

    /// <summary>One line in debug.log. Does nothing (and costs nothing) while detailed logging is off.</summary>
    public static void Debug(string area, string text)
    {
        if (!_verbose) return;
        Queue.Enqueue(Stamp(area, text));
        Signal.Set();
    }

    /// <summary>Milliseconds since the app started: handy for timing a sequence of steps.</summary>
    public static long Ms => Clock.ElapsedMilliseconds;

    private static string Stamp(string area, string text) =>
        $"{DateTime.Now:HH:mm:ss.fff}  +{Clock.ElapsedMilliseconds,9}ms  t{Environment.CurrentManagedThreadId,-3} {area,-8} {text}";

    /// <summary>"0x1A2B3C 'EVE - Name'" for a window handle (the title is skipped for a frozen window).</summary>
    public static string Win(nint hwnd)
    {
        if (hwnd == 0) return "0x0 (none)";
        try
        {
            if (!NativeMethods.IsWindow(hwnd)) return $"0x{hwnd:X} (gone)";
            if (NativeMethods.IsHungAppWindow(hwnd)) return $"0x{hwnd:X} (NOT RESPONDING)";
            int len = NativeMethods.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(Math.Max(1, len + 1));
            if (len > 0) NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string own = pid == (uint)Environment.ProcessId ? " [this app]" : "";
            string state = NativeMethods.IsIconic(hwnd) ? " min" : NativeMethods.IsZoomed(hwnd) ? " max" : "";
            return $"0x{hwnd:X} '{sb}'{state}{own}";
        }
        catch
        {
            return $"0x{hwnd:X}";
        }
    }

    public static void ClearDebug()
    {
        try
        {
            if (File.Exists(DebugPath)) File.Delete(DebugPath);
            if (File.Exists(DebugOldPath)) File.Delete(DebugOldPath);
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
            batch.Clear();
            while (Queue.TryDequeue(out var line)) batch.Append(line).Append(Environment.NewLine);
            if (batch.Length == 0) continue;
            try
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(DebugPath) && new FileInfo(DebugPath).Length > DebugMaxBytes)
                {
                    if (File.Exists(DebugOldPath)) File.Delete(DebugOldPath);
                    File.Move(DebugPath, DebugOldPath);
                }
                File.AppendAllText(DebugPath, batch.ToString());
            }
            catch { }
        }
    }
}
