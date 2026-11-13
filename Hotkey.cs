using System.Windows.Input;

namespace ShortcutBook;

/// <summary>설정에 저장하는 단축키 ("F2", "Ctrl+E" 같은 문자열)</summary>
internal static class Hotkey
{
    public const string DefaultEdit = "F2";

    public static bool TryParse(string? text, out Key key, out ModifierKeys mods)
    {
        key = Key.None; mods = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse(part, true, out key)) return false;
                    break;
            }
        }
        return key != Key.None;
    }

    public static string Format(Key key, ModifierKeys mods)
    {
        var s = "";
        if (mods.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
        if (mods.HasFlag(ModifierKeys.Alt)) s += "Alt+";
        if (mods.HasFlag(ModifierKeys.Shift)) s += "Shift+";
        if (mods.HasFlag(ModifierKeys.Windows)) s += "Win+";
        return s + key;
    }

    public static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None;

    /// <summary>글자 입력을 가로채지 않도록 수정키 없이는 F1~F24 만 허용한다.</summary>
    public static bool IsAcceptable(Key key, ModifierKeys mods) =>
        !IsModifierKey(key) && (mods != ModifierKeys.None || (key >= Key.F1 && key <= Key.F24));
}
