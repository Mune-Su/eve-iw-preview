using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>The system tray icon. This is the app's only menu: right-click the icon.</summary>
public sealed class NotifyIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public NotifyIconService(PreviewManager manager)
    {
        var settingsItem = new Forms.ToolStripMenuItem("Settings…");
        settingsItem.Font = new Font(settingsItem.Font, System.Drawing.FontStyle.Bold);
        settingsItem.Click += (_, _) => manager.OpenSettings();

        var toggleItem = new Forms.ToolStripMenuItem("Hide all thumbnails");
        toggleItem.Click += (_, _) => manager.ToggleVisibility();

        var refreshItem = new Forms.ToolStripMenuItem("Refresh clients");
        refreshItem.Click += (_, _) => manager.Refresh();

        var lockItem = new Forms.ToolStripMenuItem("Lock positions and sizes") { CheckOnClick = true };
        lockItem.Click += (_, _) => manager.SetLocked(lockItem.Checked);

        var resetItem = new Forms.ToolStripMenuItem("Reset all positions");
        resetItem.Click += (_, _) => manager.ResetPositions();

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(lockItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            lockItem.Checked = manager.Settings.LockPositions;
            toggleItem.Text = manager.ThumbnailsVisible ? "Hide all thumbnails" : "Show all thumbnails";
        };

        _icon = new Forms.NotifyIcon
        {
            Text = "AUTZ IronWorks Preview",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => manager.OpenSettings();
    }

    /// <summary>Uses the exe's own icon (the AUTZ IronWorks icon from Assets\app.ico).</summary>
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
