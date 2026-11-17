using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Views;
using Forms = System.Windows.Forms;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>The system tray icon. This is the app's only menu: right-click the icon. Everything else is in Configuration.</summary>
public sealed class NotifyIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly DispatcherTimer _pinTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private int _pinTries;

    public NotifyIconService(PreviewManager manager)
    {
        var settingsItem = new Forms.ToolStripMenuItem("Configuration");
        settingsItem.Font = new Font(settingsItem.Font, System.Drawing.FontStyle.Bold);
        settingsItem.Click += (_, _) => manager.OpenSettings();

        // the profiles: pick one to switch to it (the one in use is ticked)
        var profilesItem = new Forms.ToolStripMenuItem("Profiles");

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

        var debugItem = new Forms.ToolStripMenuItem("Detailed Logging") { CheckOnClick = false };
        debugItem.Click += (_, _) => manager.SetDebugLogging(!Log.Verbose);
        var openDebugItem = new Forms.ToolStripMenuItem("Open Debug Log");
        openDebugItem.Click += (_, _) =>
        {
            try
            {
                string? latest = Log.LatestDebugPath();
                if (latest is null) { Dialogs.Info(null, "There's no debug log yet. Turn on Detailed Logging first, then use the app."); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(latest) { UseShellExecute = true });
            }
            catch (Exception ex) { Dialogs.Info(null, "Couldn't open the debug log: " + ex.Message); }
        };
        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(profilesItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(debugItem);
        menu.Items.Add(openDebugItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            toggleItem.Text = manager.ThumbnailsVisible ? "Hide All Thumbnails" : "Show All Thumbnails";
            debugItem.Checked = Log.Verbose;
            FillProfiles(profilesItem, manager);
        };

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

        // Windows only lists the icon (so it can be pinned to the taskbar) a moment after it first appears: keep trying for a few seconds
        if (manager.State.PinTrayIcon)
        {
            _pinTimer.Tick += (_, _) =>
            {
                _pinTries++;
                if (TrayPinService.Promote() || _pinTries >= 6) _pinTimer.Stop();
            };
            _pinTimer.Start();
        }
    }

    /// <summary>Lists the profiles under "Profiles"; clicking one switches to it.</summary>
    private static void FillProfiles(Forms.ToolStripMenuItem parent, PreviewManager manager)
    {
        parent.DropDownItems.Clear();
        var names = ProfileStore.List();
        if (names.Count == 0)
        {
            parent.DropDownItems.Add(new Forms.ToolStripMenuItem("(no profiles)") { Enabled = false });
            return;
        }

        foreach (string name in names)
        {
            string chosen = name;
            var item = new Forms.ToolStripMenuItem(name.Replace("&", "&&"))
            {
                Checked = name.Equals(manager.State.ActiveProfile, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => manager.SwitchProfile(chosen);    // works with the Configuration window open too: it follows
            parent.DropDownItems.Add(item);
        }
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
        _pinTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
    }
}



