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
    public string CycleForwardHotkey { get; set; } = "Ctrl+Alt+Right";
    public string CycleBackHotkey { get; set; } = "Ctrl+Alt+Left";
    public Dictionary<string, CharacterSettings> Characters { get; set; } = new();

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
