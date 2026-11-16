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
    private const int PageThumbsIndex = 2;
    private const int PageHotkeysIndex = 3;
    private const int PageClientsIndex = 4;
    private const int PageCharactersIndex = 5;
    private const int PageProfilesIndex = 6;

    private static readonly string[] Positions =
    {
        "TopLeft", "TopCenter", "TopRight", "LeftCenter", "Center", "RightCenter",
        "BottomLeft", "BottomCenter", "BottomRight"
    };

    private static readonly string[] BorderStyles = { "Solid", "Dashed", "Dotted", "DashDot", "Double", "CornerBrackets" };

    private readonly AppSettings _work;                       // a copy: Cancel leaves the real settings untouched
    private readonly List<string> _characters;
    private readonly HashSet<string> _running;
    private readonly List<CheckBox> _showBoxes = new();
    private AppState _state;
    private readonly Dictionary<string, string> _profileHotkeys = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentCharacter;
    private string? _selectedProfile;
    private bool _loading;

    /// <summary>The edited settings. Only valid after the dialog returned true.</summary>
    public AppSettings Result => _work;

    /// <summary>If set, the app should switch to this profile after saving.</summary>
    public string? SwitchToProfile { get; private set; }

    /// <param name="characters">Known characters, logged-in ones first (in the order their clients opened).</param>
    /// <param name="running">Characters that are logged in right now.</param>
    public SettingsWindow(AppSettings settings, IReadOnlyList<string> characters, IReadOnlyCollection<string> running)
    {
        InitializeComponent();
        _work = settings.Clone();
        _state = ProfileStore.LoadState();
        _running = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        _characters = characters
            .Concat(_work.Characters.Keys.Where(AppSettings.IsCharacterKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // drop-down contents
        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        LabelPositionBox.ItemsSource = Positions;
        ActiveStyleBox.ItemsSource = BorderStyles;
        InactiveStyleBox.ItemsSource = BorderStyles;

        // General
        (_work.PositionMode == AppSettings.ModeClient ? ModeClientRadio : ModeCharacterRadio).IsChecked = true;
        InheritBox.IsChecked = _work.NewCharactersInheritPosition;
        SameSizeBox.IsChecked = _work.SameSizeForAll;
        WidthBox.Text = _work.PreviewWidth.ToString();
        HeightBox.Text = _work.PreviewHeight.ToString();
        ClickableBox.IsChecked = _work.ThumbnailsClickable;
        LockBox.IsChecked = _work.LockPositions;
        SnapBox.IsChecked = _work.SnapEnabled;
        SnapDistBox.Text = _work.SnapDistance.ToString();
        RefreshBox.Text = _work.RefreshMilliseconds.ToString();

        // Appearance
        OpacityBox.Text = _work.ThumbnailOpacityPercent.ToString();
        LabelsBox.IsChecked = _work.ShowLabels;
        FontBox.Text = _work.LabelFontName;
        FontSizeBox.Text = _work.LabelFontSize.ToString();
        BoldBox.IsChecked = _work.LabelBold;
        LabelColourBox.Hex = _work.DefaultLabelColourHex;
        LabelBgBox.Hex = _work.LabelBackgroundHex;
        LabelBgOpacityBox.Text = _work.LabelBackgroundOpacityPercent.ToString();
        LabelPositionBox.SelectedItem = Positions.Contains(_work.LabelPosition) ? _work.LabelPosition : "TopLeft";
        OffsetXBox.Text = _work.LabelOffsetX.ToString();
        OffsetYBox.Text = _work.LabelOffsetY.ToString();
        HighlightBox.IsChecked = _work.HighlightActive;
        ColourBox.Hex = _work.ActiveBorderHex;
        ActiveWidthBox.Text = _work.ActiveBorderWidth.ToString();
        ActiveStyleBox.SelectedItem = BorderStyles.Contains(_work.ActiveBorderStyle) ? _work.ActiveBorderStyle : "Solid";
        InactiveBox.IsChecked = _work.ShowInactiveBorder;
        InactiveColourBox.Hex = _work.InactiveBorderHex;
        InactiveWidthBox.Text = _work.InactiveBorderWidth.ToString();
        InactiveStyleBox.SelectedItem = BorderStyles.Contains(_work.InactiveBorderStyle) ? _work.InactiveBorderStyle : "Solid";

        // Thumbnails
        LoginScreensBox.IsChecked = _work.ShowLoginScreens;
        HideActiveBox.IsChecked = _work.HideActiveThumbnail;
        HideUnfocusedBox.IsChecked = _work.HideWhenEveNotFocused;
        HideDelayBox.Text = _work.HideDelayMs.ToString();

        // Hotkeys
        ForwardBox.Text = _work.CycleForwardHotkey;
        BackBox.Text = _work.CycleBackHotkey;
        CycleLoginBox.IsChecked = _work.CycleIncludeLoginScreens;
        ShowExcludedBox.IsChecked = _work.ShowExcludedOverlay;

        // Clients
        AutoMinBox.IsChecked = _work.AutoMinimiseInactive;
        AutoMinDelayBox.Text = _work.AutoMinimiseDelaySeconds.ToString();

        // the per-colour fallbacks and the label preview
        CharColourBox.FallbackHex = ColourField.TryParse(LabelColourBox.Hex, out _) ? LabelColourBox.Hex : "#FFFFFF";
        LabelColourBox.HexChanged += () =>
        {
            CharColourBox.FallbackHex = ColourField.TryParse(LabelColourBox.Hex, out _) ? LabelColourBox.Hex : "#FFFFFF";
            UpdatePreview();
        };
        CharColourBox.HexChanged += UpdatePreview;

        // the three "one tick per character" lists
        _showBoxes.AddRange(BuildList(ClientChecks, true, c => !c.Hidden, (c, v) => c.Hidden = !v));
        BuildList(SkipChecks, false, c => c.ExcludeFromCycling, (c, v) => c.ExcludeFromCycling = v);
        BuildList(MinChecks, false, c => c.NeverMinimise, (c, v) => c.NeverMinimise = v);

        if (_characters.Count == 0)
        {
            NoThumbText.Visibility = Visibility.Visible;
            NoSkipText.Visibility = Visibility.Visible;
            NoMinText.Visibility = Visibility.Visible;
            NoCharText.Visibility = Visibility.Visible;
            CharBox.IsEnabled = false;
            SetCharacterFieldsEnabled(false);
        }
        else
        {
            foreach (string name in _characters) CharBox.Items.Add(name);
            CharBox.SelectedIndex = 0;
        }

        // Profiles
        NextProfileBox.Text = _state.NextProfileHotkey;
        PrevProfileBox.Text = _state.PreviousProfileHotkey;
        foreach (var pair in _state.ProfileHotkeys) _profileHotkeys[pair.Key] = pair.Value;
        RefreshProfileList(_state.ActiveProfile);

        UpdatePreview();
        Nav.SelectedIndex = 0;
    }

    // ---------- navigation ----------

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageProfiles is null) return;
        var pages = new UIElement[]
        {
            PageGeneral, PageAppearance, PageThumbs, PageHotkeys, PageClients, PageCharacters, PageProfiles
        };
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- per-character tick lists (thumbnails to show / skip in cycling / never minimise) ----------

    private List<CheckBox> BuildList(StackPanel host, bool defaultValue,
        Func<CharacterSettings, bool> get, Action<CharacterSettings, bool> set)
    {
        var boxes = new List<CheckBox>();
        foreach (string name in _characters)
        {
            string chosen = name;
            var existing = _work.TryGetCharacter(name);
            var box = new CheckBox
            {
                Content = _running.Contains(name) ? name + "   (logged in)" : name,
                IsChecked = existing is null ? defaultValue : get(existing),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            box.Click += (_, _) => set(_work.GetOrAddCharacter(chosen), box.IsChecked == true);
            boxes.Add(box);
            host.Children.Add(box);
        }
        return boxes;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllShown(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAllShown(false);

    private void SetAllShown(bool shown)
    {
        LoginScreensBox.IsChecked = shown;
        for (int i = 0; i < _showBoxes.Count && i < _characters.Count; i++)
        {
            _showBoxes[i].IsChecked = shown;
            _work.GetOrAddCharacter(_characters[i]).Hidden = !shown;
        }
    }

    // ---------- characters page ----------

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
        if (_currentCharacter is null) return;
        var c = _work.TryGetCharacter(_currentCharacter) ?? new CharacterSettings();
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
        if (_currentCharacter is null) return true;
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

        var c = _work.GetOrAddCharacter(_currentCharacter);
        c.Width = width;
        c.Height = height;
        c.Label = LabelBox.Text.Trim();
        c.LabelColourHex = CharColourBox.Hex;
        c.Hotkey = CharHotkeyBox.Text.Trim();
        return true;
    }

    // ---------- hotkey boxes ----------

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

    // ---------- profiles page ----------

    private string? SelectedProfileName => (ProfileList.SelectedItem as ListBoxItem)?.Tag as string;

    private void RefreshProfileList(string? select)
    {
        _loading = true;
        ProfileList.Items.Clear();
        int selectIndex = -1;
        int index = 0;
        foreach (string name in ProfileStore.List())
        {
            bool active = name.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase);
            ProfileList.Items.Add(new ListBoxItem { Content = active ? name + "   (active)" : name, Tag = name });
            if (select is not null && name.Equals(select, StringComparison.OrdinalIgnoreCase)) selectIndex = index;
            index++;
        }
        if (selectIndex < 0 && ProfileList.Items.Count > 0) selectIndex = 0;
        ProfileList.SelectedIndex = selectIndex;
        _loading = false;

        _selectedProfile = SelectedProfileName;
        ProfileHotkeyBox.Text = _selectedProfile is not null && _profileHotkeys.TryGetValue(_selectedProfile, out var hotkey) ? hotkey : "";
    }

    private void StoreProfileHotkeyEdit()
    {
        if (_selectedProfile is null) return;
        string text = ProfileHotkeyBox.Text.Trim();
        if (text.Length == 0) _profileHotkeys.Remove(_selectedProfile);
        else _profileHotkeys[_selectedProfile] = text;
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        StoreProfileHotkeyEdit();   // keep the hotkey typed for the previously selected profile
        _selectedProfile = SelectedProfileName;
        ProfileHotkeyBox.Text = _selectedProfile is not null && _profileHotkeys.TryGetValue(_selectedProfile, out var hotkey) ? hotkey : "";
    }

    private string? AskProfileName(string title, string initial)
    {
        string? raw = PromptWindow.Ask(this, title, "Profile name:", initial);
        if (raw is null) return null;
        string name = ProfileStore.CleanName(raw);
        if (name.Length == 0)
        {
            MessageBox.Show("Please type a name.");
            return null;
        }
        if (ProfileStore.Exists(name))
        {
            MessageBox.Show("There's already a profile called \"" + name + "\".");
            return null;
        }
        return name;
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        string? name = AskProfileName("New profile", "");
        if (name is null) return;
        ProfileStore.Save(name, new AppSettings());
        RefreshProfileList(name);
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        string? source = SelectedProfileName;
        if (source is null) return;
        string? name = AskProfileName("Duplicate profile", source + " copy");
        if (name is null) return;

        if (source.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryApply()) return;             // copy what's on screen now, not what was last saved
            ProfileStore.Save(name, _work.Clone());
        }
        else
        {
            ProfileStore.Save(name, ProfileStore.Load(source));
        }
        RefreshProfileList(name);
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        string? old = SelectedProfileName;
        if (old is null) return;
        string? name = AskProfileName("Rename profile", old);
        if (name is null) return;

        if (!ProfileStore.Rename(old, name))
        {
            MessageBox.Show("Couldn't rename that profile.");
            return;
        }
        if (_profileHotkeys.TryGetValue(old, out var hotkey))
        {
            _profileHotkeys.Remove(old);
            _profileHotkeys[name] = hotkey;
        }
        _state = ProfileStore.LoadState();
        _selectedProfile = null;
        RefreshProfileList(name);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        string? name = SelectedProfileName;
        if (name is null) return;
        if (name.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("You can't delete the active profile. Switch to another profile first.");
            return;
        }
        if (ProfileStore.List().Count < 2) return;
        if (MessageBox.Show("Delete the profile \"" + name + "\"? This can't be undone.", "Delete profile",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        ProfileStore.Delete(name);
        _profileHotkeys.Remove(name);
        _selectedProfile = null;
        RefreshProfileList(_state.ActiveProfile);
    }

    private void SwitchProfile_Click(object sender, RoutedEventArgs e)
    {
        string? name = SelectedProfileName;
        if (name is null || name.Equals(_state.ActiveProfile, StringComparison.OrdinalIgnoreCase)) return;
        if (!TryApply()) return;
        CommitState();
        SwitchToProfile = name;
        DialogResult = true;
    }

    private void CommitState()
    {
        StoreProfileHotkeyEdit();
        var existing = new HashSet<string>(ProfileStore.List(), StringComparer.OrdinalIgnoreCase);

        _state = ProfileStore.LoadState();   // pick up renames made on this page
        _state.NextProfileHotkey = NextProfileBox.Text.Trim();
        _state.PreviousProfileHotkey = PrevProfileBox.Text.Trim();
        _state.ProfileHotkeys = _profileHotkeys
            .Where(p => p.Value.Length > 0 && existing.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        ProfileStore.SaveState(_state);
    }

    // ---------- validation + save ----------

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApply()) return;
        CommitState();
        DialogResult = true; // closes the dialog
    }

    private bool Fail(int page, string message)
    {
        Nav.SelectedIndex = page;
        MessageBox.Show(message);
        return false;
    }

    private static bool InRange(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), out value) && value >= min && value <= max;

    /// <summary>Checks every field (jumping to the page with a bad value) and writes them into the working copy.</summary>
    private bool TryApply()
    {
        // General
        if (!InRange(WidthBox.Text, 120, 1200, out int width))
            return Fail(PageGeneralIndex, "Thumbnail width must be 120 to 1200.");
        if (!InRange(HeightBox.Text, 0, 1200, out int height) || (height != 0 && height < 45))
            return Fail(PageGeneralIndex, "Thumbnail height must be 0 (automatic) or 45 to 1200.");
        if (!InRange(SnapDistBox.Text, 1, 100, out int snap))
            return Fail(PageGeneralIndex, "Snap distance must be 1 to 100 pixels.");
        if (!InRange(RefreshBox.Text, 500, 10000, out int refresh))
            return Fail(PageGeneralIndex, "Scan interval must be 500 to 10000 ms.");

        // Appearance
        if (!InRange(OpacityBox.Text, 10, 100, out int opacity))
            return Fail(PageAppearanceIndex, "Thumbnail opacity must be 10 to 100.");
        if (!InRange(FontSizeBox.Text, 6, 72, out int fontSize))
            return Fail(PageAppearanceIndex, "Font size must be 6 to 72.");
        if (!InRange(LabelBgOpacityBox.Text, 0, 100, out int bgOpacity))
            return Fail(PageAppearanceIndex, "Text background opacity must be 0 to 100.");
        if (!InRange(OffsetXBox.Text, -300, 300, out int offsetX) || !InRange(OffsetYBox.Text, -300, 300, out int offsetY))
            return Fail(PageAppearanceIndex, "Text offsets must be between -300 and 300 pixels.");
        if (!InRange(ActiveWidthBox.Text, 1, 20, out int activeWidth) || !InRange(InactiveWidthBox.Text, 1, 20, out int inactiveWidth))
            return Fail(PageAppearanceIndex, "Border widths must be 1 to 20 pixels.");
        if (!LabelColourBox.IsValid || !LabelBgBox.IsValid || !ColourBox.IsValid || !InactiveColourBox.IsValid)
            return Fail(PageAppearanceIndex, "One of the colours isn't valid. Use the Pick button, or type something like #F59E0B.");

        // Thumbnails / clients
        if (!InRange(HideDelayBox.Text, 0, 5000, out int hideDelay))
            return Fail(PageThumbsIndex, "The hide delay must be 0 to 5000 ms.");
        if (!InRange(AutoMinDelayBox.Text, 0, 600, out int minDelay))
            return Fail(PageClientsIndex, "The minimise delay must be 0 to 600 seconds.");

        // Hotkeys
        if (!IsValidHotkey(ForwardBox.Text) || !IsValidHotkey(BackBox.Text))
            return Fail(PageHotkeysIndex, "That hotkey isn't supported. Click the box and press the keys again, or press Backspace to clear it.");
        if (!IsValidHotkey(ProfileHotkeyBox.Text) || !IsValidHotkey(NextProfileBox.Text) || !IsValidHotkey(PrevProfileBox.Text))
            return Fail(PageProfilesIndex, "That hotkey isn't supported. Click the box and press the keys again, or press Backspace to clear it.");

        // Characters
        if (!CommitCharacter())
        {
            Nav.SelectedIndex = PageCharactersIndex;
            return false;
        }

        _work.PositionMode = ModeClientRadio.IsChecked == true ? AppSettings.ModeClient : AppSettings.ModeCharacter;
        _work.NewCharactersInheritPosition = InheritBox.IsChecked == true;
        _work.SameSizeForAll = SameSizeBox.IsChecked == true;
        _work.PreviewWidth = width;
        _work.PreviewHeight = height;
        _work.ThumbnailsClickable = ClickableBox.IsChecked == true;
        _work.LockPositions = LockBox.IsChecked == true;
        _work.SnapEnabled = SnapBox.IsChecked == true;
        _work.SnapDistance = snap;
        _work.RefreshMilliseconds = refresh;

        _work.ThumbnailOpacityPercent = opacity;
        _work.ShowLabels = LabelsBox.IsChecked == true;
        _work.LabelFontName = string.IsNullOrWhiteSpace(FontBox.Text) ? "Segoe UI" : FontBox.Text.Trim();
        _work.LabelFontSize = fontSize;
        _work.LabelBold = BoldBox.IsChecked == true;
        _work.DefaultLabelColourHex = LabelColourBox.Hex.Length > 0 ? LabelColourBox.Hex : "#FFFFFF";
        _work.LabelBackgroundHex = LabelBgBox.Hex.Length > 0 ? LabelBgBox.Hex : "#0A0F16";
        _work.LabelBackgroundOpacityPercent = bgOpacity;
        _work.LabelPosition = LabelPositionBox.SelectedItem as string ?? "TopLeft";
        _work.LabelOffsetX = offsetX;
        _work.LabelOffsetY = offsetY;
        _work.HighlightActive = HighlightBox.IsChecked == true;
        _work.ActiveBorderHex = ColourBox.Hex.Length > 0 ? ColourBox.Hex : "#F59E0B";
        _work.ActiveBorderWidth = activeWidth;
        _work.ActiveBorderStyle = ActiveStyleBox.SelectedItem as string ?? "Solid";
        _work.ShowInactiveBorder = InactiveBox.IsChecked == true;
        _work.InactiveBorderHex = InactiveColourBox.Hex.Length > 0 ? InactiveColourBox.Hex : "#606060";
        _work.InactiveBorderWidth = inactiveWidth;
        _work.InactiveBorderStyle = InactiveStyleBox.SelectedItem as string ?? "Solid";

        _work.ShowLoginScreens = LoginScreensBox.IsChecked == true;
        _work.HideActiveThumbnail = HideActiveBox.IsChecked == true;
        _work.HideWhenEveNotFocused = HideUnfocusedBox.IsChecked == true;
        _work.HideDelayMs = hideDelay;

        _work.CycleForwardHotkey = ForwardBox.Text.Trim();
        _work.CycleBackHotkey = BackBox.Text.Trim();
        _work.CycleIncludeLoginScreens = CycleLoginBox.IsChecked == true;
        _work.ShowExcludedOverlay = ShowExcludedBox.IsChecked == true;

        _work.AutoMinimiseInactive = AutoMinBox.IsChecked == true;
        _work.AutoMinimiseDelaySeconds = minDelay;

        // drop per-character entries that hold nothing (saved positions of "[client N]" entries are kept)
        foreach (string key in _work.Characters.Keys.ToList())
        {
            var c = _work.Characters[key];
            bool empty = c.Width == 0 && c.Height == 0 && c.Label.Length == 0 && c.LabelColourHex.Length == 0 &&
                         c.Hotkey.Length == 0 && c.Left is null && c.Top is null &&
                         !c.Hidden && !c.ExcludeFromCycling && !c.NeverMinimise;
            if (empty) _work.Characters.Remove(key);
        }
        return true;
    }
}
