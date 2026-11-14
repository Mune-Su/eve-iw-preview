using System.Drawing;
using System.Windows;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Views;
using Forms = System.Windows.Forms;

namespace AUTZ.IronWorks.Preview.Services;
public sealed class NotifyIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    public NotifyIconService(MainWindow main, AppSettings settings)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / Hide", null, (_,__) => main.ToggleVisibility());
        menu.Items.Add("Refresh clients", null, (_,__) => main.RefreshClients());
        menu.Items.Add("Settings", null, (_,__) => main.OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_,__) => { main.CloseForExit(); Application.Current.Shutdown(); });
        _icon = new Forms.NotifyIcon { Text = "AUTZ IronWorks Preview", Icon = SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_,__) => main.ToggleVisibility();
    }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
