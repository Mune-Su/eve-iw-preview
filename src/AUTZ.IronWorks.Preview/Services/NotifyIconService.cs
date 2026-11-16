using System.Drawing;
using System.Windows;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Views;
using Forms = System.Windows.Forms;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>The system tray icon. This is the app's only menu: right-click the icon. Everything else is in Configuration.</summary>
public sealed class NotifyIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public NotifyIconService(PreviewManager manager)
    {
        var settingsItem = new Forms.ToolStripMenuItem("Configuration (Settings)");
        settingsItem.Font = new Font(settingsItem.Font, System.Drawing.FontStyle.Bold);
        settingsItem.Click += (_, _) => manager.OpenSettings();

        var toggleItem = new Forms.ToolStripMenuItem("Hide All Thumbnails");
        toggleItem.Click += (_, _) => manager.ToggleVisibility();

        var refreshItem = new Forms.ToolStripMenuItem("Refresh Clients");
        refreshItem.Click += (_, _) => manager.Refresh();

        var resetItem = new Forms.ToolStripMenuItem("Reset All Positions");
        resetItem.Click += (_, _) =>
        {
            if (Dialogs.Ask(null, "Reset positions", "Put every thumbnail back in a stack down the left edge of the screen?", "Reset", "Cancel") == 0)
                manager.ResetPositions();
        };

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
            toggleItem.Text = manager.ThumbnailsVisible ? "Hide All Thumbnails" : "Show All Thumbnails";

        _icon = new Forms.NotifyIcon
        {
            Text = AppInfo.ShortName,          // the tray tooltip uses the short name
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => manager.OpenSettings();

        // alerts and confirmations appear as Windows notifications from the tray icon
        manager.Toast += (title, text) => _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);
    }

    /// <summary>Uses the exe's own icon (Assets\app.ico).</summary>
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
