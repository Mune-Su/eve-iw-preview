using System.Drawing;
using System.Windows;
using AUTZ.IronWorks.Preview.Models;
using Forms = System.Windows.Forms;

namespace AUTZ.IronWorks.Preview.Services;

public sealed class NotifyIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public NotifyIconService(PreviewManager manager, AppSettings settings, Action openControlWindow)
    {
        var lockItem = new Forms.ToolStripMenuItem("Lock positions and sizes") { CheckOnClick = true };
        lockItem.Click += (_, _) =>
        {
            settings.LockPositions = lockItem.Checked;
            SettingsService.Save(settings);
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => lockItem.Checked = settings.LockPositions;
        menu.Items.Add("Open control window", null, (_, _) => openControlWindow());
        menu.Items.Add("Show / Hide thumbnails", null, (_, _) => manager.ToggleVisibility());
        menu.Items.Add("Refresh clients", null, (_, _) => manager.Refresh());
        menu.Items.Add("Settings…", null, (_, _) => manager.OpenSettings());
        menu.Items.Add(lockItem);
        menu.Items.Add("Reset all positions", null, (_, _) => manager.ResetPositions());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());

        _icon = new Forms.NotifyIcon
        {
            Text = "AUTZ IronWorks Preview",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => openControlWindow();
    }

    /// <summary>Uses the exe's own icon (the AUTZ IronWorks icon once Assets\app.ico is in place).</summary>
    private static Icon LoadIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (path is not null)
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null) return icon;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
