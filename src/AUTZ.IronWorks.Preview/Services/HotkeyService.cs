using System.Runtime.InteropServices;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Global hotkeys using low-level keyboard and mouse hooks, so there are no restrictions: any key, any mouse button
/// (including the side buttons and the wheel), single keys with no modifier, chords of several keys, and
/// modifier-only hotkeys all work. The hook callbacks do almost nothing: a match is handed to the UI thread.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private sealed class Binding
    {
        public HotkeySpec Spec = new();
        public string Text = "";
        public Action Action = () => { };
        public int Weight => Spec.Inputs.Count * 4 + Spec.Modifiers.Count;
    }

    private static readonly int[] MouseVk = { 0, 0x01, 0x02, 0x04, 0x05, 0x06 };

    private readonly List<Binding> _bindings = new();
    private readonly NativeMethods.HookProc _keyProc;
    private readonly NativeMethods.HookProc _mouseProc;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HashSet<int> _downKeys = new();
    private readonly HashSet<int> _downMouse = new();
    private readonly HashSet<int> _swallowedKeys = new();
    private readonly HashSet<int> _swallowedMouse = new();
    private Binding? _armed;
    private nint _keyHook;
    private nint _mouseHook;

    /// <summary>While true nothing fires (used while the settings window is open and when the user suspends hotkeys).</summary>
    public bool Suspended { get; set; }

    /// <summary>True = the matched key or button is not swallowed: the program under it receives it as well.</summary>
    public bool PassThrough { get; set; }

    /// <summary>When set, hotkeys only fire while this returns true.</summary>
    public Func<bool>? Gate { get; set; }

    public HotkeyService()
    {
        _keyProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public bool Register(string? text, Action action)
    {
        if (!HotkeyParser.TryParse(text, out var spec)) return false;
        _bindings.Add(new Binding { Spec = spec, Text = text ?? "", Action = action });
        UpdateHooks();
        return true;
    }

    public void UnregisterAll()
    {
        _bindings.Clear();
        _downKeys.Clear();
        _downMouse.Clear();
        _swallowedKeys.Clear();
        _swallowedMouse.Clear();
        _armed = null;
        UpdateHooks();
    }

    private void UpdateHooks()
    {
        bool needKeys = _bindings.Count > 0;
        bool needMouse = _bindings.Any(b => b.Spec.UsesMouse);

        if (needKeys && _keyHook == 0)
        {
            _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc, NativeMethods.GetModuleHandle(null), 0);
            if (_keyHook == 0) Log.Write("Couldn't install the keyboard hook (error " + Marshal.GetLastWin32Error() + ")");
            else Log.Debug("HOTKEY", "keyboard hook installed");
        }
        else if (!needKeys && _keyHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_keyHook);
            _keyHook = 0;
        }

        if (needMouse && _mouseHook == 0)
        {
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, NativeMethods.GetModuleHandle(null), 0);
            if (_mouseHook == 0) Log.Write("Couldn't install the mouse hook (error " + Marshal.GetLastWin32Error() + ")");
            else Log.Debug("HOTKEY", "mouse hook installed");
        }
        else if (!needMouse && _mouseHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    private bool Allowed() => !Suspended && (Gate?.Invoke() ?? true);

    private void Fire(Binding binding)
    {
        long pressed = Log.Ms;
        Log.Debug("HOTKEY", $"'{binding.Text}' matched (swallowed: {!PassThrough})");
        _dispatcher.BeginInvoke(new Action(() =>
        {
            Log.Debug("HOTKEY", $"'{binding.Text}' running ({Log.Ms - pressed} ms after the key)");
            try { binding.Action(); }
            catch (Exception ex) { Log.Write("Hotkey action failed: " + ex.Message); }
        }));
    }

    // ---------- keyboard ----------

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                if (HandleKey((int)wParam, lParam)) return 1;
            }
            catch (Exception ex)
            {
                Log.Write("Keyboard hook: " + ex.Message);
            }
        }
        return NativeMethods.CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private bool HandleKey(int msg, nint lParam)
    {
        var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        if ((info.flags & 0x10) != 0) return false;          // injected by another program (including our own Alt tap)

        int vk = (int)info.vkCode;
        bool down = msg == 0x100 || msg == 0x104;
        bool up = msg == 0x101 || msg == 0x105;
        if (!down && !up) return false;

        if (down)
        {
            bool repeat = !_downKeys.Add(vk);
            if (repeat) return _swallowedKeys.Contains(vk);

            if (IsModifierVk(vk))
            {
                SyncModifiers();
                _armed = Allowed() ? FindModifierOnly() : null;
                return false;
            }

            _armed = null;
            if (!Allowed()) return false;
            SyncModifiers();
            var binding = Match(new HotkeyToken(HotkeyInputKind.Key, vk));
            if (binding is null) return false;

            Fire(binding);
            if (PassThrough) return false;
            _swallowedKeys.Add(vk);
            return true;
        }

        _downKeys.Remove(vk);
        if (_swallowedKeys.Remove(vk)) return true;

        if (IsModifierVk(vk) && _armed is not null)
        {
            var armed = _armed;
            _armed = null;
            Fire(armed);
        }
        return false;
    }

    // ---------- mouse ----------

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (int)wParam != 0x200)               // 0x200 = mouse move: never touch those
        {
            try
            {
                if (HandleMouse((int)wParam, lParam)) return 1;
            }
            catch (Exception ex)
            {
                Log.Write("Mouse hook: " + ex.Message);
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool HandleMouse(int msg, nint lParam)
    {
        var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        if ((info.flags & 1) != 0) return false;             // injected

        int code = 0;
        bool down = false, up = false, wheel = false;
        int delta = 0;
        int high = (int)((info.mouseData >> 16) & 0xFFFF);

        switch (msg)
        {
            case 0x201: code = 1; down = true; break;
            case 0x202: code = 1; up = true; break;
            case 0x204: code = 2; down = true; break;
            case 0x205: code = 2; up = true; break;
            case 0x207: code = 3; down = true; break;
            case 0x208: code = 3; up = true; break;
            case 0x20B: code = high == 1 ? 4 : 5; down = true; break;
            case 0x20C: code = high == 1 ? 4 : 5; up = true; break;
            case 0x20A: wheel = true; delta = (short)high; break;
            default: return false;
        }

        if (wheel)
        {
            if (!Allowed() || delta == 0) return false;
            SyncModifiers();
            var hit = Match(new HotkeyToken(HotkeyInputKind.Wheel, delta > 0 ? 1 : -1));
            if (hit is null) return false;
            Fire(hit);
            return !PassThrough;
        }

        if (down)
        {
            _downMouse.Add(code);
            _armed = null;
            if (!Allowed()) return false;
            SyncModifiers();
            var binding = Match(new HotkeyToken(HotkeyInputKind.Mouse, code));
            if (binding is null) return false;

            Fire(binding);
            if (PassThrough) return false;
            _swallowedMouse.Add(code);
            return true;
        }

        _downMouse.Remove(code);
        return up && _swallowedMouse.Remove(code);
    }

    // ---------- matching ----------

    private static bool IsModifierVk(int vk) =>
        vk is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C or 0x10 or 0x11 or 0x12;

    private static bool PhysicallyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Forget modifiers whose key-up we never saw (for example after Win+L).</summary>
    private void SyncModifiers()
    {
        for (int group = 0; group < 4; group++)
        {
            for (int side = 0; side < 2; side++)
            {
                int vk = HotkeyParser.ModifierVk[group, side];
                if (_downKeys.Contains(vk) && !PhysicallyDown(vk)) _downKeys.Remove(vk);
            }
        }
    }

    private bool IsHeld(HotkeyToken token) => token.Kind switch
    {
        HotkeyInputKind.Key => _downKeys.Contains(token.Code) && PhysicallyDown(token.Code),
        HotkeyInputKind.Mouse => _downMouse.Contains(token.Code) && token.Code < MouseVk.Length && PhysicallyDown(MouseVk[token.Code]),
        _ => false
    };

    private bool ModifiersMatch(HotkeySpec spec)
    {
        for (int group = 0; group < 4; group++)
        {
            bool left = _downKeys.Contains(HotkeyParser.ModifierVk[group, 0]);
            bool right = _downKeys.Contains(HotkeyParser.ModifierVk[group, 1]);
            var required = spec.Modifiers.Where(m => m.Group == group).ToList();

            if (required.Count == 0)
            {
                if (left || right) return false;               // an extra modifier is held: not this hotkey
                continue;
            }
            foreach (var m in required)
            {
                bool ok = m.Side switch { 0 => left || right, 1 => left, _ => right };
                if (!ok) return false;
            }
        }
        return true;
    }

    /// <summary>The binding whose trigger is this input, whose other inputs are all held, and whose modifiers match exactly.</summary>
    private Binding? Match(HotkeyToken trigger)
    {
        Binding? best = null;
        foreach (var binding in _bindings)
        {
            var inputs = binding.Spec.Inputs;
            if (inputs.Count == 0 || !inputs.Contains(trigger)) continue;

            bool others = true;
            foreach (var token in inputs)
            {
                if (token == trigger) continue;
                if (!IsHeld(token)) { others = false; break; }
            }
            if (!others || !ModifiersMatch(binding.Spec)) continue;
            if (best is null || binding.Weight > best.Weight) best = binding;
        }
        return best;
    }

    private Binding? FindModifierOnly()
    {
        foreach (var binding in _bindings)
        {
            if (binding.Spec.ModifierOnly && ModifiersMatch(binding.Spec)) return binding;
        }
        return null;
    }

    public void Dispose()
    {
        _bindings.Clear();
        if (_keyHook != 0) NativeMethods.UnhookWindowsHookEx(_keyHook);
        if (_mouseHook != 0) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _keyHook = 0;
        _mouseHook = 0;
    }
}



