using System.Windows.Input;

namespace AUTZ.IronWorks.Preview.Services;

public enum HotkeyInputKind { Key, Mouse, Wheel }

/// <summary>One non-modifier input: a key (Code = virtual key, plus HotkeyParser.NumpadFlag for the numpad copy of a key), a mouse button (1 left, 2 right, 3 middle, 4 and 5 side buttons) or a wheel notch (1 up, -1 down).</summary>
public readonly record struct HotkeyToken(HotkeyInputKind Kind, int Code);

/// <summary>A required modifier. Group: 0 Ctrl, 1 Alt, 2 Shift, 3 Win. Side: 0 either, 1 left only, 2 right only.</summary>
public readonly record struct HotkeyModifier(int Group, int Side);

public sealed class HotkeySpec
{
    public List<HotkeyModifier> Modifiers { get; } = new();
    public List<HotkeyToken> Inputs { get; } = new();

    /// <summary>Only modifiers, e.g. "RCtrl": it fires when they are released without anything else being pressed.</summary>
    public bool ModifierOnly => Inputs.Count == 0;

    public bool UsesMouse => Inputs.Any(i => i.Kind != HotkeyInputKind.Key);
}

/// <summary>Turns hotkey text ("Ctrl+Alt+Right", "Mouse4", "RCtrl") into something the hotkey service can match, and back again.</summary>
public static class HotkeyParser
{
    // virtual keys of the left / right modifiers, indexed by group
    public static readonly int[,] ModifierVk = { { 0xA2, 0xA3 }, { 0xA4, 0xA5 }, { 0xA0, 0xA1 }, { 0x5B, 0x5C } };

    private static readonly Dictionary<string, HotkeyModifier> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = new(0, 0), ["control"] = new(0, 0), ["lctrl"] = new(0, 1), ["lcontrol"] = new(0, 1), ["rctrl"] = new(0, 2), ["rcontrol"] = new(0, 2),
        ["alt"] = new(1, 0), ["lalt"] = new(1, 1), ["ralt"] = new(1, 2),
        ["shift"] = new(2, 0), ["lshift"] = new(2, 1), ["rshift"] = new(2, 2),
        ["win"] = new(3, 0), ["windows"] = new(3, 0), ["lwin"] = new(3, 1), ["rwin"] = new(3, 2)
    };

    private static readonly Dictionary<string, HotkeyToken> MouseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mouseleft"] = new(HotkeyInputKind.Mouse, 1),
        ["mouseright"] = new(HotkeyInputKind.Mouse, 2),
        ["mousemiddle"] = new(HotkeyInputKind.Mouse, 3),
        ["mouse4"] = new(HotkeyInputKind.Mouse, 4),
        ["xbutton1"] = new(HotkeyInputKind.Mouse, 4),
        ["mouse5"] = new(HotkeyInputKind.Mouse, 5),
        ["xbutton2"] = new(HotkeyInputKind.Mouse, 5),
        ["wheelup"] = new(HotkeyInputKind.Wheel, 1),
        ["wheeldown"] = new(HotkeyInputKind.Wheel, -1)
    };

    /// <summary>Added to a key's code for its numpad copy (NumLeft, NumEnter...), so it doesn't match the main key.</summary>
    public const int NumpadFlag = 0x10000;

    // keys that exist twice: on the main keyboard and on the numpad (with Num Lock off). name -> virtual key
    private static readonly Dictionary<string, int> NumpadNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NumLeft"] = 0x25, ["NumUp"] = 0x26, ["NumRight"] = 0x27, ["NumDown"] = 0x28,
        ["NumHome"] = 0x24, ["NumEnd"] = 0x23, ["NumPgUp"] = 0x21, ["NumPgDn"] = 0x22,
        ["NumIns"] = 0x2D, ["NumDel"] = 0x2E, ["NumEnter"] = 0x0D
    };

    /// <summary>True for a key that has a numpad copy with the same virtual key.</summary>
    public static bool IsDualKey(int vk) => vk is (>= 0x21 and <= 0x28) or 0x2D or 0x2E or 0x0D;

    /// <summary>
    /// The code for a key press. Windows marks the main arrows / Home / End etc. as "extended" and their numpad copies not;
    /// for Enter it is the other way round.
    /// </summary>
    public static int KeyCode(int vk, bool extended)
    {
        if (!IsDualKey(vk)) return vk;
        bool numpad = vk == 0x0D ? extended : !extended;
        return numpad ? vk | NumpadFlag : vk;
    }

    /// <summary>The plain virtual key of a code (numpad flag removed).</summary>
    public static int Vk(int code) => code & 0xFFFF;

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

    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = new HotkeySpec();
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ModifierNames.TryGetValue(part, out var modifier))
            {
                if (!spec.Modifiers.Any(m => m.Group == modifier.Group)) spec.Modifiers.Add(modifier);
                continue;
            }
            if (MouseNames.TryGetValue(part, out var mouse))
            {
                if (!spec.Inputs.Contains(mouse)) spec.Inputs.Add(mouse);
                continue;
            }

            if (NumpadNames.TryGetValue(part, out int numVk))
            {
                var numToken = new HotkeyToken(HotkeyInputKind.Key, numVk | NumpadFlag);
                if (!spec.Inputs.Contains(numToken)) spec.Inputs.Add(numToken);
                continue;
            }
            Key key;
            if (!Aliases.TryGetValue(part, out key))
            {
                string name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                if (char.IsDigit(name[0])) return false;
                if (!Enum.TryParse(name, true, out key) || key == Key.None) return false;
            }

            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return false;
            var token = new HotkeyToken(HotkeyInputKind.Key, vk);
            if (!spec.Inputs.Contains(token)) spec.Inputs.Add(token);
        }

        if (spec.Modifiers.Count == 0 && spec.Inputs.Count == 0) return false;
        if (spec.Inputs.Count(i => i.Kind == HotkeyInputKind.Wheel) > 1) return false;
        return true;
    }

    public static bool IsValid(string? text) => string.IsNullOrWhiteSpace(text) || TryParse(text, out _);

    public static string KeyToText(Key key)
    {
        if (Display.TryGetValue(key, out var text)) return text;
        if (key >= Key.D0 && key <= Key.D9) return ((int)key - (int)Key.D0).ToString();
        return key.ToString();
    }

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>The text for one pressed key: left-side modifiers are saved as plain Ctrl / Alt / Shift / Win, right-side ones as RCtrl etc.</summary>
    /// <summary>Same, but a numpad press of a key that exists twice is saved as NumLeft, NumEnter...</summary>
    public static string TokenForKey(Key key, bool numpad)
    {
        if (numpad)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            foreach (var pair in NumpadNames)
            {
                if (pair.Value == vk) return pair.Key;
            }
        }
        return TokenForKey(key);
    }

    public static string TokenForKey(Key key) => key switch
    {
        Key.LeftCtrl => "Ctrl",
        Key.RightCtrl => "RCtrl",
        Key.LeftAlt => "Alt",
        Key.RightAlt => "RAlt",
        Key.LeftShift => "Shift",
        Key.RightShift => "RShift",
        Key.LWin => "Win",
        Key.RWin => "RWin",
        _ => KeyToText(key)
    };

    public static string TokenForMouse(MouseButton button) => button switch
    {
        MouseButton.Left => "MouseLeft",
        MouseButton.Right => "MouseRight",
        MouseButton.Middle => "MouseMiddle",
        MouseButton.XButton1 => "Mouse4",
        _ => "Mouse5"
    };

    public static bool IsModifierToken(string token) => ModifierNames.ContainsKey(token);

    /// <summary>Puts modifiers first (Ctrl, Alt, Shift, Win) and the other inputs after them in the order they were pressed.</summary>
    public static string Format(IEnumerable<string> tokens)
    {
        var list = tokens.ToList();
        var modifiers = list.Where(IsModifierToken).OrderBy(t => ModifierNames[t].Group).ToList();
        var others = list.Where(t => !IsModifierToken(t)).ToList();
        return string.Join("+", modifiers.Concat(others));
    }
}

