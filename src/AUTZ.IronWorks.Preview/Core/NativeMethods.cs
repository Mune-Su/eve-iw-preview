using System.Runtime.InteropServices;
using System.Text;

namespace AUTZ.IronWorks.Preview.Core;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    internal delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime);
    internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] internal static extern void SwitchToThisWindow(nint hWnd, bool altTab);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hWnd, int command);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hWnd, int command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hWnd);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc callback, uint idProcess, uint idThread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);

    // low-level keyboard / mouse hooks (hotkeys with no restrictions on keys, buttons or combinations)
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint hMod, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] internal static extern int SetWindowLong(nint hWnd, int index, int value);

    [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);

    internal const int SW_MAXIMIZE = 3;
    internal const int SW_MINIMIZE = 6;
    internal const int SW_SHOWMINNOACTIVE = 7;
    internal const int SW_RESTORE = 9;

    internal const int VK_SHIFT = 0x10;
    internal const byte VK_MENU = 0x12;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WH_MOUSE_LL = 14;

    internal const int WM_MOUSEACTIVATE = 0x0021;
    internal const int MA_NOACTIVATE = 3;

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;

    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const uint DWM_TNP_RECTDESTINATION = 0x1;
    internal const uint DWM_TNP_OPACITY = 0x4;
    internal const uint DWM_TNP_VISIBLE = 0x8;
    internal const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint Flags;
        public RECT Destination;
        public RECT Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }
}



