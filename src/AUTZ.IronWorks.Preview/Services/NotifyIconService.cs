// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
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
        settingsItem.Click += (_, _) => { Log.Debug("TRAY", "clicked Configuration"); manager.OpenSettings(); };

        // the profiles: pick one to switch to it (the one in use is ticked)
        var profilesItem = new Forms.ToolStripMenuItem("Profiles");

        // Disable Switcher: all clients at once, or one at a time (the list is built from the clients that are open)
        var toggleItem = new Forms.ToolStripMenuItem("Disable Switcher");
        var pauseItem = new Forms.ToolStripMenuItem("Pause Hotkeys");
        pauseItem.Click += (_, _) => { Log.Debug("TRAY", "clicked Pause / Resume Hotkeys"); manager.ToggleHotkeysSuspended(); };
        var perfItem = new Forms.ToolStripMenuItem("Disable Performance");
        perfItem.Click += (_, _) => { Log.Debug("TRAY", "clicked Performance on / off"); manager.TogglePerformance(); };
          var memoryToggleItem = new Forms.ToolStripMenuItem("Disable Memory Cleaner");
          memoryToggleItem.Click += (_, _) => { Log.Debug("TRAY", "clicked Memory Cleaner on / off"); manager.ToggleMemoryCleaner(); };
          var trimItem = new Forms.ToolStripMenuItem("Trim Programs");
          trimItem.Click += async (_, _) =>
          {
              Log.Debug("TRAY", "clicked Trim Programs");
              trimItem.Enabled = false;
              try
              {
                  var result = await manager.TrimNowAsync(manager.State.Memory);
                  manager.ShowToast("Memory", result.Message);
              }
              finally { trimItem.Enabled = true; }
          };
          var cacheItem = new Forms.ToolStripMenuItem("Clear System Cache") { Enabled = PreviewManager.IsAdministrator };
          cacheItem.Click += async (_, _) =>
          {
              Log.Debug("TRAY", "clicked Clear System Cache");
              cacheItem.Enabled = false;
              try
              {
                  var result = await manager.ClearCacheNowAsync();
                  manager.ShowToast("Memory", result.Message);
              }
              finally { cacheItem.Enabled = PreviewManager.IsAdministrator; }
          };

        var explorerItem = new Forms.ToolStripMenuItem("Restart Windows Explorer");
        explorerItem.Click += async (_, _) =>
        {
            Log.Debug("TRAY", "clicked Restart Windows Explorer");
            if (Dialogs.Ask(null, "Restart Windows Explorer",
                    "Close and restart Windows Explorer?\n\nThe taskbar and desktop disappear for a moment, and any open File Explorer windows close.",
                    "Restart", "Cancel") != 0) return;
            await manager.RestartExplorerAsync();
        };

        var debugItem = new Forms.ToolStripMenuItem("Detailed Logging") { CheckOnClick = false };
        debugItem.Click += (_, _) => { manager.SetDebugLogging(!Log.Verbose); Log.Debug("TRAY", "clicked Detailed Logging (now on)"); };
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
        exitItem.Click += (_, _) => { Log.Debug("TRAY", "clicked Exit"); Application.Current.Shutdown(); };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(profilesItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(memoryToggleItem);
        menu.Items.Add(perfItem);
        menu.Items.Add(pauseItem);
          menu.Items.Add(new Forms.ToolStripSeparator());
          menu.Items.Add(trimItem);
          menu.Items.Add(cacheItem);
          menu.Items.Add(explorerItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(debugItem);
        menu.Items.Add(openDebugItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            toggleItem.Text = manager.SwitcherEnabled ? "Disable Switcher" : "Enable Switcher";
            FillSwitcher(toggleItem, manager);
            pauseItem.Text = manager.HotkeysSuspended ? "Resume Hotkeys" : "Pause Hotkeys";
            pauseItem.Enabled = manager.SwitcherEnabled;
            perfItem.Text = manager.State.Performance.Enabled ? "Disable Performance" : "Enable Performance";
              profilesItem.Enabled = manager.SwitcherEnabled;
              memoryToggleItem.Text = manager.MemoryCleanerEnabled ? "Disable Memory Cleaner" : "Enable Memory Cleaner";
              trimItem.Enabled = manager.MemoryCleanerEnabled;
              cacheItem.Enabled = manager.MemoryCleanerEnabled && PreviewManager.IsAdministrator;
              debugItem.Checked = Log.Verbose;
            FillProfiles(profilesItem, manager);
        };

          menu.Closed += (_, _) => toggleItem.DropDown.Close();
          toggleItem.DropDown.KeyDown += (_, e) => { if (e.KeyCode == System.Windows.Forms.Keys.Escape) menu.Close(); };
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

        JabberHub.Instance.Received += ping =>
        {
            if (!JabberHub.Instance.Config.Notify) { Log.Debug("JABBER", "notification skipped: disabled"); return; }
            Log.Debug("JABBER", "notification queued");
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                _icon.ShowBalloonTip(6000, "Jabber Ping", ping.Body.Length > 160 ? ping.Body[..160] : ping.Body, Forms.ToolTipIcon.Info)));
        };
        _icon.BalloonTipClicked += (_, _) => JabberHub.OpenWindow();
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

    /// <summary>The Disable Switcher submenu: all clients at once, then one line per client (ticked = the switcher leaves it alone).</summary>
    private static void FillSwitcher(Forms.ToolStripMenuItem parent, PreviewManager manager)
    {
        parent.DropDownItems.Clear();
        parent.DropDown.AutoClose = false;
        var all = new Forms.ToolStripMenuItem(manager.SwitcherEnabled ? "Disable All" : "Enable All");
        all.Click += (_, _) => { Log.Debug("TRAY", "clicked Disable / Enable All (switcher)"); manager.ToggleSwitcher(); };
        parent.DropDownItems.Add(all);
        parent.DropDownItems.Add(new Forms.ToolStripSeparator());

        var clients = manager.SwitcherClientList();
        if (clients.Count == 0)
        {
            parent.DropDownItems.Add(new Forms.ToolStripMenuItem("(no clients open)") { Enabled = false });
            return;
        }
        foreach (var client in clients)
        {
            string key = client.Key;
            bool disabled = client.Disabled;
            var item = new Forms.ToolStripMenuItem(client.Text.Replace("&", "&&"))
            {
                Checked = disabled,
                Enabled = manager.SwitcherEnabled && key.Length > 0,
                ToolTipText = disabled ? "Off for this client. Click to turn it back on." : "Click to turn the switcher off for this client."
            };
            item.Click += (_, _) =>
            {
                Log.Debug("TRAY", "clicked a client under Disable Switcher");
                bool nowDisabled = !item.Checked;
                manager.SetClientDisabled(key, nowDisabled);
                item.Checked = nowDisabled;
                item.ToolTipText = nowDisabled ? "Off for this client. Click to turn it back on." : "Click to turn the switcher off for this client.";
            };
            parent.DropDownItems.Add(item);
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
            item.Click += (_, _) => { Log.Debug("TRAY", "clicked a profile"); manager.SwitchProfile(chosen); };    // works with the Configuration window open too: it follows
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



