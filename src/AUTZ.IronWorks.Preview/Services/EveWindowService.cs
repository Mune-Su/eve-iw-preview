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
    private readonly HashSet<uint> _diagLogged = new();      // clients whose start-up details are already in the detailed log
    private readonly List<EveClientWindow> _scan = new();
    private readonly List<uint> _gonePids = new();
    private readonly List<nint> _goneHandles = new();
    private readonly StringBuilder _titleBuffer = new(128);
    private readonly NativeMethods.EnumWindowsProc _enumProc;
    private const int LongestClientTitle = 80;     // "EVE - " + a character name is far shorter: a longer title can't be a client

    public EveWindowService()
    {
        _enumProc = OnWindow;                      // one delegate for the life of the app instead of one per scan
    }

    /// <summary>The process ids of the EVE clients the latest scan found.</summary>
    public IReadOnlySet<uint> ClientProcessIds => _pidsThisScan;

    /// <summary>All EVE clients (logged in or not), in the order they were opened.</summary>
    public IReadOnlyList<EveClientWindow> FindClients()
    {
        _scan.Clear();
        _pidsThisScan.Clear();
        if (!Log.Verbose && _diagLogged.Count > 0) _diagLogged.Clear();     // logging was switched off: log every client again next time
        NativeMethods.EnumWindows(_enumProc, 0);

        // forget clients that have closed (no allocations unless something actually closed)
        _gonePids.Clear();
        foreach (uint pid in _startTimes.Keys)
        {
            if (!_pidsThisScan.Contains(pid)) _gonePids.Add(pid);
        }
        foreach (uint pid in _gonePids) { _startTimes.Remove(pid); _diagLogged.Remove(pid); }

        _goneHandles.Clear();
        foreach (nint h in _firstSeen.Keys)
        {
            if (!IsFound(h)) _goneHandles.Add(h);
        }
        foreach (nint h in _goneHandles) _firstSeen.Remove(h);

        _goneHandles.Clear();
        foreach (nint h in _titles.Keys)
        {
            if (!IsFound(h)) _goneHandles.Add(h);
        }
        foreach (nint h in _goneHandles) _titles.Remove(h);

        return _scan.OrderBy(x => x.Started).ThenBy(x => (long)x.Handle).ToList();
    }

    private bool IsFound(nint handle)
    {
        for (int i = 0; i < _scan.Count; i++)
        {
            if (_scan[i].Handle == handle) return true;
        }
        return false;
    }

    private bool OnWindow(nint h, nint lParam)
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
            if (len < 3 || len > LongestClientTitle) return true;
            _titleBuffer.Clear();
            NativeMethods.GetWindowText(h, _titleBuffer, _titleBuffer.Capacity);
            if (!LooksLikeEve(_titleBuffer)) return true;          // almost every window stops here, without a string being made
            title = _titleBuffer.ToString().Trim();
        }

        if (!IsEveTitle(title)) return true;
        _titles.TryGetValue(h, out string? before);
        _titles[h] = title;
        string character = ParseCharacter(title);
        if (!title.Equals("EVE", StringComparison.OrdinalIgnoreCase)) Log.RegisterPilot(character);   // so the logs show a code, not the name
        if (Log.Verbose && before != title)
        {
            // detailed log: which client (process) is behind each window, and when a login screen turns into a character
            NativeMethods.GetWindowThreadProcessId(h, out uint titlePid);
            string now = title.Equals("EVE", StringComparison.OrdinalIgnoreCase) ? "login screen" : "'" + title + "'";
            string was = before is null ? "" : (before.Equals("EVE", StringComparison.OrdinalIgnoreCase) ? "login screen" : "'" + before + "'");
            string line = before is null
                ? $"window 0x{h:X} pid {titlePid}: appeared as {now}"
                : $"window 0x{h:X} pid {titlePid}: {was} -> {now}";
            Log.Debug("CLIENTINFO", line);
        }
        _scan.Add(new EveClientWindow(h, title, character, OpenedAt(h)));
        return true;
    }

    private static bool LooksLikeEve(StringBuilder text) =>
        text.Length >= 3 &&
        (text[0] == 'E' || text[0] == 'e') &&
        (text[1] == 'V' || text[1] == 'v') &&
        (text[2] == 'E' || text[2] == 'e');

    /// <summary>Process start time of the client; if Windows won't tell us, the time we first saw the window.</summary>
    private DateTime OpenedAt(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
        {
            _pidsThisScan.Add(pid);
            if (Log.Verbose && _diagLogged.Add(pid)) ClientDiagnostics.LogClient(pid, hwnd, "client found");
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
