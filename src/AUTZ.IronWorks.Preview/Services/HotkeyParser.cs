using System.Windows.Input;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Parses text like "Ctrl+Alt+Right", "Ctrl+Shift+1" or "F13" into Win32 modifiers + virtual key.</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModWin = 0x8;

    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        Key key = Key.None;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string lower = part.ToLowerInvariant();
            if (lower == "ctrl" || lower == "control") { modifiers |= ModControl; continue; }
            if (lower == "alt") { modifiers |= ModAlt; continue; }
            if (lower == "shift") { modifiers |= ModShift; continue; }
            if (lower == "win" || lower == "windows") { modifiers |= ModWin; continue; }

            string name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
            if (char.IsDigit(name[0])) return false;
            if (!Enum.TryParse<Key>(name, true, out key) || key == Key.None) return false;
        }

        if (key == Key.None) return false;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return false;
        virtualKey = (uint)vk;
        return true;
    }
}
