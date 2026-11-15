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
/// One borderless, always-on-top window per EVE client showing a live DWM thumbnail.
/// Left-click switches to that client. Hold the RIGHT mouse button and drag to move it
/// (it snaps to neighbouring thumbnails); a right-click without dragging opens the menu.
/// </summary>
public sealed class ThumbnailWindow : Window
{
    private const int DragThreshold = 4; // pixels before a right-click turns into a drag

    private readonly AppSettings _settings;
    private nint _thumbnail;
    private LabelWindow? _label;

    private string _labelText = "";
    private Brush _labelBrush = Brushes.White;
    private bool _showLabel = true;
    private Brush? _highlight;

    private bool _dragging;
    private bool _dragMoved;
    private NativeMethods.POINT _dragStartCursor;
    private NativeMethods.RECT _dragStartRect;

    public EveClientWindow Client { get; }

    /// <summary>Menu shown on a right-click that didn't drag.</summary>
    public ContextMenu? Menu { get; set; }

    /// <summary>Returns the screen rectangles of the other thumbnails, used for snapping.</summary>
    internal Func<ThumbnailWindow, IReadOnlyList<NativeMethods.RECT>>? SnapTargets { get; set; }

    public event Action<ThumbnailWindow>? Clicked;
    public event Action<ThumbnailWindow>? Moved;

    public ThumbnailWindow(EveClientWindow client, AppSettings settings, double width, double height, double left, double top)
    {
        Client = client;
        _settings = settings;

        Title = "AUTZ IronWorks Preview - " + client.CharacterName;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Hand;
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
        MouseRightButtonDown += OnRightDown;
        MouseMove += OnMouseMoved;
        MouseRightButtonUp += OnRightUp;
        LostMouseCapture += (_, _) => _dragging = false;
        Closed += OnClosed;
    }

    internal NativeMethods.RECT GetBounds()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    public void SetLabel(string text, Brush brush, bool show)
    {
        _labelText = text;
        _labelBrush = brush;
        _showLabel = show;
        _label?.SetLabel(text, brush, show);
    }

    public void SetHighlight(Brush? brush)
    {
        _highlight = brush;
        _label?.SetHighlight(brush);
    }

    // ---------- thumbnail ----------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;

        // Keep thumbnails out of Alt+Tab and the taskbar.
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);

        if (NativeMethods.DwmRegisterThumbnail(hwnd, Client.Handle, out _thumbnail) != 0) _thumbnail = 0;
        UpdateThumbnail();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_label is not null) return;
        _label = new LabelWindow { Owner = this };
        _label.SetLabel(_labelText, _labelBrush, _showLabel);
        _label.SetHighlight(_highlight);
        _label.SyncToOwner();
        _label.Show();
        UpdateThumbnail();
    }

    private void UpdateThumbnail()
    {
        if (_thumbnail == 0) return;
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || !NativeMethods.GetClientRect(hwnd, out var rect)) return;

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
            Opacity = 255,
            Visible = true,
            SourceClientAreaOnly = true
        };
        NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref props);
    }

    // ---------- mouse ----------

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Clicked?.Invoke(this);
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        nint hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetCursorPos(out _dragStartCursor);
        NativeMethods.GetWindowRect(hwnd, out _dragStartRect);
        _dragging = true;
        _dragMoved = false;
        CaptureMouse();
    }

    private void OnMouseMoved(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (e.RightButton != MouseButtonState.Pressed) { EndDrag(); return; }
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

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_dragging) return;

        bool moved = _dragMoved;
        EndDrag();

        if (moved)
        {
            Moved?.Invoke(this);
        }
        else if (Menu is not null)
        {
            Menu.PlacementTarget = this;
            Menu.Placement = PlacementMode.MousePoint;
            Menu.IsOpen = true;
        }
    }

    private void EndDrag()
    {
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_thumbnail != 0)
        {
            NativeMethods.DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = 0;
        }
    }
}
