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
public sealed record CleanResult(bool Ok, string Message, int Count, long FreedBytes);

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
/// Memory cleaning, done with functions built into Windows (the same ones the memory cleaner tools use):
///  - trim working sets: asks Windows to move the memory a program isn't actively using out of its working set (EmptyWorkingSet).
///    The program gets it back, on demand, if it needs it again. Works for your own programs without administrator rights.
///  - clear the system cache: flushes Windows' file cache (SetSystemFileCacheSize). Needs administrator rights.
/// Nothing here runs unless the user presses a button or switches an automatic option on.
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
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(nint token, bool disableAll, ref TOKEN_PRIVILEGES state, uint length, nint previous, nint returned);

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    // Windows' own core processes are never touched (trimming them gains nothing and can make the desktop stutter)
    private static readonly HashSet<string> AlwaysSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system", "smss", "csrss", "wininit",
        "services", "lsass", "lsaiso", "winlogon", "dwm", "fontdrvhost"
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

    /// <summary>
    /// Trims the working set of every program except: the one in front, EVE clients (if asked), the ones on the user's
    /// never-trim list, Windows' core processes, and this app. Runs on a background thread.
    /// </summary>
    public static CleanResult TrimWorkingSets(MemoryCleanerSettings settings, IReadOnlySet<uint> eveClientPids, uint foregroundPid)
    {
        var before = Read();
        var excluded = ParseNames(settings.ExcludedPrograms);
        int trimmed = 0;

        System.Diagnostics.Process[] all;
        try
        {
            all = System.Diagnostics.Process.GetProcesses();
        }
        catch (Exception ex)
        {
            return new CleanResult(false, "Couldn't list the running programs: " + ex.Message, 0, 0);
        }

        foreach (var process in all)
        {
            try
            {
                int pid = process.Id;
                if (pid <= 4 || pid == Environment.ProcessId || (uint)pid == foregroundPid) continue;

                string name = process.ProcessName;
                if (AlwaysSkip.Contains(name) || excluded.Contains(name)) continue;
                if (settings.SkipEveClients && (eveClientPids.Contains((uint)pid) || name.Equals("exefile", StringComparison.OrdinalIgnoreCase))) continue;

                nint handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, pid);
                if (handle == 0) continue;                          // not ours to trim (needs administrator)
                try
                {
                    if (EmptyWorkingSet(handle)) trimmed++;
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

        Thread.Sleep(300);                                          // let Windows settle before measuring
        var after = Read();
        long freed = (long)after.AvailPhys - (long)before.AvailPhys;

        string message = trimmed == 0
            ? "No programs could be trimmed" + (IsAdministrator ? "." : " (run as administrator to include more of them).")
            : $"Trimmed {trimmed} program{(trimmed == 1 ? "" : "s")}" + (freed > 0 ? $"; about {FormatMb(freed)} returned to available memory." : ".");
        return new CleanResult(trimmed > 0, message, trimmed, Math.Max(0, freed));
    }

    /// <summary>Flushes Windows' file cache. Needs administrator rights. Runs on a background thread.</summary>
    public static CleanResult ClearSystemCache()
    {
        if (!IsAdministrator)
            return new CleanResult(false, "Clearing the system cache needs the app to run as administrator.", 0, 0);

        var before = Read();
        if (!EnablePrivilege("SeIncreaseQuotaPrivilege"))
            return new CleanResult(false, "Windows didn't give the app permission to clear the system cache.", 0, 0);

        if (!SetSystemFileCacheSize(nuint.MaxValue, nuint.MaxValue, 0))
            return new CleanResult(false, $"Windows refused to clear the system cache (error {Marshal.GetLastWin32Error()}).", 0, 0);

        Thread.Sleep(300);
        var after = Read();
        long freed = (long)after.AvailPhys - (long)before.AvailPhys;
        return new CleanResult(true, freed > 0 ? $"System cache cleared; about {FormatMb(freed)} returned to available memory." : "System cache cleared.", 1, Math.Max(0, freed));
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

    private static HashSet<string> ParseNames(string text)
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
