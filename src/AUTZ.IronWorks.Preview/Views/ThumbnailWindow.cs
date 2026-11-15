using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>
/// One borderless, always-on-top window per EVE client showing a live DWM thumbnail.
/// Drag it anywhere; click it (without dragging) to switch to that client.
/// </summary>
public sealed class ThumbnailWindow : Window
{
    private readonly AppSettings _settings;
    private nint _thumbnail;
    private LabelWindow? _label;

    private string _labelText = "";
    private Brush _labelBrush = Brushes.White;
    private bool _showLabel = true;
    private Brush? _highlight;

    public EveClientWindow Client { get; }

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
        MouseLeftButtonDown += OnMouseDown;
        Closed += OnClosed;
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

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;

        // Keep thumbnails out of Alt+Tab.
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

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        double startLeft = Left, startTop = Top;
        if (!_settings.LockPositions)
        {
            try { DragMove(); } // blocks until the mouse button is released
            catch (InvalidOperationException) { }
        }

        if (Math.Abs(Left - startLeft) < 2 && Math.Abs(Top - startTop) < 2) Clicked?.Invoke(this);
        else Moved?.Invoke(this);
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
