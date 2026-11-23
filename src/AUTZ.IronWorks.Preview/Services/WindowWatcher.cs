using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Tells the app straight away when the window in front changes, or an EVE window's title changes.
/// Title changes are only watched inside EVE clients, so other programs don't wake the app.
/// </summary>
internal sealed class WindowWatcher : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback;   // kept in a field so it isn't garbage collected
    private readonly Dictionary<uint, nint> _titleHooks = new();
    private nint _foregroundHook;

    public event Action<nint>? ForegroundChanged;
    public event Action<nint>? TitleChanged;

    public WindowWatcher()
    {
        _callback = OnEvent;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            0, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Watches title changes inside exactly these processes (called after every client scan; nothing happens if they haven't changed).</summary>
    public void WatchProcesses(IReadOnlySet<uint> processIds)
    {
        foreach (uint pid in processIds)
        {
            if (pid == 0 || _titleHooks.ContainsKey(pid)) continue;
            nint hook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE,
                0, _callback, pid, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (hook != 0) _titleHooks[pid] = hook;
        }

        if (_titleHooks.Count > processIds.Count)
        {
            foreach (uint pid in _titleHooks.Keys.ToList())
            {
                if (processIds.Contains(pid)) continue;
                NativeMethods.UnhookWinEvent(_titleHooks[pid]);
                _titleHooks.Remove(pid);
            }
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        try
        {
            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
                ForegroundChanged?.Invoke(hwnd);
            else if (eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE && idObject == 0 && idChild == 0)
                TitleChanged?.Invoke(hwnd);
        }
        catch (Exception ex)
        {
            Log.Write("WindowWatcher: " + ex);
        }
    }

    public void Dispose()
    {
        if (_foregroundHook != 0) NativeMethods.UnhookWinEvent(_foregroundHook);
        foreach (nint hook in _titleHooks.Values) NativeMethods.UnhookWinEvent(hook);
        _titleHooks.Clear();
        _foregroundHook = 0;
    }
}
