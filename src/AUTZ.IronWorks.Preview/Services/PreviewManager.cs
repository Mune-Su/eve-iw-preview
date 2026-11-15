using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Views;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Owns one ThumbnailWindow per EVE client, the hotkeys, and the settings dialog.</summary>
public sealed class PreviewManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly EveWindowService _eve = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly Dictionary<nint, ThumbnailWindow> _windows = new();
    private readonly DispatcherTimer _scanTimer = new();
    private readonly DispatcherTimer _activeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IReadOnlyList<EveClientWindow> _clients = Array.Empty<EveClientWindow>();
    private SettingsWindow? _settingsWindow;
    private bool _visible = true;

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
        _clients = _eve.FindClients();
        var live = _clients.ToDictionary(c => c.Handle);

        foreach (nint handle in _windows.Keys.ToList())
        {
            // closed client, or the character name changed (login screen -> logged in)
            if (!live.TryGetValue(handle, out var client) ||
                client.CharacterName != _windows[handle].Client.CharacterName)
            {
                _windows[handle].Close();
                _windows.Remove(handle);
            }
        }

        foreach (var client in _clients)
        {
            if (!_windows.ContainsKey(client.Handle)) _windows[client.Handle] = CreateWindow(client);
        }

        UpdateActive();
    }

    private ThumbnailWindow CreateWindow(EveClientWindow client)
    {
        var (width, height) = SizeFor(client.CharacterName);
        var (left, top) = PositionFor(client.CharacterName);

        var window = new ThumbnailWindow(client, _settings, width, height, left, top);
        window.Clicked += w => _eve.Activate(w.Client.Handle);
        window.Moved += OnMoved;
        window.ContextMenu = BuildMenu();
        ApplyStyle(window);
        if (_visible) window.Show();
        return window;
    }

    // ---------- sizing, position, appearance ----------

    private (double Width, double Height) SizeFor(string name)
    {
        var c = _settings.TryGetCharacter(name);
        double width = c is not null && c.Width > 0 ? c.Width : _settings.PreviewWidth;
        double height = c is not null && c.Height > 0 ? c.Height : Math.Round(width * 9d / 16d);
        return (width, height);
    }

    private (double Left, double Top) PositionFor(string name)
    {
        var c = _settings.TryGetCharacter(name);
        if (c is not null && c.Left.HasValue && c.Top.HasValue && IsOnScreen(c.Left.Value, c.Top.Value))
            return (c.Left.Value, c.Top.Value);

        // no saved position: stack down the left edge
        double top = 24;
        foreach (var w in _windows.Values)
        {
            if (Math.Abs(w.Left - 24) < 1) top = Math.Max(top, w.Top + w.Height + 8);
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
        string name = window.Client.CharacterName;
        var c = _settings.TryGetCharacter(name);

        string label = name;
        if (c is not null && !string.IsNullOrWhiteSpace(c.Label)) label = c.Label.Trim();

        Brush brush = BrushFrom(c?.LabelColourHex, _settings.DefaultLabelColourHex, Brushes.White);
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
        var c = _settings.GetOrAddCharacter(window.Client.CharacterName);
        c.Left = window.Left;
        c.Top = window.Top;
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
        foreach (var w in _windows.Values.OrderBy(x => x.Client.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            w.Left = 24;
            w.Top = top;
            top += w.Height + 8;
        }
    }

    // ---------- settings ----------

    public void ApplySettings()
    {
        _scanTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.RefreshMilliseconds, 500, 10000));
        foreach (var w in _windows.Values)
        {
            var (width, height) = SizeFor(w.Client.CharacterName);
            w.Width = width;
            w.Height = height;
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

        var names = _clients
            .Select(c => c.CharacterName)
            .Where(n => !n.Equals("EVE", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var dialog = new SettingsWindow(_settings, names);
        _settingsWindow = dialog;
        bool? saved = dialog.ShowDialog();
        _settingsWindow = null;

        if (saved == true)
        {
            SettingsService.Save(_settings);
            ApplySettings();
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

    private void Cycle(int direction)
    {
        var list = _eve.FindClients();
        if (list.Count == 0) return;

        nint foreground = NativeMethods.GetForegroundWindow();
        int index = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Handle == foreground) index = i;
        }

        int next = index < 0
            ? (direction > 0 ? 0 : list.Count - 1)
            : (index + direction + list.Count) % list.Count;
        _eve.Activate(list[next].Handle);
    }

    private void ActivateCharacter(string name)
    {
        var client = _eve.FindClients()
            .FirstOrDefault(c => c.CharacterName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (client is not null) _eve.Activate(client.Handle);
    }

    // ---------- visibility & menu ----------

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

        var lockItem = new MenuItem { Header = "Lock positions", IsCheckable = true };
        lockItem.Click += (_, _) =>
        {
            _settings.LockPositions = lockItem.IsChecked;
            SettingsService.Save(_settings);
        };
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
        _activeTimer.Stop();
        _hotkeys.Dispose();
        foreach (var w in _windows.Values.ToList()) w.Close();
        _windows.Clear();
    }
}
