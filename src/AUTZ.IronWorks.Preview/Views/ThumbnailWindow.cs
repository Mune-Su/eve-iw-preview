// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>
/// One live thumbnail per EVE client.
/// Click to switch. Shift + right-drag to move. Shift + drag the orange corner to resize.
/// It never takes focus from the game.
/// </summary>
public sealed class ThumbnailWindow : Window
{
    private const int DragThreshold = 4;     // pixels before a Shift + right-click turns into a drag
    private const double GripSize = 18;      // size of the resize corner, in DIPs
    private const double MinWidthDip = 80;
    private const double MaxWidthDip = 1600;
    private const double MinHeightDip = 45;

    private readonly AppSettings _settings;
    private nint _thumbnail;
    private LabelWindow? _label;

    private LabelSpec? _labelSpec;
    private BorderSpec? _borderSpec;
    private bool _excluded;

    private bool _dragging;
    private bool _dragMoved;
    private NativeMethods.POINT _dragStartCursor;
    private NativeMethods.RECT _dragStartRect;

    // Shift is polled only while the mouse is over this thumbnail (nothing runs the rest of the time), so the corner
    // appears the moment Shift goes down even if the mouse is not moving
    private readonly System.Windows.Threading.DispatcherTimer _shiftTimer =
        new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };

    private bool _resizing;
    private NativeMethods.POINT _resizeStartCursor;
    private NativeMethods.RECT _resizeStartRect;

    public EveClientWindow Client { get; }

    /// <summary>Returns the screen rectangles of the other thumbnails, used for snapping.</summary>
    internal Func<ThumbnailWindow, IReadOnlyList<NativeMethods.RECT>>? SnapTargets { get; set; }

    public event Action<ThumbnailWindow>? Clicked;
    public event Action<ThumbnailWindow>? Moved;
    public event Action<ThumbnailWindow>? Resized;

    /// <summary>Raised while resizing, with the new size in DIPs, so other thumbnails can follow.</summary>
    public event Action<ThumbnailWindow, double, double>? Resizing;

    public ThumbnailWindow(EveClientWindow client, AppSettings settings, double width, double height, double left, double top)
    {
        Client = client;
        _settings = settings;

        Title = AppInfo.Name + " - " + client.CharacterName;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = settings.AlwaysOnTop;
        Background = Brushes.Black;
        Cursor = DefaultCursor;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        Height = height;
        Left = left;
        Top = top;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SizeChanged += (_, _) => { UpdateThumbnail(); _label?.SyncToOwner(); };
        LocationChanged += (_, _) => _label?.SyncToOwner();
        DpiChanged += (_, _) => UpdateThumbnail();
        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += OnLeftUp;
        MouseRightButtonDown += OnRightDown;             // Shift + right-drag moves the thumbnail
        MouseRightButtonUp += OnRightUp;
        MouseMove += OnMouseMoved;
        _shiftTimer.Tick += (_, _) =>
        {
            UpdateGrip();
            UpdateCursor(Mouse.GetPosition(this));
        };
        MouseEnter += (_, _) =>
        {
            _shiftTimer.Start();
            UpdateGrip();
        };
        MouseLeave += (_, _) =>
        {
            if (!_resizing && !_dragging) _shiftTimer.Stop();
            UpdateGrip();
        };
        LostMouseCapture += (_, _) =>
        {
            bool moved = _dragging && _dragMoved;
            bool resized = _resizing;
            _dragging = false;
            _resizing = false;
            UpdateGrip();
            if (moved) Moved?.Invoke(this);
            if (resized) Resized?.Invoke(this);
        };
        Closed += OnClosed;
    }

    /// <summary>Character thumbnails and login screen thumbnails each have their own "clickable" option.</summary>
    private bool CanClick => Client.IsLoginScreen ? _settings.LoginScreensClickable : _settings.ThumbnailsClickable;

    private Cursor DefaultCursor => CanClick ? Cursors.Hand : Cursors.Arrow;

    internal NativeMethods.RECT GetBounds()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    public void SetLabel(LabelSpec spec)
    {
        _labelSpec = spec;
        _label?.SetLabel(spec);
    }

    public void SetBorder(BorderSpec? spec)
    {
        _borderSpec = spec;
        _label?.SetBorder(spec);
    }

    public void SetExcluded(bool excluded)
    {
        _excluded = excluded;
        _label?.SetExcluded(excluded);
    }

    /// <summary>Re-applies size and opacity to the DWM thumbnail (used after settings change).</summary>
    public void RefreshThumbnail() => UpdateThumbnail();

    private static bool ShiftHeld() =>
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;

    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return NativeMethods.MA_NOACTIVATE;          // deliver the click, but don't activate this window
        }
        return 0;
    }

    public void SetAlwaysOnTop(bool onTop)
    {
        Topmost = onTop;
        if (_label is not null) _label.Topmost = onTop;
    }

    /// <summary>The orange resize corner shows only while Shift is held over the thumbnail (or while it is being resized).</summary>
    private void UpdateGrip() =>
        _label?.SetGripVisible(!_settings.LockPositions && (_resizing || (IsMouseOver && ShiftHeld())));

    /// <summary>Cursor hints: corner (Shift held) = resize, Shift = move, otherwise a hand only when clicking does something.</summary>
    private void UpdateCursor(Point p)
    {
        if (_resizing || _dragging) return;
        bool shift = ShiftHeld();
        if (!_settings.LockPositions && shift && IsInGrip(p)) Cursor = Cursors.SizeNWSE;
        else if (!_settings.LockPositions && shift) Cursor = Cursors.SizeAll;
        else Cursor = DefaultCursor;
    }

    // ---------- thumbnail ----------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;

        // An overlay: out of Alt+Tab and the taskbar, and it never takes focus when clicked (so a click can't pull
        // the taskbar up over a full-screen game).
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        if (NativeMethods.DwmRegisterThumbnail(hwnd, Client.Handle, out _thumbnail) != 0) _thumbnail = 0;
        UpdateThumbnail();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_label is not null) return;
        _label = new LabelWindow { Owner = this, Topmost = Topmost };
        if (_labelSpec is not null) _label.SetLabel(_labelSpec);
        _label.SetBorder(_borderSpec);
        _label.SetExcluded(_excluded);
        _label.SyncToOwner();
        _label.Show();
        UpdateThumbnail();
    }

    private void UpdateThumbnail()
    {
        if (_thumbnail == 0) return;
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || !NativeMethods.GetClientRect(hwnd, out var rect)) return;

        byte opacity = (byte)Math.Clamp((int)Math.Round(_settings.ThumbnailOpacityPercent * 255 / 100.0), 25, 255);

        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            Flags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE |
                    NativeMethods.DWM_TNP_OPACITY | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            Destination = new NativeMethods.RECT
            {
                Left = 0,
                Top = 0,
                Right = Math.Max(1, rect.Right),
                Bottom = Math.Max(1, rect.Bottom)
            },
            Opacity = opacity,
            Visible = true,
            SourceClientAreaOnly = true
        };
        NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref props);
    }

    // ---------- mouse ----------

    private bool IsInGrip(Point p) => p.X >= ActualWidth - GripSize && p.Y >= ActualHeight - GripSize;

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        // the corner only exists while Shift is held; without Shift a click there is a normal click
        if (ShiftHeld() && IsInGrip(e.GetPosition(this)))
        {
            if (!_settings.LockPositions) BeginResize();
            return; // the corner never switches clients
        }

        // the left button only ever switches (or, in the corner, resizes): it never moves the thumbnail
        if (CanClick) Clicked?.Invoke(this);
        else AUTZ.IronWorks.Preview.Services.Log.Debug("SWITCH", "thumbnail click ignored: clicking is switched off for " + (Client.IsLoginScreen ? "login screen" : "character") + " thumbnails (Thumbnails > Interaction)");
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            e.Handled = true;
            EndResize();
        }
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ShiftHeld() && !_settings.LockPositions) BeginDrag();     // Shift + right-drag moves the thumbnail; a plain right-click does nothing
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        e.Handled = true;
        FinishDrag();
    }

    private void BeginResize()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetCursorPos(out _resizeStartCursor);
        NativeMethods.GetWindowRect(hwnd, out _resizeStartRect);
        _resizing = true;
        UpdateGrip();
        CaptureMouse();
    }

    private void DoResize(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { EndResize(); return; }

        NativeMethods.GetCursorPos(out var cursor);
        int dx = cursor.X - _resizeStartCursor.X;
        int dy = cursor.Y - _resizeStartCursor.Y;

        int startW = Math.Max(1, _resizeStartRect.Right - _resizeStartRect.Left);
        int startH = Math.Max(1, _resizeStartRect.Bottom - _resizeStartRect.Top);
        double ratio = startW / (double)startH;

        var dpi = VisualTreeHelper.GetDpi(this);
        double minW = Math.Max(MinWidthDip * dpi.DpiScaleX, MinHeightDip * dpi.DpiScaleY * ratio);
        double maxW = Math.Max(MaxWidthDip * dpi.DpiScaleX, minW);

        // follow whichever direction you pulled further, and keep the original shape
        double scale = Math.Max((startW + dx) / (double)startW, (startH + dy) / (double)startH);
        int newW = (int)Math.Round(Math.Clamp(startW * scale, minW, maxW));
        int newH = Math.Max(1, (int)Math.Round(newW / ratio));

        nint hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, 0, 0, 0, newW, newH,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        Resizing?.Invoke(this, newW / dpi.DpiScaleX, newH / dpi.DpiScaleY);
    }

    private void EndResize()
    {
        _resizing = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!IsMouseOver) _shiftTimer.Stop();
        UpdateGrip();
        Resized?.Invoke(this);
    }

    private void BeginDrag()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetCursorPos(out _dragStartCursor);
        NativeMethods.GetWindowRect(hwnd, out _dragStartRect);
        _dragging = true;
        _dragMoved = false;
        CaptureMouse();
    }

    private void OnMouseMoved(object sender, MouseEventArgs e)
    {
        if (_resizing) { DoResize(e); return; }

        if (!_dragging)
        {
            UpdateCursor(e.GetPosition(this));
            return;
        }

        if (e.RightButton != MouseButtonState.Pressed) { FinishDrag(); return; }
        if (_settings.LockPositions) return;

        NativeMethods.GetCursorPos(out var cursor);
        int dx = cursor.X - _dragStartCursor.X;
        int dy = cursor.Y - _dragStartCursor.Y;
        if (!_dragMoved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
        _dragMoved = true;

        int width = _dragStartRect.Right - _dragStartRect.Left;
        int height = _dragStartRect.Bottom - _dragStartRect.Top;
        int x = _dragStartRect.Left + dx;
        int y = _dragStartRect.Top + dy;

        if (_settings.SnapEnabled && SnapTargets is not null)
            (x, y) = SnapHelper.Snap(x, y, width, height, SnapTargets(this), _settings.SnapDistance);

        nint hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void FinishDrag()
    {
        bool moved = _dragMoved;
        _dragging = false;
        _dragMoved = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!IsMouseOver) _shiftTimer.Stop();
        if (moved) Moved?.Invoke(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _shiftTimer.Stop();
        if (_thumbnail != 0)
        {
            NativeMethods.DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = 0;
        }
    }
}


