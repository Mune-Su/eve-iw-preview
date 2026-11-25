using System.Runtime.InteropServices;
using System.Security.Principal;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>How much memory the PC is using right now.</summary>
public readonly record struct MemoryStatus(int LoadPercent, ulong TotalPhys, ulong AvailPhys, ulong TotalCommit, ulong AvailCommit)
{
    public bool IsValid => TotalPhys > 0;
    public ulong UsedPhys => TotalPhys >= AvailPhys ? TotalPhys - AvailPhys : 0;
    public ulong UsedCommit => TotalCommit >= AvailCommit ? TotalCommit - AvailCommit : 0;
    public int CommitPercent => TotalCommit == 0 ? 0 : (int)Math.Round(UsedCommit * 100.0 / TotalCommit);
}

/// <summary>What a cleaning run did.</summary>
/// <param name="TrimmedBytes">The memory the trimmed (or, in an estimate, trimmable) programs were holding just before the trim.</param>
public sealed record CleanResult(bool Ok, string Message, int Count, long FreedBytes, long TrimmedBytes = 0);

/// <summary>What Trim Programs would touch right now. Ratio is what earlier trims really gave back for each byte trimmed (0 = not known yet).</summary>
public sealed record TrimEstimate(long Bytes, int Programs, double Ratio);

/// <summary>Windows' cache lists: modified pages (written to disk by Clear system cache) and standby pages (cached files and data).</summary>
public readonly record struct CacheEstimate(bool Ok, long ModifiedBytes, long StandbyBytes);

/// <summary>What is on the clipboard. State: 0 empty, 1 sized (Bytes), 2 holds data of a kind that isn't measured, 3 another program is using it.</summary>
public readonly record struct ClipboardInfo(int State, long Bytes);

/// <summary>One line of the Top Programs list: every process with the same name added together.</summary>
/// <param name="Trend">"▲" / "▼" / "–": memory now compared with about a minute ago ("" until there are enough readings).</param>
/// <param name="Growing">True when the memory has kept growing for several minutes (a possible leak).</param>
public sealed record ProgramUsage(string Name, long Bytes, int Count, string Trend, bool Growing);

/// <summary>What a trim run is allowed to touch.</summary>
public sealed class TrimScope
{
    /// <summary>The program in front is never trimmed.</summary>
    public uint ForegroundPid;
    /// <summary>The EVE client processes.</summary>
    public IReadOnlySet<uint> EvePids = new HashSet<uint>();
    /// <summary>EVE clients that are in the background and have been idle long enough (only used in the "idle" EVE mode).</summary>
    public IReadOnlySet<uint> EveIdlePids = new HashSet<uint>();
    /// <summary>Only programs with these names (null = every program).</summary>
    public IReadOnlySet<string>? OnlyNames;
    /// <summary>The user named these programs: the smallest-size rule and the EVE rule don't apply.</summary>
    public bool Explicit;
    /// <summary>False = the never-trim list is ignored too (the Trim button next to a program).</summary>
    public bool HonourNeverList = true;
}

/// <summary>Average, lowest and highest memory use over the readings taken so far (while the Memory page is open and by the automatic checks).</summary>
public sealed class MemoryUsageStats
{
    private long _count;
    private double _sumUsed;
    private double _sumPercent;
    public long Samples => _count;
    public ulong MinUsed { get; private set; } = ulong.MaxValue;
    public ulong MaxUsed { get; private set; }
    public int MinPercent { get; private set; } = 100;
    public int MaxPercent { get; private set; }
    public ulong AvgUsed => _count == 0 ? 0 : (ulong)(_sumUsed / _count);
    public int AvgPercent => _count == 0 ? 0 : (int)Math.Round(_sumPercent / _count);

    public void Add(MemoryStatus s)
    {
        if (!s.IsValid) return;
        _count++;
        _sumUsed += s.UsedPhys;
        _sumPercent += s.LoadPercent;
        if (s.UsedPhys < MinUsed) MinUsed = s.UsedPhys;
        if (s.UsedPhys > MaxUsed) MaxUsed = s.UsedPhys;
        if (s.LoadPercent < MinPercent) MinPercent = s.LoadPercent;
        if (s.LoadPercent > MaxPercent) MaxPercent = s.LoadPercent;
    }
}

/// <summary>
/// Memory cleaning with Windows' own functions:
///  - trim: takes back memory programs aren't using (they get it back if needed). No admin needed.
///  - clear cache: flushes Windows' file cache. Needs admin.
///  - restart Windows Explorer.
/// Nothing runs unless you press a button or turn on an automatic option. Program names are never logged.
/// </summary>
internal static class MemoryCleaner
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool EmptyWorkingSet(nint process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetSystemFileCacheSize(nuint minimum, nuint maximum, uint flags);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int infoClass, nint buffer, int length, out int returnLength);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern int CountClipboardFormats();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(nint token, bool disableAll, ref TOKEN_PRIVILEGES state, uint length, nint previous, nint returned);

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    // SystemMemoryListInformation commands
    private const int SystemMemoryListInformation = 80;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    // Windows' own core processes are never touched (trimming them gains nothing and can make the desktop stutter),
    // and neither is the audio engine (trimming it can crackle the sound)
    private static readonly HashSet<string> AlwaysSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system", "smss", "csrss", "wininit",
        "services", "lsass", "lsaiso", "winlogon", "dwm", "fontdrvhost", "audiodg"
    };

    private static bool? _admin;

    public static bool IsAdministrator
    {
        get
        {
            _admin ??= CheckAdministrator();
            return _admin.Value;
        }
    }

    private static bool CheckAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static MemoryStatus Read()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return default;
        return new MemoryStatus((int)m.dwMemoryLoad, m.ullTotalPhys, m.ullAvailPhys, m.ullTotalPageFile, m.ullAvailPageFile);
    }

    public static string FormatMb(long bytes) => (bytes / 1048576.0).ToString("N0") + " MB";

    /// <summary>MB for anything from 1 MB up, KB below that, so a small clipboard doesn't read as "0 MB".</summary>
    public static string FormatSize(long bytes) =>
        bytes >= 1048576 ? FormatMb(bytes) :
        bytes >= 1024 ? (bytes / 1024.0).ToString("N0") + " KB" :
        bytes + " bytes";
    private static string FormatMb(ulong bytes) => (bytes / 1048576UL).ToString("N0") + " MB";

    // ---------- what is using the memory ----------

    /// <summary>Memory used by each program (same-name processes added together). Windows' core processes and this app are left out.</summary>
    public static Dictionary<string, (long Bytes, int Count)> Usage()
    {
        var map = new Dictionary<string, (long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);
        System.Diagnostics.Process[] all;
        try
        {
            all = System.Diagnostics.Process.GetProcesses();
        }
        catch
        {
            return map;
        }
        foreach (var process in all)
        {
            try
            {
                int pid = process.Id;
                if (pid <= 4 || pid == Environment.ProcessId) continue;
                string name = process.ProcessName;
                if (AlwaysSkip.Contains(name)) continue;
                long bytes = process.WorkingSet64;
                map[name] = map.TryGetValue(name, out var current) ? (current.Bytes + bytes, current.Count + 1) : (bytes, 1);
            }
            catch
            {
                // a program that closed while we were looking at it
            }
            finally
            {
                process.Dispose();
            }
        }
        return map;
    }

    // ---------- trimming ----------

    /// <summary>Trims the programs the scope allows. Never the program in front, Windows itself, this app, or the never-trim list. Runs in the background.</summary>
    public static CleanResult TrimWorkingSets(MemoryCleanerSettings settings, TrimScope scope, bool dryRun = false)
    {
        var before = Read();
        long trimmedBytes = 0;
        var excluded = scope.HonourNeverList ? ParseNames(settings.ExcludedPrograms) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long minBytes = scope.Explicit ? 0 : Math.Max(0, settings.MinTrimMb) * 1048576L;
        int trimmed = 0;
        int processes = 0;
        System.Diagnostics.Process[] all;
        try
        {
            all = System.Diagnostics.Process.GetProcesses();
        }
        catch (Exception ex)
        {
            return new CleanResult(false, "Couldn't list the running programs: " + ex.Message, 0, 0);
        }
        var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in all)
        {
            try
            {
                int pid = process.Id;
                if (pid <= 4 || pid == Environment.ProcessId || (uint)pid == scope.ForegroundPid) continue;
                string name = process.ProcessName;
                if (AlwaysSkip.Contains(name) || excluded.Contains(name)) continue;
                if (scope.OnlyNames is not null && !scope.OnlyNames.Contains(name)) continue;
                if (!scope.Explicit && (scope.EvePids.Contains((uint)pid) || name.Equals("exefile", StringComparison.OrdinalIgnoreCase)))
                {
                    // never: skipped. idle: only a background client that has been idle long enough. all: like any other program.
                    if (settings.EveIdleTrim) { if (!scope.EveIdlePids.Contains((uint)pid)) continue; }
                    else if (settings.SkipEveClients) continue;
                }
                if (minBytes > 0 && process.WorkingSet64 < minBytes) continue;
                nint handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, pid);
                if (handle == 0) continue;                          // not ours to trim (needs administrator)
                try
                {
                    // dryRun: only work out what a trim would reach (nothing is trimmed)
                    if (dryRun || EmptyWorkingSet(handle))
                    {
                        processes++;
                        counted.Add(name);
                        trimmedBytes += process.WorkingSet64;       // what it held just before the trim
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch
            {
                // a program that closed while we were looking at it
            }
            finally
            {
                process.Dispose();
            }
        }
        trimmed = counted.Count;
        if (dryRun) return new CleanResult(true, "", trimmed, 0, trimmedBytes);
        Thread.Sleep(300);                                          // let Windows settle before measuring
        var after = Read();
        long freed = (long)after.AvailPhys - (long)before.AvailPhys;
        string message = trimmed == 0
            ? "Nothing to trim" + (IsAdministrator ? "." : " (run as administrator to include more programs).")
            : $"Trimmed {trimmed} program{(trimmed == 1 ? "" : "s")} ({processes} process{(processes == 1 ? "" : "es")}); available memory {FormatMb(before.AvailPhys)} → {FormatMb(after.AvailPhys)}" +
              (freed > 0 ? $" (+{FormatMb(freed)})." : ".");
        return new CleanResult(trimmed > 0, message, trimmed, Math.Max(0, freed), trimmedBytes);
    }

    /// <summary>Reads "name=MB" limits, one per line. Names as in Task Manager, without .exe.</summary>
    public static bool TryParseLimits(string? text, out Dictionary<string, int> limits, out string error)
    {
        limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        error = "";
        foreach (string raw in (text ?? "").Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = raw.LastIndexOf('=');
            string name = eq > 0 ? raw[..eq].Trim() : "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (eq <= 0 || name.Length == 0 || !int.TryParse(raw[(eq + 1)..].Trim(), out int mb) || mb < 50 || mb > 65536)
            {
                error = "“" + raw + "” isn't a valid limit. Use name=megabytes (50 to 65536), one per line, for example: chrome=1500";
                return false;
            }
            limits[name] = mb;
        }
        return true;
    }

    // ---------- clearing the system cache ----------

    /// <summary>Flushes Windows' file cache and low-priority cached pages. Needs admin. Runs in the background.</summary>
    public static CleanResult ClearSystemCache()
    {
        if (!IsAdministrator)
            return new CleanResult(false, "Clearing the system cache needs the app to run as administrator.", 0, 0);
        var before = Read();
        int steps = 0;
        int lastError = 0;
        if (EnablePrivilege("SeIncreaseQuotaPrivilege"))
        {
            if (SetSystemFileCacheSize(nuint.MaxValue, nuint.MaxValue, 0)) steps++;
            else lastError = Marshal.GetLastWin32Error();
        }
        if (EnablePrivilege("SeProfileSingleProcessPrivilege"))
        {
            if (MemoryListCommand(MemoryFlushModifiedList)) steps++;          // modified pages are written to disk
            if (MemoryListCommand(MemoryPurgeLowPriorityStandbyList)) steps++; // low-priority cached pages are dropped
        }
        if (steps == 0)
        {
            return new CleanResult(false, lastError != 0
                ? $"Windows refused to clear the system cache (error {lastError})."
                : "Windows didn't give the app permission to clear the system cache.", 0, 0);
        }
        Thread.Sleep(300);
        var after = Read();
        long freed = (long)after.AvailPhys - (long)before.AvailPhys;
        string message = $"System cache cleared; available memory {FormatMb(before.AvailPhys)} → {FormatMb(after.AvailPhys)}" +
                         (freed > 0 ? $" (+{FormatMb(freed)})." : ".");
        return new CleanResult(true, message, steps, Math.Max(0, freed));
    }

    private static bool _listPrivilegeTried;

    /// <summary>How much Clear system cache can work with. Admin only. Cached pages already count as available, so the in-use figure barely moves.</summary>
    public static CacheEstimate ReadCacheLists()
    {
        if (!IsAdministrator) return default;
        if (!_listPrivilegeTried)
        {
            _listPrivilegeTried = true;
            EnablePrivilege("SeProfileSingleProcessPrivilege");
        }
        int size = 176;                         // SYSTEM_MEMORY_LIST_INFORMATION on 64-bit Windows: 22 counters of 8 bytes
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int status = NtQuerySystemInformation(SystemMemoryListInformation, buffer, size, out int needed);
            if (status != 0 && needed > size)
            {
                Marshal.FreeHGlobal(buffer);
                buffer = 0;
                size = needed;
                buffer = Marshal.AllocHGlobal(size);
                status = NtQuerySystemInformation(SystemMemoryListInformation, buffer, size, out _);
            }
            if (status != 0 || size < 104) return default;
            long page = Environment.SystemPageSize;
            long modified = Marshal.ReadInt64(buffer, 16) * page;                  // ModifiedPageCount
            long standby = 0;
            for (int offset = 40; offset < 104; offset += 8) standby += Marshal.ReadInt64(buffer, offset);   // PageCountByPriority[0..7]
            return new CacheEstimate(true, Math.Max(0, modified), Math.Max(0, standby * page));
        }
        catch
        {
            return default;
        }
        finally
        {
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint _clipSequence = uint.MaxValue;
    private static ClipboardInfo _clipLast;

    /// <summary>Size of what's on the clipboard. Only re-read when the clipboard changes. Call it from a background thread.</summary>
    public static ClipboardInfo ReadClipboard()
    {
        try
        {
            uint sequence = GetClipboardSequenceNumber();
            if (sequence == _clipSequence && _clipLast.State != 3) return _clipLast;
            ClipboardInfo info;
            if (CountClipboardFormats() == 0)
            {
                info = new ClipboardInfo(0, 0);
            }
            else
            {
                bool opened = false;
                for (int attempt = 0; attempt < 3 && !opened; attempt++)
                {
                    opened = OpenClipboard(0);
                    if (!opened) Thread.Sleep(15);
                }
                if (!opened)
                {
                    info = new ClipboardInfo(3, 0);                 // another program has it open: not remembered, so the next look tries again
                }
                else
                {
                    long bytes = 0;
                    bool any = false;
                    try
                    {
                        foreach (uint format in new uint[] { 13, 8 })      // CF_UNICODETEXT, CF_DIB (text and pictures: nearly all of the size)
                        {
                            if (!IsClipboardFormatAvailable(format)) continue;
                            nint data = GetClipboardData(format);
                            if (data == 0) continue;
                            bytes += (long)GlobalSize(data);
                            any = true;
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                    info = any ? new ClipboardInfo(1, bytes) : new ClipboardInfo(2, 0);
                }
            }
            _clipSequence = sequence;
            _clipLast = info;
            return info;
        }
        catch
        {
            return new ClipboardInfo(2, 0);
        }
    }

    private static bool MemoryListCommand(int command)
    {
        try
        {
            int value = command;
            return NtSetSystemInformation(SystemMemoryListInformation, ref value, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out nint token)) return false;      // TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return false;
            var state = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };      // SE_PRIVILEGE_ENABLED
            AdjustTokenPrivileges(token, false, ref state, 0, 0, 0);
            return Marshal.GetLastWin32Error() == 0;                                          // 1300 = not all privileges assigned
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // ---------- Windows Explorer ----------

    /// <summary>Closes and restarts Windows Explorer (taskbar, desktop, File Explorer). Runs in the background.</summary>
    public static CleanResult RestartExplorer()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch
                {
                    // already gone, or not ours to close
                }
                finally
                {
                    process.Dispose();
                }
            }
            for (int waited = 0; waited < 4000; waited += 250)
            {
                Thread.Sleep(250);
                var running = System.Diagnostics.Process.GetProcessesByName("explorer");
                bool back = running.Length > 0;
                foreach (var p in running) p.Dispose();
                if (back) return new CleanResult(true, "Windows Explorer restarted.", 1, 0);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
            return new CleanResult(true, "Windows Explorer restarted.", 1, 0);
        }
        catch (Exception ex)
        {
            return new CleanResult(false, "Couldn't restart Windows Explorer: " + ex.Message, 0, 0);
        }
    }

    /// <summary>Program names separated by commas, semicolons or new lines, without .exe.</summary>
    public static HashSet<string> ParseNames(string text)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in (text ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }
}

