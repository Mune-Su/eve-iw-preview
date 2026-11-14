using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Views;
public sealed class PreviewCard : HwndHost
{
    private readonly EveClientWindow _client;
    private nint _host;
    private nint _thumbnail;
    public PreviewCard(EveClientWindow client) { _client = client; Cursor = System.Windows.Input.Cursors.Hand; MouseLeftButtonUp += (_,__) => ClientClicked?.Invoke(_client.Handle); }
    public event Action<nint>? ClientClicked;
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _host = CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000, 0, 0, Math.Max(1,(int)ActualWidth), Math.Max(1,(int)ActualHeight), hwndParent.Handle, 0, 0, 0);
        if (_host != 0 && NativeMethods.DwmRegisterThumbnail(_host, _client.Handle, out _thumbnail) == 0) UpdateThumbnail();
        return new HandleRef(this, _host);
    }
    protected override void DestroyWindowCore(HandleRef hwnd) { if (_thumbnail != 0) NativeMethods.DwmUnregisterThumbnail(_thumbnail); if (_host != 0) DestroyWindow(_host); }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); if (_host != 0) { MoveWindow(_host, 0, 0, Math.Max(1,(int)ActualWidth), Math.Max(1,(int)ActualHeight), true); UpdateThumbnail(); } }
    private void UpdateThumbnail()
    {
        if (_thumbnail == 0) return;
        var p = new NativeMethods.DWM_THUMBNAIL_PROPERTIES { Flags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE | NativeMethods.DWM_TNP_OPACITY | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY, Destination = new NativeMethods.RECT { Left=0, Top=0, Right=Math.Max(1,(int)ActualWidth), Bottom=Math.Max(1,(int)ActualHeight)}, Visible=true, Opacity=255, SourceClientAreaOnly=true };
        NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref p);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] private static extern nint CreateWindowEx(int exStyle,string className,string name,int style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint param);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyWindow(nint h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool MoveWindow(nint h,int x,int y,int w,int hgt,bool repaint);
}
