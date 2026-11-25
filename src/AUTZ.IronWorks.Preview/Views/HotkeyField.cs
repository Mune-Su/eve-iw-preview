// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>A box that records a hotkey: click it, then press a key, mouse button or combo. ✕ clears it.</summary>
public sealed class HotkeyField : UserControl
{
    private readonly TextBox _box = new();
    private readonly Button _clear = new();
    private readonly List<string> _peak = new();
    private readonly HashSet<string> _held = new();

    public event Action? TextChanged;

    /// <summary>Raised with true when any hotkey box gets the keyboard (it is about to record a key) and false when it lets go.</summary>
    public static event Action<bool>? RecordingChanged;

    public string Text
    {
        get => _box.Text.Trim();
        set
        {
            _held.Clear();
            _peak.Clear();
            _box.Text = value ?? "";
        }
    }

    public bool IsValid => HotkeyParser.IsValid(Text);

    // v0.19.31: WPF doesn't say whether a key came from the numpad. The raw key message does (the "extended" bit), so it is noted here.
    private static int _lastVk;
    private static bool _lastExtended;
    private static bool _watching;

    private static void WatchRawKeys()
    {
        if (_watching) return;
        _watching = true;
        ComponentDispatcher.ThreadPreprocessMessage += (ref MSG msg, ref bool handled) =>
        {
            if (msg.message is 0x100 or 0x101 or 0x104 or 0x105)        // key down / up, system key down / up
            {
                _lastVk = (int)msg.wParam;
                _lastExtended = (((long)msg.lParam >> 24) & 1) != 0;
            }
        };
    }

    private static string Token(Key key)
    {
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (!HotkeyParser.IsDualKey(vk) || vk != _lastVk) return HotkeyParser.TokenForKey(key);
        return HotkeyParser.TokenForKey(key, HotkeyParser.KeyCode(vk, _lastExtended) != vk);
    }

    public HotkeyField()
    {
        WatchRawKeys();
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _box.IsReadOnly = true;
        _box.Margin = new Thickness(0);
        _box.Cursor = Cursors.Arrow;
        _box.ToolTip = "Click, then press a key, a mouse button or a combination";
        InputMethod.SetIsInputMethodEnabled(_box, false);
        _box.IsUndoEnabled = false;
        _box.ContextMenu = null;

        _clear.Content = "✕";
        _clear.Margin = new Thickness(6, 0, 0, 0);
        _clear.Padding = new Thickness(11, 0, 11, 0);
        _clear.ToolTip = "Clear this hotkey";
        _clear.Click += (_, _) =>
        {
            _held.Clear();
            _peak.Clear();
            _box.Text = "";
            TextChanged?.Invoke();
        };

        Grid.SetColumn(_clear, 1);
        grid.Children.Add(_box);
        grid.Children.Add(_clear);
        Content = grid;

        _box.PreviewKeyDown += OnKeyDown;
        _box.PreviewKeyUp += OnKeyUp;
        _box.PreviewMouseDown += OnMouseDown;
        _box.PreviewMouseUp += OnMouseUp;
        _box.PreviewMouseWheel += OnMouseWheel;
        _box.GotKeyboardFocus += (_, _) => RecordingChanged?.Invoke(true);
        _box.LostKeyboardFocus += (_, _) =>
        {
            Finish();
            RecordingChanged?.Invoke(false);
        };
    }

    private void Press(string token)
    {
        if (_held.Count == 0) _peak.Clear();               // a new chord starts
        _held.Add(token);
        if (!_peak.Contains(token)) _peak.Add(token);
        _box.Text = HotkeyParser.Format(_peak);
    }

    private void Release(string token)
    {
        _held.Remove(token);
        if (_held.Count == 0) Finish();
    }

    /// <summary>Everything has been let go: the chord on screen becomes the hotkey.</summary>
    private void Finish()
    {
        _held.Clear();
        if (_peak.Count == 0) return;
        _box.Text = HotkeyParser.Format(_peak);
        _peak.Clear();
        TextChanged?.Invoke();
    }

    private static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key
    };

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        Key key = RealKey(e);
        if (key == Key.None) return;
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None && _held.Count == 0) return;   // let Tab move on

        e.Handled = true;
        if (e.IsRepeat) return;
        Press(Token(key));
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        Key key = RealKey(e);
        if (key == Key.None) return;
        e.Handled = true;
        Release(Token(key));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_box.IsKeyboardFocused)
        {
            _box.Focus();                                    // the first click only focuses the box
            e.Handled = true;
            return;
        }

        e.Handled = true;
        // a plain left-click is how you click the box: only record it together with another key or button
        if (e.ChangedButton == MouseButton.Left && _held.Count == 0) return;
        Press(HotkeyParser.TokenForMouse(e.ChangedButton));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Release(HotkeyParser.TokenForMouse(e.ChangedButton));
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_box.IsKeyboardFocused || e.Delta == 0) return;
        e.Handled = true;
        string token = e.Delta > 0 ? "WheelUp" : "WheelDown";
        Press(token);
        Release(token);
    }
}


