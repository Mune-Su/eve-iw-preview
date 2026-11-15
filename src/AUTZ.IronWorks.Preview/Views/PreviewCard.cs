using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>
/// A live DWM thumbnail of one EVE client. The thumbnail is registered against the top-level
/// preview window and positioned over this element. DWM paints it above WPF content, so labels
/// must sit beside/below the card, never on top of it.
/// </summary>
public sealed class PreviewCard : FrameworkElement
{
    private readonly EveClientWindow _client;
    private Window? _owner;
    private nint _thumbnail;
    private string _lastState = "";

    public event Action<nint>? ClientClicked;

    public PreviewCard(EveClientWindow client)
    {
        _client = client;
        Cursor = Cursors.Hand;
        Loaded += OnCardLoaded;
        Unloaded += OnCardUnloaded;
    }

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (_thumbnail != 0) return;
        _owner = Window.GetWindow(this);
        if (_owner is null) return;
        nint hwnd = new WindowInteropHelper(_owner).Handle;
        if (hwnd == 0) return;

        if (NativeMethods.DwmRegisterThumbnail(hwnd, _client.Handle, out _thumbnail) != 0)
        {
            _thumbnail = 0;
            return;
        }

        LayoutUpdated += OnLayoutUpdated;
        IsVisibleChanged += OnVisibleChanged;
        UpdateThumbnail();
    }

    private void OnCardUnloaded(object sender, RoutedEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        IsVisibleChanged -= OnVisibleChanged;
        if (_thumbnail != 0)
        {
            NativeMethods.DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = 0;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateThumbnail();
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateThumbnail();

    private void UpdateThumbnail()
    {
        if (_thumbnail == 0 || _owner is null) return;

        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            Flags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE
                  | NativeMethods.DWM_TNP_OPACITY | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            Opacity = 255,
            SourceClientAreaOnly = true,
            Visible = false
        };

        if (IsVisible && _owner.IsVisible && ActualWidth > 0 && ActualHeight > 0)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(_owner);
                Point topLeft = TransformToAncestor(_owner).Transform(new Point(0, 0));
                int left = (int)Math.Round(topLeft.X * dpi.DpiScaleX);
                int top = (int)Math.Round(topLeft.Y * dpi.DpiScaleY);
                int right = left + Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
                int bottom = top + Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
                props.Destination = new NativeMethods.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
                props.Visible = true;
            }
            catch (InvalidOperationException)
            {
                return; // not connected to the window yet; next layout pass will retry
            }
        }

        string state = $"{props.Visible}:{props.Destination.Left}:{props.Destination.Top}:{props.Destination.Right}:{props.Destination.Bottom}";
        if (state == _lastState) return;
        _lastState = state;
        NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref props);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Solid backing so the whole card is clickable and looks tidy if the thumbnail is unavailable.
        drawingContext.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ClientClicked?.Invoke(_client.Handle);
    }
}
