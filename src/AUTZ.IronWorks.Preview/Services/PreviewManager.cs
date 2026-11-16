using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Views;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>A character the app knows about, for the "which thumbnails to show" lists.</summary>
public sealed record ClientEntry(string Name, bool Shown, bool Running);

/// <summary>Owns one ThumbnailWindow per shown EVE client, the hotkeys, and the settings dialog.</summary>
public sealed class PreviewManager : IDisposable
{
    /// <summary>Where a thumbnail's position/size is stored, and which client slot (opening order) it is.</summary>
    private sealed class Meta
    {
        public string Key = "";
        public int Slot;
    }

    private readonly AppSettings _settings;
    private readonly EveWindowService _eve = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly Dictionary<nint, ThumbnailWindow> _windows = new();
    private readonly Dictionary<ThumbnailWindow, Meta> _meta = new();
    private readonly DispatcherTimer _scanTimer = new();
    private readonly DispatcherTimer _activeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IReadOnlyList<EveClientWindow> _clients = Array.Empty<EveClientWindow>();
    private SettingsWindow? _settingsWindow;
    private bool _visible = true;

    public bool ThumbnailsVisible => _visible;

    public PreviewManager(AppSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        _scanTimer.Tick += (_, _) => Refresh();
        _activeTimer.Tick += (_, _) => UpdateActive();
        ApplySettings();
        Refresh();
        _scanTimer.Start();
        _activeTimer.Start();
    }

    // ---------- client scanning ----------

    public void Refresh()
    {
        _clients = _eve.FindClients();   // already in the order the clients were opened
        var live = _clients.ToDictionary(c => c.Handle);

        foreach (nint handle in _windows.Keys.ToList())
        {
            // closed client, login -> logged in (name changed), or the user unticked this client
            if (!live.TryGetValue(handle, out var client) ||
                client.CharacterName != _windows[handle].Client.CharacterName ||
                !ShouldShow(client))
            {
                CloseWindow(handle);
            }
        }

        for (int i = 0; i < _clients.Count; i++)
        {
            var client = _clients[i];
            int slot = i + 1;
            if (_windows.TryGetValue(client.Handle, out var existing))
            {
                var meta = _meta[existing];
                if (meta.Slot != slot)
                {
                    meta.Slot = slot;
                    if (client.IsLoginScreen) ApplyStyle(existing);
                }
            }
            else if (ShouldShow(client))
            {
                _windows[client.Handle] = CreateWindow(client, slot);
            }
        }

        UpdateActive();
    }

    private void CloseWindow(nint handle)
    {
        if (!_windows.TryGetValue(handle, out var window)) return;
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
    /// The settings entry that holds this thumbnail's position and size. Logged-in clients use their character.
    /// A login screen uses the character that usually logs in on that slot, so it sits where that character's
    /// thumbnail will be; if there is none yet it gets its own "[login N]" entry.
    /// </summary>
    private string KeyFor(EveClientWindow client, int slot)
    {
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
        SettingsService.Save(_settings);
    }

    private ThumbnailWindow CreateWindow(EveClientWindow client, int slot)
    {
        string key = KeyFor(client, slot);

        if (!client.IsLoginScreen)
        {
            RememberSlot(slot, client.CharacterName);

            // a character with no saved spot takes over where its login screen was sitting
            var mine = _settings.TryGetCharacter(client.CharacterName);
            var login = _settings.TryGetCharacter($"[login {slot}]");
            if ((mine is null || !mine.Left.HasValue || !mine.Top.HasValue) &&
                login is not null && login.Left.HasValue && login.Top.HasValue)
            {
                var c = _settings.GetOrAddCharacter(client.CharacterName);
                c.Left = login.Left;
                c.Top = login.Top;
                SettingsService.Save(_settings);
            }
        }

        var (width, height) = SizeFor(key);
        var (left, top) = PositionFor(key);
        var window = new ThumbnailWindow(client, _settings, width, height, left, top);
        _meta[window] = new Meta { Key = key, Slot = slot };
        window.Clicked += w => _eve.Activate(w.Client.Handle);
        window.Moved += OnMoved;
        window.Resized += OnResized;
        window.Menu = BuildMenu();
        window.SnapTargets = SnapTargetsFor;
        ApplyStyle(window);
        if (_visible) window.Show();
        return window;
    }

    private IReadOnlyList<NativeMethods.RECT> SnapTargetsFor(ThumbnailWindow self) =>
        _windows.Values.Where(w => w != self && w.IsVisible).Select(w => w.GetBounds()).ToList();

    // ---------- sizing, position, appearance ----------

    private (double Width, double Height) SizeFor(string key)
    {
        var c = _settings.TryGetCharacter(key);
        double width = c is not null && c.Width > 0 ? c.Width : _settings.PreviewWidth;
        double height = c is not null && c.Height > 0 ? c.Height : Math.Round(width * 9d / 16d);
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
        var c = _settings.TryGetCharacter(meta.Key);
        string label = login ? $"Login screen {meta.Slot}" : window.Client.CharacterName;
        if (!login && c is not null && !string.IsNullOrWhiteSpace(c.Label)) label = c.Label.Trim();
        Brush brush = BrushFrom(login ? null : c?.LabelColourHex, _settings.DefaultLabelColourHex, Brushes.White);
        window.SetLabel(label, brush, _settings.ShowLabels);
    }

    private static Brush BrushFrom(string? hex, string? fallbackHex, Brush ultimate)
    {
        foreach (string? candidate in new[] { hex, fallbackHex })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(candidate));
                brush.Freeze();
                return brush;
            }
            catch { }
        }
        return ultimate;
    }

    private void UpdateActive()
    {
        Brush? highlight = _settings.HighlightActive ? BrushFrom(_settings.ActiveBorderHex, null, Brushes.Orange) : null;
        nint foreground = NativeMethods.GetForegroundWindow();
        foreach (var w in _windows.Values)
            w.SetHighlight(highlight is not null && w.Client.Handle == foreground ? highlight : null);
    }

    private void OnMoved(ThumbnailWindow window)
    {
        if (!_meta.TryGetValue(window, out var meta)) return;
        var c = _settings.GetOrAddCharacter(meta.Key);
        c.Left = window.Left;
        c.Top = window.Top;
        SettingsService.Save(_settings);
    }

    private void OnResized(ThumbnailWindow window)
    {
        if (!_meta.TryGetValue(window, out var meta)) return;
        var c = _settings.GetOrAddCharacter(meta.Key);
        c.Width = (int)Math.Round(window.ActualWidth);
        c.Height = (int)Math.Round(window.ActualHeight);
        SettingsService.Save(_settings);
    }

    public void ResetPositions()
    {
        foreach (var c in _settings.Characters.Values)
        {
            c.Left = null;
            c.Top = null;
        }
        SettingsService.Save(_settings);
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

    // ---------- which thumbnails to show ----------

    /// <summary>Logged-in characters (in the order their clients opened), then other known characters A-Z.</summary>
    public IReadOnlyList<string> KnownCharacters()
    {
        var running = RunningCharacters();
        var others = _settings.Characters.Keys
            .Where(AppSettings.IsCharacterKey)
            .Except(running, StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        return running.Concat(others).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> RunningCharacters() =>
        _clients.Where(c => !c.IsLoginScreen).Select(c => c.CharacterName).ToList();

    public IReadOnlyList<ClientEntry> GetClientEntries()
    {
        var running = new HashSet<string>(RunningCharacters(), StringComparer.OrdinalIgnoreCase);
        return KnownCharacters()
            .Select(n => new ClientEntry(n, !(_settings.TryGetCharacter(n)?.Hidden ?? false), running.Contains(n)))
            .ToList();
    }

    public void SetCharacterShown(string name, bool shown)
    {
        _settings.GetOrAddCharacter(name).Hidden = !shown;
        SettingsService.Save(_settings);
        Refresh();
    }

    public void SetLoginScreensShown(bool shown)
    {
        _settings.ShowLoginScreens = shown;
        SettingsService.Save(_settings);
        Refresh();
    }

    // ---------- settings ----------

    public void ApplySettings()
    {
        _scanTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.RefreshMilliseconds, 500, 10000));
        foreach (var w in _windows.Values)
        {
            if (!_meta.TryGetValue(w, out var meta)) continue;
            var (width, height) = SizeFor(meta.Key);
            w.Width = width;
            w.Height = height;
            w.RefreshThumbnail();   // picks up the opacity setting
            ApplyStyle(w);
        }
        UpdateActive();
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
        if (saved == true)
        {
            SettingsService.Save(_settings);
            ApplySettings();
            Refresh();   // picks up any thumbnails that were ticked / unticked
        }
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

        if (failed.Count > 0)
        {
            MessageBox.Show(
                "These hotkeys couldn't be registered (another app is probably using them):\n\n" + string.Join("\n", failed),
                "AUTZ IronWorks Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private nint _lastCycled;
    private bool _lastCycledOk = true;

    /// <summary>
    /// Cycles through every client (including hidden thumbnails) in the order the clients were opened.
    /// It remembers where it got to, so if Windows refuses to focus one client the next press still moves on
    /// instead of getting stuck.
    /// </summary>
    private void Cycle(int direction)
    {
        var list = _eve.FindClients();
        if (list.Count == 0) return;
        int count = list.Count;

        nint foreground = NativeMethods.GetForegroundWindow();
        int index = -1;
        for (int i = 0; i < count; i++)
            if (list[i].Handle == foreground) index = i;

        int last = -1;
        for (int i = 0; i < count; i++)
            if (list[i].Handle == _lastCycled) last = i;

        // if the last jump didn't actually take, carry on from where we aimed; otherwise trust what's in front
        if (last >= 0 && (!_lastCycledOk || index < 0)) index = last;

        int next = index < 0
            ? (direction > 0 ? 0 : count - 1)
            : ((index + direction) % count + count) % count;

        _lastCycled = list[next].Handle;
        _lastCycledOk = _eve.Activate(list[next].Handle);
    }

    private void ActivateCharacter(string name)
    {
        var client = _eve.FindClients()
            .FirstOrDefault(c => c.CharacterName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (client is not null) _eve.Activate(client.Handle);
    }

    // ---------- visibility & thumbnail right-click menu ----------

    public void ToggleVisibility()
    {
        _visible = !_visible;
        foreach (var w in _windows.Values)
        {
            if (_visible) w.Show();
            else w.Hide();
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) => OpenSettings();

        var lockItem = new MenuItem { Header = "Lock positions and sizes", IsCheckable = true };
        lockItem.Click += (_, _) =>
        {
            _settings.LockPositions = lockItem.IsChecked;
            SettingsService.Save(_settings);
        };

        var clickItem = new MenuItem { Header = "Thumbnails clickable", IsCheckable = true };
        clickItem.Click += (_, _) =>
        {
            _settings.ThumbnailsClickable = clickItem.IsChecked;
            SettingsService.Save(_settings);
        };

        menu.Opened += (_, _) =>
        {
            lockItem.IsChecked = _settings.LockPositions;
            clickItem.IsChecked = _settings.ThumbnailsClickable;
        };

        var resetItem = new MenuItem { Header = "Reset all positions" };
        resetItem.Click += (_, _) => ResetPositions();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(settingsItem);
        menu.Items.Add(clickItem);
        menu.Items.Add(lockItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _activeTimer.Stop();
        _hotkeys.Dispose();
        foreach (var w in _windows.Values.ToList()) w.Close();
        _windows.Clear();
        _meta.Clear();
    }
}
