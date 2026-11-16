using System.Drawing;
using System.Windows;
using AUTZ.IronWorks.Preview.Models;
using Forms = System.Windows.Forms;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>The system tray icon. This is the app's only menu: right-click the icon.</summary>
public sealed class NotifyIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public NotifyIconService(PreviewManager manager, AppSettings settings)
    {
        var menu = new Forms.ContextMenuStrip();

        var settingsItem = new Forms.ToolStripMenuItem("Settings…");
        settingsItem.Font = new Font(settingsItem.Font, System.Drawing.FontStyle.Bold);
        settingsItem.Click += (_, _) => manager.OpenSettings();

        var toggleItem = new Forms.ToolStripMenuItem("Hide all thumbnails");
        toggleItem.Click += (_, _) => manager.ToggleVisibility();

        // which clients get a thumbnail (rebuilt each time the menu opens)
        var thumbsItem = new Forms.ToolStripMenuItem("Thumbnails to show");

        var refreshItem = new Forms.ToolStripMenuItem("Refresh clients");
        refreshItem.Click += (_, _) => manager.Refresh();

        var lockItem = new Forms.ToolStripMenuItem("Lock positions and sizes") { CheckOnClick = true };
        lockItem.Click += (_, _) =>
        {
            settings.LockPositions = lockItem.Checked;
            SettingsService.Save(settings);
        };

        var clickItem = new Forms.ToolStripMenuItem("Thumbnails clickable") { CheckOnClick = true };
        clickItem.Click += (_, _) =>
        {
            settings.ThumbnailsClickable = clickItem.Checked;
            SettingsService.Save(settings);
        };

        var resetItem = new Forms.ToolStripMenuItem("Reset all positions");
        resetItem.Click += (_, _) => manager.ResetPositions();

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(thumbsItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(clickItem);
        menu.Items.Add(lockItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            lockItem.Checked = settings.LockPositions;
            clickItem.Checked = settings.ThumbnailsClickable;
            toggleItem.Text = manager.ThumbnailsVisible ? "Hide all thumbnails" : "Show all thumbnails";
            RebuildThumbnailMenu(manager, settings, thumbsItem);
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

    private static void RebuildThumbnailMenu(PreviewManager manager, AppSettings settings, Forms.ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var login = new Forms.ToolStripMenuItem("Login screens") { Checked = settings.ShowLoginScreens, CheckOnClick = true };
        login.Click += (_, _) => manager.SetLoginScreensShown(login.Checked);
        parent.DropDownItems.Add(login);
        parent.DropDownItems.Add(new Forms.ToolStripSeparator());

        var entries = manager.GetClientEntries();
        if (entries.Count == 0)
        {
            parent.DropDownItems.Add(new Forms.ToolStripMenuItem("No characters seen yet") { Enabled = false });
            return;
        }

        foreach (var entry in entries)
        {
            string name = entry.Name;
            var item = new Forms.ToolStripMenuItem(entry.Running ? name : name + " (not logged in)")
            {
                Checked = entry.Shown,
                CheckOnClick = true
            };
            item.Click += (_, _) => manager.SetCharacterShown(name, item.Checked);
            parent.DropDownItems.Add(item);
        }
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
