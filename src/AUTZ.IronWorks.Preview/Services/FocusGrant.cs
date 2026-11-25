// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Brings a client to the front when Windows would refuse.
/// Windows only lets the app that got the last input change the window in front. A hook-matched hotkey isn't that input,
/// so the app presses a key no keyboard has. That key is registered as a hotkey, so Windows hands this app the right to
/// switch, and the switch is made from there. No Alt tap, no input attach, no waiting.
/// </summary>
internal static class FocusGrant
{
    private const int WM_HOTKEY = 0x0312;
    /// <summary>Unassigned in the Windows key table, so no real key sends it.</summary>
    private const uint ConduitVk = 0x97;
    private const int IdBase = 0xB000;
    /// <summary>Ctrl, Alt, Shift and Win are bits 0-3, so 0-15 is every mix. A held modifier still matches.</summary>
    private const int Combos = 16;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    private static HwndSource? _window;
    private static nint _target;
    private static long _requestedAt;
    private static int _registered;

    public static uint Vk => ConduitVk;
    public static bool Ready => _registered > 0;

    /// <summary>Once at start-up, on the UI thread.</summary>
    public static void Install()
    {
        if (_window is not null) return;
        var p = new HwndSourceParameters("EVE-IW-Preview focus") { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);
        for (int i = 0; i < Combos; i++)
        {
            if (RegisterHotKey(_window.Handle, IdBase + i, (uint)i, ConduitVk)) _registered++;
            else Log.Debug("FOCUS", $"focus grant combo 0x{i:X} not registered (error {Marshal.GetLastWin32Error()})");
        }
        if (_registered == 0) Log.Write("Focus grant couldn't be set up. Switching uses the old fallbacks.");
        else Log.Debug("FOCUS", $"focus grant ready ({_registered}/{Combos} combos)");
    }

    public static void Uninstall()
    {
        if (_window is null) return;
        for (int i = 0; i < Combos; i++) UnregisterHotKey(_window.Handle, IdBase + i);
        _window.RemoveHook(WndProc);
        _window.Dispose();
        _window = null;
        _registered = 0;
    }

    /// <summary>Presses the hidden key. WM_HOTKEY then arrives here and makes the switch.</summary>
    public static void Request(nint target)
    {
        _target = target;
        _requestedAt = Log.Ms;
        keybd_event((byte)ConduitVk, 0, 0, UIntPtr.Zero);
        keybd_event((byte)ConduitVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return 0;
        int id = (int)wParam;
        if (id < IdBase || id >= IdBase + Combos) return 0;
        handled = true;
        nint target = _target;
        _target = 0;
        if (target == 0 || !IsWindow(target)) return 0;
        // one quick retry rather than leave you on the wrong client
        bool ok = SetForegroundWindow(target) || SetForegroundWindow(target);
        Log.Debug("FOCUS", $"  focus grant -> {(ok ? "OK" : "REFUSED")} in {Log.Ms - _requestedAt} ms; foreground now {Log.Win(GetForegroundWindow())}");
        return 0;
    }
}
