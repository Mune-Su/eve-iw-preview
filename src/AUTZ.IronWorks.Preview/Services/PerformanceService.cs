using System.Diagnostics;
using System.Runtime.InteropServices;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Performance options, each with its own switch, all off by default:
/// High Performance power plan, client-in-front priority, background priority, background efficiency mode.
/// Turning one off puts things back straight away. After a crash, they're put back on the next start.
/// </summary>
internal sealed class PerformanceService
{
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(nint userRootPowerKey, ref Guid schemeGuid);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(nint process, int infoClass, ref PowerThrottlingState info, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private const int ProcessPowerThrottling = 4;
    private const uint ThrottleExecutionSpeed = 1;
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    // what this app has set on each client process, so it can be put back
    private readonly Dictionary<uint, (ProcessPriorityClass Priority, bool Efficient)> _applied = new();
    private readonly HashSet<uint> _refused = new();         // processes Windows won't let us change (logged once)
    private bool _planFailedLogged;

    /// <summary>
    /// Brings everything in line with the settings. Returns true when the saved restore info changed (the caller saves app.json).
    /// </summary>
    public bool Update(PerformanceSettings p, IReadOnlyCollection<uint> clientPids, uint frontPid)
    {
        bool dirty = UpdatePowerPlan(p, clientPids.Count > 0);

        bool raise = p.Enabled && p.RaiseFrontClient;
        bool lower = p.Enabled && p.LowerBackgroundClients;
        bool eco = p.Enabled && p.EfficiencyBackground;

        // clients no longer in the list (closed, or the switcher was turned off for them): put back if still running
        foreach (uint pid in _applied.Keys.Where(k => !clientPids.Contains(k)).ToList())
        {
            PutBackQuietly(pid);
            _applied.Remove(pid);
        }
        _refused.RemoveWhere(pid => !clientPids.Contains(pid));

        foreach (uint pid in clientPids)
        {
            if (_refused.Contains(pid)) continue;
            bool front = pid == frontPid;
            var priority = front ? (raise ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal)
                                 : (lower ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal);
            bool efficient = !front && eco;
            bool known = _applied.TryGetValue(pid, out var now);
            if (!known && priority == ProcessPriorityClass.Normal && !efficient) continue;      // never touched, and nothing to do
            if (known && now.Priority == priority && now.Efficient == efficient) continue;
            if (Set(pid, priority, efficient))
            {
                if (priority == ProcessPriorityClass.Normal && !efficient) _applied.Remove(pid);
                else _applied[pid] = (priority, efficient);
            }
        }

        bool changed = _applied.Count > 0;
        if (p.PrioritiesChanged != changed)
        {
            p.PrioritiesChanged = changed;
            dirty = true;
        }
        return dirty;
    }

    /// <summary>Puts everything back (app closing, or the master switch turned off).</summary>
    public bool RestoreAll(PerformanceSettings p)
    {
        foreach (uint pid in _applied.Keys.ToList()) PutBackQuietly(pid);
        _applied.Clear();
        bool dirty = RestorePlan(p);
        if (p.PrioritiesChanged)
        {
            p.PrioritiesChanged = false;
            dirty = true;
        }
        return dirty;
    }

    /// <summary>After a crash: clients that are still running may keep a changed priority. Put every EVE client back to normal.</summary>
    public bool CleanUpAfterCrash(PerformanceSettings p, IReadOnlyCollection<uint> clientPids)
    {
        if (!p.PrioritiesChanged) return false;
        Log.Write("Performance: the app didn't close normally last time. EVE client priorities were put back to normal.");
        foreach (uint pid in clientPids) PutBackQuietly(pid);
        p.PrioritiesChanged = false;
        return true;
    }

    private bool UpdatePowerPlan(PerformanceSettings p, bool anyClient)
    {
        bool want = p.Enabled && p.HighPerformancePlan && anyClient;
        if (!want) return RestorePlan(p);
        if (p.RestorePowerPlan.Length > 0) return false;           // already switched (or switched before a crash)

        Guid? current = ActivePlan();
        if (current is null || current.Value == HighPerformance) return false;
        Guid target = HighPerformance;
        uint error = PowerSetActiveScheme(0, ref target);
        if (error != 0)
        {
            if (!_planFailedLogged) Log.Write($"Performance: couldn't switch to the High Performance power plan (error {error}). This PC may not have it.");
            _planFailedLogged = true;
            return false;
        }
        p.RestorePowerPlan = current.Value.ToString();
        Log.Debug("PERF", "power plan -> High Performance");
        return true;
    }

    private static bool RestorePlan(PerformanceSettings p)
    {
        if (p.RestorePowerPlan.Length == 0) return false;
        if (Guid.TryParse(p.RestorePowerPlan, out Guid old))
        {
            // only put it back if the plan is still the one we set (you may have picked another one yourself since)
            if (ActivePlan() == HighPerformance)
            {
                uint error = PowerSetActiveScheme(0, ref old);
                if (error == 0) Log.Debug("PERF", "power plan put back");
                else Log.Write($"Performance: couldn't put the power plan back (error {error}).");
            }
        }
        p.RestorePowerPlan = "";
        return true;
    }

    private static Guid? ActivePlan()
    {
        if (PowerGetActiveScheme(0, out nint pointer) != 0 || pointer == 0) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    private static void PutBackQuietly(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            process.PriorityClass = ProcessPriorityClass.Normal;
            var state = new PowerThrottlingState { Version = 1 };
            SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        }
        catch
        {
            // it has closed
        }
    }

    private bool Set(uint pid, ProcessPriorityClass priority, bool efficient)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.PriorityClass != priority) process.PriorityClass = priority;
            var state = new PowerThrottlingState
            {
                Version = 1,
                ControlMask = efficient ? ThrottleExecutionSpeed : 0,      // 0 = let Windows decide again
                StateMask = efficient ? ThrottleExecutionSpeed : 0
            };
            SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
            Log.Debug("PERF", $"client pid {pid}: priority {priority}, efficiency mode {(efficient ? "on" : "off")}");
            return true;
        }
        catch (Exception ex)
        {
            if (_refused.Add(pid)) Log.Write($"Performance: couldn't change an EVE client's priority ({ex.Message}). If EVE runs as administrator, the app must too.");
            return false;
        }
    }
}
