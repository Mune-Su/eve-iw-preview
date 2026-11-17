using System.Diagnostics;
using System.Text;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

public sealed class EveWindowService
{
    private readonly Dictionary<uint, DateTime> _startTimes = new();
    private readonly Dictionary<nint, DateTime> _firstSeen = new();
    private readonly Dictionary<nint, string> _titles = new();
    private readonly HashSet<uint> _pidsThisScan = new();

    /// <summary>All EVE clients (logged in or not), in the order they were opened.</summary>
    public IReadOnlyList<EveClientWindow> FindClients()
    {
        var found = new List<EveClientWindow>();
        _pidsThisScan.Clear();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)) return true;

            string title;
            if (NativeMethods.IsHungAppWindow(h))
            {
                // never ask a frozen client for its title (that call waits for it): use the last one we saw
                if (!_titles.TryGetValue(h, out var cached)) return true;
                title = cached;
            }
            else
            {
                int len = NativeMethods.GetWindowTextLength(h);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(h, sb, sb.Capacity);
                title = sb.ToString().Trim();
            }

            if (!IsEveTitle(title)) return true;
            _titles[h] = title;
            found.Add(new EveClientWindow(h, title, ParseCharacter(title), OpenedAt(h)));
            return true;
        }, 0);

        // forget clients that have closed
        foreach (uint pid in _startTimes.Keys.Where(p => !_pidsThisScan.Contains(p)).ToList()) _startTimes.Remove(pid);
        var handles = found.Select(f => f.Handle).ToHashSet();
        foreach (nint gone in _firstSeen.Keys.Where(k => !handles.Contains(k)).ToList()) _firstSeen.Remove(gone);
        foreach (nint gone in _titles.Keys.Where(k => !handles.Contains(k)).ToList()) _titles.Remove(gone);

        return found.OrderBy(x => x.Started).ThenBy(x => (long)x.Handle).ToList();
    }

    /// <summary>Process start time of the client; if Windows won't tell us, the time we first saw the window.</summary>
    private DateTime OpenedAt(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
        {
            _pidsThisScan.Add(pid);
            if (_startTimes.TryGetValue(pid, out var cached)) return cached;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                DateTime started = process.StartTime.ToUniversalTime();
                _startTimes[pid] = started;
                return started;
            }
            catch { }
        }
        if (!_firstSeen.TryGetValue(hwnd, out var seen))
        {
            seen = DateTime.UtcNow;
            _firstSeen[hwnd] = seen;
        }
        return seen;
    }

    private static bool IsEveTitle(string title) =>
        title.Equals("EVE", StringComparison.OrdinalIgnoreCase) ||
        title.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase);

    private static string ParseCharacter(string title) =>
        title.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase) ? title[6..].Trim() : title;

    public bool Activate(nint handle) => Activate(handle, out _);

    /// <summary>
    /// Brings an EVE client to the front without ever waiting on it. A frozen ("not responding") client is refused so
    /// cycling can carry on past it. The normal call is tried first (instant when Windows allows it); the slower
    /// fallbacks only run if Windows refuses. <paramref name="how"/> says what happened.
    /// </summary>
    public bool Activate(nint handle, out string how)
    {
        long t0 = Log.Ms;
        bool ok = ActivateCore(handle, out how);
        Log.Debug("FOCUS", $"activate {Log.Win(handle)} -> {(ok ? "OK" : "FAILED")} via '{how}' in {Log.Ms - t0} ms; foreground now {Log.Win(NativeMethods.GetForegroundWindow())}");
        return ok;
    }

    private bool ActivateCore(nint handle, out string how)
    {
        how = "window gone";
        if (!NativeMethods.IsWindow(handle)) return false;
        if (NativeMethods.IsHungAppWindow(handle))
        {
            how = "not responding";
            return false;
        }
        Log.Debug("FOCUS", $"  start: target {Log.Win(handle)}, foreground {Log.Win(NativeMethods.GetForegroundWindow())}");
        // asynchronous: a busy client can never block this app
        if (NativeMethods.IsIconic(handle))
        {
            Log.Debug("WINDOW", $"  target is minimised -> ShowWindowAsync(RESTORE) {Log.Win(handle)}");
            NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_RESTORE);
        }
        // fast path
        bool set = NativeMethods.SetForegroundWindow(handle);
        Log.Debug("FOCUS", $"  step 1 SetForegroundWindow returned {set}");
        if (set)
        {
            // Windows said yes. The switch can take a few ms to land (nothing, or still the old client, in front);
            // that is NOT a refusal, so no fallback runs. The manager treats this client as active until Windows
            // confirms it (v0.9.6).
            // Return immediately. PreviewManager treats the target as active until Windows confirms it, so waiting
            // here only throttles held-key cycling. This keeps each repeat free to start the next switch at once.
            how = NativeMethods.GetForegroundWindow() == handle ? "direct" : "direct (Windows finishing the switch)";
            return true;
        }

        nint foreground = NativeMethods.GetForegroundWindow();
        bool foregroundHung = foreground != 0 && NativeMethods.IsHungAppWindow(foreground);
        uint foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();

        Log.Debug("FOCUS", $"  step 1 refused by Windows; foreground {Log.Win(foreground)}{(foregroundHung ? " is HUNG (skipping input attach)" : "")}");
        // attaching input queues to a frozen window can hang input for everything, so never do it then
        if (!foregroundHung)
        {
            // fallback 1: join the focused window's input queue to the target's (the way EVE-O Preview does it)
            if (foregroundThread != 0 && targetThread != 0 && foregroundThread != targetThread &&
                NativeMethods.AttachThreadInput(foregroundThread, targetThread, true))
            {
                bool ok1;
                try { ok1 = NativeMethods.SetForegroundWindow(handle); }
                finally { NativeMethods.AttachThreadInput(foregroundThread, targetThread, false); }
                if (ok1) { Settled(handle, true); how = "attach foreground->target"; return true; }
                Log.Debug("FOCUS", "  step 2 attach foreground->target: refused");
            }

            // fallback 2: join this app's input queue to the focused window's
            if (foregroundThread != 0 && foregroundThread != thisThread &&
                NativeMethods.AttachThreadInput(thisThread, foregroundThread, true))
            {
                bool ok2;
                try { ok2 = NativeMethods.SetForegroundWindow(handle); }
                finally { NativeMethods.AttachThreadInput(thisThread, foregroundThread, false); }
                if (ok2) { Settled(handle, true); how = "attach this->foreground"; return true; }
                Log.Debug("FOCUS", "  step 3 attach this->foreground: refused");
            }
        }

        // fallback 3: a quick Alt tap makes Windows allow the focus change
        Log.Debug("FOCUS", "  step 4 sending an Alt tap (EVE may briefly see Alt)");
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        bool ok3 = NativeMethods.SetForegroundWindow(handle);
        if (ok3) { Settled(handle, true); how = "alt tap"; return true; }

        // last resort
        Log.Debug("FOCUS", "  step 5 SwitchToThisWindow (last resort)");
        NativeMethods.SwitchToThisWindow(handle, true);
        if (Settled(handle, true)) { how = "switch-to-this-window"; return true; }

        how = "refused by Windows";
        return false;
    }

    /// <summary>
    /// True once the target is in front. A switch Windows has accepted takes a few ms to land: for a moment nothing
    /// (0x0) or still the old client is in front. Wait up to 60 ms for it; never treat either as a refusal.
    /// </summary>
    private static bool Settled(nint handle, bool accepted)
    {
        nint fg = NativeMethods.GetForegroundWindow();
        if (fg == handle) return true;
        if (!accepted) return false;
        // real 60 ms limit: Thread.Sleep(5) can sleep ~15 ms each, which froze input for ~180 ms per press
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (fg != handle && sw.ElapsedMilliseconds < 60)
        {
            Thread.Sleep(1);
            fg = NativeMethods.GetForegroundWindow();
        }
        if (fg != handle) Log.Debug("FOCUS", $"  settle: foreground still {Log.Win(fg)} after 60 ms (switch still landing)");
        return fg == handle;
    }
}



