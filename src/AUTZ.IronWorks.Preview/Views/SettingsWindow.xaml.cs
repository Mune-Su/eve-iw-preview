using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

public partial class SettingsWindow : Window
{
    // order matches the left-hand menu
    private const int PageGeneralIndex = 0;
    private const int PageAppearanceIndex = 1;
    private const int PageHotkeysIndex = 3;
    private const int PageCharactersIndex = 4;

    private readonly AppSettings _settings;
    private readonly Dictionary<string, CharacterSettings> _edits = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CheckBox> _showBoxes = new();
    private string? _currentCharacter;
    private bool _loading;

    /// <param name="characters">Known characters, logged-in ones first (in the order their clients opened).</param>
    /// <param name="running">Characters that are logged in right now.</param>
    public SettingsWindow(AppSettings settings, IReadOnlyList<string> characters, IReadOnlyCollection<string> running)
    {
        InitializeComponent();
        _settings = settings;

        WidthBox.Text = settings.PreviewWidth.ToString();
        RefreshBox.Text = settings.RefreshMilliseconds.ToString();
        LabelsBox.IsChecked = settings.ShowLabels;
        LabelColourBox.Hex = settings.DefaultLabelColourHex;
        HighlightBox.IsChecked = settings.HighlightActive;
        ColourBox.Hex = settings.ActiveBorderHex;
        LockBox.IsChecked = settings.LockPositions;
        ClickableBox.IsChecked = settings.ThumbnailsClickable;
        OpacityBox.Text = settings.ThumbnailOpacityPercent.ToString();
        SnapBox.IsChecked = settings.SnapEnabled;
        SnapDistBox.Text = settings.SnapDistance.ToString();
        ForwardBox.Text = settings.CycleForwardHotkey;
        BackBox.Text = settings.CycleBackHotkey;
        LoginScreensBox.IsChecked = settings.ShowLoginScreens;

        // the per-character colour falls back to the default colour, so keep them in step
        CharColourBox.FallbackHex = LabelColourBox.Hex;
        LabelColourBox.HexChanged += () =>
        {
            CharColourBox.FallbackHex = LabelColourBox.IsValid && LabelColourBox.Hex.Length > 0 ? LabelColourBox.Hex : "#FFFFFF";
            UpdatePreview();
        };
        CharColourBox.HexChanged += UpdatePreview;

        var runningSet = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        var names = characters
            .Concat(settings.Characters.Keys.Where(AppSettings.IsCharacterKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string name in names)
        {
            var existing = settings.Characters.TryGetValue(name, out var found) ? found : null;
            _edits[name] = existing is not null ? Clone(existing) : new CharacterSettings();
            CharBox.Items.Add(name);

            string chosen = name;
            var box = new CheckBox
            {
                Content = runningSet.Contains(name) ? name + "   (logged in)" : name,
                IsChecked = !_edits[name].Hidden,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            box.Click += (_, _) => _edits[chosen].Hidden = box.IsChecked != true;
            _showBoxes.Add(box);
            ClientChecks.Children.Add(box);
        }

        if (names.Count == 0)
        {
            NoCharText.Visibility = Visibility.Visible;
            NoThumbText.Visibility = Visibility.Visible;
            CharBox.IsEnabled = false;
            SetCharacterFieldsEnabled(false);
        }
        else
        {
            CharBox.SelectedIndex = 0;
        }

        UpdatePreview();
        Nav.SelectedIndex = 0;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageCharacters is null) return;
        var pages = new UIElement[] { PageGeneral, PageAppearance, PageThumbs, PageHotkeys, PageCharacters };
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllShown(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAllShown(false);

    private void SetAllShown(bool shown)
    {
        LoginScreensBox.IsChecked = shown;
        for (int i = 0; i < _showBoxes.Count; i++)
        {
            _showBoxes[i].IsChecked = shown;
            string name = CharBox.Items[i] as string ?? "";
            if (_edits.TryGetValue(name, out var c)) c.Hidden = !shown;
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
        Hotkey = c.Hotkey,
        Hidden = c.Hidden
    };

    private void SetCharacterFieldsEnabled(bool enabled)
    {
        LabelBox.IsEnabled = enabled;
        CharColourBox.IsEnabled = enabled;
        CharWidthBox.IsEnabled = enabled;
        CharHeightBox.IsEnabled = enabled;
        CharHotkeyBox.IsEnabled = enabled;
    }

    /// <summary>Shows the label the way it will look: its text in its colour.</summary>
    private void UpdatePreview()
    {
        if (LabelPreview is null || LabelBox is null || CharColourBox is null || LabelColourBox is null) return;
        string text = LabelBox.Text.Trim();
        if (text.Length == 0) text = _currentCharacter ?? "Character name";
        LabelPreview.Text = text;
        string hex = CharColourBox.Hex.Length > 0 ? CharColourBox.Hex : LabelColourBox.Hex;
        LabelPreview.Foreground = ColourField.TryParse(hex, out var colour) ? new SolidColorBrush(colour) : Brushes.White;
    }

    private void LabelBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    /// <summary>Hotkey boxes record the key combination you press (including ` and other punctuation keys).</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
        if (key == Key.Tab) return; // let Tab move to the next box
        e.Handled = true;
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // wait for the real key

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && (key == Key.Back || key == Key.Delete))
        {
            box.Text = "";
            return;
        }
        box.Text = HotkeyParser.ToText(modifiers, key);
        box.CaretIndex = box.Text.Length;
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
        CharColourBox.Hex = c.LabelColourHex;
        CharWidthBox.Text = c.Width.ToString();
        CharHeightBox.Text = c.Height.ToString();
        CharHotkeyBox.Text = c.Hotkey;
        UpdatePreview();
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
        if (!CharColourBox.IsValid)
        {
            MessageBox.Show(who + "that isn't a valid colour. Use the Pick button, or type something like #F59E0B, or clear it.");
            return false;
        }
        if (!IsValidHotkey(CharHotkeyBox.Text))
        {
            MessageBox.Show(who + "that hotkey isn't supported. Click the box and press the keys again.");
            return false;
        }

        c.Width = width;
        c.Height = height;
        c.Label = LabelBox.Text.Trim();
        c.LabelColourHex = CharColourBox.Hex;
        c.Hotkey = CharHotkeyBox.Text.Trim();
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // each check jumps to the page that holds the bad value
        if (!int.TryParse(WidthBox.Text.Trim(), out int width) || width < 120 || width > 1200)
        {
            Nav.SelectedIndex = PageGeneralIndex;
            MessageBox.Show("Preview width must be 120 to 1200.");
            return;
        }
        if (!int.TryParse(RefreshBox.Text.Trim(), out int refresh) || refresh < 500 || refresh > 10000)
        {
            Nav.SelectedIndex = PageGeneralIndex;
            MessageBox.Show("Scan interval must be 500 to 10000 ms.");
            return;
        }
        if (!int.TryParse(SnapDistBox.Text.Trim(), out int snap) || snap < 1 || snap > 100)
        {
            Nav.SelectedIndex = PageGeneralIndex;
            MessageBox.Show("Snap distance must be 1 to 100 pixels.");
            return;
        }
        if (!int.TryParse(OpacityBox.Text.Trim(), out int opacity) || opacity < 10 || opacity > 100)
        {
            Nav.SelectedIndex = PageAppearanceIndex;
            MessageBox.Show("Thumbnail opacity must be 10 to 100.");
            return;
        }
        if (!LabelColourBox.IsValid || !ColourBox.IsValid)
        {
            Nav.SelectedIndex = PageAppearanceIndex;
            MessageBox.Show("One of the colours isn't valid. Use the Pick button, or type something like #F59E0B.");
            return;
        }
        if (!IsValidHotkey(ForwardBox.Text) || !IsValidHotkey(BackBox.Text))
        {
            Nav.SelectedIndex = PageHotkeysIndex;
            MessageBox.Show("That hotkey isn't supported. Click the box and press the keys again, or press Backspace to clear it.");
            return;
        }
        if (!CommitCharacter())
        {
            Nav.SelectedIndex = PageCharactersIndex;
            return;
        }

        _settings.PreviewWidth = width;
        _settings.RefreshMilliseconds = refresh;
        _settings.ShowLabels = LabelsBox.IsChecked == true;
        _settings.DefaultLabelColourHex = LabelColourBox.Hex.Length > 0 ? LabelColourBox.Hex : "#FFFFFF";
        _settings.HighlightActive = HighlightBox.IsChecked == true;
        _settings.ActiveBorderHex = ColourBox.Hex.Length > 0 ? ColourBox.Hex : "#F59E0B";
        _settings.LockPositions = LockBox.IsChecked == true;
        _settings.ThumbnailsClickable = ClickableBox.IsChecked == true;
        _settings.ThumbnailOpacityPercent = opacity;
        _settings.SnapEnabled = SnapBox.IsChecked == true;
        _settings.SnapDistance = snap;
        _settings.ShowLoginScreens = LoginScreensBox.IsChecked == true;
        _settings.CycleForwardHotkey = ForwardBox.Text.Trim();
        _settings.CycleBackHotkey = BackBox.Text.Trim();

        foreach (var pair in _edits)
        {
            CharacterSettings c = pair.Value;
            bool isEmpty = c.Width == 0 && c.Height == 0 && c.Label.Length == 0 && c.LabelColourHex.Length == 0 &&
                           c.Hotkey.Length == 0 && c.Left is null && c.Top is null && !c.Hidden;
            if (isEmpty) _settings.Characters.Remove(pair.Key);
            else _settings.Characters[pair.Key] = c;
        }

        DialogResult = true; // closes the dialog
    }
}
