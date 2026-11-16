using System.Diagnostics;
using System.Text;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

public sealed class EveWindowService
{
    private readonly Dictionary<uint, DateTime> _startTimes = new();
    private readonly Dictionary<nint, DateTime> _firstSeen = new();
    private readonly HashSet<uint> _pidsThisScan = new();

    /// <summary>All EVE clients (logged in or not), in the order they were opened.</summary>
    public IReadOnlyList<EveClientWindow> FindClients()
    {
        var found = new List<EveClientWindow>();
        _pidsThisScan.Clear();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)) return true;
            int len = NativeMethods.GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(h, sb, sb.Capacity);
            string title = sb.ToString().Trim();
            if (!IsEveTitle(title)) return true;
            found.Add(new EveClientWindow(h, title, ParseCharacter(title), OpenedAt(h)));
            return true;
        }, 0);

        // forget clients that have closed
        foreach (uint pid in _startTimes.Keys.Where(p => !_pidsThisScan.Contains(p)).ToList()) _startTimes.Remove(pid);
        var handles = found.Select(f => f.Handle).ToHashSet();
        foreach (nint gone in _firstSeen.Keys.Where(k => !handles.Contains(k)).ToList()) _firstSeen.Remove(gone);

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

    /// <summary>Brings an EVE client to the front. Attaches to the current foreground thread so Windows allows it.</summary>
    public bool Activate(nint handle)
    {
        if (NativeMethods.IsIconic(handle)) NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread &&
                        NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
        NativeMethods.SetForegroundWindow(handle);
        if (attached) NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);

        // Windows sometimes refuses; SwitchToThisWindow is the stronger fallback
        if (NativeMethods.GetForegroundWindow() != handle)
            NativeMethods.SwitchToThisWindow(handle, true);
        return NativeMethods.GetForegroundWindow() == handle;
    }
}
