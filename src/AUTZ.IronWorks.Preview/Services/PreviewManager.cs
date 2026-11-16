using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Views;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Owns one ThumbnailWindow per shown EVE client, the hotkeys, profiles, and the settings dialog.</summary>
public sealed class PreviewManager : IDisposable
{
    /// <summary>Where a thumbnail's position/size is stored, and which client slot (opening order) it is.</summary>
    private sealed class Meta
    {
        public string LayoutKey = "";
        public int Slot;
    }

    private sealed record SavedBox(double Left, double Top, double Width, double Height);

    private AppSettings _settings;
    private AppState _state;
    private readonly EveWindowService _eve = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly WindowWatcher _watcher = new();
    private readonly Dictionary<nint, ThumbnailWindow> _windows = new();
    private readonly Dictionary<ThumbnailWindow, Meta> _meta = new();
    private readonly Dictionary<nint, SavedBox> _inherit = new();
    private readonly HashSet<nint> _clientHandles = new();
    private readonly DispatcherTimer _scanTimer = new();
    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _minimiseTimer = new();
    private readonly DispatcherTimer _refreshSoonTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private IReadOnlyList<EveClientWindow> _clients = Array.Empty<EveClientWindow>();
    private SettingsWindow? _settingsWindow;

    private bool _visible = true;          // the tray "hide / show all thumbnails" switch
    private bool _hiddenByFocus;           // hidden because EVE doesn't have focus
    private nint _activeClient;            // the EVE client that has focus right now (0 = none)
    private nint _lastActivated;           // last client we switched to (used by the next / previous hotkeys)

    public bool ThumbnailsVisible => _visible;

    public AppSettings Settings => _settings;

    public PreviewManager(AppSettings settings, AppState state)
    {
        _settings = settings;
        _state = state;
    }

    public void Start()
    {
        _scanTimer.Tick += (_, _) => Refresh();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _hiddenByFocus = true;
            ApplyVisibility();
        };
        _minimiseTimer.Tick += (_, _) =>
        {
            _minimiseTimer.Stop();
            MinimiseInactive();
        };
        _refreshSoonTimer.Tick += (_, _) =>
        {
            _refreshSoonTimer.Stop();
            Refresh();
        };

        _watcher.ForegroundChanged += hwnd => SyncForeground(hwnd);
        _watcher.TitleChanged += hwnd =>
        {
            // a known client changed its title (login screen -> character): refresh right away
            if (_clientHandles.Contains(hwnd) && !_refreshSoonTimer.IsEnabled) _refreshSoonTimer.Start();
        };

        ApplySettings();
        Refresh();
        _scanTimer.Start();
    }

    // ---------- client scanning ----------

    public void Refresh()
    {
        _clients = _eve.FindClients();   // already in the order the clients were opened
        _clientHandles.Clear();
        foreach (var c in _clients) _clientHandles.Add(c.Handle);
        var live = _clients.ToDictionary(c => c.Handle);

        foreach (nint stale in _inherit.Keys.Where(h => !live.ContainsKey(h)).ToList()) _inherit.Remove(stale);

        foreach (nint handle in _windows.Keys.ToList())
        {
            var window = _windows[handle];
            if (!live.TryGetValue(handle, out var client))
            {
                CloseWindow(handle, false);                  // client closed
            }
            else if (client.CharacterName != window.Client.CharacterName)
            {
                CloseWindow(handle, true);                   // login screen -> character: remember where it was
            }
            else if (!ShouldShow(client))
            {
                CloseWindow(handle, false);                  // unticked in settings
            }
        }

        for (int i = 0; i < _clients.Count; i++)
        {
            var client = _clients[i];
            int slot = i + 1;

            if (_windows.TryGetValue(client.Handle, out var existing))
            {
                var meta = _meta[existing];
                bool slotChanged = meta.Slot != slot;
                meta.Slot = slot;

                string key = LayoutKeyFor(client, slot);
                if (key != meta.LayoutKey) Relayout(existing, meta, key);
                if (slotChanged && client.IsLoginScreen) ApplyStyle(existing);
            }
            else if (ShouldShow(client))
            {
                _windows[client.Handle] = CreateWindow(client, slot);
            }
        }

        SyncForeground(NativeMethods.GetForegroundWindow(), true);
    }

    private void CloseWindow(nint handle, bool rememberGeometry)
    {
        if (!_windows.TryGetValue(handle, out var window)) return;
        if (rememberGeometry)
            _inherit[handle] = new SavedBox(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        window.Close();
        _meta.Remove(window);
        _windows.Remove(handle);
    }

    private bool ShouldShow(EveClientWindow client)
    {
        if (client.IsLoginScreen) return _settings.ShowLoginScreens;
        var c = _settings.TryGetCharacter(client.CharacterName);
        return c is null || !c.Hidden;
    }

    /// <summary>
    /// The settings entry that holds a thumbnail's position and size.
    /// "Client" mode: the Nth client to open always uses the Nth entry, logged in or not.
    /// "Character" mode: a character's own entry. A login screen uses the entry of the character that usually
    /// logs in on that slot, so it sits where that character's thumbnail will be.
    /// </summary>
    private string LayoutKeyFor(EveClientWindow client, int slot)
    {
        if (_settings.PositionMode == AppSettings.ModeClient) return $"[client {slot}]";
        if (!client.IsLoginScreen) return client.CharacterName;

        if (_settings.SlotCharacters.TryGetValue(slot.ToString(), out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped) &&
            !_clients.Any(c => !c.IsLoginScreen && c.CharacterName.Equals(mapped, StringComparison.OrdinalIgnoreCase)))
            return mapped;
        return $"[login {slot}]";
    }

    private void RememberSlot(int slot, string character)
    {
        string key = slot.ToString();
        if (_settings.SlotCharacters.TryGetValue(key, out var old) && old == character) return;
        _settings.SlotCharacters[key] = character;
        SaveSettings();
    }

    private ThumbnailWindow CreateWindow(EveClientWindow client, int slot)
    {
        string key = LayoutKeyFor(client, slot);
        if (!client.IsLoginScreen) RememberSlot(slot, client.CharacterName);

        SavedBox? inherited = null;
        if (_inherit.TryGetValue(client.Handle, out var box))
        {
            inherited = box;
            _inherit.Remove(client.Handle);
        }

        var saved = _settings.TryGetCharacter(key);
        bool hasPosition = saved is not null && saved.Left.HasValue && saved.Top.HasValue &&
                           IsOnScreen(saved.Left.Value, saved.Top.Value);

        // a character with no saved spot takes over the spot (and size) of the login screen it replaces
        if (!hasPosition && inherited is not null && _settings.NewCharactersInheritPosition)
        {
            var c = _settings.GetOrAddCharacter(key);
            c.Left = inherited.Left;
            c.Top = inherited.Top;
            if (!_settings.SameSizeForAll && c.Width <= 0 && c.Height <= 0)
            {
                c.Width = (int)Math.Round(inherited.Width);
                c.Height = (int)Math.Round(inherited.Height);
            }
            SaveSettings();
        }

        var (width, height) = SizeFor(key);
        var (left, top) = PositionFor(key);
        var window = new ThumbnailWindow(client, _settings, width, height, left, top);
        _meta[window] = new Meta { LayoutKey = key, Slot = slot };
        window.Clicked += w => ActivateClient(w.Client.Handle);
        window.Moved += OnMoved;
        window.Resized += OnResized;
        window.Resizing += OnResizing;
        window.Menu = BuildMenu();
        window.SnapTargets = SnapTargetsFor;
        ApplyStyle(window);
        window.SetBorder(BorderFor(window));
        if (ShouldBeVisible(window)) window.Show();
        return window;
    }

    /// <summary>Moves an existing thumbnail to where another layout entry says it should be.</summary>
    private void Relayout(ThumbnailWindow window, Meta meta, string key)
    {
        meta.LayoutKey = key;
        var saved = _settings.TryGetCharacter(key);
        if (saved is not null && saved.Left.HasValue && saved.Top.HasValue && IsOnScreen(saved.Left.Value, saved.Top.Value))
        {
            window.Left = saved.Left.Value;
            window.Top = saved.Top.Value;
        }
        var (width, height) = SizeFor(key);
        window.Width = width;
        window.Height = height;
    }

    private IReadOnlyList<NativeMethods.RECT> SnapTargetsFor(ThumbnailWindow self) =>
        _windows.Values.Where(w => w != self && w.IsVisible).Select(w => w.GetBounds()).ToList();

    // ---------- sizing, position, appearance ----------

    private (double Width, double Height) SizeFor(string key)
    {
        double defaultWidth = _settings.PreviewWidth;
        double defaultHeight = _settings.PreviewHeight > 0 ? _settings.PreviewHeight : Math.Round(defaultWidth * 9d / 16d);
        if (_settings.SameSizeForAll) return (defaultWidth, defaultHeight);

        var c = _settings.TryGetCharacter(key);
        double width = c is not null && c.Width > 0 ? c.Width : defaultWidth;
        double height = c is not null && c.Height > 0
            ? c.Height
            : (c is not null && c.Width > 0 ? Math.Round(c.Width * 9d / 16d) : defaultHeight);
        return (width, height);
    }

    private (double Left, double Top) PositionFor(string key)
    {
        var c = _settings.TryGetCharacter(key);
        if (c is not null && c.Left.HasValue && c.Top.HasValue && IsOnScreen(c.Left.Value, c.Top.Value))
            return (c.Left.Value, c.Top.Value);

        // no saved position: stack down the left edge (windows are created in client opening order)
        double top = 24;
        foreach (var w in _windows.Values)
        {
            if (Math.Abs(w.Left - 24) < 1) top = Math.Max(top, w.Top + w.Height);
        }
        return (24, top);
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50 &&
        left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
        top >= SystemParameters.VirtualScreenTop - 10 &&
        top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    private void ApplyStyle(ThumbnailWindow window)
    {
        if (!_meta.TryGetValue(window, out var meta)) return;
        bool login = window.Client.IsLoginScreen;
        var c = login ? null : _settings.TryGetCharacter(window.Client.CharacterName);

        string text = login ? $"Login screen {meta.Slot}" : window.Client.CharacterName;
        if (c is not null && !string.IsNullOrWhiteSpace(c.Label)) text = c.Label.Trim();

        Brush foreground = Ui.BrushFrom(c?.LabelColourHex, _settings.DefaultLabelColourHex, Brushes.White);
        Brush background = Ui.BrushWithOpacity(_settings.LabelBackgroundHex, _settings.LabelBackgroundOpacityPercent, Brushes.Transparent);

        window.SetLabel(new LabelSpec(
            text, foreground, _settings.ShowLabels,
            _settings.LabelFontName, Math.Clamp(_settings.LabelFontSize, 6, 72), _settings.LabelBold,
            background, _settings.LabelPosition, _settings.LabelOffsetX, _settings.LabelOffsetY));

        window.SetExcluded(!login && _settings.ShowExcludedOverlay && c is not null && c.ExcludeFromCycling);
    }

    private BorderSpec? BorderFor(ThumbnailWindow window)
    {
        bool active = window.Client.Handle == _activeClient;
        if (active && _settings.HighlightActive)
            return new BorderSpec(_settings.ActiveBorderHex, _settings.ActiveBorderWidth, _settings.ActiveBorderStyle);
        if (!active && _settings.ShowInactiveBorder)
            return new BorderSpec(_settings.InactiveBorderHex, _settings.InactiveBorderWidth, _settings.InactiveBorderStyle);
        return null;
    }

    private void UpdateBorders()
    {
        foreach (var w in _windows.Values) w.SetBorder(BorderFor(w));
    }

    private void SaveSettings() => ProfileStore.Save(_state.ActiveProfile, _settings);

    private void OnMoved(ThumbnailWindow window)
    {
        if (!_meta.TryGetValue(window, out var meta)) return;
        var c = _settings.GetOrAddCharacter(meta.LayoutKey);
        c.Left = window.Left;
        c.Top = window.Top;
        SaveSettings();
    }

    private void OnResizing(ThumbnailWindow window, double width, double height)
    {
        if (!_settings.SameSizeForAll) return;
        foreach (var other in _windows.Values)
        {
            if (other == window) continue;
            other.Width = width;
            other.Height = height;
        }
    }

    private void OnResized(ThumbnailWindow window)
    {
        if (_settings.SameSizeForAll)
        {
            _settings.PreviewWidth = (int)Math.Round(window.ActualWidth);
            _settings.PreviewHeight = (int)Math.Round(window.ActualHeight);
            SaveSettings();
            return;
        }

        if (!_meta.TryGetValue(window, out var meta)) return;
        var c = _settings.GetOrAddCharacter(meta.LayoutKey);
        c.Width = (int)Math.Round(window.ActualWidth);
        c.Height = (int)Math.Round(window.ActualHeight);
        SaveSettings();
    }

    public void ResetPositions()
    {
        foreach (var c in _settings.Characters.Values)
        {
            c.Left = null;
            c.Top = null;
        }
        SaveSettings();

        double top = 24;
        // stack in the order the clients were opened
        foreach (var client in _clients)
        {
            if (!_windows.TryGetValue(client.Handle, out var w)) continue;
            w.Left = 24;
            w.Top = top;
            top += w.Height;
        }
    }

    public void SetLocked(bool locked)
    {
        _settings.LockPositions = locked;
        SaveSettings();
    }

    // ---------- focus tracking: highlight, hide-active, hide-when-EVE-not-focused, auto-minimise ----------

    private static bool IsOwnWindow(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Called the instant focus moves (and after each scan) so everything reacts without polling.</summary>
    private void SyncForeground(nint hwnd, bool force = false)
    {
        if (hwnd == 0) return;
        if (IsOwnWindow(hwnd)) return;   // our own thumbnails / settings window: leave things as they are

        nint active = _clientHandles.Contains(hwnd) ? hwnd : 0;
        bool changed = active != _activeClient;
        _activeClient = active;

        UpdateFocusState(active != 0);
        UpdateBorders();
        ApplyVisibility();
        if (changed || force) ScheduleMinimise(changed);
    }

    private void UpdateFocusState(bool eveFocused)
    {
        if (!_settings.HideWhenEveNotFocused)
        {
            _hideTimer.Stop();
            _hiddenByFocus = false;
            return;
        }

        if (eveFocused)
        {
            _hideTimer.Stop();
            _hiddenByFocus = false;
        }
        else if (!_hiddenByFocus && !_hideTimer.IsEnabled)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _settings.HideDelayMs));
            _hideTimer.Start();
        }
    }

    private bool ShouldBeVisible(ThumbnailWindow w) =>
        _visible && !_hiddenByFocus &&
        !(_settings.HideActiveThumbnail && w.Client.Handle == _activeClient);

    private void ApplyVisibility()
    {
        foreach (var w in _windows.Values)
        {
            bool show = ShouldBeVisible(w);
            if (show && !w.IsVisible) w.Show();
            else if (!show && w.IsVisible) w.Hide();
        }
    }

    private void ScheduleMinimise(bool restartTimer)
    {
        if (!_settings.AutoMinimiseInactive || _activeClient == 0)
        {
            _minimiseTimer.Stop();
            return;
        }

        int seconds = Math.Clamp(_settings.AutoMinimiseDelaySeconds, 0, 600);
        if (seconds == 0)
        {
            _minimiseTimer.Stop();
            MinimiseInactive();
        }
        else if (restartTimer || !_minimiseTimer.IsEnabled)
        {
            _minimiseTimer.Stop();
            _minimiseTimer.Interval = TimeSpan.FromSeconds(seconds);
            _minimiseTimer.Start();
        }
    }

    /// <summary>Minimises every other client (not login screens, not ones marked "never minimise").</summary>
    private void MinimiseInactive()
    {
        if (!_settings.AutoMinimiseInactive || _activeClient == 0) return;

        foreach (var client in _clients)
        {
            if (client.Handle == _activeClient || client.IsLoginScreen) continue;
            var c = _settings.TryGetCharacter(client.CharacterName);
            if (c is not null && c.NeverMinimise) continue;
            if (!NativeMethods.IsWindow(client.Handle) || NativeMethods.IsIconic(client.Handle)) continue;
            NativeMethods.ShowWindowAsync(client.Handle, NativeMethods.SW_SHOWMINNOACTIVE);
        }
    }

    // ---------- which thumbnails to show ----------

    public IReadOnlyList<string> RunningCharacters() =>
        _clients.Where(c => !c.IsLoginScreen).Select(c => c.CharacterName).ToList();

    /// <summary>Logged-in characters (in the order their clients opened), then every other known character A-Z.</summary>
    public IReadOnlyList<string> KnownCharacters()
    {
        var running = RunningCharacters();
        var others = _settings.Characters.Keys.Where(AppSettings.IsCharacterKey)
            .Concat(_settings.SlotCharacters.Values.Where(v => !string.IsNullOrWhiteSpace(v)))
            .Except(running, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        return running.Concat(others).ToList();
    }

    // ---------- settings ----------

    public void ApplySettings()
    {
        _scanTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.RefreshMilliseconds, 500, 10000));

        foreach (var w in _windows.Values)
        {
            if (!_meta.TryGetValue(w, out var meta)) continue;
            var (width, height) = SizeFor(meta.LayoutKey);
            w.Width = width;
            w.Height = height;
            w.RefreshThumbnail();   // picks up the opacity setting
            ApplyStyle(w);
        }

        if (!_settings.HideWhenEveNotFocused)
        {
            _hideTimer.Stop();
            _hiddenByFocus = false;
        }

        UpdateBorders();
        ApplyVisibility();
        RegisterHotkeys();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var dialog = new SettingsWindow(_settings, KnownCharacters(), RunningCharacters());
        _settingsWindow = dialog;
        bool? saved = dialog.ShowDialog();
        _settingsWindow = null;

        // the dialog may have renamed, added or removed profiles and changed profile hotkeys
        _state = ProfileStore.LoadState();

        if (saved == true)
        {
            _settings.CopyFrom(dialog.Result);
            SaveSettings();
            ApplySettings();
            Refresh();
            if (dialog.SwitchToProfile is { } profile) SwitchProfile(profile);
        }
        else
        {
            RegisterHotkeys();
        }
    }

    // ---------- profiles ----------

    public void SwitchProfile(string name)
    {
        if (_settingsWindow is not null) return;
        if (name.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase) || !ProfileStore.Exists(name)) return;

        SaveSettings();
        foreach (nint handle in _windows.Keys.ToList()) CloseWindow(handle, false);
        _inherit.Clear();

        _settings = ProfileStore.Load(name);
        _state.ActiveProfile = name;
        ProfileStore.SaveState(_state);

        ApplySettings();
        Refresh();
    }

    private void CycleProfile(int direction)
    {
        var names = ProfileStore.List();
        if (names.Count < 2) return;
        int index = 0;
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i].Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase)) index = i;
        }
        SwitchProfile(names[((index + direction) % names.Count + names.Count) % names.Count]);
    }

    // ---------- hotkeys ----------

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failed = new List<string>();

        void TryRegister(string? text, string what, Action action)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!_hotkeys.Register(text, action)) failed.Add(what + ": " + text);
        }

        TryRegister(_settings.CycleForwardHotkey, "Next client", () => Cycle(1));
        TryRegister(_settings.CycleBackHotkey, "Previous client", () => Cycle(-1));
        foreach (var pair in _settings.Characters)
        {
            string name = pair.Key;
            TryRegister(pair.Value.Hotkey, name, () => ActivateCharacter(name));
        }

        TryRegister(_state.NextProfileHotkey, "Next profile", () => CycleProfile(1));
        TryRegister(_state.PreviousProfileHotkey, "Previous profile", () => CycleProfile(-1));
        foreach (var pair in _state.ProfileHotkeys)
        {
            string profile = pair.Key;
            TryRegister(pair.Value, "Profile " + profile, () => SwitchProfile(profile));
        }

        if (failed.Count > 0)
        {
            MessageBox.Show(
                "These hotkeys couldn't be registered (another app is probably using them):\n\n" + string.Join("\n", failed),
                "AUTZ IronWorks Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool IsCyclable(EveClientWindow client)
    {
        if (client.IsLoginScreen) return _settings.CycleIncludeLoginScreens;
        var c = _settings.TryGetCharacter(client.CharacterName);
        return c is null || !c.ExcludeFromCycling;
    }

    /// <summary>The client list for hotkeys: the last scan, unless a client has closed since (then rescan).</summary>
    private List<EveClientWindow> CurrentClients()
    {
        var list = _clients.Where(c => NativeMethods.IsWindow(c.Handle)).ToList();
        if (list.Count == 0 || list.Count != _clients.Count) list = _eve.FindClients().ToList();
        return list;
    }

    private static int IndexOf(List<EveClientWindow> list, nint handle)
    {
        if (handle == 0) return -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Handle == handle) return i;
        }
        return -1;
    }

    /// <summary>
    /// Next / previous client, in the order the clients were opened. It works out where you are from the focused
    /// window (or the last client it switched to), skips clients that are excluded, and if Windows refuses to
    /// focus one it carries on to the next instead of getting stuck. Every press is written to preview.log.
    /// </summary>
    private void Cycle(int direction)
    {
        var all = CurrentClients();
        int count = all.Count;
        if (count == 0) return;

        nint foreground = NativeMethods.GetForegroundWindow();
        int start = IndexOf(all, foreground);
        string from = "focus";
        if (start < 0)
        {
            start = IndexOf(all, _lastActivated);
            from = "last switched";
        }

        int step = direction > 0 ? 1 : -1;
        int position = start >= 0 ? start : (direction > 0 ? -1 : 0);
        var log = new System.Text.StringBuilder();
        string names = string.Join(", ", all.Select((c, i) => $"#{i + 1} {c.CharacterName}"));
        log.Append($"Cycle {(direction > 0 ? "next" : "previous")}: [{names}] focus=0x{foreground:X}, from {(start >= 0 ? $"#{start + 1} ({from})" : "nothing")}");

        for (int tried = 0; tried < count; tried++)
        {
            position = ((position + step) % count + count) % count;
            var client = all[position];
            if (!IsCyclable(client)) continue;
            if (client.Handle == foreground) continue;

            bool ok = _eve.Activate(client.Handle, out string how);
            _lastActivated = client.Handle;
            log.Append($" -> #{position + 1} '{client.CharacterName}' {(ok ? "ok" : "FAILED")} ({how})");
            if (ok)
            {
                SyncForeground(client.Handle);
                Log.Write(log.ToString());
                return;
            }
        }

        Log.Write(log.ToString() + " -> nothing to switch to");
    }

    private void ActivateClient(nint handle)
    {
        if (_eve.Activate(handle, out string how))
        {
            _lastActivated = handle;
            SyncForeground(handle);
        }
        else
        {
            Log.Write($"Click switch failed ({how})");
        }
    }

    private void ActivateCharacter(string name)
    {
        var client = CurrentClients()
            .FirstOrDefault(c => c.CharacterName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (client is not null) ActivateClient(client.Handle);
    }

    // ---------- visibility & thumbnail right-click menu ----------

    public void ToggleVisibility()
    {
        _visible = !_visible;
        ApplyVisibility();
    }

    /// <summary>Right-click menu on a thumbnail. Everything else lives in Settings.</summary>
    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) => OpenSettings();

        var lockItem = new MenuItem { Header = "Lock positions and sizes", IsCheckable = true };
        lockItem.Click += (_, _) => SetLocked(lockItem.IsChecked);
        menu.Opened += (_, _) => lockItem.IsChecked = _settings.LockPositions;

        var resetItem = new MenuItem { Header = "Reset all positions" };
        resetItem.Click += (_, _) => ResetPositions();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(settingsItem);
        menu.Items.Add(lockItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _hideTimer.Stop();
        _minimiseTimer.Stop();
        _refreshSoonTimer.Stop();
        _watcher.Dispose();
        _hotkeys.Dispose();
        foreach (var w in _windows.Values.ToList()) w.Close();
        _windows.Clear();
        _meta.Clear();
    }
}
