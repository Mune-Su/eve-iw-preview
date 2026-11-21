using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;

public partial class SettingsWindow : Window
{
    // order matches the left-hand menu
    private const int PageGeneral_ = 0;
    private const int PageThumbs_ = 1;          // appearance and behaviour of the thumbnails, together
    private const int PageGroups_ = 2;          // the character groups and Find Running Clients
    private const int PageHotkeys_ = 3;
    private const int PageAlerts_ = 4;          // notifications and log monitoring
    private const int PageMemory_ = 5;

    private const int MaxSlotRows = 20;
    private const int MaxThumbRows = 60;

    private static readonly string[] Positions =
    {
        "TopLeft", "TopCenter", "TopRight", "LeftCenter", "Center", "RightCenter",
        "BottomLeft", "BottomCenter", "BottomRight"
    };

    private static readonly string[] BorderStyles = { "Solid", "Dashed", "Dotted", "DashDot", "Double", "CornerBrackets" };
    private static readonly string[] ActionTexts = { "Do nothing", "Maximise it", "Restore it (un-maximise)" };
    private static readonly string[] ActionValues = { "None", "Maximise", "Restore" };
    private static readonly string[] WhatsNewTexts = { "When the app has been updated", "Every time it starts", "Never" };

    private sealed record PageDef(string Title, ScrollViewer Page, Control[] Cards);

    private readonly PreviewManager _manager;
    private readonly List<PageDef> _pages = new();
    private readonly List<Border> _navItems = new();
    private readonly List<Border> _navBars = new();
    private readonly List<TextBlock> _navTexts = new();
    private int _pageIndex = -1;

    private readonly Dictionary<string, AppSettings> _edited = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _baseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _profileHotkeys = new(StringComparer.OrdinalIgnoreCase);
    private sealed class ThumbRow
    {
        public Grid Panel = null!;
        public CheckBox Show = null!;
        public TextBox Name = null!;
    }
    private readonly List<ThumbRow> _thumbRows = new();
    private ThumbnailGroup? _shownThumbGroup;      // the thumbnail group on screen (it is in use)
    private SlotHotkeyGroup? _shownSlotGroup;      // the client-hotkey group on screen (it is in use)
    private bool _groupLoading;
    private readonly List<(string Name, CheckBox Box)> _scanBoxes = new();
    private readonly List<HotkeyField> _slotFields = new();
    private HashSet<string> _running;
    private bool _fontsLoaded;
    private readonly System.Windows.Threading.DispatcherTimer _memTimer = new() { Interval = TimeSpan.FromSeconds(2) };   // runs only while the Memory page is showing
    private readonly System.Windows.Threading.DispatcherTimer _progTimer = new() { Interval = TimeSpan.FromSeconds(10) };  // Top Programs: only while the Memory page is showing and the window isn't minimised
    private bool _progBusy;
    private TrimEstimate? _trimEstimate;
    private ClipboardInfo? _clipInfo;
    private bool _clipBusy;
    private IReadOnlyList<ProgramUsage> _programs = Array.Empty<ProgramUsage>();
    private readonly List<string> _runningList;    // the logged-in characters, in client order
    private bool _orderLoading;
    private List<string> _characters = new();
    private AppSettings _work;                 // the profile shown right now (a copy: nothing real changes until APPLY or SAVE)
    private AppState _state;
    private string _stateBaseline = "";
    private string _activeProfile;
    private string _shownProfile;
    private string? _currentCharacter;
    private int _charFailPage = PageThumbs_;           // the page holding the per-character field that failed to validate
    private bool _loading;
    private bool _quiet;                       // validate without showing messages or jumping pages
    private bool _closing;

    public SettingsWindow(PreviewManager manager)
    {
        InitializeComponent();
        _manager = manager;

        _state = manager.State.Clone();
        _state.RunOnStartup = StartupService.IsEnabled();
        _activeProfile = _state.ActiveProfile;
        _shownProfile = _activeProfile;
        _work = manager.Settings.Clone();
        _edited[_shownProfile] = _work;
        _runningList = manager.RunningCharacters().ToList();
        _running = new HashSet<string>(_runningList, StringComparer.OrdinalIgnoreCase);

        // drop-down contents
        // the list of every installed font is only built when the font box is first used (it takes time and memory)
        FontBox.PreviewMouseDown += (_, _) => EnsureFonts();
        FontBox.GotKeyboardFocus += (_, _) => EnsureFonts();
        LabelPositionBox.ItemsSource = Positions;
        ActiveStyleBox.ItemsSource = BorderStyles;
        InactiveStyleBox.ItemsSource = BorderStyles;
        CharActionBox.ItemsSource = ActionTexts;
        NotifySoundCombo.ItemsSource = NotificationService.SoundNames;
        WhatsNewCombo.ItemsSource = WhatsNewTexts;

        // the per-character colour falls back to the default colour, so keep them in step
        LabelColourBox.HexChanged += () =>
        {
            CharColourBox.FallbackHex = ColourField.TryParse(LabelColourBox.Hex, out _) ? LabelColourBox.Hex : "#FFFFFF";
            UpdatePreview();
        };
        CharColourBox.HexChanged += UpdatePreview;
        FontSizeBox.TextChanged += (_, _) => UpdatePreview();

        PreviewMouseWheel += OnPreviewMouseWheel;

        _memTimer.Tick += (_, _) => RefreshMemory();
        _progTimer.Tick += (_, _) => RefreshPrograms();
        HotkeyField.RecordingChanged += OnHotkeyRecording;
        StateChanged += (_, _) => UpdateMemoryTimers();
        MemExcludeBox.LostFocus += (_, _) => BuildProgramRows();
        MemTrack.SizeChanged += (_, _) => UpdateMemoryBar();
        LoadMemoryControls();

        foreach (var pair in _state.ProfileHotkeys) _profileHotkeys[pair.Key] = pair.Value;
        NextProfileBox.Text = _state.NextProfileHotkey;
        PrevProfileBox.Text = _state.PreviousProfileHotkey;
        RunOnStartupBox.IsChecked = _state.RunOnStartup;
        DebugLogBox.IsChecked = _state.DebugLogging;
        (_state.DebugLogMode == "PerSession" ? DebugSessionRadio : DebugContinuousRadio).IsChecked = true;
        PinTrayBox.IsChecked = _state.PinTrayIcon;
        AlwaysAdminBox.IsChecked = _state.AlwaysAdmin;
        UpdateAlwaysAdminControls();
        UpdateComponentToggleButtons();
        WhatsNewCombo.SelectedIndex = _state.WhatsNewMode switch { "Always" => 1, "Never" => 2, _ => 0 };

        VersionText.Text = "v" + AppInfo.Version;
        SidebarVersion.Text = "Version " + AppInfo.Version;
        FolderText.Text = "Settings:  " + DataFolder + "\nLog:       " + LogPath;

        BuildNav();
        RefreshProfileBox();
        LoadFrom();
        CaptureBaseline(_shownProfile);
        _stateBaseline = StateSnapshot();
        SelectPage(0);
    }

    private void UpdateComponentToggleButtons()
    {
        SwitcherToggleBtn.Content = _manager.SwitcherEnabled ? "Disable Switcher" : "Enable Switcher";
        MemoryCleanerToggleBtn.Content = _manager.MemoryCleanerEnabled ? "Disable Memory Cleaner" : "Enable Memory Cleaner";
    }

    private void SwitcherToggle_Click(object sender, RoutedEventArgs e)
    {
        _manager.ToggleSwitcher();
        _state.SwitcherEnabled = _manager.SwitcherEnabled;
        UpdateComponentToggleButtons();
    }

    private void MemoryCleanerToggle_Click(object sender, RoutedEventArgs e)
    {
        _manager.ToggleMemoryCleaner();
        _state.MemoryCleanerEnabled = _manager.MemoryCleanerEnabled;
        UpdateComponentToggleButtons();
        UpdateMemoryTimers();
    }

    private static string DataFolder => AppInfo.DataFolder;

    private static string LogPath => Path.Combine(DataFolder, "preview.log");

    // ---------- left-hand menu ----------

    private void BuildNav()
    {
        _pages.Add(new PageDef("General", PageGeneral, new Control[] { CardComponents, CardStartup, CardClients }));
        _pages.Add(new PageDef("Thumbnails", PageThumbs, new Control[] { CardSize, CardLabel, CardActiveBorder, CardCharacter, CardInteraction }));
        _pages.Add(new PageDef("Grouping", PageGroups, new Control[] { CardRoster, CardScan, CardClientOrder }));
        _pages.Add(new PageDef("Hotkeys", PageHotkeys, new Control[] { CardCycling, CardHotkeyOptions, CardSlotKeys, CardCharActions, CardSkipped, CardProfileKeys }));
        _pages.Add(new PageDef("Alerts", PageAlerts, new Control[] { CardNotifyMain, CardLogMain, CardLogEvents }));
        _pages.Add(new PageDef("Memory", PageMemory, new Control[] { CardMemoryNow, CardMemoryAuto, CardMemoryTop, CardMemoryKeep }));
        _pages.Add(new PageDef("About", PageAbout, new Control[] { CardAbout, CardWhatsNew, CardFiles, CardDiagnostics }));

        for (int i = 0; i < _pages.Count; i++)
        {
            int index = i;
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2, 0, 0, 2), Background = Brushes.Transparent };
            var text = new TextBlock
            {
                Text = _pages[i].Title,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(11, 9, 10, 9),
                VerticalAlignment = VerticalAlignment.Center
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(bar);
            grid.Children.Add(text);

            var item = new Border
            {
                Child = grid,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            item.MouseLeftButtonUp += (_, _) => SelectPage(index);
            item.MouseEnter += (_, _) =>
            {
                if (index == _pageIndex) return;
                item.Background = Theme.Card;
                _navTexts[index].Foreground = Theme.Text;
            };
            item.MouseLeave += (_, _) =>
            {
                if (index == _pageIndex) return;
                item.Background = Brushes.Transparent;
                _navTexts[index].Foreground = Theme.Muted;
            };
            NavPanel.Children.Add(item);

            _navItems.Add(item);
            _navBars.Add(bar);
            _navTexts.Add(text);
        }
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        bool leavingThumbs = _pageIndex == PageGroups_ && index != PageGroups_;     // the character list is on this page
        _pageIndex = index;

        for (int i = 0; i < _pages.Count; i++)
        {
            bool on = i == index;
            _pages[i].Page.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            _navItems[i].Background = on ? Theme.Selected : Brushes.Transparent;       // accent-lo
            _navItems[i].BorderBrush = on ? Theme.AccentLine : Brushes.Transparent;    // accent-line
            _navBars[i].Background = on ? Theme.Accent : Brushes.Transparent;          // the orange left bar
            _navTexts[i].Foreground = on ? Brushes.White : Theme.Muted;
        }
        PageTitleText.Text = _pages[index].Title;
        _pages[index].Page.ScrollToVerticalOffset(0);
        if (leavingThumbs) RefreshAfterThumbEdit();

        // the memory and program readings are only taken while the Memory page is showing
        if (index == PageMemory_) RefreshMemory();
        UpdateMemoryTimers();
    }

    /// <summary>The memory readings and the Top Programs list run only while the Memory page is showing and the window isn't minimised.</summary>
    private void UpdateMemoryTimers()
    {
        if (_pageIndex == PageMemory_ && WindowState != WindowState.Minimized)
        {
            if (!_memTimer.IsEnabled) _memTimer.Start();
            if (!_progTimer.IsEnabled)
            {
                _progTimer.Start();
                RefreshPrograms();
            }
        }
        else
        {
            _memTimer.Stop();
            _progTimer.Stop();
        }
    }

    /// <summary>Opens a collapsed card (a card the user has collapsed would otherwise hide what the search or an error points at).</summary>
    private static void ExpandCard(Control card)
    {
        if (card is Expander expander) expander.IsExpanded = true;
    }

    private void ExpandCardsOnPage(int page)
    {
        if (page < 0 || page >= _pages.Count) return;
        foreach (var card in _pages[page].Cards) ExpandCard(card);
    }

    private static void ScrollToCard(ScrollViewer page, FrameworkElement card)
    {
        try
        {
            page.UpdateLayout();
            if (page.Content is not FrameworkElement content) return;
            double y = card.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
            page.ScrollToVerticalOffset(Math.Max(0, y - 6));
        }
        catch { }
    }

    // ---------- status line ----------

    private void SetStatus(string text, bool good)
    {
        StatusText.Text = text;
        StatusText.Foreground = good ? Theme.Success : Theme.Accent;
    }

    // ---------- loading the shown profile into the controls ----------

    private void LoadFrom()
    {
        var s = _work;

        s.EnsureGroups();

        // fill the group in use from the characters already known the first time a profile is opened
        if (!s.RosterInitialised)
        {
            var first = s.ActiveThumbGroup() ?? s.ThumbnailGroups[0];
            foreach (string name in s.Characters.Keys.Where(AppSettings.IsCharacterKey)
                         .Concat(s.SlotCharacters.Values.Where(v => !string.IsNullOrWhiteSpace(v)))
                         .Concat(_running))
            {
                if (!first.Characters.Contains(name, StringComparer.OrdinalIgnoreCase)) first.Characters.Add(name);
            }
            s.RosterInitialised = true;
        }

        // General
        (s.PositionMode == AppSettings.ModeClient ? ModeClientRadio : ModeCharacterRadio).IsChecked = true;
        InheritBox.IsChecked = s.NewCharactersInheritPosition;
        SameSizeBox.IsChecked = s.SameSizeForAll;
        WidthBox.Text = s.PreviewWidth.ToString();
        HeightBox.Text = s.PreviewHeight.ToString();
        ClickableBox.IsChecked = s.ThumbnailsClickable;
        LoginClickableBox.IsChecked = s.LoginScreensClickable;
        LockBox.IsChecked = s.LockPositions;
        SnapBox.IsChecked = s.SnapEnabled;
        OnTopBox.IsChecked = s.AlwaysOnTop;
        SnapDistBox.Text = s.SnapDistance.ToString();
        RefreshBox.Text = s.RefreshMilliseconds.ToString();

        // Appearance
        OpacitySlider.Value = Math.Clamp(s.ThumbnailOpacityPercent, 10, 100);
        LabelsBox.IsChecked = s.ShowLabels;
        if (_fontsLoaded)
        {
            var fonts = (FontBox.ItemsSource as IEnumerable<string>) ?? Array.Empty<string>();
            FontBox.SelectedItem =
                fonts.FirstOrDefault(f => f.Equals(s.LabelFontName, StringComparison.OrdinalIgnoreCase)) ??
                fonts.FirstOrDefault(f => f.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            FontBox.ItemsSource = new List<string> { string.IsNullOrWhiteSpace(s.LabelFontName) ? "Segoe UI" : s.LabelFontName };
            FontBox.SelectedIndex = 0;
        }
        FontSizeBox.Text = s.LabelFontSize.ToString();
        BoldBox.IsChecked = s.LabelBold;
        LabelColourBox.Hex = s.DefaultLabelColourHex;
        LabelOpacitySlider.Value = Math.Clamp(s.LabelOpacityPercent, 10, 100);
        LabelBgBox.Hex = s.LabelBackgroundHex;
        LabelBgOpacitySlider.Value = Math.Clamp(s.LabelBackgroundOpacityPercent, 0, 100);
        LabelPositionBox.SelectedItem = Positions.Contains(s.LabelPosition) ? s.LabelPosition : "TopLeft";
        OffsetXBox.Text = s.LabelOffsetX.ToString();
        OffsetYBox.Text = s.LabelOffsetY.ToString();
        HighlightBox.IsChecked = s.HighlightActive;
        ColourBox.Hex = s.ActiveBorderHex;
        ActiveWidthBox.Text = s.ActiveBorderWidth.ToString();
        ActiveStyleBox.SelectedItem = BorderStyles.Contains(s.ActiveBorderStyle) ? s.ActiveBorderStyle : "Solid";
        InactiveBox.IsChecked = s.ShowInactiveBorder;
        InactiveColourBox.Hex = s.InactiveBorderHex;
        InactiveWidthBox.Text = s.InactiveBorderWidth.ToString();
        InactiveStyleBox.SelectedItem = BorderStyles.Contains(s.InactiveBorderStyle) ? s.InactiveBorderStyle : "Solid";
        UpdateSliderLabels();

        // Thumbnails
        LoginScreensBox.IsChecked = s.ShowLoginScreens;
        LoadThumbGroups();
        HideActiveBox.IsChecked = s.HideActiveThumbnail;
        HideUnfocusedBox.IsChecked = s.HideWhenEveNotFocused;
        HideDelayBox.Text = s.HideDelayMs.ToString();

        // Hotkeys
        ForwardBox.Text = s.CycleForwardHotkey;
        BackBox.Text = s.CycleBackHotkey;
        CycleLoginBox.IsChecked = s.CycleIncludeLoginScreens;
        _orderLoading = true;
        try
        {
            (s.ClientOrderMode == AppSettings.OrderManual ? OrderManualRadio :
             s.ClientOrderMode == AppSettings.OrderSaved ? OrderSavedRadio : OrderAutoRadio).IsChecked = true;
        }
        finally { _orderLoading = false; }
        UpdateOrderNote();
        HotkeysOnlyEveBox.IsChecked = s.HotkeysOnlyInEve;
        PassThroughBox.IsChecked = s.HotkeyPassThrough;
        HotkeysInConfigBox.IsChecked = s.HotkeysInConfig;
        LoadSlotGroups();
        ProfileHotkeyBox.Text = _profileHotkeys.TryGetValue(_shownProfile, out var hotkey) ? hotkey : "";

        // Clients
        AutoMinBox.IsChecked = s.AutoMinimiseInactive;
        KeepLastBox.IsChecked = s.AutoMinimiseKeepLastActive;
        MinOnStartBox.IsChecked = s.AutoMinimiseOnStart;
        AutoMinDelayBox.Text = s.AutoMinimiseDelaySeconds.ToString();

        // Notifications
        NotifyEnabledBox.IsChecked = s.NotificationsEnabled;
        NotifySkipActiveBox.IsChecked = s.NotifySkipActiveClient;
        NotifyCooldownBox.Text = s.NotifyCooldownSeconds.ToString();
        NotifyFlashBox.IsChecked = s.NotifyFlashBorder;
        NotifyColourBox.Hex = s.NotifyBorderHex;
        NotifySecondsBox.Text = s.NotifyBorderSeconds.ToString();
        NotifyToastBox.IsChecked = s.NotifyToast;
        NotifySoundBox.IsChecked = s.NotifySound;
        NotifySoundCombo.SelectedItem = NotificationService.SoundNames.Contains(s.NotifySoundName) ? s.NotifySoundName : "Exclamation";
        NotifySoundFileBox.Text = s.NotifySoundFile;
        NotifySpeakBox.IsChecked = s.NotifySpeak;

        // Log monitoring
        LogEnabledBox.IsChecked = s.LogMonitoringEnabled;
        LogPollBox.Text = s.LogPollMilliseconds.ToString();
        GameFolderBox.Text = s.GameLogFolder;
        ChatFolderBox.Text = s.ChatLogFolder;
        LogAttackBox.IsChecked = s.LogWatchAttack;
        LogCrystalBox.IsChecked = s.LogWatchCrystalBroke;
        LogFleetFollowBox.IsChecked = s.LogWatchFleetFollow;
        LogCompressionBox.IsChecked = s.LogWatchMiningCompression;
        LogConversationBox.IsChecked = s.LogWatchConversationInvite;
        LogDecloakBox.IsChecked = s.LogWatchDecloak;
        LogMiningBox.IsChecked = s.LogWatchMiningIdle;
        LogRegroupBox.IsChecked = s.LogWatchFleetRegroup;
        LogMiningSecondsBox.Text = s.LogMiningIdleSeconds.ToString();
        LogKeywordsBox.Text = (s.LogKeywords ?? "").Replace("\r", "").Replace("\n", Environment.NewLine);
        LogSystemBox.IsChecked = s.ShowSystemName;
        LogStatusText.Text = _manager.LogStatus;

        CharColourBox.FallbackHex = ColourField.TryParse(LabelColourBox.Hex, out _) ? LabelColourBox.Hex : "#FFFFFF";
        RebuildCharacterLists();
        UpdatePreview();
    }

    /// <summary>Builds the list of installed fonts the first time the font box is used.</summary>
    private void EnsureFonts()
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;
        string? current = FontBox.SelectedItem as string;
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        FontBox.ItemsSource = fonts;
        FontBox.SelectedItem =
            fonts.FirstOrDefault(f => f.Equals(current, StringComparison.OrdinalIgnoreCase)) ??
            fonts.FirstOrDefault(f => f.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateSliderLabels()
    {
        if (OpacityLabel is null || OpacitySlider is null || BgOpacityLabel is null || LabelBgOpacitySlider is null) return;
        if (LabelOpacityLabel is not null && LabelOpacitySlider is not null)
            LabelOpacityLabel.Text = "Text Opacity: " + (int)Math.Round(LabelOpacitySlider.Value) + "%";
        OpacityLabel.Text = "Thumbnail Opacity: " + (int)Math.Round(OpacitySlider.Value) + "%";
        BgOpacityLabel.Text = "Background Opacity: " + (int)Math.Round(LabelBgOpacitySlider.Value) + "%";
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSliderLabels();
    private void LabelOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderLabels();
        UpdatePreview();
    }
    private void CharLabelStyle_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();
    private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSliderLabels();

    // ---------- client hotkey groups ----------

    private string? AskGroupName(string title, string initial, IEnumerable<string> existing, string current = "")
    {
        string? raw = PromptWindow.Ask(this, title, "Group name:", initial);
        if (raw is null) return null;
        string name = raw.Trim();
        if (name.Length == 0)
        {
            Warn("Please type a name.");
            return null;
        }
        if (name.Length > 40)
        {
            Warn("Please keep the name to 40 characters or fewer.");
            return null;
        }
        if (existing.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase) && !n.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            Warn("There's already a group called \"" + name + "\".");
            return null;
        }
        return name;
    }

    /// <summary>Fills the group drop-down, shows the group in use and builds its lines.</summary>
    private void LoadSlotGroups()
    {
        _work.EnsureGroups();
        _groupLoading = true;
        try
        {
            SlotGroupBox.Items.Clear();
            foreach (var g in _work.ClientHotkeyGroups) SlotGroupBox.Items.Add(g.Name);
            _shownSlotGroup = _work.FindSlotGroup(_work.ActiveClientHotkeyGroup);
            SlotGroupBox.SelectedItem = _shownSlotGroup is null
                ? null
                : SlotGroupBox.Items.Cast<string>().FirstOrDefault(n => n.Equals(_shownSlotGroup.Name, StringComparison.OrdinalIgnoreCase));
        }
        finally { _groupLoading = false; }
        DeleteSlotGroupBtn.IsEnabled = _work.ClientHotkeyGroups.Count > 1;
        BuildSlotRows();
    }

    /// <summary>One line per client: "Client 1" with its hotkey box. A group starts with just the first line; + adds more.</summary>
    private void BuildSlotRows()
    {
        SlotHotkeyHost.Children.Clear();
        _slotFields.Clear();
        var group = _shownSlotGroup;
        if (group is null) return;

        int highest = 0;
        foreach (string key in group.Hotkeys.Keys)
        {
            if (int.TryParse(key, out int n) && n > highest) highest = n;
        }
        int rows = Math.Clamp(Math.Max(group.Rows, highest), 1, MaxSlotRows);

        for (int i = 1; i <= rows; i++)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "Client " + i,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var field = new HotkeyField { Text = group.Hotkeys.TryGetValue(i.ToString(), out var key) ? key : "" };
            Grid.SetColumn(field, 1);
            row.Children.Add(label);
            row.Children.Add(field);
            SlotHotkeyHost.Children.Add(row);
            _slotFields.Add(field);
        }

        AddSlotBtn.IsEnabled = rows < MaxSlotRows;
        RemoveSlotBtn.IsEnabled = rows > 1;
    }

    /// <summary>Stores what is typed in the lines into the group on screen.</summary>
    private void CommitSlotGroup()
    {
        var group = _shownSlotGroup;
        if (group is null) return;
        group.Hotkeys = new Dictionary<string, string>();
        for (int i = 0; i < _slotFields.Count; i++)
        {
            if (_slotFields[i].Text.Length > 0) group.Hotkeys[(i + 1).ToString()] = _slotFields[i].Text;
        }
        group.Rows = Math.Max(1, _slotFields.Count);
    }

    private void SlotGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_groupLoading || _work is null || SlotGroupBox.SelectedItem is not string name) return;
        var group = _work.FindSlotGroup(name);
        if (group is null || ReferenceEquals(group, _shownSlotGroup)) return;
        CommitSlotGroup();
        _work.ActiveClientHotkeyGroup = group.Name;
        _shownSlotGroup = group;
        BuildSlotRows();
        SetStatus($"Client hotkey group “{group.Name}” is in use. Press Apply or Save to use it.", false);
    }

    private void AddSlot_Click(object sender, RoutedEventArgs e)
    {
        var group = _shownSlotGroup;
        if (group is null) return;
        CommitSlotGroup();
        if (group.Rows >= MaxSlotRows) return;
        group.Rows++;
        BuildSlotRows();
        if (_slotFields.Count > 0) _slotFields[_slotFields.Count - 1].Focus();
    }

    private void RemoveSlot_Click(object sender, RoutedEventArgs e)
    {
        var group = _shownSlotGroup;
        if (group is null) return;
        CommitSlotGroup();
        if (group.Rows <= 1) return;
        group.Hotkeys.Remove(group.Rows.ToString());
        group.Rows--;
        BuildSlotRows();
    }

    private void NewSlotGroup_Click(object sender, RoutedEventArgs e)
    {
        string? name = AskGroupName("New client hotkey group", "", _work.ClientHotkeyGroups.Select(g => g.Name));
        if (name is null) return;
        CommitSlotGroup();
        _work.ClientHotkeyGroups.Add(new SlotHotkeyGroup { Name = name });
        _work.ActiveClientHotkeyGroup = name;
        LoadSlotGroups();
        SetStatus($"Group “{name}” created and in use. Press Apply or Save to use it.", false);
    }

    private void RenameSlotGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = _shownSlotGroup;
        if (group is null) return;
        string? name = AskGroupName("Rename client hotkey group", group.Name, _work.ClientHotkeyGroups.Select(g => g.Name), group.Name);
        if (name is null) return;
        CommitSlotGroup();
        group.Name = name;
        _work.ActiveClientHotkeyGroup = name;
        LoadSlotGroups();
    }

    private void DeleteSlotGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = _shownSlotGroup;
        if (group is null) return;
        if (_work.ClientHotkeyGroups.Count <= 1)
        {
            Warn("There has to be at least one group.");
            return;
        }
        if (Dialogs.Ask(this, "Delete group", "Delete the client hotkey group \"" + group.Name + "\" and its hotkeys?", "Delete", "Cancel") != 0) return;
        CommitSlotGroup();
        _work.ClientHotkeyGroups.Remove(group);
        _work.ActiveClientHotkeyGroup = _work.ClientHotkeyGroups[0].Name;
        LoadSlotGroups();
        SetStatus("Group deleted. Press Apply or Save to use it.", false);
    }

    // ---------- the character lists ----------

    /// <summary>Rebuilds every list that shows characters: the Thumbnails list, the skip and never-minimise ticks and the Characters picker.</summary>
    private void RebuildCharacterLists()
    {
        _characters = _work.AllGroupCharacters()
            .Concat(_work.Characters.Keys.Where(AppSettings.IsCharacterKey))
            .Concat(_running)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        SkipChecks.Children.Clear();
        MinChecks.Children.Clear();
        BuildThumbRows(0);

        RebuildOrderList();
        BuildTickList(SkipChecks, c => c.ExcludeFromCycling, (c, v) => c.ExcludeFromCycling = v);
        BuildTickList(MinChecks, c => c.NeverMinimise, (c, v) => c.NeverMinimise = v);

        bool any = _characters.Count > 0;
        NoSkipText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        NoMinText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        NoCharText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        NoCharText2.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        _loading = true;
        _currentCharacter = null;
        CharBox.Items.Clear();
        CharBox2.Items.Clear();
        foreach (string name in _characters)
        {
            CharBox.Items.Add(name);
            CharBox2.Items.Add(name);
        }
        _loading = false;

        CharBox.IsEnabled = any;
        CharBox2.IsEnabled = any;
        SetCharacterFieldsEnabled(any);
        if (any)
        {
            CharBox.SelectedIndex = 0;
        }
        else
        {
            LabelBox.Text = "";
            CharColourBox.Hex = "";
            CharLabelSizeBox.Text = "";
            CharLabelOpacityBox.Text = "";
            CharWidthBox.Text = "";
            CharHeightBox.Text = "";
            CharHotkeyBox.Text = "";
            CharActionBox.SelectedIndex = 0;
            CharMinLeaveBox.IsChecked = false;
        }
    }

    // ---------- client order ----------

    private static string ModeOfRadios(bool manual, bool saved) =>
        manual ? AppSettings.OrderManual : saved ? AppSettings.OrderSaved : AppSettings.OrderAutomatic;

    private void OrderMode_Changed(object sender, RoutedEventArgs e)
    {
        UpdateOrderNote();
        if (_orderLoading || _work is null) return;
        _work.ClientOrderMode = ModeOfRadios(OrderManualRadio.IsChecked == true, OrderSavedRadio.IsChecked == true);
        RebuildOrderList();
    }

    private void PositionMode_Changed(object sender, RoutedEventArgs e) => UpdateOrderNote();

    /// <summary>Positions by client + Automatic order is the one pairing where thumbnails change place when a client closes and reopens: say so.</summary>
    private void UpdateOrderNote()
    {
        if (OrderPositionNote is null || ModeClientRadio is null || OrderAutoRadio is null) return;
        OrderPositionNote.Visibility = ModeClientRadio.IsChecked == true && OrderAutoRadio.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Known characters that aren't in the order yet: the logged-in ones first (in client order), then the rest A-Z.</summary>
    private List<string> PendingOrderCharacters()
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var order = _work.ClientOrder;
        var others = _work.AllGroupCharacters()
            .Concat(_work.Characters.Keys.Where(AppSettings.IsCharacterKey))
            .Concat(_work.SlotCharacters.Values.Where(v => !string.IsNullOrWhiteSpace(v)))
            .Except(_runningList, cmp)
            .Distinct(cmp)
            .OrderBy(n => n, cmp);
        return _runningList.Concat(others)
            .Distinct(cmp)
            .Where(n => !order.Contains(n, cmp))
            .ToList();
    }

    private void RebuildOrderList()
    {
        if (OrderChecks is null || _work is null) return;
        OrderChecks.Children.Clear();
        OrderListPanel.Visibility = _work.ClientOrderMode == AppSettings.OrderManual ? Visibility.Visible : Visibility.Collapsed;
        OrderSavedPanel.Visibility = _work.ClientOrderMode == AppSettings.OrderSaved ? Visibility.Visible : Visibility.Collapsed;
        UpdateCapturePanel();
        if (_work.ClientOrderMode == AppSettings.OrderSaved) UpdateSavedOrderPanel();
        if (_work.ClientOrderMode != AppSettings.OrderManual) return;

        var order = _work.ClientOrder;
        for (int i = 0; i < order.Count; i++)
        {
            int index = i;
            string name = order[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new CheckBox
            {
                Content = Ui.SelectableLabel($"{i + 1}.  {name}" + (_running.Contains(name) ? "   (logged in)" : "")),
                IsChecked = true,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Untick to take this character out of the order"
            };
            box.Click += (_, _) =>
            {
                if (box.IsChecked != true) order.RemoveAt(index);
                RebuildOrderList();
            };

            var up = new Button { Content = "▲", FontSize = 11, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), IsEnabled = index > 0, ToolTip = "Move up" };
            up.Click += (_, _) => MoveInOrder(index, -1);
            var down = new Button { Content = "▼", FontSize = 11, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(4, 0, 0, 0), IsEnabled = index < order.Count - 1, ToolTip = "Move down" };
            down.Click += (_, _) => MoveInOrder(index, 1);

            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);
            row.Children.Add(box);
            row.Children.Add(up);
            row.Children.Add(down);
            OrderChecks.Children.Add(row);
        }

        // known characters that aren't part of the order yet: shown unticked, and only counted once ticked
        foreach (string name in PendingOrderCharacters())
        {
            string chosen = name;
            var box = new CheckBox
            {
                Content = Ui.SelectableLabel(name + (_running.Contains(name) ? "   (logged in)" : "") + "   (not in the order)"),
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Tick to add this character to the end of the order"
            };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true && !order.Contains(chosen, StringComparer.OrdinalIgnoreCase)) order.Add(chosen);
                RebuildOrderList();
            };
            OrderChecks.Children.Add(box);
        }

        NoOrderText.Visibility = OrderChecks.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The "Saved" order as it is right now (it builds itself while the window is open), and the state of "Reset order".</summary>
    private void UpdateSavedOrderPanel()
    {
        bool isActive = _shownProfile.Equals(_activeProfile, StringComparison.OrdinalIgnoreCase);
        var order = isActive ? _manager.Settings.ClientOrder : _work.ClientOrder;
        SavedOrderText.Text = order.Count == 0
            ? "Nothing saved yet. It builds up by itself as your clients log in."
            : string.Join("\n", order.Select((n, i) => $"{i + 1}.  {n}" + (_running.Contains(n) ? "   (logged in)" : "")));

        ResetOrderBtn.IsEnabled = isActive;
        OrderResetNote.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
        OrderResetNote.Text = "Switch to this profile first: Reset order works on the profile that is running.";
    }

    /// <summary>The order was changed in the running app (Save current order / Reset order): this window's copy and its "nothing unsaved" baseline follow.</summary>
    private void SyncOrderFromManager(IReadOnlyList<string> order)
    {
        _work.ClientOrder = new List<string>(order);
        _work.SavedAccountSlots = new Dictionary<string, int>(_manager.Settings.SavedAccountSlots, StringComparer.OrdinalIgnoreCase);
        if (!_baseline.TryGetValue(_shownProfile, out var json)) return;
        try
        {
            var baseline = JsonSerializer.Deserialize<AppSettings>(json);
            if (baseline is null) return;
            baseline.ClientOrder = new List<string>(order);
            baseline.SavedAccountSlots = new Dictionary<string, int>(_manager.Settings.SavedAccountSlots, StringComparer.OrdinalIgnoreCase);
            _baseline[_shownProfile] = ToJson(baseline);
        }
        catch { }
    }

    /// <summary>"Save current order" (Automatic and Saved mode): what is stored right now, and whether the button can be used.</summary>
    private void UpdateCapturePanel()
    {
        if (OrderCapturePanel is null || _work is null) return;
        OrderCapturePanel.Visibility = _work.ClientOrderMode == AppSettings.OrderManual ? Visibility.Collapsed : Visibility.Visible;
        bool isActive = _shownProfile.Equals(_activeProfile, StringComparison.OrdinalIgnoreCase);
        var stored = isActive ? _manager.Settings.ClientOrder : _work.ClientOrder;
        SaveOrderBtn.IsEnabled = isActive;
        SaveOrderNote.Text = !isActive
            ? "Switch to this profile first: Save current order works on the profile that is running."
            : stored.Count == 0
                ? "Nothing is stored yet."
                : "Stored right now:  " + string.Join(",  ", stored.Select((n, i) => $"{i + 1}. {n}"));
    }

    private void SaveCurrentOrder_Click(object sender, RoutedEventArgs e)
    {
        var saved = _manager.SaveCurrentClientOrder();
        if (saved.Count == 0)
        {
            SetStatus("No logged-in EVE clients are open, so there is no order to save. Log your characters in, then try again.", false);
            return;
        }
        SyncOrderFromManager(saved);
        RebuildOrderList();
        SetStatus(_work.ClientOrderMode == AppSettings.OrderSaved
            ? $"Saved the order of {saved.Count} client(s)."
            : $"Saved the order of {saved.Count} client(s). Choose Saved above and press Apply or Save to use it.", true);
    }

    private void ResetOrder_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Ask(this, "Reset client order",
                "Clear the saved client order now?\n\nThe clients that are open right now are put back in the order they were opened. Any character that isn't open is forgotten.",
                "Reset", "Cancel") != 0) return;
        int cleared = _manager.ClearClientOrder();
        var now = _manager.Settings.ClientOrder;
        SyncOrderFromManager(now);
        _running = new HashSet<string>(_manager.RunningCharacters(), StringComparer.OrdinalIgnoreCase);
        RebuildOrderList();
        SetStatus(now.Count == 0
            ? $"Saved order cleared ({cleared} name(s) removed). No clients are open, so it builds again as they open."
            : $"Saved order cleared ({cleared} name(s) removed) and rebuilt from the {now.Count} open client(s), in the order they were opened.", true);
    }

    private void MoveInOrder(int index, int direction)
    {
        var order = _work.ClientOrder;
        int to = index + direction;
        if (index < 0 || index >= order.Count || to < 0 || to >= order.Count) return;
        (order[index], order[to]) = (order[to], order[index]);
        RebuildOrderList();
    }

    private void OrderTickAll_Click(object sender, RoutedEventArgs e)
    {
        _work.ClientOrder.AddRange(PendingOrderCharacters());
        RebuildOrderList();
    }

    private void OrderUntickAll_Click(object sender, RoutedEventArgs e)
    {
        _work.ClientOrder.Clear();
        RebuildOrderList();
    }

    private void BuildTickList(StackPanel host, Func<CharacterSettings, bool> get, Action<CharacterSettings, bool> set)
    {
        foreach (string name in _characters)
        {
            string chosen = name;
            var existing = _work.TryGetCharacter(name);
            var box = new CheckBox
            {
                Content = Ui.SelectableLabel(_running.Contains(name) ? name + "   (logged in)" : name),
                IsChecked = existing is not null && get(existing)
            };
            box.Click += (_, _) => set(_work.GetOrAddCharacter(chosen), box.IsChecked == true);
            host.Children.Add(box);
        }
    }

    /// <summary>Keeps what was typed for the current character before the lists are rebuilt (invalid input is ignored).</summary>
    private void CommitCurrentQuietly()
    {
        bool before = _quiet;
        _quiet = true;
        try { CommitCharacter(); }
        finally { _quiet = before; }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllShown(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAllShown(false);

    private void SetAllShown(bool shown)
    {
        LoginScreensBox.IsChecked = shown;
        foreach (var row in _thumbRows) row.Show.IsChecked = shown;       // stored when you press Apply or Save
    }

    // ---------- thumbnail groups ----------

    /// <summary>Fills the group drop-down and shows the group in use. (The lines are built by RebuildCharacterLists.)</summary>
    private void LoadThumbGroups()
    {
        _work.EnsureGroups();
        _groupLoading = true;
        try
        {
            ThumbGroupBox.Items.Clear();
            foreach (var g in _work.ThumbnailGroups) ThumbGroupBox.Items.Add(g.Name);
            _shownThumbGroup = _work.FindThumbGroup(_work.ActiveThumbnailGroup);
            ThumbGroupBox.SelectedItem = _shownThumbGroup is null
                ? null
                : ThumbGroupBox.Items.Cast<string>().FirstOrDefault(n => n.Equals(_shownThumbGroup.Name, StringComparison.OrdinalIgnoreCase));
        }
        finally { _groupLoading = false; }
        ShowUnlistedBox.IsChecked = _shownThumbGroup?.ShowOthers ?? true;
        DeleteThumbGroupBtn.IsEnabled = _work.ThumbnailGroups.Count > 1;
    }

    /// <summary>One line per character in the group. A group with nobody in it starts with a single empty line; + adds more.</summary>
    private void BuildThumbRows(int extraBlank)
    {
        ClientChecks.Children.Clear();
        _thumbRows.Clear();
        var group = _shownThumbGroup;
        if (group is null) return;

        foreach (string name in group.Characters) AddThumbRow(name);
        int blanks = Math.Max(extraBlank, group.Characters.Count == 0 ? 1 : 0);
        for (int i = 0; i < blanks; i++) AddThumbRow("");
        AddThumbBtn.IsEnabled = _thumbRows.Count < MaxThumbRows;
    }

    private ThumbRow AddThumbRow(string name)
    {
        var existing = name.Length == 0 ? null : _work.TryGetCharacter(name);

        var panel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var show = new CheckBox
        {
            IsChecked = !(existing?.Hidden ?? false),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Show this character's thumbnail"
        };
        var box = new TextBox { Text = name, Margin = new Thickness(0), ToolTip = "Character name" };
        var tag = new TextBlock
        {
            Text = name.Length > 0 && _running.Contains(name) ? "logged in" : "",
            FontSize = 11,
            Foreground = Theme.Success,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var remove = new Button
        {
            Content = "✕",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Take this character out of the group"
        };

        var row = new ThumbRow { Panel = panel, Show = show, Name = box };
        remove.Click += (_, _) => RemoveThumbRow(row);

        Grid.SetColumn(box, 1);
        Grid.SetColumn(tag, 2);
        Grid.SetColumn(remove, 3);
        panel.Children.Add(show);
        panel.Children.Add(box);
        panel.Children.Add(tag);
        panel.Children.Add(remove);
        ClientChecks.Children.Add(panel);
        _thumbRows.Add(row);
        return row;
    }

    /// <summary>
    /// Stores the lines into the group on screen. With validate = true a bad name is reported (and false returned);
    /// otherwise a bad name is just left out.
    /// </summary>
    private bool CommitThumbRows(bool validate)
    {
        var group = _shownThumbGroup;
        if (group is null) return true;

        var names = new List<string>();
        foreach (var row in _thumbRows)
        {
            string name = row.Name.Text.Trim();
            if (name.Length == 0) continue;
            if (name.Length > 60 || name.StartsWith('['))
            {
                if (validate) return Fail(PageGroups_, "“" + name + "” isn't a valid character name.");
                continue;
            }
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            names.Add(name);

            if (row.Show.IsChecked != true) _work.GetOrAddCharacter(name).Hidden = true;
            else if (_work.TryGetCharacter(name) is { } known) known.Hidden = false;
        }

        group.Characters = names;
        group.ShowOthers = ShowUnlistedBox.IsChecked == true;
        return true;
    }

    private void AddThumb_Click(object sender, RoutedEventArgs e)
    {
        if (_thumbRows.Count >= MaxThumbRows) return;
        var row = AddThumbRow("");
        AddThumbBtn.IsEnabled = _thumbRows.Count < MaxThumbRows;
        row.Name.Focus();
    }

    private void RemoveThumbRow(ThumbRow row)
    {
        _thumbRows.Remove(row);
        ClientChecks.Children.Remove(row.Panel);
        CommitThumbRows(false);
        CommitCurrentQuietly();
        RebuildCharacterLists();
        SetStatus("Taken out of the group. Press Apply or Save to use it.", false);
    }

    private string ThumbSignature() => string.Join("|", _work.AllGroupCharacters());

    /// <summary>Leaving the Characters page: if the names changed, the other lists (skip, never minimise, characters, order) follow.</summary>
    private void RefreshAfterThumbEdit()
    {
        string before = ThumbSignature();
        CommitThumbRows(false);
        if (ThumbSignature() == before) return;
        CommitCurrentQuietly();
        RebuildCharacterLists();
    }

    private void ThumbGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_groupLoading || _work is null || ThumbGroupBox.SelectedItem is not string name) return;
        var group = _work.FindThumbGroup(name);
        if (group is null || ReferenceEquals(group, _shownThumbGroup)) return;
        CommitThumbRows(false);
        _work.ActiveThumbnailGroup = group.Name;
        _shownThumbGroup = group;
        ShowUnlistedBox.IsChecked = group.ShowOthers;
        CommitCurrentQuietly();
        RebuildCharacterLists();
        SetStatus($"Thumbnail group “{group.Name}” is in use. Press Apply or Save to use it.", false);
    }

    private void NewThumbGroup_Click(object sender, RoutedEventArgs e)
    {
        string? name = AskGroupName("New thumbnail group", "", _work.ThumbnailGroups.Select(g => g.Name));
        if (name is null) return;
        CommitThumbRows(false);
        _work.ThumbnailGroups.Add(new ThumbnailGroup { Name = name });
        _work.ActiveThumbnailGroup = name;
        LoadThumbGroups();
        CommitCurrentQuietly();
        RebuildCharacterLists();
        SetStatus($"Group “{name}” created and in use. Add its characters, then press Apply or Save.", false);
    }

    private void RenameThumbGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = _shownThumbGroup;
        if (group is null) return;
        string? name = AskGroupName("Rename thumbnail group", group.Name, _work.ThumbnailGroups.Select(g => g.Name), group.Name);
        if (name is null) return;
        CommitThumbRows(false);
        group.Name = name;
        _work.ActiveThumbnailGroup = name;
        LoadThumbGroups();
        CommitCurrentQuietly();
        RebuildCharacterLists();
    }

    private void DeleteThumbGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = _shownThumbGroup;
        if (group is null) return;
        if (_work.ThumbnailGroups.Count <= 1)
        {
            Warn("There has to be at least one group.");
            return;
        }
        if (Dialogs.Ask(this, "Delete group", "Delete the thumbnail group \"" + group.Name + "\"? The characters' own settings are kept.", "Delete", "Cancel") != 0) return;
        _work.ThumbnailGroups.Remove(group);
        _work.ActiveThumbnailGroup = _work.ThumbnailGroups[0].Name;
        LoadThumbGroups();
        CommitCurrentQuietly();
        RebuildCharacterLists();
        SetStatus("Group deleted. Press Apply or Save to use it.", false);
    }

    // ---------- scanning for running clients ----------

    private bool AddToShownGroup(string name)
    {
        var group = _shownThumbGroup;
        if (group is null || name.Length == 0 || name.Length > 60 || name.StartsWith('[')) return false;
        if (group.Characters.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
        group.Characters.Add(name);
        return true;
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => RunScan();

    private void RunScan()
    {
        CommitThumbRows(false);                       // keep what is typed in the lines
        var found = _manager.ScanCharacters();
        _running = new HashSet<string>(found, StringComparer.OrdinalIgnoreCase);

        ScanHost.Children.Clear();
        _scanBoxes.Clear();
        foreach (string name in found)
        {
            bool already = _shownThumbGroup?.Characters.Contains(name, StringComparer.OrdinalIgnoreCase) ?? false;
            var box = new CheckBox
            {
                Content = Ui.SelectableLabel(already ? name + "   (already in the group)" : name),
                IsChecked = !already,
                IsEnabled = !already,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ScanHost.Children.Add(box);
            _scanBoxes.Add((name, box));
        }

        ScanNote.Text = found.Count == 0
            ? "No logged-in clients were found. Log a character in (a client still on the login screen has no name yet) and scan again."
            : $"Found {found.Count} logged-in client(s). Tick the ones to add.";

        // refresh the "(logged in)" markers on the lists
        CommitCurrentQuietly();
        RebuildCharacterLists();
    }

    private void ScanSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (_, box) in _scanBoxes)
        {
            if (box.IsEnabled) box.IsChecked = true;
        }
    }

    private void AddScanned_Click(object sender, RoutedEventArgs e)
    {
        int added = 0;
        CommitThumbRows(false);
        foreach (var (name, box) in _scanBoxes)
        {
            if (box.IsEnabled && box.IsChecked == true && AddToShownGroup(name)) added++;
        }
        if (added == 0)
        {
            SetStatus(_scanBoxes.Count == 0 ? "Press Scan first." : "Nothing ticked to add.", false);
            return;
        }

        CommitCurrentQuietly();
        RebuildCharacterLists();
        RunScan();
        SetStatus($"Added {added} character(s) to the group. Press Apply or Save to use them.", false);
    }

    // ---------- characters page ----------

    private void SetCharacterFieldsEnabled(bool enabled)
    {
        LabelBox.IsEnabled = enabled;
        CharColourBox.IsEnabled = enabled;
        CharLabelSizeBox.IsEnabled = enabled;
        CharLabelOpacityBox.IsEnabled = enabled;
        CharWidthBox.IsEnabled = enabled;
        CharHeightBox.IsEnabled = enabled;
        CharHotkeyBox.IsEnabled = enabled;
        CharActionBox.IsEnabled = enabled;
        CharMinLeaveBox.IsEnabled = enabled;
    }

    /// <summary>Shows the label the way it will look: its text in its colour.</summary>
    private void UpdatePreview()
    {
        if (LabelPreview is null || LabelBox is null || CharColourBox is null || LabelColourBox is null || CharLabelSizeBox is null || CharLabelOpacityBox is null || LabelOpacitySlider is null || FontSizeBox is null) return;
        string text = LabelBox.Text.Trim();
        if (text.Length == 0) text = _currentCharacter ?? "Character name";
        LabelPreview.Text = text;
        string hex = CharColourBox.Hex.Length > 0 ? CharColourBox.Hex : LabelColourBox.Hex;
        // the size and opacity this character will really get: its own value if it has one, otherwise the default
        int ownOpacity = int.TryParse(CharLabelOpacityBox.Text.Trim(), out int o) && o >= 10 && o <= 100 ? o : 0;
        int opacity = ownOpacity > 0 ? ownOpacity : (int)Math.Round(LabelOpacitySlider.Value);
        int ownSize = int.TryParse(CharLabelSizeBox.Text.Trim(), out int z) && z >= 6 && z <= 72 ? z : 0;
        int size = ownSize > 0 ? ownSize : (int.TryParse(FontSizeBox.Text.Trim(), out int d) && d >= 6 && d <= 72 ? d : 13);
        LabelPreview.FontSize = Math.Min(size, 40);                // the preview box stays a sensible size for very large text
        LabelPreview.Foreground = ColourField.TryParse(hex, out var colour)
            ? Ui.BrushWithOpacity(hex, opacity, Brushes.White)
            : Brushes.White;
    }

    private void LabelBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void CharBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        // the Thumbnails page and the Hotkeys page each have a character picker; they always show the same character
        var box = sender as ComboBox ?? CharBox;
        var other = ReferenceEquals(box, CharBox) ? CharBox2 : CharBox;

        // keep what was typed for the previous character; if it's invalid, stay on it (and go to the page with the bad field)
        if (_currentCharacter is not null && !CommitCharacter())
        {
            _loading = true;
            box.SelectedItem = _currentCharacter;
            _loading = false;
            if (!_quiet) SelectPage(_charFailPage);
            return;
        }

        _currentCharacter = box.SelectedItem as string;
        _loading = true;
        other.SelectedItem = _currentCharacter;
        _loading = false;
        if (_currentCharacter is null) return;
        var c = _work.TryGetCharacter(_currentCharacter) ?? new CharacterSettings();
        LabelBox.Text = c.Label;
        CharColourBox.Hex = c.LabelColourHex;
        CharLabelSizeBox.Text = c.LabelSize.ToString();
        CharLabelOpacityBox.Text = c.LabelOpacityPercent.ToString();
        CharWidthBox.Text = c.Width.ToString();
        CharHeightBox.Text = c.Height.ToString();
        CharHotkeyBox.Text = c.Hotkey;
        int action = Array.IndexOf(ActionValues, c.ActivateAction);
        CharActionBox.SelectedIndex = action < 0 ? 0 : action;
        CharMinLeaveBox.IsChecked = c.MinimiseOnLeave;
        UpdatePreview();
    }

    /// <summary>Validates and stores the per-character fields into the working copy.</summary>
    private bool CommitCharacter()
    {
        if (_currentCharacter is null) return true;
        string who = _currentCharacter + ": ";
        _charFailPage = PageThumbs_;

        if (!int.TryParse(string.IsNullOrWhiteSpace(CharWidthBox.Text) ? "0" : CharWidthBox.Text.Trim(), out int width) ||
            (width != 0 && (width < 80 || width > 1600)))
        {
            Warn(who + "width must be 0 (default) or 80 to 1600.");
            return false;
        }
        if (!int.TryParse(string.IsNullOrWhiteSpace(CharHeightBox.Text) ? "0" : CharHeightBox.Text.Trim(), out int height) ||
            (height != 0 && (height < 45 || height > 1200)))
        {
            Warn(who + "height must be 0 (automatic) or 45 to 1200.");
            return false;
        }
        if (!int.TryParse(string.IsNullOrWhiteSpace(CharLabelSizeBox.Text) ? "0" : CharLabelSizeBox.Text.Trim(), out int labelSize) ||
            (labelSize != 0 && (labelSize < 6 || labelSize > 72)))
        {
            Warn(who + "label size must be 0 (default) or 6 to 72.");
            return false;
        }
        if (!int.TryParse(string.IsNullOrWhiteSpace(CharLabelOpacityBox.Text) ? "0" : CharLabelOpacityBox.Text.Trim(), out int labelOpacity) ||
            (labelOpacity != 0 && (labelOpacity < 10 || labelOpacity > 100)))
        {
            Warn(who + "label opacity must be 0 (default) or 10 to 100.");
            return false;
        }
        if (!CharColourBox.IsValid)
        {
            Warn(who + "that isn't a valid colour. Use the Pick button, or type something like #F97316, or clear it.");
            return false;
        }
        if (!CharHotkeyBox.IsValid)
        {
            _charFailPage = PageHotkeys_;
            Warn(who + "that hotkey isn't supported. Click the box and press the keys again.");
            return false;
        }

        var c = _work.GetOrAddCharacter(_currentCharacter);
        c.Width = width;
        c.Height = height;
        c.Label = LabelBox.Text.Trim();
        c.LabelColourHex = CharColourBox.Hex;
        c.LabelSize = labelSize;
        c.LabelOpacityPercent = labelOpacity;
        c.Hotkey = CharHotkeyBox.Text.Trim();
        c.ActivateAction = CharActionBox.SelectedIndex is >= 0 and < 3 ? ActionValues[CharActionBox.SelectedIndex] : "None";
        c.MinimiseOnLeave = CharMinLeaveBox.IsChecked == true;
        return true;
    }

    // ---------- messages ----------

    private void Warn(string message)
    {
        if (!_quiet) Dialogs.Info(this, message);
    }

    // ---------- profile bar ----------

    private void RefreshProfileBox()
    {
        _loading = true;
        ProfileBox.Items.Clear();
        foreach (string name in ProfileStore.List()) ProfileBox.Items.Add(name);
        ProfileBox.SelectedItem = ProfileBox.Items.Cast<string>()
            .FirstOrDefault(n => n.Equals(_shownProfile, StringComparison.OrdinalIgnoreCase));
        _loading = false;
        ProfileNote.Visibility = _shownProfile.Equals(_activeProfile, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StoreProfileHotkeyEdit()
    {
        string text = ProfileHotkeyBox.Text.Trim();
        if (text.Length == 0) _profileHotkeys.Remove(_shownProfile);
        else _profileHotkeys[_shownProfile] = text;
    }

    /// <summary>Loads a profile into the controls without keeping anything from the one that was shown.</summary>
    private void ShowProfile(string name)
    {
        _shownProfile = name;
        if (!_edited.TryGetValue(name, out var settings))
        {
            settings = ProfileStore.Load(name);
            _edited[name] = settings;
        }
        _work = settings;
        LoadFrom();
        if (!_baseline.ContainsKey(name)) CaptureBaseline(name);
        RefreshProfileBox();
    }

    /// <summary>Checks and keeps the edits to the profile on screen, then shows another one.</summary>
    private bool SwitchShown(string name)
    {
        if (!TryApply()) return false;
        StoreProfileHotkeyEdit();
        _edited[_shownProfile] = _work;
        ShowProfile(name);
        return true;
    }

    /// <summary>
    /// The tray menu switched the running profile while this window is open: show the same profile here. Edits that are
    /// still on screen for the profile that was shown are kept (they stay unsaved, like when you change profile in the box).
    /// </summary>
    public void FollowActiveProfile(string name)
    {
        bool before = _quiet;
        _quiet = true;
        try { TryApply(); }                      // an invalid field is simply not kept: the switch never waits on it
        finally { _quiet = before; }
        StoreProfileHotkeyEdit();
        _edited[_shownProfile] = _work;

        _activeProfile = name;
        ShowProfile(name);
        SetStatus($"Switched to profile “{name}” from the tray menu.", true);
    }

    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedItem is not string name) return;
        if (name.Equals(_shownProfile, StringComparison.OrdinalIgnoreCase)) return;
        if (!SwitchShown(name)) RefreshProfileBox();   // invalid edits: stay on the current profile
    }

    private string? AskProfileName(string title, string initial)
    {
        string? raw = PromptWindow.Ask(this, title, "Profile name:", initial);
        if (raw is null) return null;
        string name = ProfileStore.CleanName(raw);
        if (name.Length == 0)
        {
            Warn("Please type a name.");
            return null;
        }
        if (ProfileStore.Exists(name))
        {
            Warn("There's already a profile called \"" + name + "\".");
            return null;
        }
        return name;
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        string? name = AskProfileName("New profile", "");
        if (name is null) return;
        ProfileStore.Save(name, new AppSettings());
        if (!SwitchShown(name)) RefreshProfileBox();
    }

    private void CopyProfile_Click(object sender, RoutedEventArgs e)
    {
        string? name = AskProfileName("Copy profile", _shownProfile + " copy");
        if (name is null) return;
        if (!TryApply()) return;                        // copy what's on screen now, not what was last saved
        ProfileStore.Save(name, _work.Clone());
        if (!SwitchShown(name)) RefreshProfileBox();
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        string old = _shownProfile;
        string? name = AskProfileName("Rename profile", old);
        if (name is null) return;

        if (!ProfileStore.Rename(old, name))
        {
            Warn("Couldn't rename that profile.");
            return;
        }
        if (_edited.Remove(old, out var settings)) _edited[name] = settings;
        if (_baseline.Remove(old, out var baseline)) _baseline[name] = baseline;
        if (_profileHotkeys.Remove(old, out var hotkey)) _profileHotkeys[name] = hotkey;
        if (_activeProfile.Equals(old, StringComparison.OrdinalIgnoreCase)) _activeProfile = name;
        _shownProfile = name;
        _manager.NotifyProfileRenamed(old, name);
        RefreshProfileBox();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = _shownProfile;
        if (name.Equals(_activeProfile, StringComparison.OrdinalIgnoreCase))
        {
            Warn("You can't delete the active profile. Switch to another profile and save first.");
            return;
        }
        if (Dialogs.Ask(this, "Delete profile", "Delete the profile \"" + name + "\"? This can't be undone.", "Delete", "Cancel") != 0) return;

        ProfileStore.Delete(name);
        _edited.Remove(name);
        _baseline.Remove(name);
        _profileHotkeys.Remove(name);
        ShowProfile(_activeProfile);
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a profile",
            Filter = "Supported profiles (*.json;*.ini)|*.json;*.ini|EVE-IW / EVE-Maj profiles (*.json)|*.json|EVE-APM profiles (*.ini)|*.ini"
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!ProfileImportService.TryImport(dialog.FileName, out var imported, out string error) || imported is null)
        {
            Warn(error.Length > 0 ? error : "That profile format isn't supported.");
            return;
        }
        string summary = $"{imported.Source} profile detected.\n\n" +
                         $"Characters: {imported.Characters}\n" +
                         $"Saved positions: {imported.Positions}\n" +
                         $"Groups: {imported.Groups}\n\n" +
                         "A new EVE-IW profile will be created. The original file and your existing profiles will not be changed.";
        if (Dialogs.Ask(this, "Import profile", summary, "Import", "Cancel") != 0) return;
        string? name = AskProfileName("Import profile", imported.SuggestedName);
        if (name is null) return;
        ProfileStore.Save(name, imported.Settings);
        if (!SwitchShown(name)) RefreshProfileBox();
        else SetStatus($"Imported {imported.Source} profile as “{name}”. Press Save to make it active.", true);
    }

    // ---------- general page buttons ----------

    private void Recover_Click(object sender, RoutedEventArgs e)
    {
        int moved = _manager.RecoverOffscreen(false);
        SetStatus(moved == 0 ? "No thumbnails were off-screen." : $"Moved {moved} thumbnail(s) back on screen.", true);
    }

    private void ResetPositions_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Ask(this, "Reset positions", "Put every thumbnail back in a stack down the left edge of the screen? This is saved straight away.", "Reset", "Cancel") != 0) return;
        _manager.ResetPositions();
        foreach (var c in _work.Characters.Values)
        {
            c.Left = null;
            c.Top = null;
        }
        SetStatus("Thumbnail positions were reset.", true);
    }

    // ---------- notifications and log monitoring ----------

    private void TestNotify_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApply()) return;
        _manager.TestNotification(_work.Clone());
        SetStatus("Test alert sent.", false);
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a sound", Filter = "Wave sounds (*.wav)|*.wav" };
        if (dialog.ShowDialog(this) != true) return;
        NotifySoundFileBox.Text = dialog.FileName;
        NotifySoundCombo.SelectedItem = "Custom";
    }

    private string? BrowseFolder(string current)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = current, ShowNewFolderButton = false };
        nint handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        return dialog.ShowDialog(new Win32Owner(handle)) == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        string? folder = BrowseFolder(string.IsNullOrWhiteSpace(GameFolderBox.Text) ? LogMonitorService.DefaultGameFolder : GameFolderBox.Text);
        if (folder is not null) GameFolderBox.Text = folder;
    }

    private void BrowseChat_Click(object sender, RoutedEventArgs e)
    {
        string? folder = BrowseFolder(string.IsNullOrWhiteSpace(ChatFolderBox.Text) ? LogMonitorService.DefaultChatFolder : ChatFolderBox.Text);
        if (folder is not null) ChatFolderBox.Text = folder;
    }

    // ---------- about ----------

    private void ViewChangelog_Click(object sender, RoutedEventArgs e) =>
        new ChangelogWindow(AppInfo.Version) { Owner = this, Topmost = false }.Show();

    private static void OpenPath(Window owner, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Info(owner, "Couldn't open " + path + "\n\n" + ex.Message);
        }
    }

    /// <summary>The "by AUTZ IronWorks" line opens the website in the default browser.</summary>
    private void Website_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenPath(this, e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OpenTaskbarSettings_Click(object sender, RoutedEventArgs e) => OpenPath(this, "ms-settings:taskbar");

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(DataFolder); } catch { }
        OpenPath(this, DataFolder);
    }

    private void OpenDebugLog_Click(object sender, RoutedEventArgs e)
    {
        string? latest = Log.LatestDebugPath();
        if (latest is null)
        {
            Dialogs.Info(this, "There's no debug log yet. Tick Detailed logging, press Apply or Save, then use the app.");
            return;
        }
        OpenPath(this, latest);
    }

    private void ClearDebugLog_Click(object sender, RoutedEventArgs e)
    {
        Log.ClearDebug();
        SetStatus("Debug log cleared.", true);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(LogPath))
        {
            Dialogs.Info(this, "There's no log yet. It is only written when something goes wrong (for example a switch fails). Turn on Detailed logging under Diagnostics for a full trace.");
            return;
        }
        OpenPath(this, LogPath);
    }

    // ---------- selectable text ----------

    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    /// <summary>
    /// The descriptions are read-only text boxes (so their text can be selected and copied). A text box swallows the mouse
    /// wheel, so while the pointer is over one the wheel is passed on to the page to scroll it.
    /// </summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.OriginalSource is not DependencyObject source) return;

        TextBox? text = null;
        for (DependencyObject? d = source; d is not null; d = ParentOf(d))
        {
            if (d is TextBox box) { text = box; break; }
        }
        if (text is null || text.Tag as string != "SelectableText") return;

        for (DependencyObject? d = ParentOf(text); d is not null; d = ParentOf(d))
        {
            if (d is ScrollViewer page)
            {
                page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta);
                e.Handled = true;
                return;
            }
        }
    }

    // ---------- search ----------

    private static string TextOf(DependencyObject root)
    {
        var sb = new StringBuilder();

        void Walk(object? node)
        {
            switch (node)
            {
                case null:
                    return;
                case string s:
                    sb.Append(s).Append(' ');
                    return;
                case TextBlock tb:
                    sb.Append(tb.Text).Append(' ');
                    break;
                case TextBox box:
                    sb.Append(box.Text).Append(' ');
                    break;
                case HeaderedContentControl h when h.Header is string header:
                    sb.Append(header).Append(' ');
                    break;
            }
            if (node is DependencyObject d)
            {
                foreach (object child in LogicalTreeHelper.GetChildren(d)) Walk(child);
            }
        }

        Walk(root);
        return sb.ToString();
    }

    /// <summary>Outlines every section that mentions the search text and jumps to the first one.</summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        Brush accent = Theme.Accent;
        Brush normal = Theme.Line;
        int firstPage = -1;
        Control? firstCard = null;

        for (int i = 0; i < _pages.Count; i++)
        {
            foreach (var card in _pages[i].Cards)
            {
                bool match = query.Length >= 2 && TextOf(card).Contains(query, StringComparison.OrdinalIgnoreCase);
                card.BorderBrush = match ? accent : normal;
                if (match) ExpandCard(card);                  // a collapsed section that matches opens so the match can be seen
                if (match && firstCard is null)
                {
                    firstPage = i;
                    firstCard = card;
                }
            }
        }

        if (firstCard is not null)
        {
            SelectPage(firstPage);
            ScrollToCard(_pages[firstPage].Page, firstCard);
        }
    }

    // ---------- change tracking (for the unsaved-changes prompt) ----------

    private static string ToJson(AppSettings s) => JsonSerializer.Serialize(s);

    private void CaptureBaseline(string name)
    {
        bool before = _quiet;
        _quiet = true;
        try { TryApply(); }
        finally { _quiet = before; }
        _baseline[name] = ToJson(_work);
    }

    private string StateSnapshot() => string.Join("|",
        _state.NextProfileHotkey, _state.PreviousProfileHotkey, _state.RunOnStartup, _state.WhatsNewMode, _state.DebugLogging, _state.DebugLogMode, _state.PinTrayIcon, _state.AlwaysAdmin,
        JsonSerializer.Serialize(_state.Memory),
        string.Join(",", _profileHotkeys.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Where(p => p.Value.Length > 0).Select(p => p.Key + "=" + p.Value)));

    /// <summary>The app-wide values (profile hotkeys, startup, what's-new) as they are on screen.</summary>
    private void ReadGlobalState()
    {
        var existing = new HashSet<string>(ProfileStore.List(), StringComparer.OrdinalIgnoreCase);
        _state.NextProfileHotkey = NextProfileBox.Text.Trim();
        _state.PreviousProfileHotkey = PrevProfileBox.Text.Trim();
        _state.ProfileHotkeys = _profileHotkeys
            .Where(p => p.Value.Length > 0 && existing.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        _state.RunOnStartup = RunOnStartupBox.IsChecked == true;
        _state.DebugLogging = DebugLogBox.IsChecked == true;
        _state.DebugLogMode = DebugSessionRadio.IsChecked == true ? "PerSession" : "Continuous";
        _state.PinTrayIcon = PinTrayBox.IsChecked == true;
        _state.AlwaysAdmin = AlwaysAdminBox.IsChecked == true;
        _state.Memory = ReadMemorySettings();
        _state.WhatsNewMode = WhatsNewCombo.SelectedIndex switch { 1 => "Always", 2 => "Never", _ => "Update" };
    }

    private bool IsDirty()
    {
        bool before = _quiet;
        _quiet = true;
        bool ok;
        try { ok = TryApply(); }
        finally { _quiet = before; }
        if (!ok) return true;          // something on screen isn't valid, so it can't match what's saved

        StoreProfileHotkeyEdit();
        ReadGlobalState();
        _edited[_shownProfile] = _work;
        foreach (var pair in _edited)
        {
            if (_baseline.TryGetValue(pair.Key, out var saved) && ToJson(pair.Value) != saved) return true;
        }
        return StateSnapshot() != _stateBaseline;
    }

    private void Rebaseline()
    {
        foreach (var pair in _edited) _baseline[pair.Key] = ToJson(pair.Value);
        _stateBaseline = StateSnapshot();
    }

    // ---------- the buttons ----------

    /// <summary>APPLY (save = false) or SAVE (save = true). Returns false if something on screen isn't valid.</summary>
    private bool Push(bool save)
    {
        if (!TryApply()) return false;
        StoreProfileHotkeyEdit();
        ReadGlobalState();
        _edited[_shownProfile] = _work;

        if (save)
        {
            var all = _edited.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            _manager.SaveFromDialog(all, _shownProfile, _state.Clone());
            _activeProfile = _shownProfile;
            Rebaseline();
            SetStatus($"✓ Saved to profile “{_shownProfile}” at {DateTime.Now:HH:mm:ss}.", true);
        }
        else
        {
            _manager.ApplyFromDialog(_shownProfile, _work.Clone(), _state.Clone());
            _activeProfile = _shownProfile;
            SetStatus($"Applied to this session at {DateTime.Now:HH:mm:ss}. Not saved yet: press Save to keep it.", false);
        }

        RefreshProfileBox();
        LogStatusText.Text = _manager.LogStatus;
        return true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Push(false);

    private void Save_Click(object sender, RoutedEventArgs e) => Push(true);

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Ask(this, "Reset settings",
                $"Put every setting in the profile “{_shownProfile}” back to its default? Your character list, positions and per-character settings are kept. Nothing changes until you press Apply or Save.",
                "Reset", "Cancel") != 0) return;

        var fresh = new AppSettings
        {
            ThumbnailGroups = _work.ThumbnailGroups,
            ActiveThumbnailGroup = _work.ActiveThumbnailGroup,
            RosterInitialised = true,
            Characters = _work.Characters,
            SlotCharacters = _work.SlotCharacters,
            ClientOrder = _work.ClientOrder,
            SavedAccountSlots = _work.SavedAccountSlots
        };
        _work = fresh;
        _edited[_shownProfile] = _work;
        LoadFrom();
        SetStatus("Settings reset to their defaults. Press Apply or Save to use them.", false);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing) return;

        if (!IsDirty())
        {
            _closing = true;
            return;
        }

        int choice = Dialogs.Ask(this, "Unsaved changes",
            "You have changes that aren't saved to the config file. Save them before closing?",
            "Save", "Discard", "Cancel");

        if (choice == 0)
        {
            if (Push(true)) _closing = true;
            else e.Cancel = true;
        }
        else if (choice == 1)
        {
            _manager.RevertToSaved();
            _closing = true;
        }
        else
        {
            e.Cancel = true;
        }
    }

    // ---------- memory page ----------

    private static int ParseOr(string text, int min, int max, int fallback) =>
        int.TryParse(text.Trim(), out int value) && value >= min && value <= max ? value : fallback;

    /// <summary>Puts the saved memory-cleaner settings on screen.</summary>
    private void LoadMemoryControls()
    {
        var m = _state.Memory;
        MemTrimEveryBox.IsChecked = m.TrimEveryMinutes;
        MemTrimMinutesBox.Text = m.TrimMinutes.ToString();
        MemCacheEveryBox.IsChecked = m.ClearCacheEvery;
        MemCacheMinutesBox.Text = m.CacheMinutes.ToString();
        MemTrimAboveBox.IsChecked = m.TrimAboveUsage;
        MemTrimPercentBox.Text = m.TrimUsagePercent.ToString();
        MemCacheAboveBox.IsChecked = m.ClearCacheAboveUsage;
        MemCachePercentBox.Text = m.CacheUsagePercent.ToString();
        (m.EveIdleTrim ? MemEveIdleRadio : m.SkipEveClients ? MemEveNeverRadio : MemEveAllRadio).IsChecked = true;
        MemEveIdleBox.Text = m.EveIdleMinutes.ToString();
        MemMinMbBox.Text = m.MinTrimMb.ToString();
        MemCacheOnlyLowBox.IsChecked = m.CacheOnlyWhenLow;
        MemCacheLowPercentBox.Text = m.CacheLowFreePercent.ToString();
        MemLimitsBox.IsChecked = m.UseProgramLimits;
        MemLimitsText.Text = (m.ProgramLimits ?? "").Replace("\r", "").Replace("\n", Environment.NewLine);
        MemExcludeBox.Text = m.ExcludedPrograms;
        MemNotifyBox.IsChecked = m.ShowNotification;

        // clearing the system cache needs administrator rights: without them those controls are greyed out (their values are kept)
        bool admin = PreviewManager.IsAdministrator;
        MemCacheBtn.IsEnabled = admin;
        MemCacheEveryBox.IsEnabled = admin;
        MemCacheMinutesBox.IsEnabled = admin;
        MemCacheAboveBox.IsEnabled = admin;
        MemCachePercentBox.IsEnabled = admin;
        MemCacheOnlyLowBox.IsEnabled = admin;
        MemCacheLowPercentBox.IsEnabled = admin;
        MemAdminNote.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        MemAdminPanel.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The memory-cleaner settings as they are on screen (a value that isn't a valid number keeps its saved value here; TryApply reports it).</summary>
    private MemoryCleanerSettings ReadMemorySettings()
    {
        var old = _state.Memory;
        return new MemoryCleanerSettings
        {
            TrimEveryMinutes = MemTrimEveryBox.IsChecked == true,
            TrimMinutes = ParseOr(MemTrimMinutesBox.Text, 1, 1440, old.TrimMinutes),
            ClearCacheEvery = MemCacheEveryBox.IsChecked == true,
            CacheMinutes = ParseOr(MemCacheMinutesBox.Text, 1, 1440, old.CacheMinutes),
            TrimAboveUsage = MemTrimAboveBox.IsChecked == true,
            TrimUsagePercent = ParseOr(MemTrimPercentBox.Text, 50, 99, old.TrimUsagePercent),
            ClearCacheAboveUsage = MemCacheAboveBox.IsChecked == true,
            CacheUsagePercent = ParseOr(MemCachePercentBox.Text, 50, 99, old.CacheUsagePercent),
            SkipEveClients = MemEveNeverRadio.IsChecked == true,
            EveIdleTrim = MemEveIdleRadio.IsChecked == true,
            EveIdleMinutes = ParseOr(MemEveIdleBox.Text, 1, 1440, old.EveIdleMinutes),
            MinTrimMb = ParseOr(MemMinMbBox.Text, 0, 4096, old.MinTrimMb),
            CacheOnlyWhenLow = MemCacheOnlyLowBox.IsChecked == true,
            CacheLowFreePercent = ParseOr(MemCacheLowPercentBox.Text, 5, 90, old.CacheLowFreePercent),
            UseProgramLimits = MemLimitsBox.IsChecked == true,
            ProgramLimits = string.Join("\n", MemLimitsText.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            ExcludedPrograms = MemExcludeBox.Text.Trim(),
            ShowNotification = MemNotifyBox.IsChecked == true
        };
    }

    private static string Mb(ulong bytes) => (bytes / 1048576UL).ToString("N0") + " MB";

    private void RefreshMemory()
    {
        var s = _manager.SampleMemory();
        if (!s.IsValid)
        {
            MemUsageText.Text = "Couldn't read the memory use.";
            return;
        }

        MemUsageText.Text = $"Memory in use:  {Mb(s.UsedPhys)} of {Mb(s.TotalPhys)}   ({s.LoadPercent}%)";
        MemCommitText.Text = $"Committed:  {Mb(s.UsedCommit)} of {Mb(s.TotalCommit)}   ({s.CommitPercent}%)";

        var st = _manager.MemoryStats;
        MemStatsText.Text = st.Samples == 0 ? "" :
            $"Average:  {Mb(st.AvgUsed)} ({st.AvgPercent}%)     Highest:  {Mb(st.MaxUsed)} ({st.MaxPercent}%)     Lowest:  {Mb(st.MinUsed)} ({st.MinPercent}%)";
        if (_manager.LastMemoryResult.Length > 0) MemResultText.Text = _manager.LastMemoryResult;
        MemSessionText.Text = _manager.SessionSummary;

        _memPercent = s.LoadPercent;
        UpdateMemoryBar();
        RefreshClipboardEstimate();
        UpdateEstimates();
    }

    /// <summary>The clipboard size is read on a background thread, and only again when the clipboard has changed.</summary>
    private async void RefreshClipboardEstimate()
    {
        if (_clipBusy) return;
        _clipBusy = true;
        try
        {
            _clipInfo = await _manager.ReadClipboardAsync();
            UpdateEstimates();
        }
        catch
        {
            _clipInfo = new ClipboardInfo(2, 0);
        }
        finally
        {
            _clipBusy = false;
        }
    }

    /// <summary>The small grey line under each button: what pressing it would free (an estimate, not a promise).</summary>
    private void UpdateEstimates()
    {
        if (MemTrimEstimate is null || MemCacheEstimate is null || MemClipEstimate is null) return;
        var t = _trimEstimate;
        MemTrimEstimate.Text =
            t is null ? "Estimating..." :
            t.Programs == 0 || t.Bytes < 5L * 1048576 ? "Nothing worth trimming right now" :
            t.Ratio > 0
                ? $"Estimate: about {MemoryCleaner.FormatMb((long)(t.Bytes * t.Ratio))} (from {t.Programs} programs)"
                : $"Estimate: up to {MemoryCleaner.FormatMb(t.Bytes)} (from {t.Programs} programs)";
        if (!PreviewManager.IsAdministrator)
        {
            MemCacheEstimate.Text = "Needs administrator rights";
        }
        else
        {
            var cache = MemoryCleaner.ReadCacheLists();
            MemCacheEstimate.Text = !cache.Ok
                ? "Couldn't read the cache size"
                : $"Cached files: up to {MemoryCleaner.FormatSize(cache.StandbyBytes)}; {MemoryCleaner.FormatSize(cache.ModifiedBytes)} to disk";
        }
        var clip = _clipInfo;
        MemClipEstimate.Text =
            clip is null ? "Estimating..." :
            clip.Value.State == 0 ? "The clipboard is empty" :
            clip.Value.State == 1 ? $"Frees about {MemoryCleaner.FormatSize(clip.Value.Bytes)}" :
            clip.Value.State == 3 ? "Another program is using it" :
            "Holds data (size not measured)";
    }

    private int _memPercent;

    private void UpdateMemoryBar()
    {
        double room = Math.Max(0, MemTrack.ActualWidth - 2);
        MemFill.Width = room * Math.Clamp(_memPercent, 0, 100) / 100.0;
    }

    private async void MemTrim_Click(object sender, RoutedEventArgs e)
    {
        MemTrimBtn.IsEnabled = false;
        MemResultText.Text = "Trimming...";
        try
        {
            var result = await _manager.TrimNowAsync(ReadMemorySettings());
            MemResultText.Text = _manager.LastMemoryResult.Length > 0 ? _manager.LastMemoryResult : result.Message;
        }
        catch (Exception ex)
        {
            MemResultText.Text = "Couldn't trim: " + ex.Message;
        }
        finally
        {
            MemTrimBtn.IsEnabled = true;
            RefreshMemory();
            RefreshPrograms();
        }
    }

    private async void MemCache_Click(object sender, RoutedEventArgs e)
    {
        MemCacheBtn.IsEnabled = false;
        MemResultText.Text = "Clearing the system cache...";
        try
        {
            var result = await _manager.ClearCacheNowAsync();
            MemResultText.Text = _manager.LastMemoryResult.Length > 0 ? _manager.LastMemoryResult : result.Message;
        }
        catch (Exception ex)
        {
            MemResultText.Text = "Couldn't clear the system cache: " + ex.Message;
        }
        finally
        {
            MemCacheBtn.IsEnabled = PreviewManager.IsAdministrator;
            RefreshMemory();
        }
    }

    private void MemClipboard_Click(object sender, RoutedEventArgs e)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                long held = _clipInfo is { State: 1 } info ? info.Bytes : 0;
                System.Windows.Clipboard.Clear();
                MemResultText.Text = $"{DateTime.Now:HH:mm:ss}  Clipboard cleared" + (held > 0 ? $" ({MemoryCleaner.FormatSize(held)})." : ".");
                RefreshClipboardEstimate();
                return;
            }
            catch
            {
                System.Threading.Thread.Sleep(60);          // another program has the clipboard open for a moment
            }
        }
        MemResultText.Text = "Couldn't clear the clipboard (another program is using it). Try again.";
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Ask(this, "Restart Windows Explorer",
                "Close and restart Windows Explorer?\n\nThe taskbar and desktop disappear for a moment, and any open File Explorer windows close. Your programs keep running.",
                "Restart", "Cancel") != 0) return;
        MemExplorerBtn.IsEnabled = false;
        MemResultText.Text = "Restarting Windows Explorer...";
        try
        {
            var result = await _manager.RestartExplorerAsync();
            MemResultText.Text = _manager.LastMemoryResult.Length > 0 ? _manager.LastMemoryResult : result.Message;
        }
        catch (Exception ex)
        {
            MemResultText.Text = "Couldn't restart Windows Explorer: " + ex.Message;
        }
        finally
        {
            MemExplorerBtn.IsEnabled = true;
        }
    }

    // ----- Top Programs -----

    private async void RefreshPrograms()
    {
        if (_progBusy) return;
        _progBusy = true;
        try
        {
            var list = await _manager.SampleProgramsAsync(10);
            if (_pageIndex != PageMemory_) return;                 // the page was left while the programs were being read
            _programs = list;
            BuildProgramRows();
            _trimEstimate = await _manager.EstimateTrimAsync(ReadMemorySettings());
            UpdateEstimates();
        }
        catch (Exception ex)
        {
            TopProgsNote.Text = "Couldn't read the programs: " + ex.Message;
        }
        finally
        {
            _progBusy = false;
        }
    }

    /// <summary>One line per program: name, size, trend, and the Trim and Never trim buttons.</summary>
    private void BuildProgramRows()
    {
        if (TopProgsHost is null) return;
        TopProgsHost.Children.Clear();
        var never = MemoryCleaner.ParseNames(MemExcludeBox.Text);
        foreach (var program in _programs)
        {
            string name = program.Name;
            bool isNever = never.Contains(name);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = name + (program.Count > 1 ? $"  ×{program.Count}" : "") + (isNever ? "   (never trimmed)" : ""),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = isNever ? Theme.Muted : Theme.Text
            };
            var size = new TextBlock
            {
                Text = MemoryCleaner.FormatMb(program.Bytes),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 14, 0),
                FontWeight = FontWeights.SemiBold
            };
            string trendText = program.Growing ? "▲ growing" : program.Trend;
            var trend = new TextBlock
            {
                Text = trendText,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = program.Growing ? (Brush)FindResource("BrushWarn") : Theme.Muted,
                FontWeight = program.Growing ? FontWeights.SemiBold : FontWeights.Normal,
                ToolTip = program.Growing ? "This program's memory has kept rising for several minutes" : "Compared with about a minute ago"
            };
            var trim = new Button { Content = "Trim", Padding = new Thickness(10, 4, 10, 4), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Trim this program's memory now" };
            trim.Click += (_, _) => TrimOne(name, trim);
            var keep = new Button
            {
                Content = isNever ? "Allow" : "Never trim",
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 12,
                MinWidth = 78,
                ToolTip = isNever ? "Let the automatic cleaning trim this program again" : "Put this program on the never-trim list"
            };
            keep.Click += (_, _) => ToggleNeverTrim(name);
            Grid.SetColumn(size, 1);
            Grid.SetColumn(trend, 2);
            Grid.SetColumn(trim, 3);
            Grid.SetColumn(keep, 4);
            row.Children.Add(label);
            row.Children.Add(size);
            row.Children.Add(trend);
            row.Children.Add(trim);
            row.Children.Add(keep);
            TopProgsHost.Children.Add(row);
        }
        TopProgsNote.Text = _programs.Count == 0 ? "Reading programs..." : $"Updated {DateTime.Now:HH:mm:ss}.";
    }

    private async void TrimOne(string name, Button button)
    {
        button.IsEnabled = false;
        MemResultText.Text = "Trimming...";
        try
        {
            var result = await _manager.TrimProgramAsync(name, ReadMemorySettings());
            MemResultText.Text = _manager.LastMemoryResult.Length > 0 ? _manager.LastMemoryResult : result.Message;
        }
        catch (Exception ex)
        {
            MemResultText.Text = "Couldn't trim: " + ex.Message;
        }
        finally
        {
            button.IsEnabled = true;
            RefreshMemory();
            RefreshPrograms();
        }
    }

    /// <summary>Adds the program to the never-trim list, or takes it off again. Kept like any other edit: Apply or Save makes it count.</summary>
    private void ToggleNeverTrim(string name)
    {
        var names = MemoryCleaner.ParseNames(MemExcludeBox.Text);
        if (!names.Remove(name)) names.Add(name);
        MemExcludeBox.Text = string.Join(", ", names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        BuildProgramRows();
        SetStatus("The never-trim list changed. Press Apply or Save to use it.", false);
    }

    /// <summary>
    /// Changing "always start as administrator" needs the app to be running as administrator (the scheduled task can only be made or
    /// removed from an elevated copy). Without that, the box is greyed out and says how to turn it on.
    /// </summary>
    private void UpdateAlwaysAdminControls()
    {
        bool admin = PreviewManager.IsAdministrator;
        AlwaysAdminBox.IsEnabled = admin;
        AlwaysAdminNote.Text = admin
            ? "Starts the app with administrator rights every time, without a Windows prompt (it uses a scheduled task, which Save sets up or removes). Run on Startup and double-clicking the exe both start it this way."
            : "To turn this on, go to Memory and press Restart as administrator, then choose Always. It can only be changed while the app is running as administrator.";
    }

    /// <summary>The scheduled task couldn't be made after all: untick the box and say why.</summary>
    internal void ShowAdminTaskFailed(string reason)
    {
        AlwaysAdminBox.IsChecked = false;
        _state.AlwaysAdmin = false;
        SetStatus("Couldn't set up starting as administrator: " + reason, false);
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        int choice = Dialogs.Ask(this, "Restart as administrator",
            "Restart EVE-IronWorks-Preview with administrator rights?\n\nWindows will ask you to allow it.\n\nAlways: you only do this once. The app is set to start as administrator every time after that, without asking again (including with Windows). You can turn it off under General.\n\nJust this once: nothing is remembered.\n\nChanges in this window that you haven't saved are lost, so press Save first if you need them. Your EVE clients are not affected.",
            "Always", "Just this once", "Cancel");
        if (choice != 0 && choice != 1) return;
        if (!_manager.RestartAsAdministrator(out string error, always: choice == 0))
        {
            MemResultText.Text = error;
            return;
        }
        _closing = true;                                    // the new copy is starting: close this one without the unsaved-changes question
        Application.Current.Shutdown();
    }

    private void OnHotkeyRecording(bool recording) => _manager.SetRecordingHotkey(recording);

    protected override void OnClosed(EventArgs e)
    {
        HotkeyField.RecordingChanged -= OnHotkeyRecording;
        _memTimer.Stop();
        _progTimer.Stop();
        base.OnClosed(e);
    }

    // ---------- validation ----------

    private bool Fail(int page, string message)
    {
        if (!_quiet)
        {
            SelectPage(page);
            ExpandCardsOnPage(page);                          // so the field that needs fixing isn't hidden in a collapsed section
            Warn(message);
        }
        return false;
    }

    private static bool InRange(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), out value) && value >= min && value <= max;

    /// <summary>Checks every field (jumping to the page with a bad value) and writes them into the working copy.</summary>
    private bool TryApply()
    {
        // Appearance, Thumbnails, General
        if (!InRange(WidthBox.Text, 120, 1200, out int width))
            return Fail(PageThumbs_, "Thumbnail width must be 120 to 1200.");
        if (!InRange(HeightBox.Text, 0, 1200, out int height) || (height != 0 && height < 45))
            return Fail(PageThumbs_, "Thumbnail height must be 0 (automatic) or 45 to 1200.");
        if (!InRange(SnapDistBox.Text, 1, 100, out int snap))
            return Fail(PageThumbs_, "Snap distance must be 1 to 100 pixels.");
        if (!InRange(RefreshBox.Text, 500, 10000, out int refresh))
            return Fail(PageGeneral_, "Scan interval must be 500 to 10000 ms.");

        // Appearance
        if (!InRange(FontSizeBox.Text, 6, 72, out int fontSize))
            return Fail(PageThumbs_, "Font size must be 6 to 72.");
        if (!InRange(OffsetXBox.Text, -300, 300, out int offsetX) || !InRange(OffsetYBox.Text, -300, 300, out int offsetY))
            return Fail(PageThumbs_, "Text offsets must be between -300 and 300 pixels.");
        if (!InRange(ActiveWidthBox.Text, 1, 20, out int activeWidth) || !InRange(InactiveWidthBox.Text, 1, 20, out int inactiveWidth))
            return Fail(PageThumbs_, "Border widths must be 1 to 20 pixels.");
        if (!LabelColourBox.IsValid || !LabelBgBox.IsValid || !ColourBox.IsValid || !InactiveColourBox.IsValid)
            return Fail(PageThumbs_, "One of the colours isn't valid. Use the Pick button, or type something like #F97316.");

        // Thumbnails / clients
        if (!InRange(HideDelayBox.Text, 0, 5000, out int hideDelay))
            return Fail(PageThumbs_, "The hide delay must be 0 to 5000 ms.");
        if (!InRange(AutoMinDelayBox.Text, 0, 600, out int minDelay))
            return Fail(PageGeneral_, "The minimise delay must be 0 to 600 seconds.");

        // Hotkeys
        if (!ForwardBox.IsValid || !BackBox.IsValid || _slotFields.Any(f => !f.IsValid))
            return Fail(PageHotkeys_, "One of the hotkeys isn't supported. Click the box and press the keys again, or use ✕ to clear it.");
        if (!ProfileHotkeyBox.IsValid || !NextProfileBox.IsValid || !PrevProfileBox.IsValid)
            return Fail(PageHotkeys_, "One of the profile hotkeys isn't supported. Click the box and press the keys again, or use ✕ to clear it.");

        // Thumbnail group: the characters typed in its lines
        if (!CommitThumbRows(true)) return false;

        // Memory cleaner
        if (!InRange(MemTrimMinutesBox.Text, 1, 1440, out _) || !InRange(MemCacheMinutesBox.Text, 1, 1440, out _))
            return Fail(PageMemory_, "The minutes for automatic memory cleaning must be 1 to 1440.");
        if (!InRange(MemTrimPercentBox.Text, 50, 99, out _) || !InRange(MemCachePercentBox.Text, 50, 99, out _))
            return Fail(PageMemory_, "The memory-use limits must be 50 to 99 percent.");

        if (!InRange(MemMinMbBox.Text, 0, 4096, out _))
            return Fail(PageMemory_, "The smallest program to trim must be 0 to 4096 MB.");
        if (!InRange(MemEveIdleBox.Text, 1, 1440, out _))
            return Fail(PageMemory_, "The idle time for EVE clients must be 1 to 1440 minutes.");
        if (!InRange(MemCacheLowPercentBox.Text, 5, 90, out _))
            return Fail(PageMemory_, "The free-memory level for clearing the cache must be 5 to 90 percent.");
        if (!MemoryCleaner.TryParseLimits(MemLimitsText.Text, out _, out string limitError))
            return Fail(PageMemory_, limitError);
        // Notifications
        if (!InRange(NotifyCooldownBox.Text, 0, 3600, out int cooldown))
            return Fail(PageAlerts_, "The cooldown must be 0 to 3600 seconds.");
        if (!InRange(NotifySecondsBox.Text, 1, 120, out int flashSeconds))
            return Fail(PageAlerts_, "The flash time must be 1 to 120 seconds.");
        if (!NotifyColourBox.IsValid)
            return Fail(PageThumbs_, "The flash colour isn't valid. Use the Pick button, or type something like #F87171.");

        // Log monitoring
        if (!InRange(LogPollBox.Text, 250, 10000, out int logPoll))
            return Fail(PageAlerts_, "The check interval must be 250 to 10000 ms.");
        if (!InRange(LogMiningSecondsBox.Text, 10, 86400, out int miningSeconds))
            return Fail(PageAlerts_, "The mining quiet time must be 10 seconds or more.");

        // Characters
        if (!CommitCharacter())
        {
            if (!_quiet)
            {
                SelectPage(_charFailPage);
                ExpandCardsOnPage(_charFailPage);
            }
            return false;
        }

        _work.PositionMode = ModeClientRadio.IsChecked == true ? AppSettings.ModeClient : AppSettings.ModeCharacter;
        _work.ClientOrderMode = ModeOfRadios(OrderManualRadio.IsChecked == true, OrderSavedRadio.IsChecked == true);
        _work.NewCharactersInheritPosition = InheritBox.IsChecked == true;
        _work.SameSizeForAll = SameSizeBox.IsChecked == true;
        _work.PreviewWidth = width;
        _work.PreviewHeight = height;
        _work.ThumbnailsClickable = ClickableBox.IsChecked == true;
        _work.LoginScreensClickable = LoginClickableBox.IsChecked == true;
        _work.LockPositions = LockBox.IsChecked == true;
        _work.SnapEnabled = SnapBox.IsChecked == true;
        _work.AlwaysOnTop = OnTopBox.IsChecked == true;
        _work.SnapDistance = snap;
        _work.RefreshMilliseconds = refresh;

        _work.ThumbnailOpacityPercent = (int)Math.Round(OpacitySlider.Value);
        _work.ShowLabels = LabelsBox.IsChecked == true;
        _work.LabelFontName = FontBox.SelectedItem as string ?? "Segoe UI";
        _work.LabelFontSize = fontSize;
        _work.LabelBold = BoldBox.IsChecked == true;
        _work.DefaultLabelColourHex = LabelColourBox.Hex.Length > 0 ? LabelColourBox.Hex : "#FFFFFF";
        _work.LabelOpacityPercent = (int)Math.Round(LabelOpacitySlider.Value);
        _work.LabelBackgroundHex = LabelBgBox.Hex.Length > 0 ? LabelBgBox.Hex : "#0C0C0E";
        _work.LabelBackgroundOpacityPercent = (int)Math.Round(LabelBgOpacitySlider.Value);
        _work.LabelPosition = LabelPositionBox.SelectedItem as string ?? "TopLeft";
        _work.LabelOffsetX = offsetX;
        _work.LabelOffsetY = offsetY;
        _work.HighlightActive = HighlightBox.IsChecked == true;
        _work.ActiveBorderHex = ColourBox.Hex.Length > 0 ? ColourBox.Hex : "#F97316";
        _work.ActiveBorderWidth = activeWidth;
        _work.ActiveBorderStyle = ActiveStyleBox.SelectedItem as string ?? "Solid";
        _work.ShowInactiveBorder = InactiveBox.IsChecked == true;
        _work.InactiveBorderHex = InactiveColourBox.Hex.Length > 0 ? InactiveColourBox.Hex : "#6F6F7D";
        _work.InactiveBorderWidth = inactiveWidth;
        _work.InactiveBorderStyle = InactiveStyleBox.SelectedItem as string ?? "Solid";

        _work.ShowLoginScreens = LoginScreensBox.IsChecked == true;
        _work.HideActiveThumbnail = HideActiveBox.IsChecked == true;
        _work.HideWhenEveNotFocused = HideUnfocusedBox.IsChecked == true;
        _work.HideDelayMs = hideDelay;

        _work.CycleForwardHotkey = ForwardBox.Text;
        _work.CycleBackHotkey = BackBox.Text;
        _work.CycleIncludeLoginScreens = CycleLoginBox.IsChecked == true;
        _work.HotkeysOnlyInEve = HotkeysOnlyEveBox.IsChecked == true;
        _work.HotkeyPassThrough = PassThroughBox.IsChecked == true;
        _work.HotkeysInConfig = HotkeysInConfigBox.IsChecked == true;
        CommitSlotGroup();

        _work.AutoMinimiseInactive = AutoMinBox.IsChecked == true;
        _work.AutoMinimiseKeepLastActive = KeepLastBox.IsChecked == true;
        _work.AutoMinimiseOnStart = MinOnStartBox.IsChecked == true;
        _work.AutoMinimiseDelaySeconds = minDelay;

        _work.NotificationsEnabled = NotifyEnabledBox.IsChecked == true;
        _work.NotifySkipActiveClient = NotifySkipActiveBox.IsChecked == true;
        _work.NotifyCooldownSeconds = cooldown;
        _work.NotifyFlashBorder = NotifyFlashBox.IsChecked == true;
        _work.NotifyBorderHex = NotifyColourBox.Hex.Length > 0 ? NotifyColourBox.Hex : "#F87171";
        _work.NotifyBorderSeconds = flashSeconds;
        _work.NotifyToast = NotifyToastBox.IsChecked == true;
        _work.NotifySound = NotifySoundBox.IsChecked == true;
        _work.NotifySoundName = NotifySoundCombo.SelectedItem as string ?? "Exclamation";
        _work.NotifySoundFile = NotifySoundFileBox.Text.Trim();
        _work.NotifySpeak = NotifySpeakBox.IsChecked == true;

        _work.LogMonitoringEnabled = LogEnabledBox.IsChecked == true;
        _work.LogPollMilliseconds = logPoll;
        _work.GameLogFolder = GameFolderBox.Text.Trim();
        _work.ChatLogFolder = ChatFolderBox.Text.Trim();
        _work.LogWatchAttack = LogAttackBox.IsChecked == true;
        _work.LogWatchCrystalBroke = LogCrystalBox.IsChecked == true;
        _work.LogWatchFleetFollow = LogFleetFollowBox.IsChecked == true;
        _work.LogWatchMiningCompression = LogCompressionBox.IsChecked == true;
        _work.LogWatchConversationInvite = LogConversationBox.IsChecked == true;
        _work.LogWatchDecloak = LogDecloakBox.IsChecked == true;
        _work.LogWatchMiningIdle = LogMiningBox.IsChecked == true;
        _work.LogWatchFleetRegroup = LogRegroupBox.IsChecked == true;
        _work.LogMiningIdleSeconds = miningSeconds;
        _work.LogKeywords = string.Join("\n", LogKeywordsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        _work.ShowSystemName = LogSystemBox.IsChecked == true;

        // drop per-character entries that hold nothing (saved positions of "[client N]" entries are kept)
        foreach (string key in _work.Characters.Keys.ToList())
        {
            var c = _work.Characters[key];
            bool empty = c.Width == 0 && c.Height == 0 && c.Label.Length == 0 && c.LabelColourHex.Length == 0 && c.LabelSize == 0 && c.LabelOpacityPercent == 0 &&
                         c.Hotkey.Length == 0 && c.Left is null && c.Top is null &&
                         !c.Hidden && !c.ExcludeFromCycling && !c.NeverMinimise && !c.MinimiseOnLeave &&
                         c.ActivateAction == "None";
            if (empty) _work.Characters.Remove(key);
        }
        return true;
    }
}


