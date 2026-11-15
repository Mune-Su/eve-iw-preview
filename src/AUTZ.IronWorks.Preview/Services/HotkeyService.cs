using System.Windows.Interop;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Global hotkeys via RegisterHotKey, received on a hidden message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("AUTZ IronWorks Preview hotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public bool Register(string? text, Action action)
    {
        if (!HotkeyParser.TryParse(text, out uint modifiers, out uint virtualKey)) return false;
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, virtualKey)) return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
