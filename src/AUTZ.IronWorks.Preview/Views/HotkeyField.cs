using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>
/// A box that records a hotkey: click it, then press any key, mouse button, wheel notch or combination. Nothing is
/// off limits except a plain left-click (that is how you click the box) and a plain Tab (that moves to the next box).
/// Use the clear button to remove a hotkey.
/// </summary>
public sealed class HotkeyField : UserControl
{
    private readonly TextBox _box = new();
    private readonly Button _clear = new();
    private readonly List<string> _peak = new();
    private readonly HashSet<string> _held = new();

    public event Action? TextChanged;

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

    public HotkeyField()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
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
        _box.LostKeyboardFocus += (_, _) => Finish();
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
        Press(HotkeyParser.TokenForKey(key));
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        Key key = RealKey(e);
        if (key == Key.None) return;
        e.Handled = true;
        Release(HotkeyParser.TokenForKey(key));
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


