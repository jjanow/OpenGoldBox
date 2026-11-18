namespace GoldBox.Client.Input;

/// <summary>
/// What a key gives the program through the BIOS (INT 16h function 0, read by the Crt unit's ReadKey): a character, or
/// for an extended key 0 then its scan code (docs/re/view3d.md "Keys while the program is busy"). The Client's key
/// names (MonoGame <c>Keys</c> names, with "Alt+" / "Ctrl+" as <see cref="ActionInput"/> writes them) both ways.
/// </summary>
public static class DosKeys
{
    /// <summary>The scan codes of the letter keys, A..Z (US layout).</summary>
    private static readonly byte[] LetterScan =
        [0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32, 0x31, 0x18, 0x19, 0x10, 0x13,
         0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C];

    /// <summary>Extended keys (character 0): the arrows, Home, End, PgUp, PgDn, Ins, Del, F1..F10.</summary>
    private static readonly Dictionary<string, byte> Extended = new()
    {
        ["Up"] = 0x48, ["Down"] = 0x50, ["Left"] = 0x4B, ["Right"] = 0x4D, ["Home"] = 0x47, ["End"] = 0x4F,
        ["PageUp"] = 0x49, ["PageDown"] = 0x51, ["Insert"] = 0x52, ["Delete"] = 0x53,
        ["F1"] = 0x3B, ["F2"] = 0x3C, ["F3"] = 0x3D, ["F4"] = 0x3E, ["F5"] = 0x3F, ["F6"] = 0x40, ["F7"] = 0x41,
        ["F8"] = 0x42, ["F9"] = 0x43, ["F10"] = 0x44,
    };

    /// <summary>Keys that give a character (the keypad with NumLock on).</summary>
    private static readonly Dictionary<string, char> Plain = new()
    {
        ["Space"] = ' ', ["Enter"] = '\r', ["Escape"] = '\x1B', ["Back"] = '\b', ["Tab"] = '\t',
        ["OemMinus"] = '-', ["OemPlus"] = '=', ["OemComma"] = ',', ["OemPeriod"] = '.', ["OemQuestion"] = '/',
        ["OemSemicolon"] = ';', ["OemQuotes"] = '\'', ["OemOpenBrackets"] = '[', ["OemCloseBrackets"] = ']',
        ["OemPipe"] = '\\', ["OemTilde"] = '`',
        ["Add"] = '+', ["Subtract"] = '-', ["Multiply"] = '*', ["Divide"] = '/', ["Decimal"] = '.',
    };

    /// <summary>
    /// The bytes ReadKey returns for a key name: (character, scan code); character 0 is an extended key (the scan code
    /// comes next). Null for a name the PC keyboard has no BIOS key for (Ctrl+Alt, F11, F12, …).
    /// </summary>
    public static (byte Ascii, byte Scan)? Of(string name)
    {
        bool alt = false, ctrl = false;
        while (true)
        {
            if (name.StartsWith("Alt+", StringComparison.Ordinal)) { alt = true; name = name[4..]; }
            else if (name.StartsWith("Ctrl+", StringComparison.Ordinal)) { ctrl = true; name = name[5..]; }
            else break;
        }
        if (alt && ctrl)
            return null;
        if (name.Length == 1 && name[0] is >= 'A' and <= 'Z')
        {
            byte scan = LetterScan[name[0] - 'A'];
            return alt ? ((byte)0, scan) : ctrl ? ((byte)(name[0] - 'A' + 1), scan) : ((byte)char.ToLowerInvariant(name[0]), scan);
        }
        if (alt || ctrl)
            return null;
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
            return ((byte)name[1], (byte)(name[1] == '0' ? 0x0B : name[1] - '0' + 1));
        if (name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7 && char.IsAsciiDigit(name[6]))
            return ((byte)name[6], 0);
        if (Extended.TryGetValue(name, out byte s))
            return (0, s);
        return Plain.TryGetValue(name, out char c) ? ((byte)c, 0) : null;
    }

    /// <summary>The key name of a character the program reads (a letter upper case), or null when no key gives it.</summary>
    public static string? OfCharacter(byte c)
    {
        char ch = (char)c;
        if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            return char.ToUpperInvariant(ch).ToString();
        if (ch is >= '0' and <= '9')
            return "D" + ch;
        foreach (var (name, p) in Plain)
            if (p == ch)
                return name;
        return null;
    }

    /// <summary>The key name of an extended key's scan code (Alt+letter included), or null.</summary>
    public static string? OfScan(byte scan)
    {
        foreach (var (name, s) in Extended)
            if (s == scan)
                return name;
        int i = Array.IndexOf(LetterScan, scan);
        return i >= 0 ? "Alt+" + (char)('A' + i) : null;
    }
}
