using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PrimeShift.Core.Hotkeys;

/// <summary>Modifier flags with the same values RegisterHotKey expects.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>A global hotkey: modifiers plus a Win32 virtual-key code. Text form: "Ctrl+Alt+F11".</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<uint, string> KeyNames = BuildKeyNames();

    private static readonly Dictionary<string, uint> KeyCodes =
        KeyNames.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Win, "Win"),
        (HotkeyModifiers.Control, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
    ];

    private static readonly HashSet<uint> ModifierKeyCodes =
        [0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];

    public static bool IsModifierKey(uint virtualKey) => ModifierKeyCodes.Contains(virtualKey);

    public static string KeyName(uint virtualKey) =>
        KeyNames.TryGetValue(virtualKey, out var name) ? name : $"0x{virtualKey:X2}";

    public override string ToString() => Format("+");

    /// <summary>Spaced form for display, e.g. "Ctrl + Alt + F11".</summary>
    public string ToDisplayString() => Format(" + ");

    /// <summary>Modifiers alone in display form, e.g. "Ctrl + Alt", for a combination still being typed.</summary>
    public static string ModifiersDisplayString(HotkeyModifiers modifiers) =>
        string.Join(" + ", ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name));

    private string Format(string separator)
    {
        var mods = Modifiers;
        var parts = ModifierNames.Where(m => mods.HasFlag(m.Flag)).Select(m => m.Name).ToList();
        parts.Add(KeyName(VirtualKey));
        return string.Join(separator, parts);
    }

    /// <summary>Why this combination cannot be used as a global hotkey, or null if it can.</summary>
    public string? Validate()
    {
        if (VirtualKey == 0 || IsModifierKey(VirtualKey))
        {
            return "请再按下一个非修饰键";
        }
        if (Modifiers == HotkeyModifiers.None)
        {
            return "至少需要一个修饰键（Ctrl、Alt、Shift 或 Win）";
        }
        // Shift+letter and the like would break normal typing everywhere.
        var isFunctionKey = VirtualKey is >= 0x70 and <= 0x87;
        if (Modifiers == HotkeyModifiers.Shift && !isFunctionKey)
        {
            return "只用 Shift 时只能搭配 F1–F24，请加上 Ctrl、Alt 或 Win";
        }
        return null;
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey, out string? error)
    {
        hotkey = null;
        error = null;
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty))
        {
            error = $"快捷键格式错误：{text}";
            return false;
        }
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" => HotkeyModifiers.Win,
                _ => (HotkeyModifiers?)null,
            };
            if (flag is null)
            {
                error = $"无法识别的修饰键：{part}";
                return false;
            }
            modifiers |= flag.Value;
        }
        var key = parts[^1];
        uint vk;
        if (KeyCodes.TryGetValue(key, out var code))
        {
            vk = code;
        }
        else if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                 && uint.TryParse(key.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                 && hex is > 0 and < 0xFF)
        {
            vk = hex;
        }
        else
        {
            error = $"无法识别的按键：{key}";
            return false;
        }
        var candidate = new Hotkey(modifiers, vk);
        error = candidate.Validate();
        if (error is not null)
        {
            return false;
        }
        hotkey = candidate;
        return true;
    }

    private static Dictionary<uint, string> BuildKeyNames()
    {
        var names = new Dictionary<uint, string>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            names[c] = c.ToString();
        }
        for (var d = 0; d <= 9; d++)
        {
            names[(uint)('0' + d)] = d.ToString(CultureInfo.InvariantCulture);
            names[(uint)(0x60 + d)] = $"Num{d}";
        }
        for (var f = 1; f <= 24; f++)
        {
            names[(uint)(0x70 + f - 1)] = $"F{f}";
        }
        (uint, string)[] named =
        [
            (0x08, "Backspace"), (0x09, "Tab"), (0x0D, "Enter"), (0x13, "Pause"), (0x1B, "Esc"),
            (0x20, "Space"), (0x21, "PageUp"), (0x22, "PageDown"), (0x23, "End"), (0x24, "Home"),
            (0x25, "Left"), (0x26, "Up"), (0x27, "Right"), (0x28, "Down"), (0x2C, "PrintScreen"),
            (0x2D, "Insert"), (0x2E, "Delete"), (0x6A, "Num*"), (0x6B, "NumPlus"), (0x6D, "Num-"),
            (0x6E, "Num."), (0x6F, "Num/"), (0xBA, ";"), (0xBB, "="), (0xBC, ","), (0xBD, "-"),
            (0xBE, "."), (0xBF, "/"), (0xC0, "`"), (0xDB, "["), (0xDC, "\\"), (0xDD, "]"), (0xDE, "'"),
        ];
        foreach (var (vk, name) in named)
        {
            names[vk] = name;
        }
        return names;
    }
}
