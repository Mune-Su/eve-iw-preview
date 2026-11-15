using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, CharacterSettings> _edits = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentCharacter;
    private bool _loading;

    public SettingsWindow(AppSettings settings, IEnumerable<string> detectedCharacters)
    {
        InitializeComponent();
        _settings = settings;

        WidthBox.Text = settings.PreviewWidth.ToString();
        RefreshBox.Text = settings.RefreshMilliseconds.ToString();
        LabelsBox.IsChecked = settings.ShowLabels;
        LabelColourBox.Text = settings.DefaultLabelColourHex;
        HighlightBox.IsChecked = settings.HighlightActive;
        ColourBox.Text = settings.ActiveBorderHex;
        LockBox.IsChecked = settings.LockPositions;
        ForwardBox.Text = settings.CycleForwardHotkey;
        BackBox.Text = settings.CycleBackHotkey;

        var names = settings.Characters.Keys
            .Concat(detectedCharacters)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string name in names)
        {
            _edits[name] = settings.Characters.TryGetValue(name, out var existing)
                ? Clone(existing)
                : new CharacterSettings();
            CharBox.Items.Add(name);
        }

        if (names.Count == 0)
        {
            NoCharText.Visibility = Visibility.Visible;
            CharBox.IsEnabled = false;
            SetCharacterFieldsEnabled(false);
        }
        else
        {
            CharBox.SelectedIndex = 0;
        }
    }

    private static CharacterSettings Clone(CharacterSettings c) => new()
    {
        Left = c.Left,
        Top = c.Top,
        Width = c.Width,
        Height = c.Height,
        Label = c.Label,
        LabelColourHex = c.LabelColourHex,
        Hotkey = c.Hotkey
    };

    private void SetCharacterFieldsEnabled(bool enabled)
    {
        LabelBox.IsEnabled = enabled;
        CharColourBox.IsEnabled = enabled;
        CharWidthBox.IsEnabled = enabled;
        CharHeightBox.IsEnabled = enabled;
        CharHotkeyBox.IsEnabled = enabled;
    }

    private void CharBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        // keep what was typed for the previous character; if it's invalid, stay on it
        if (_currentCharacter is not null && !CommitCharacter())
        {
            _loading = true;
            CharBox.SelectedItem = _currentCharacter;
            _loading = false;
            return;
        }

        _currentCharacter = CharBox.SelectedItem as string;
        if (_currentCharacter is null || !_edits.TryGetValue(_currentCharacter, out var c)) return;

        LabelBox.Text = c.Label;
        CharColourBox.Text = c.LabelColourHex;
        CharWidthBox.Text = c.Width.ToString();
        CharHeightBox.Text = c.Height.ToString();
        CharHotkeyBox.Text = c.Hotkey;
    }

    private bool IsValidColour(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        try { _ = (Color)ColorConverter.ConvertFromString(text.Trim()); return true; }
        catch { return false; }
    }

    private static bool IsValidHotkey(string text) =>
        string.IsNullOrWhiteSpace(text) || HotkeyParser.TryParse(text, out _, out _);

    /// <summary>Validates and stores the per-character fields into the working copy.</summary>
    private bool CommitCharacter()
    {
        if (_currentCharacter is null || !_edits.TryGetValue(_currentCharacter, out var c)) return true;
        string who = _currentCharacter + ": ";

        if (!int.TryParse(string.IsNullOrWhiteSpace(CharWidthBox.Text) ? "0" : CharWidthBox.Text.Trim(), out int width) ||
            (width != 0 && (width < 80 || width > 1600)))
        {
            MessageBox.Show(who + "width must be 0 (default) or 80 to 1600.");
            return false;
        }
        if (!int.TryParse(string.IsNullOrWhiteSpace(CharHeightBox.Text) ? "0" : CharHeightBox.Text.Trim(), out int height) ||
            (height != 0 && (height < 45 || height > 1200)))
        {
            MessageBox.Show(who + "height must be 0 (automatic) or 45 to 1200.");
            return false;
        }
        if (!IsValidColour(CharColourBox.Text))
        {
            MessageBox.Show(who + "enter a valid hex colour such as #F59E0B, or leave it empty.");
            return false;
        }
        if (!IsValidHotkey(CharHotkeyBox.Text))
        {
            MessageBox.Show(who + "hotkey not recognised. Use something like Ctrl+Alt+1 or Ctrl+Shift+F5.");
            return false;
        }

        c.Width = width;
        c.Height = height;
        c.Label = LabelBox.Text.Trim();
        c.LabelColourHex = CharColourBox.Text.Trim();
        c.Hotkey = CharHotkeyBox.Text.Trim();
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WidthBox.Text.Trim(), out int width) || width < 120 || width > 1200)
        {
            MessageBox.Show("Preview width must be 120 to 1200.");
            return;
        }
        if (!int.TryParse(RefreshBox.Text.Trim(), out int refresh) || refresh < 500 || refresh > 10000)
        {
            MessageBox.Show("Scan interval must be 500 to 10000 ms.");
            return;
        }
        if (!IsValidColour(LabelColourBox.Text) || !IsValidColour(ColourBox.Text))
        {
            MessageBox.Show("Enter valid hex colours such as #F59E0B.");
            return;
        }
        if (!IsValidHotkey(ForwardBox.Text) || !IsValidHotkey(BackBox.Text))
        {
            MessageBox.Show("Next/previous hotkey not recognised. Use something like Ctrl+Alt+Right, or leave it empty.");
            return;
        }
        if (!CommitCharacter()) return;

        _settings.PreviewWidth = width;
        _settings.RefreshMilliseconds = refresh;
        _settings.ShowLabels = LabelsBox.IsChecked == true;
        _settings.DefaultLabelColourHex = LabelColourBox.Text.Trim();
        _settings.HighlightActive = HighlightBox.IsChecked == true;
        _settings.ActiveBorderHex = ColourBox.Text.Trim();
        _settings.LockPositions = LockBox.IsChecked == true;
        _settings.CycleForwardHotkey = ForwardBox.Text.Trim();
        _settings.CycleBackHotkey = BackBox.Text.Trim();

        foreach (var pair in _edits)
        {
            CharacterSettings c = pair.Value;
            bool isEmpty = c.Width == 0 && c.Height == 0 && c.Label.Length == 0 && c.LabelColourHex.Length == 0 &&
                           c.Hotkey.Length == 0 && c.Left is null && c.Top is null;
            if (isEmpty) _settings.Characters.Remove(pair.Key);
            else _settings.Characters[pair.Key] = c;
        }

        DialogResult = true; // closes the dialog
    }
}
