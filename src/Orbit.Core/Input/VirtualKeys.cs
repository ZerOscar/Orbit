// 虚拟键码和可读名字之间的映射。配置文件里存名字（"Space"）而不是数字，改起来直观。

namespace Orbit.Core.Input;

public static class VirtualKeys
{
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;

    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Back",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [0x1B] = "Escape",
        [0x20] = "Space",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "PrintScreen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5D] = "ContextMenu",
        [0xC0] = "OemTilde",
        [0xBA] = "OemSemicolon",
        [0xBB] = "OemPlus",
        [0xBC] = "OemComma",
        [0xBD] = "OemMinus",
        [0xBE] = "OemPeriod",
        [0xBF] = "OemQuestion",
        [0xDB] = "OemOpenBrackets",
        [0xDC] = "OemPipe",
        [0xDD] = "OemCloseBrackets",
        [0xDE] = "OemQuotes",
    };

    static VirtualKeys()
    {
        for (var vk = 0x30; vk <= 0x39; vk++)
            Names[vk] = ((char)vk).ToString();
        for (var vk = 0x41; vk <= 0x5A; vk++)
            Names[vk] = ((char)vk).ToString();
        for (var i = 1; i <= 12; i++)
            Names[0x70 + i - 1] = $"F{i}";
    }

    /// <summary>键码的名字；未收录的返回 "Vk123"。</summary>
    public static string NameOf(int virtualKey) =>
        Names.TryGetValue(virtualKey, out var name) ? name : $"Vk{virtualKey}";

    public static bool TryParse(string? name, out int virtualKey)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var (key, value) in Names)
            {
                if (string.Equals(value, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    virtualKey = key;
                    return true;
                }
            }
            if (name.StartsWith("Vk", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name.AsSpan(2), out var raw) && raw > 0)
            {
                virtualKey = raw;
                return true;
            }
        }
        virtualKey = 0;
        return false;
    }

    public static ModifierKeys ModifiersOf(bool ctrl, bool alt, bool shift, bool meta) =>
        (ctrl ? ModifierKeys.Ctrl : ModifierKeys.None) |
        (alt ? ModifierKeys.Alt : ModifierKeys.None) |
        (shift ? ModifierKeys.Shift : ModifierKeys.None) |
        (meta ? ModifierKeys.Meta : ModifierKeys.None);

    public static string Describe(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Meta)) parts.Add("Win");
        return string.Join("+", parts);
    }
}
