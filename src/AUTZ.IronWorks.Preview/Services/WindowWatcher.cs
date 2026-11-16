using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Windows tells us the moment the focused window changes or a window's title changes, so highlights,
/// hiding and the login-to-character swap react instantly instead of waiting for a polling timer.
/// The callbacks arrive on the UI thread (the hooks are created there).
/// </summary>
internal sealed class WindowWatcher : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback;   // kept in a field so it isn't garbage collected
    private nint _foregroundHook;
    private nint _titleHook;

    public event Action<nint>? ForegroundChanged;
    public event Action<nint>? TitleChanged;

    public WindowWatcher()
    {
        _callback = OnEvent;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            0, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        _titleHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE,
            0, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
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
        if (_titleHook != 0) NativeMethods.UnhookWinEvent(_titleHook);
        _foregroundHook = 0;
        _titleHook = 0;
    }
}


