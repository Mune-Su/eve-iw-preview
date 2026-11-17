using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Views;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Owns one ThumbnailWindow per shown EVE client, the hotkeys, profiles, alerts, log monitoring and the settings window.
/// "Session" settings are what is running now; "saved" settings are what is in the config files. APPLY changes the
/// first, SAVE writes the second.
/// </summary>
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
    private bool _sessionOnly;             // true while the running settings differ from the saved ones (after APPLY)

    private readonly EveWindowService _eve = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly WindowWatcher _watcher = new();
    private readonly LogMonitorService _logs = new();
    private readonly NotificationService _notify = new();
    private readonly Dictionary<nint, ThumbnailWindow> _windows = new();
    private readonly Dictionary<ThumbnailWindow, Meta> _meta = new();
    private readonly Dictionary<nint, SavedBox> _inherit = new();
    private readonly HashSet<nint> _clientHandles = new();
    private readonly HashSet<nint> _everFocused = new();
    private readonly Dictionary<string, string> _systems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastNotified = new();
    private readonly Dictionary<nint, DateTime> _alertUntil = new();
    private readonly DispatcherTimer _scanTimer = new();
    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _minimiseTimer = new();
    private readonly DispatcherTimer _alertTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _refreshSoonTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly DispatcherTimer _trimTimer = new();
    private IReadOnlyList<EveClientWindow> _lastScan = Array.Empty<EveClientWindow>();   // what the latest scan found, before ordering
    private bool _scanDirty = true;                    // true = the next scan must redo the whole layout even if no client changed
    private static readonly TimeSpan IdleScan = TimeSpan.FromSeconds(3);                // scan interval while no EVE client is open
    private IReadOnlyList<EveClientWindow> _clients = Array.Empty<EveClientWindow>();
    private Dictionary<nint, string> _loginAssign = new();   // v0.10.0: login screen -> the saved character whose place it holds
    private Dictionary<nint, int> _slots = new();            // v0.11.1: client -> its numbered place (Saved / Manual order keep a closed client's place free)
    private SettingsWindow? _settingsWindow;

    private bool _visible = true;          // the tray "hide / show all thumbnails" switch
    private bool _hiddenByFocus;           // hidden because EVE doesn't have focus
    private nint _activeClient;            // the EVE client that has focus right now (0 = none)
    private nint _lastActive;              // the EVE client that had focus most recently
    private nint _lastActivated;           // last client we switched to (used by the next / previous hotkeys)
    private nint _pendingTarget;           // v0.9.6: a switch Windows has accepted but not finished yet (0 = none)
    private long _pendingUntil;            // Log.Ms after which the pending switch is forgotten
    private long _staleUntil;              // v0.9.9: after a switch, late focus reports from earlier clients are ignored until then
    private const int StaleMs = 250;
    private const int PendingMs = 400;     // how long a switch is allowed to take before Windows' word is trusted again
    private string _alertHex = "#EF4444";
    private string _lastClientList = "";    // detailed log: only log the client list when it changes
    private bool _blink;

    public bool ThumbnailsVisible => _visible;
    public bool SettingsOpen => _settingsWindow is not null;
    public bool HotkeysSuspended { get; private set; }
    public AppSettings Settings => _settings;
    public AppState State => _state;
    public string LogStatus => _logs.Status;

    /// <summary>Raised when something should be shown as a Windows notification (title, text).</summary>
    public event Action<string, string>? Toast;

    public PreviewManager(AppSettings settings, AppState state)
    {
        _settings = settings;
        _state = state;
    }

    public void Start()
    {
        _scanTimer.Tick += (_, _) => Scan();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _hiddenByFocus = true;
            Log.Debug("THUMB", "EVE not in front for the hide delay -> hiding all thumbnails");
            ApplyVisibility();
        };
        _minimiseTimer.Tick += (_, _) =>
        {
            _minimiseTimer.Stop();
            MinimiseInactive();
        };
        _alertTimer.Tick += (_, _) => OnAlertTick();
        _displayTimer.Tick += (_, _) =>
        {
            _displayTimer.Stop();
            RecoverOffscreen(false);
        };
        _refreshSoonTimer.Tick += (_, _) =>
        {
            _refreshSoonTimer.Stop();
            Scan();
        };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            MemoryTrim.Trim();
        };

        _watcher.ForegroundChanged += hwnd =>
        {
            Log.Debug("WINEVENT", $"foreground -> {Log.Win(hwnd)}");
            if (hwnd == 0) return;                                  // mid-switch gap: nothing to learn from it
            if (IsPending())
            {
                if (hwnd == _pendingTarget)
                {
                    Log.Debug("SWITCH", $"  switch confirmed by Windows: {Log.Win(hwnd)}");
                    ClearPending();
                }
                else if (_clientHandles.Contains(hwnd) || IsOwnWindow(hwnd))
                {
                    // the client we are leaving reported in late: it must not undo the switch
                    Log.Debug("SWITCH", $"  ignored stale foreground {Log.Win(hwnd)} (switching to {Log.Win(_pendingTarget)})");
                    return;
                }
                else
                {
                    ClearPending();                                 // something else really came to the front
                }
            }
            else if (Log.Ms <= _staleUntil && hwnd != _lastActivated && (_clientHandles.Contains(hwnd) || IsOwnWindow(hwnd)))
            {
                // held-key cycling: Windows reports earlier switches after the latest one has been confirmed. Without
                // this the highlight jumped back to a client you had already passed.
                Log.Debug("SWITCH", $"  ignored late foreground {Log.Win(hwnd)} (last switch was to {Log.Win(_lastActivated)})");
                return;
            }
            SyncForeground(hwnd);
        };
        _watcher.TitleChanged += hwnd =>
        {
            // a known client changed its title (login screen -> character): refresh right away
            if (_clientHandles.Contains(hwnd) && !_refreshSoonTimer.IsEnabled)
            {
                Log.Debug("WINEVENT", $"client title changed {Log.Win(hwnd)} -> refresh");
                _refreshSoonTimer.Start();
            }
        };

        _logs.EventRaised += OnLogEvent;
        _logs.SystemChanged += OnSystemChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        StartupService.Refresh(_state.RunOnStartup);
        ApplySettings();
        Refresh();
        _scanTimer.Start();
        ScheduleTrim(10000);                           // once start-up has settled, hand back the memory start-up needed
    }

    /// <summary>Compacts the heap and empties the working set after the given delay (see MemoryTrim).</summary>
    private void ScheduleTrim(int milliseconds)
    {
        _trimTimer.Stop();
        _trimTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _trimTimer.Start();
    }

    // ---------- client scanning ----------

    /// <summary>Looks at the clients and redoes the whole layout (tray "Refresh Clients", and after every settings change).</summary>
    public void Refresh()
    {
        _scanDirty = true;
        Scan();
    }

    /// <summary>
    /// The periodic scan. If no client has opened, closed or changed its title since the last scan (nearly every time) the layout
    /// is already right, so all that is left is to keep the focus state up to date.
    /// </summary>
    private void Scan()
    {
        var raw = _eve.FindClients();
        _watcher.WatchProcesses(_eve.ClientProcessIds);       // title changes are only watched inside the EVE processes

        if (!_scanDirty && SameClients(raw, _lastScan))
        {
            FollowForeground();
            UpdateScanInterval();
            return;
        }
        _scanDirty = false;
        _lastScan = raw;

        _clients = OrderClients(raw);                   // the client order: opening order, or the saved / manual order
        if (Log.Verbose)
        {
            string list = $"(order: {_settings.ClientOrderMode}) " +
                string.Join(", ", _clients.Select((c, i) => $"#{i + 1} {(c.IsLoginScreen ? "[login]" : c.CharacterName)} 0x{c.Handle:X}"));
            if (list != _lastClientList)
            {
                _lastClientList = list;
                Log.Debug("CLIENTS", $"{_clients.Count} client(s): {(list.Length == 0 ? "none" : list)}");
            }
        }
        _clientHandles.Clear();
        foreach (var c in _clients) _clientHandles.Add(c.Handle);
        var live = _clients.ToDictionary(c => c.Handle);

        foreach (nint stale in _inherit.Keys.Where(h => !live.ContainsKey(h)).ToList()) _inherit.Remove(stale);
        foreach (nint gone in _everFocused.Where(h => !live.ContainsKey(h)).ToList()) _everFocused.Remove(gone);

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
            int slot = SlotOf(client, i);

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

        FollowForeground();
        UpdateScanInterval();
    }

    private static bool SameClients(IReadOnlyList<EveClientWindow> a, IReadOnlyList<EveClientWindow> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// v0.9.6: while a switch is still finishing, the foreground Windows reports is stale. Don't let the scan
    /// overwrite the active client with it.
    /// </summary>
    private void FollowForeground()
    {
        if (IsPending()) SyncForeground(_pendingTarget, true);
        else SyncForeground(NativeMethods.GetForegroundWindow(), true);
    }

    /// <summary>With no EVE client open there is nothing to keep up with, so look less often (a client that starts is found within a few seconds).</summary>
    private void UpdateScanInterval()
    {
        var wanted = TimeSpan.FromMilliseconds(Math.Clamp(_settings.RefreshMilliseconds, 500, 10000));
        if (_lastScan.Count == 0 && wanted < IdleScan) wanted = IdleScan;
        if (_scanTimer.Interval != wanted) _scanTimer.Interval = wanted;
    }

    // ---------- client order ----------

    /// <summary>Puts the clients (given oldest first) in the order the profile asks for. "Saved" also remembers new characters and handles "Reset order".</summary>
    private IReadOnlyList<EveClientWindow> OrderClients(IReadOnlyList<EveClientWindow> openOrder)
    {
        if (_settings.ClientOrderResetPending)
        {
            if (_settings.ClientOrderMode != AppSettings.OrderSaved)
            {
                Persist(s => s.ClientOrderResetPending = false);               // the mode changed since: nothing to reset
            }
            else if (openOrder.Count == 0)
            {
                // every client is closed (or none was ever detected): wipe the list, it builds again as clients open
                Log.Debug("ORDER", "saved order cleared (no EVE clients are open)");
                Persist(s =>
                {
                    s.ClientOrder.Clear();
                    s.ClientOrderResetPending = false;
                });
            }
        }

        if (_settings.ClientOrderMode == AppSettings.OrderSaved)
        {
            var list = new List<string>(_settings.ClientOrder);
            var added = new List<string>();
            foreach (var client in openOrder.Where(c => !c.IsLoginScreen))
            {
                if (list.Contains(client.CharacterName, StringComparer.OrdinalIgnoreCase)) continue;
                ClientOrdering.AddInPlace(list, client.CharacterName, openOrder);
                added.Add(client.CharacterName);
            }
            if (added.Count > 0)
            {
                Log.Debug("ORDER", "saved order: adding " + string.Join(", ", added));
                Persist(s => s.ClientOrder = new List<string>(list));
            }
        }

        var ordered = ClientOrdering.Apply(openOrder, _settings, out var assign, out var slots);
        _loginAssign = assign;
        _slots = slots;
        return ordered;
    }

    /// <summary>The numbered place of a client: the same number the thumbnail position and the client hotkey use.</summary>
    private int SlotOf(EveClientWindow client, int indexInList) =>
        _slots.TryGetValue(client.Handle, out int slot) ? slot : indexInList + 1;

    public bool OrderResetPending => _settings.ClientOrderResetPending;

    /// <summary>"Reset order": the saved order is cleared the next time no EVE client is open (at once if none is open now).</summary>
    public void ArmClientOrderReset()
    {
        Log.Debug("ORDER", "reset order requested");
        Persist(s => s.ClientOrderResetPending = true);
        Refresh();
    }

    public void CancelClientOrderReset()
    {
        Log.Debug("ORDER", "reset order cancelled");
        Persist(s => s.ClientOrderResetPending = false);
    }

    // ---------- switch in progress ----------
    private bool IsPending()
    {
        if (_pendingTarget == 0) return false;
        if (Log.Ms <= _pendingUntil && NativeMethods.IsWindow(_pendingTarget)) return true;
        Log.Debug("SWITCH", $"  pending switch to {Log.Win(_pendingTarget)} expired; foreground is {Log.Win(NativeMethods.GetForegroundWindow())}");
        ClearPending();
        return false;
    }

    private void SetPending(nint target)
    {
        _pendingTarget = target;
        _pendingUntil = Log.Ms + PendingMs;
    }

    private void ClearPending()
    {
        _pendingTarget = 0;
        _pendingUntil = 0;
    }

    /// <summary>
    /// The EVE client you are really on: the one a switch is heading to if it hasn't finished yet, otherwise the
    /// window in front (if it's a client), otherwise the last client that had focus.
    /// </summary>
    private nint CurrentClientHandle(out string from)
    {
        if (IsPending()) { from = "switch in progress"; return _pendingTarget; }
        nint fg = NativeMethods.GetForegroundWindow();
        if (fg != 0 && _clientHandles.Contains(fg)) { from = "focus"; return fg; }
        if (_activeClient != 0) { from = "active"; return _activeClient; }
        from = "last switched";
        return _lastActivated;
    }

    private void CloseWindow(nint handle, bool rememberGeometry)
    {
        if (!_windows.TryGetValue(handle, out var window)) return;
        Log.Debug("THUMB", $"close thumbnail of '{window.Client.CharacterName}' 0x{handle:X}{(rememberGeometry ? " (login -> character, position kept)" : "")}");
        if (rememberGeometry)
            _inherit[handle] = new SavedBox(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        window.Close();
        _meta.Remove(window);
        _windows.Remove(handle);
    }

    private bool ShouldShow(EveClientWindow client)
    {
        if (client.IsLoginScreen) return _settings.ShowLoginScreens;
        // the thumbnail group in use: only its characters get a thumbnail (unless the group also shows others)
        var group = _settings.ActiveThumbGroup();
        if (group is not null && !group.ShowOthers &&
            !group.Characters.Any(n => n.Equals(client.CharacterName, StringComparison.OrdinalIgnoreCase)))
            return false;
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

        // saved / manual order: a login screen sits where the saved character whose place it holds will be
        if (_settings.ClientOrderMode != AppSettings.OrderAutomatic && _loginAssign.TryGetValue(client.Handle, out var held))
            return held;

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
        Persist(s => s.SlotCharacters[key] = character);
    }

    private ThumbnailWindow CreateWindow(EveClientWindow client, int slot)
    {
        string key = LayoutKeyFor(client, slot);
        if (!client.IsLoginScreen) RememberSlot(slot, client.CharacterName);

        SavedBox? inherited = null;
        if (_inherit.TryGetValue(client.Handle, out var previous))
        {
            inherited = previous;
            _inherit.Remove(client.Handle);
        }

        var saved = _settings.TryGetCharacter(key);
        bool hasPosition = saved is not null && saved.Left.HasValue && saved.Top.HasValue &&
                           IsOnScreen(saved.Left.Value, saved.Top.Value);

        // a character with no saved spot takes over the spot (and size) of the login screen it replaces
        if (!hasPosition && inherited is not null && _settings.NewCharactersInheritPosition)
        {
            var box = inherited;
            bool keepSize = !_settings.SameSizeForAll;
            Persist(s =>
            {
                var c = s.GetOrAddCharacter(key);
                c.Left = box.Left;
                c.Top = box.Top;
                if (keepSize && c.Width <= 0 && c.Height <= 0)
                {
                    c.Width = (int)Math.Round(box.Width);
                    c.Height = (int)Math.Round(box.Height);
                }
            });
        }

        var (width, height) = SizeFor(key);
        var (left, top) = PositionFor(key);
        var window = new ThumbnailWindow(client, _settings, width, height, left, top);
        Log.Debug("THUMB", $"create thumbnail for slot {slot} '{client.CharacterName}' 0x{client.Handle:X} key '{key}' at {left:0},{top:0} size {width:0}x{height:0}");
        _meta[window] = new Meta { LayoutKey = key, Slot = slot };
        window.Clicked += w => ActivateClient(w.Client.Handle);
        window.Moved += OnMoved;
        window.Resized += OnResized;
        window.Resizing += OnResizing;
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
        if (!login && _settings.ShowSystemName && _systems.TryGetValue(window.Client.CharacterName, out var system))
            text += "  ·  " + system;

        Brush foreground = Ui.BrushFrom(c?.LabelColourHex, _settings.DefaultLabelColourHex, Brushes.White);
        Brush background = Ui.BrushWithOpacity(_settings.LabelBackgroundHex, _settings.LabelBackgroundOpacityPercent, Brushes.Transparent);

        window.SetLabel(new LabelSpec(
            text, foreground, _settings.ShowLabels,
            _settings.LabelFontName, Math.Clamp(_settings.LabelFontSize, 6, 72), _settings.LabelBold,
            background, _settings.LabelPosition, _settings.LabelOffsetX, _settings.LabelOffsetY));

        window.SetExcluded(!login && c is not null && c.ExcludeFromCycling);     // the red X is always shown for skipped clients
    }

    private BorderSpec? BorderFor(ThumbnailWindow window)
    {
        if (_blink && _alertUntil.TryGetValue(window.Client.Handle, out var until) && until > DateTime.UtcNow)
            return new BorderSpec(_alertHex, 4, "Solid");

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

    // ---------- saving ----------

    /// <summary>
    /// Makes a change to the running settings and writes it to the config file. While APPLIED-but-unsaved settings are
    /// running, only this one change is written (to the saved copy), so positions you drag are kept without
    /// accidentally saving settings you only applied.
    /// </summary>
    private void Persist(Action<AppSettings> change)
    {
        change(_settings);
        if (!_sessionOnly)
        {
            ProfileStore.Save(_state.ActiveProfile, _settings);
            return;
        }

        var saved = ProfileStore.Load(_state.ActiveProfile);
        change(saved);
        ProfileStore.Save(_state.ActiveProfile, saved);
    }

    private void OnMoved(ThumbnailWindow window)
    {
        if (!_meta.TryGetValue(window, out var meta)) return;
        string key = meta.LayoutKey;
        double left = window.Left, top = window.Top;
        Persist(s =>
        {
            var c = s.GetOrAddCharacter(key);
            c.Left = left;
            c.Top = top;
        });
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
        int width = (int)Math.Round(window.ActualWidth);
        int height = (int)Math.Round(window.ActualHeight);

        if (_settings.SameSizeForAll)
        {
            Persist(s =>
            {
                s.PreviewWidth = width;
                s.PreviewHeight = height;
            });
            return;
        }

        if (!_meta.TryGetValue(window, out var meta)) return;
        string key = meta.LayoutKey;
        Persist(s =>
        {
            var c = s.GetOrAddCharacter(key);
            c.Width = width;
            c.Height = height;
        });
    }

    public void ResetPositions()
    {
        Persist(s =>
        {
            foreach (var c in s.Characters.Values)
            {
                c.Left = null;
                c.Top = null;
            }
        });

        double top = 24;
        // stack in the order the clients were opened
        foreach (var client in _clients)
        {
            if (!_windows.TryGetValue(client.Handle, out var w)) continue;
            w.Left = 24;
            w.Top = top;
            top += w.Height;
        }
        Toast?.Invoke(AppInfo.Name, "Thumbnail positions were reset.");
    }

    public void SetLocked(bool locked) => Persist(s => s.LockPositions = locked);

    /// <summary>Moves every thumbnail that isn't visibly on a monitor back onto the main one. Returns how many were moved.</summary>
    public int RecoverOffscreen(bool announce = true)
    {
        var areas = System.Windows.Forms.Screen.AllScreens.Select(s => s.WorkingArea).ToList();
        if (areas.Count == 0) return 0;
        var primary = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? areas[0];

        int moved = 0;
        int y = primary.Top + 24;
        foreach (var client in _clients)
        {
            if (!_windows.TryGetValue(client.Handle, out var window)) continue;

            var b = window.GetBounds();
            int width = b.Right - b.Left;
            int height = b.Bottom - b.Top;
            bool visible = areas.Any(a =>
                Math.Min(b.Right, a.Right) - Math.Max(b.Left, a.Left) >= Math.Min(60, width) &&
                Math.Min(b.Bottom, a.Bottom) - Math.Max(b.Top, a.Top) >= Math.Min(40, height));
            if (visible) continue;

            nint hwnd = new WindowInteropHelper(window).Handle;
            NativeMethods.SetWindowPos(hwnd, 0, primary.Left + 24, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            y += height + 8;
            OnMoved(window);
            moved++;
        }

        if (announce || moved > 0)
            Toast?.Invoke(AppInfo.Name, moved == 0 ? "No thumbnails were off-screen." : $"Moved {moved} thumbnail(s) back on screen.");
        return moved;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // raised on another thread: hop to the UI thread and wait a moment for Windows to settle
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _displayTimer.Stop();
            _displayTimer.Start();
        }));
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
        if (IsOwnWindow(hwnd)) return;   // our own windows: leave things as they are

        nint previous = _activeClient;
        nint active = _clientHandles.Contains(hwnd) ? hwnd : 0;
        bool changed = active != _activeClient;
        _activeClient = active;

        if (changed) Log.Debug("FOCUS", $"active EVE client: {(previous == 0 ? "none" : Log.Win(previous))} -> {(active == 0 ? "none (" + Log.Win(hwnd) + " is in front)" : Log.Win(active))}");
        if (active != 0)
        {
            _lastActive = active;
            _everFocused.Add(active);
        }
        if (changed && previous != 0 && active != 0) HandleLeave(previous);

        UpdateFocusState(active != 0);
        UpdateBorders();
        ApplyVisibility();
        if (changed || force) ScheduleMinimise(changed);
    }

    /// <summary>Minimise a client as soon as you switch away from it, if that is set for it.</summary>
    private void HandleLeave(nint previous)
    {
        var client = _clients.FirstOrDefault(c => c.Handle == previous);
        if (client is null || client.IsLoginScreen) return;
        var c = _settings.TryGetCharacter(client.CharacterName);
        if (c is not null && c.MinimiseOnLeave && NativeMethods.IsWindow(previous) && !NativeMethods.IsIconic(previous))
        {
            Log.Debug("WINDOW", $"minimise on leave (per-character setting) {Log.Win(previous)}");
            NativeMethods.ShowWindowAsync(previous, NativeMethods.SW_SHOWMINNOACTIVE);
        }
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
            Log.Debug("THUMB", $"EVE lost focus -> thumbnails hide in {_settings.HideDelayMs} ms unless an EVE client comes back");
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
            if (show && !w.IsVisible)
            {
                Log.Debug("THUMB", $"show thumbnail '{w.Client.CharacterName}'");
                w.Show();
            }
            else if (!show && w.IsVisible)
            {
                string why = !_visible ? "hidden from the tray" : _hiddenByFocus ? "EVE not in front" : "it is the active client (hide active)";
                Log.Debug("THUMB", $"hide thumbnail '{w.Client.CharacterName}' ({why})");
                w.Hide();
            }
        }
    }

    private void ScheduleMinimise(bool restartTimer)
    {
        if (!_settings.AutoMinimiseInactive)
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
            Log.Debug("WINDOW", $"auto-minimise scheduled in {seconds} s");
            _minimiseTimer.Start();
        }
    }

    /// <summary>
    /// Minimises every client except the one in front (or, if EVE isn't in front, the one used last). Clients that
    /// haven't been in front yet are included from the start unless that is switched off, so a fresh start-up doesn't
    /// waste resources on clients you aren't looking at.
    /// </summary>
    private void MinimiseInactive()
    {
        if (!_settings.AutoMinimiseInactive || _clients.Count == 0) return;

        nint keep = IsPending() ? _pendingTarget : _activeClient;   // never minimise the client a switch is heading to
        if (keep == 0 && _settings.AutoMinimiseKeepLastActive)
        {
            keep = _clientHandles.Contains(_lastActive) ? _lastActive : _clients[_clients.Count - 1].Handle;
        }

        foreach (var client in _clients)
        {
            if (client.Handle == keep) continue;
            if (!_settings.AutoMinimiseOnStart && (client.IsLoginScreen || !_everFocused.Contains(client.Handle))) continue;

            var c = client.IsLoginScreen ? null : _settings.TryGetCharacter(client.CharacterName);
            if (c is not null && c.NeverMinimise) continue;
            if (!NativeMethods.IsWindow(client.Handle) || NativeMethods.IsIconic(client.Handle)) continue;
            Log.Debug("WINDOW", $"auto-minimise {Log.Win(client.Handle)} (keeping {Log.Win(keep)})");
            NativeMethods.ShowWindowAsync(client.Handle, NativeMethods.SW_SHOWMINNOACTIVE);
        }
    }

    // ---------- which characters are known ----------

    public IReadOnlyList<string> RunningCharacters() =>
        _clients.Where(c => !c.IsLoginScreen).Select(c => c.CharacterName).ToList();

    /// <summary>Logged-in clients right now, found with a fresh look at the windows (used by the Thumbnails page scan).</summary>
    public IReadOnlyList<string> ScanCharacters() =>
        _eve.FindClients().Where(c => !c.IsLoginScreen).Select(c => c.CharacterName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
        _scanDirty = true;
        RegisterLogNames();
        _scanTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.RefreshMilliseconds, 500, 10000));
        LogSettings("settings applied");

        foreach (var w in _windows.Values)
        {
            if (!_meta.TryGetValue(w, out var meta)) continue;
            var (width, height) = SizeFor(meta.LayoutKey);
            w.Width = width;
            w.Height = height;
            w.SetAlwaysOnTop(_settings.AlwaysOnTop);
            w.RefreshThumbnail();   // picks up the opacity setting
            ApplyStyle(w);
        }

        if (!_settings.HideWhenEveNotFocused)
        {
            _hideTimer.Stop();
            _hiddenByFocus = false;
        }

        ConfigureLogs();
        UpdateBorders();
        ApplyVisibility();
        RegisterHotkeys();
    }

    /// <summary>The logs never show a character or profile name, only a code: tell the log which names to replace.</summary>
    private void RegisterLogNames()
    {
        foreach (string name in _settings.Characters.Keys.Where(AppSettings.IsCharacterKey)) Log.RegisterPilot(name);
        foreach (string name in _settings.AllGroupCharacters()) Log.RegisterPilot(name);
        foreach (string name in _settings.ClientOrder) Log.RegisterPilot(name);
        foreach (string name in _settings.SlotCharacters.Values) Log.RegisterPilot(name);
        Log.RegisterProfile(_state.ActiveProfile);
        foreach (string name in ProfileStore.List()) Log.RegisterProfile(name);
    }

    /// <summary>Detailed log: the settings that change what happens when you switch clients.</summary>
    private void LogSettings(string why)
    {
        if (!Log.Verbose) return;
        RegisterLogNames();            // so no name can reach the log before it has been registered
        var s = _settings;
        Log.Debug("SETTINGS", $"{why}: profile '{_state.ActiveProfile}'" +
            $" | auto-minimise {(s.AutoMinimiseInactive ? $"ON delay {s.AutoMinimiseDelaySeconds}s keepLast {s.AutoMinimiseKeepLastActive} onStart {s.AutoMinimiseOnStart}" : "off")}" +
            $" | hide active {s.HideActiveThumbnail} | hide when EVE not in front {s.HideWhenEveNotFocused} ({s.HideDelayMs} ms)" +
            $" | highlight {s.HighlightActive} | always on top {s.AlwaysOnTop} | clickable {s.ThumbnailsClickable}" +
            $" | hotkeys only in EVE {s.HotkeysOnlyInEve} pass-through {s.HotkeyPassThrough} | scan {s.RefreshMilliseconds} ms");
        foreach (var pair in s.Characters.Where(p => AppSettings.IsCharacterKey(p.Key) &&
                     (p.Value.ActivateAction != "None" || p.Value.MinimiseOnLeave || p.Value.NeverMinimise || p.Value.ExcludeFromCycling)))
        {
            var c = pair.Value;
            Log.Debug("SETTINGS", $"  '{pair.Key}': on switch-to {c.ActivateAction}, minimise on leave {c.MinimiseOnLeave}, never minimise {c.NeverMinimise}, skipped by cycling {c.ExcludeFromCycling}");
        }
    }

    /// <summary>Turns detailed (admin) logging on or off and remembers it (tray menu / settings).</summary>
    public void SetDebugLogging(bool on)
    {
        _state.DebugLogging = on;
        Log.PerSessionFiles = _state.DebugLogMode == "PerSession";
        Log.Verbose = on;
        ProfileStore.SaveState(_state);
        if (on)
        {
            _lastClientList = "";
            LogSettings("current settings");
            Refresh();
        }
        Toast?.Invoke(AppInfo.Name, on ? "Detailed logging is on. Reproduce the problem, then open debug.log." : "Detailed logging is off.");
    }

    private void ConfigureLogs()
    {
        var keywords = (_settings.LogKeywords ?? "")
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logs.Configure(new LogConfig(
            _settings.LogMonitoringEnabled,
            (_settings.GameLogFolder ?? "").Trim(),
            (_settings.ChatLogFolder ?? "").Trim(),
            _settings.LogPollMilliseconds,
            _settings.LogWatchAttack,
            _settings.LogWatchMiningIdle,
            _settings.LogMiningIdleSeconds,
            keywords,
            _settings.ShowSystemName));
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            // the window is no longer always on top: bring it back to the front (and out of the taskbar if minimised)
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _hotkeys.Suspended = true;       // so the hotkey boxes in the window can record any key
        var dialog = new SettingsWindow(this);
        _settingsWindow = dialog;
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            _settingsWindow = null;
            _hotkeys.Suspended = HotkeysSuspended;
            RegisterHotkeys();
            ScheduleTrim(3000);                        // the Configuration window is the biggest thing the app ever builds: give it back
        }
    }

    // ----- called by the settings window -----

    /// <summary>APPLY: put these settings into the running session. Nothing is written to the config file.</summary>
    public void ApplyFromDialog(string profile, AppSettings settings, AppState global)
    {
        ApplyGlobal(global);

        if (profile.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            MergeLiveGeometry(settings);
            _settings.CopyFrom(settings);
        }
        else
        {
            foreach (nint handle in _windows.Keys.ToList()) CloseWindow(handle, false);
            _inherit.Clear();
            _settings = settings;
            _state.ActiveProfile = profile;
        }

        _sessionOnly = true;
        ApplySettings();
        Refresh();
    }

    /// <summary>SAVE: write every edited profile to its file, make the shown profile the active one, and run it.</summary>
    public void SaveFromDialog(IReadOnlyDictionary<string, AppSettings> edited, string shown, AppState global)
    {
        ApplyGlobal(global);
        bool same = shown.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase);

        AppSettings? running = null;
        foreach (var pair in edited)
        {
            bool isShown = pair.Key.Equals(shown, StringComparison.OrdinalIgnoreCase);
            if (!isShown && !ProfileStore.Exists(pair.Key)) continue;

            var copy = pair.Value.Clone();
            if (isShown)
            {
                if (same) MergeLiveGeometry(copy);
                running = copy;
            }
            ProfileStore.Save(pair.Key, copy);
        }

        if (running is not null)
        {
            if (same)
            {
                _settings.CopyFrom(running);
            }
            else
            {
                foreach (nint handle in _windows.Keys.ToList()) CloseWindow(handle, false);
                _inherit.Clear();
                _settings = running;
                _state.ActiveProfile = shown;
            }
        }

        _sessionOnly = false;
        ProfileStore.SaveState(_state);
        ApplySettings();
        Refresh();
    }

    /// <summary>Throw away anything that was only applied: go back to what is in the config files.</summary>
    public void RevertToSaved()
    {
        var disk = ProfileStore.LoadState();
        bool sameProfile = disk.ActiveProfile.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase);

        _state.NextProfileHotkey = disk.NextProfileHotkey;
        _state.PreviousProfileHotkey = disk.PreviousProfileHotkey;
        _state.ProfileHotkeys = new Dictionary<string, string>(disk.ProfileHotkeys);
        _state.WhatsNewMode = disk.WhatsNewMode;
        _state.RunOnStartup = disk.RunOnStartup;
        StartupService.Set(_state.RunOnStartup);
        _state.DebugLogging = disk.DebugLogging;
        _state.DebugLogMode = disk.DebugLogMode;
        _state.PinTrayIcon = disk.PinTrayIcon;
        Log.PerSessionFiles = disk.DebugLogMode == "PerSession";
        Log.Verbose = disk.DebugLogging;

        var saved = ProfileStore.Exists(disk.ActiveProfile) ? ProfileStore.Load(disk.ActiveProfile) : new AppSettings();
        if (sameProfile)
        {
            _settings.CopyFrom(saved);
        }
        else
        {
            foreach (nint handle in _windows.Keys.ToList()) CloseWindow(handle, false);
            _inherit.Clear();
            _settings = saved;
            _state.ActiveProfile = disk.ActiveProfile;
        }

        _sessionOnly = false;
        ApplySettings();
        Refresh();
    }

    public void NotifyProfileRenamed(string oldName, string newName)
    {
        if (_state.ActiveProfile.Equals(oldName, StringComparison.OrdinalIgnoreCase)) _state.ActiveProfile = newName;
    }

    private void ApplyGlobal(AppState global)
    {
        _state.NextProfileHotkey = global.NextProfileHotkey;
        _state.PreviousProfileHotkey = global.PreviousProfileHotkey;
        _state.ProfileHotkeys = new Dictionary<string, string>(global.ProfileHotkeys);
        _state.WhatsNewMode = global.WhatsNewMode;
        _state.RunOnStartup = global.RunOnStartup;
        StartupService.Set(_state.RunOnStartup);
        _state.DebugLogging = global.DebugLogging;
        _state.DebugLogMode = global.DebugLogMode;
        _state.PinTrayIcon = global.PinTrayIcon;
        Log.PerSessionFiles = global.DebugLogMode == "PerSession";
        Log.Verbose = global.DebugLogging;
        if (_state.PinTrayIcon) TrayPinService.Promote();
    }

    /// <summary>
    /// The settings window never edits positions, so anything the app moved or created while the window was open
    /// (a client logging in, for example) is carried over instead of being overwritten by the window's older copy.
    /// </summary>
    private void MergeLiveGeometry(AppSettings incoming)
    {
        foreach (var pair in _settings.Characters)
        {
            var live = pair.Value;
            if (!incoming.Characters.TryGetValue(pair.Key, out var c))
            {
                if (AppSettings.IsCharacterKey(pair.Key)) continue;      // removed in the window: stays removed
                c = incoming.GetOrAddCharacter(pair.Key);
            }
            c.Left = live.Left;
            c.Top = live.Top;
            if (c.Width <= 0 && c.Height <= 0)
            {
                c.Width = live.Width;
                c.Height = live.Height;
            }
        }
        foreach (var slot in _settings.SlotCharacters) incoming.SlotCharacters[slot.Key] = slot.Value;

        // the "Saved" order builds itself while the window is open, and "Reset order" acts at once: the live values win
        incoming.ClientOrderResetPending = _settings.ClientOrderResetPending;
        if (_settings.ClientOrderMode == AppSettings.OrderSaved && incoming.ClientOrderMode == AppSettings.OrderSaved)
            incoming.ClientOrder = new List<string>(_settings.ClientOrder);
    }

    // ---------- profiles ----------

    public void SwitchProfile(string name)
    {
        if (name.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase) || !ProfileStore.Exists(name)) return;

        foreach (nint handle in _windows.Keys.ToList()) CloseWindow(handle, false);
        _inherit.Clear();

        _settings = ProfileStore.Load(name);
        _state.ActiveProfile = name;
        _sessionOnly = false;
        ProfileStore.SaveState(_state);

        ApplySettings();
        Refresh();

        // the Configuration window shows the same profile list: it follows the switch
        _settingsWindow?.FollowActiveProfile(name);
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
        _hotkeys.PassThrough = _settings.HotkeyPassThrough;
        _hotkeys.Gate = _settings.HotkeysOnlyInEve
            ? () => (_pendingTarget != 0 && Log.Ms <= _pendingUntil) || _clientHandles.Contains(NativeMethods.GetForegroundWindow())   // read-only: runs on the hook thread
            : null;
        _hotkeys.Suspended = HotkeysSuspended || _settingsWindow is not null;

        var invalid = new List<string>();

        void Register(string? text, string what, Action action)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!_hotkeys.Register(text, action)) invalid.Add(what + ": " + text);
            else Log.Debug("HOTKEY", $"registered '{text}' = {what}");
        }

        Register(_settings.CycleForwardHotkey, "Next client", () => Cycle(1));
        Register(_settings.CycleBackHotkey, "Previous client", () => Cycle(-1));

        foreach (var pair in _settings.Characters)
        {
            string name = pair.Key;
            if (!AppSettings.IsCharacterKey(name)) continue;
            Register(pair.Value.Hotkey, name, () => ActivateCharacter(name));
        }

        foreach (var pair in _settings.ActiveSlotHotkeys())            // the client-hotkey group in use
        {
            if (!int.TryParse(pair.Key, out int slot) || slot < 1) continue;
            Register(pair.Value, "Client " + slot, () => ActivateSlot(slot));
        }

        Register(_state.NextProfileHotkey, "Next profile", () => CycleProfile(1));
        Register(_state.PreviousProfileHotkey, "Previous profile", () => CycleProfile(-1));
        foreach (var pair in _state.ProfileHotkeys)
        {
            string profile = pair.Key;
            Register(pair.Value, "Profile " + profile, () => SwitchProfile(profile));
        }

        if (invalid.Count > 0) Log.Write("Hotkeys that couldn't be read: " + string.Join("; ", invalid));
    }

    public void ToggleHotkeysSuspended()
    {
        HotkeysSuspended = !HotkeysSuspended;
        _hotkeys.Suspended = HotkeysSuspended || _settingsWindow is not null;
        Toast?.Invoke(AppInfo.Name, HotkeysSuspended ? "Hotkeys are suspended." : "Hotkeys are back on.");
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
        if (list.Count == 0 || list.Count != _clients.Count) list = OrderClients(_eve.FindClients()).ToList();
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
    /// window (or the last client it switched to), skips clients that are excluded, and moves on past any client that
    /// is frozen or that Windows refuses to focus, so one stuck client can never stop the cycling. Every press is
    /// written to preview.log.
    /// </summary>
    private void Cycle(int direction)
    {
        var all = CurrentClients();
        int count = all.Count;
        if (count == 0) return;

        // v0.9.6: work out where you are from the switch in progress (if any), not from a foreground window that
        // Windows hasn't updated yet. Reading the stale window made a press pick the same client again.
        nint current = CurrentClientHandle(out string from);
        int start = IndexOf(all, current);

        int step = direction > 0 ? 1 : -1;
        int position = start >= 0 ? start : (direction > 0 ? -1 : 0);
        string way = direction > 0 ? "next" : "previous";
        bool failed = false;

        for (int tried = 0; tried < count; tried++)
        {
            position = ((position + step) % count + count) % count;
            var client = all[position];
            if (!IsCyclable(client))
            {
                Log.Debug("SWITCH", $"  skip #{position + 1} '{client.CharacterName}' (excluded from cycling)");
                continue;
            }
            if (client.Handle == current) continue;
            Log.Debug("SWITCH", $"  try #{position + 1} '{client.CharacterName}'");

            bool ok = SwitchTo(client, out string how);
            if (ok)
            {
                // a successful press is only recorded in the detailed log: writing a file on every press (and holding the key
                // makes 30 a second) is exactly the kind of work a tray app shouldn't do
                Log.Debug("INFO", $"Cycle {way}: from {(start >= 0 ? $"#{start + 1} ({from})" : "nothing")} -> #{position + 1} '{client.CharacterName}' ok ({how})");
                return;
            }

            failed = true;
            Log.Write($"Cycle {way}: switch to #{position + 1} '{client.CharacterName}' FAILED ({how})");   // failures always reach preview.log

            // one press = one switch. Only a client that has closed or frozen is stepped over; if Windows refused,
            // stop here instead of trying every other client in the same press.
            if (how != "window gone" && how != "not responding") break;
        }

        if (failed) Log.Debug("INFO", $"Cycle {way}: no switch");
    }

    /// <summary>Brings a client to the front and applies its "when I switch to it" action.</summary>
    private bool SwitchTo(EveClientWindow client, out string how)
    {
        bool ok = _eve.Activate(client.Handle, out how);
        _lastActivated = client.Handle;
        if (!ok) return false;
        _staleUntil = Log.Ms + StaleMs;

        // Windows has accepted the switch. Until it confirms (or 400 ms pass) this client counts as the active one.
        if (NativeMethods.GetForegroundWindow() == client.Handle) ClearPending();
        else SetPending(client.Handle);

        var c = client.IsLoginScreen ? null : _settings.TryGetCharacter(client.CharacterName);
        if (c is not null)
        {
            if (c.ActivateAction == "Maximise")
            {
                Log.Debug("WINDOW", $"switch-to action: ShowWindowAsync(MAXIMIZE) {Log.Win(client.Handle)}");
                NativeMethods.ShowWindowAsync(client.Handle, NativeMethods.SW_MAXIMIZE);
            }
            else if (c.ActivateAction == "Restore" && NativeMethods.IsZoomed(client.Handle))
            {
                Log.Debug("WINDOW", $"switch-to action: ShowWindowAsync(RESTORE) {Log.Win(client.Handle)}");
                NativeMethods.ShowWindowAsync(client.Handle, NativeMethods.SW_RESTORE);
            }
        }

        SyncForeground(client.Handle);
        return true;
    }

    private void ActivateClient(nint handle)
    {
        var client = _clients.FirstOrDefault(c => c.Handle == handle);
        if (client is null) return;
        Log.Debug("SWITCH", $"thumbnail clicked: '{client.CharacterName}'");
        if (!SwitchTo(client, out string how)) Log.Write($"Click switch to '{client.CharacterName}' failed ({how})");
    }

    private void ActivateCharacter(string name)
    {
        var client = CurrentClients()
            .FirstOrDefault(c => c.CharacterName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (client is null)
        {
            Log.Debug("SWITCH", $"character hotkey '{name}': that character isn't logged in");
            return;
        }
        Log.Debug("SWITCH", $"character hotkey -> '{name}'");
        if (!SwitchTo(client, out string how)) Log.Write($"Hotkey switch to '{name}' failed ({how})");
    }

    /// <summary>Hotkey for "client N": the client in place N of the client order (login screens count too).</summary>
    private void ActivateSlot(int slot)
    {
        var list = CurrentClients();
        EveClientWindow? client = null;
        for (int i = 0; i < list.Count; i++)
        {
            if (SlotOf(list[i], i) == slot) { client = list[i]; break; }
        }
        if (client is null)
        {
            Log.Write(slot >= 1 && slot <= list.Count
                ? $"Client {slot} hotkey: place {slot} is empty (that client is closed)"
                : $"Client {slot} hotkey: only {list.Count} client(s) open");
            return;
        }
        Log.Debug("SWITCH", $"client {slot} hotkey -> '{client.CharacterName}'");
        if (!SwitchTo(client, out string how)) Log.Write($"Client {slot} hotkey: '{client.CharacterName}' failed ({how})");
    }

    // ---------- log monitoring and notifications ----------

    private void OnSystemChanged(string character, string system)
    {
        _systems[character] = system;
        foreach (var w in _windows.Values)
        {
            if (!w.Client.IsLoginScreen && w.Client.CharacterName.Equals(character, StringComparison.OrdinalIgnoreCase))
                ApplyStyle(w);
        }
    }

    private void OnLogEvent(LogEvent e)
    {
        if (!_settings.NotificationsEnabled) return;

        var client = _clients.FirstOrDefault(c => !c.IsLoginScreen &&
            c.CharacterName.Equals(e.Character, StringComparison.OrdinalIgnoreCase));
        if (client is null) return;
        if (_settings.NotifySkipActiveClient && client.Handle == _activeClient) return;

        string key = client.CharacterName + "|" + e.Kind;
        var now = DateTime.UtcNow;
        if (_lastNotified.TryGetValue(key, out var last) &&
            (now - last).TotalSeconds < Math.Max(0, _settings.NotifyCooldownSeconds)) return;
        _lastNotified[key] = now;

        string detail = e.Message.Length > 90 ? e.Message[..90] + "…" : e.Message;
        string message = e.Kind switch
        {
            "attack" => $"{client.CharacterName} is under attack",
            "mining" => $"{client.CharacterName} has stopped mining",
            _ => $"{client.CharacterName}: {detail}"
        };

        _alertHex = _settings.NotifyBorderHex;
        Deliver(_settings, message, client.Handle);
    }

    /// <summary>Sends a test alert using the given settings (so unsaved changes can be tried out).</summary>
    public void TestNotification(AppSettings s)
    {
        var client = _clients.FirstOrDefault(c => !c.IsLoginScreen) ?? _clients.FirstOrDefault();
        string name = client?.CharacterName ?? "Test";
        _alertHex = s.NotifyBorderHex;
        Deliver(s, $"{name}: this is a test notification", client?.Handle ?? 0);
    }

    private void Deliver(AppSettings s, string message, nint handle)
    {
        if (s.NotifyFlashBorder && handle != 0 && _windows.ContainsKey(handle))
        {
            _alertUntil[handle] = DateTime.UtcNow.AddSeconds(Math.Clamp(s.NotifyBorderSeconds, 1, 120));
            _blink = true;
            if (!_alertTimer.IsEnabled) _alertTimer.Start();
            UpdateBorders();
        }
        if (s.NotifyToast) Toast?.Invoke(AppInfo.Name, message);
        if (s.NotifySound) _notify.PlaySound(s.NotifySoundName, s.NotifySoundFile);
        if (s.NotifySpeak) _notify.Speak(message);
    }

    private void OnAlertTick()
    {
        var now = DateTime.UtcNow;
        foreach (nint expired in _alertUntil.Where(p => p.Value <= now).Select(p => p.Key).ToList())
            _alertUntil.Remove(expired);

        if (_alertUntil.Count == 0)
        {
            _alertTimer.Stop();
            _blink = false;
        }
        else
        {
            _blink = !_blink;
        }
        UpdateBorders();
    }

    // ---------- visibility ----------

    public void ToggleVisibility()
    {
        _visible = !_visible;
        ApplyVisibility();
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _scanTimer.Stop();
        _hideTimer.Stop();
        _minimiseTimer.Stop();
        _alertTimer.Stop();
        _displayTimer.Stop();
        _refreshSoonTimer.Stop();
        _trimTimer.Stop();
        _watcher.Dispose();
        _logs.Dispose();
        _hotkeys.Dispose();
        foreach (var w in _windows.Values.ToList()) w.Close();
        _windows.Clear();
        _meta.Clear();
    }
}




