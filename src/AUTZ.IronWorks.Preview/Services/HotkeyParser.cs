using System.Windows.Input;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Converts hotkey text such as "Ctrl+Alt+Right", "Ctrl+Shift+1", "F13" or "`" to Win32 modifiers + virtual key,
/// and turns a pressed key combination back into text.
/// </summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModWin = 0x8;

    // punctuation keys can be typed as the character itself
    private static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["`"] = Key.OemTilde, ["~"] = Key.OemTilde, ["tilde"] = Key.OemTilde, ["grave"] = Key.OemTilde, ["backtick"] = Key.OemTilde,
        ["-"] = Key.OemMinus, ["minus"] = Key.OemMinus,
        ["="] = Key.OemPlus, ["equals"] = Key.OemPlus, ["plus"] = Key.OemPlus,
        ["["] = Key.OemOpenBrackets,
        ["]"] = Key.OemCloseBrackets,
        [";"] = Key.OemSemicolon,
        ["'"] = Key.OemQuotes,
        [","] = Key.OemComma,
        ["."] = Key.OemPeriod,
        ["/"] = Key.OemQuestion,
        ["\\"] = Key.OemPipe,
        ["esc"] = Key.Escape,
        ["pgup"] = Key.PageUp,
        ["pgdn"] = Key.PageDown,
        ["del"] = Key.Delete,
        ["ins"] = Key.Insert
    };

    private static readonly Dictionary<Key, string> Display = new()
    {
        [Key.OemTilde] = "`",
        [Key.OemMinus] = "-",
        [Key.OemPlus] = "=",
        [Key.OemOpenBrackets] = "[",
        [Key.OemCloseBrackets] = "]",
        [Key.OemSemicolon] = ";",
        [Key.OemQuotes] = "'",
        [Key.OemComma] = ",",
        [Key.OemPeriod] = ".",
        [Key.OemQuestion] = "/",
        [Key.OemPipe] = "\\"
    };

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

            if (Aliases.TryGetValue(part, out var aliased)) { key = aliased; continue; }

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

    public static string KeyToText(Key key)
    {
        if (Display.TryGetValue(key, out var text)) return text;
        if (key >= Key.D0 && key <= Key.D9) return ((int)key - (int)Key.D0).ToString();
        return key.ToString();
    }

    public static string ToText(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyToText(key));
        return string.Join("+", parts);
    }
}
