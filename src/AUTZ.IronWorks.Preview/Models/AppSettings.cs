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
}

public sealed class AppSettings
{
    public int PreviewWidth { get; set; } = 280;
    public int RefreshMilliseconds { get; set; } = 1500;
    public bool ShowLabels { get; set; } = true;
    public string DefaultLabelColourHex { get; set; } = "#FFFFFF";
    public bool HighlightActive { get; set; } = false;
    public string ActiveBorderHex { get; set; } = "#F59E0B";
    public bool LockPositions { get; set; } = false;

    /// <summary>False = clicking a thumbnail does nothing, so you can only switch with the hotkeys.</summary>
    public bool ThumbnailsClickable { get; set; } = true;

    /// <summary>How solid the thumbnails are (10-100). 60 = a bit see-through.</summary>
    public int ThumbnailOpacityPercent { get; set; } = 60;
    public bool SnapEnabled { get; set; } = true;
    public int SnapDistance { get; set; } = 15;
    public string CycleForwardHotkey { get; set; } = "Ctrl+Alt+Right";
    public string CycleBackHotkey { get; set; } = "Ctrl+Alt+Left";

    /// <summary>Show a thumbnail for clients that are still on the login screen.</summary>
    public bool ShowLoginScreens { get; set; } = true;

    /// <summary>
    /// Which character was last logged in on the Nth client to open ("1", "2", ...). A login screen takes the
    /// thumbnail position of the character that usually ends up in that slot.
    /// </summary>
    public Dictionary<string, string> SlotCharacters { get; set; } = new();

    public Dictionary<string, CharacterSettings> Characters { get; set; } = new();

    /// <summary>Login-screen positions are stored under keys like "[login 2]"; real characters never start with "[".</summary>
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
}
