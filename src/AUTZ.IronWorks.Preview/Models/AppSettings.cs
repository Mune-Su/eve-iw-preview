using System.Text.Json;

namespace AUTZ.IronWorks.Preview.Models;

/// <summary>Per-character overrides. Zero / empty values mean "use the default".</summary>
public sealed class CharacterSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Label { get; set; } = "";
    public string LabelColourHex { get; set; } = "";
    public string Hotkey { get; set; } = "";

    /// <summary>True = no thumbnail is shown for this character.</summary>
    public bool Hidden { get; set; }

    /// <summary>True = the next / previous client hotkeys skip this character.</summary>
    public bool ExcludeFromCycling { get; set; }

    /// <summary>True = auto-minimise leaves this character's client alone.</summary>
    public bool NeverMinimise { get; set; }
}

/// <summary>Everything in one profile (a "layout"): appearance, behaviour, positions, per-character settings.</summary>
public sealed class AppSettings
{
    public const string ModeCharacter = "Character";
    public const string ModeClient = "Client";

    // ----- general -----
    /// <summary>"Character": a character's thumbnail follows them. "Client": the Nth client to open always uses the Nth position.</summary>
    public string PositionMode { get; set; } = ModeCharacter;
    public int PreviewWidth { get; set; } = 280;
    /// <summary>0 = automatic 16:9 from the width.</summary>
    public int PreviewHeight { get; set; } = 0;
    /// <summary>True = every thumbnail is the same size (resize one and they all follow).</summary>
    public bool SameSizeForAll { get; set; } = false;
    public int RefreshMilliseconds { get; set; } = 1500;
    public bool LockPositions { get; set; } = false;
    /// <summary>False = clicking a thumbnail does nothing, so you can only switch with the hotkeys.</summary>
    public bool ThumbnailsClickable { get; set; } = true;
    public bool SnapEnabled { get; set; } = true;
    public int SnapDistance { get; set; } = 15;
    /// <summary>A character with no saved spot takes the position (and size) of the login screen it replaces.</summary>
    public bool NewCharactersInheritPosition { get; set; } = true;

    // ----- appearance -----
    /// <summary>How solid the thumbnails are (10-100). 60 = a bit see-through.</summary>
    public int ThumbnailOpacityPercent { get; set; } = 60;
    public bool ShowLabels { get; set; } = true;
    public string DefaultLabelColourHex { get; set; } = "#FFFFFF";
    public string LabelFontName { get; set; } = "Segoe UI";
    public int LabelFontSize { get; set; } = 13;
    public bool LabelBold { get; set; } = true;
    public string LabelBackgroundHex { get; set; } = "#0A0F16";
    public int LabelBackgroundOpacityPercent { get; set; } = 67;
    /// <summary>TopLeft, TopCenter, TopRight, LeftCenter, Center, RightCenter, BottomLeft, BottomCenter, BottomRight.</summary>
    public string LabelPosition { get; set; } = "TopLeft";
    public int LabelOffsetX { get; set; } = 0;
    public int LabelOffsetY { get; set; } = 0;

    // ----- borders (Solid, Dashed, Dotted, DashDot, Double, CornerBrackets) -----
    public bool HighlightActive { get; set; } = false;
    public string ActiveBorderHex { get; set; } = "#F59E0B";
    public int ActiveBorderWidth { get; set; } = 3;
    public string ActiveBorderStyle { get; set; } = "Solid";
    public bool ShowInactiveBorder { get; set; } = false;
    public string InactiveBorderHex { get; set; } = "#606060";
    public int InactiveBorderWidth { get; set; } = 1;
    public string InactiveBorderStyle { get; set; } = "Solid";

    // ----- which thumbnails are shown -----
    /// <summary>Show a thumbnail for clients that are still on the login screen.</summary>
    public bool ShowLoginScreens { get; set; } = true;
    /// <summary>Hide the thumbnail of the client you're currently in.</summary>
    public bool HideActiveThumbnail { get; set; } = false;
    /// <summary>Hide every thumbnail while something other than EVE has focus.</summary>
    public bool HideWhenEveNotFocused { get; set; } = false;
    public int HideDelayMs { get; set; } = 500;

    // ----- hotkeys and cycling -----
    public string CycleForwardHotkey { get; set; } = "Ctrl+Alt+Right";
    public string CycleBackHotkey { get; set; } = "Ctrl+Alt+Left";
    public bool CycleIncludeLoginScreens { get; set; } = true;
    /// <summary>Draw a red X on thumbnails that the next / previous hotkeys skip.</summary>
    public bool ShowExcludedOverlay { get; set; } = true;

    // ----- client behaviour -----
    public bool AutoMinimiseInactive { get; set; } = false;
    public int AutoMinimiseDelaySeconds { get; set; } = 2;

    /// <summary>Which character was last logged in on the Nth client to open ("1", "2", ...).</summary>
    public Dictionary<string, string> SlotCharacters { get; set; } = new();

    /// <summary>Per-character settings. Keys that start with "[" ("[client 2]", "[login 1]") only hold a saved position and size.</summary>
    public Dictionary<string, CharacterSettings> Characters { get; set; } = new();

    public static bool IsCharacterKey(string key) => !key.StartsWith('[');

    public CharacterSettings? TryGetCharacter(string name) =>
        Characters.TryGetValue(name, out var c) ? c : null;

    public CharacterSettings GetOrAddCharacter(string name)
    {
        if (!Characters.TryGetValue(name, out var c))
        {
            c = new CharacterSettings();
            Characters[name] = c;
        }
        return c;
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();

    /// <summary>Copies every setting from another instance into this one (this object stays the same instance).</summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite) property.SetValue(this, property.GetValue(other));
        }
    }
}

/// <summary>App-wide state that isn't part of a profile: which profile is active and the profile hotkeys.</summary>
public sealed class AppState
{
    public string ActiveProfile { get; set; } = "Default";
    public string NextProfileHotkey { get; set; } = "";
    public string PreviousProfileHotkey { get; set; } = "";
    public Dictionary<string, string> ProfileHotkeys { get; set; } = new();
}
