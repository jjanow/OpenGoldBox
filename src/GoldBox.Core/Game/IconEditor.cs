using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The icon editor (Pool of Radiance DOS v1.3, GAME.OVR 0x1D1AC; Create's last step and the camp's Alter Icon), as the
/// program's state byte [bp-8] runs it (docs/re/start-menu.md §3). Each menu is a line of the data segment (DS:07BF +
/// 41·state, <see cref="MenuLine"/>) read by the horizontal menu routine 0x2CC8C, which answers the hotkey of the word
/// taken (a capital letter or digit) or 0 for Escape; <see cref="Key"/> takes that answer.
/// <list type="bullet">
/// <item>Main (1): P → Parts, 1 → Colours on the low nibbles (color-1), 2 → Colours on the high nibbles (color-2),
/// S → Size; E or Escape leaves (<see cref="Key"/> returns true): the kept values go back into the record, then the
/// caller asks the IconOk question and, on No, calls <see cref="Restart"/>.</item>
/// <item>Parts (2): H (head 0xBD) or W (weapon 0xBE) → Step; E / Escape → Main.</item>
/// <item>Colours (3): B 0xC1, A 0xC2, L 0xC3, H or F 0xC4 (the line's third word is the hair word for color-1, the
/// face word for color-2), S 0xC5, W 0xC6, any other non-exit key 0xC1 → Step; E / Escape → Main.</item>
/// <item>Size (4): the line's first word is the other size; L sets 0xC0 := 2, S := 1 (and stay); K keeps the size, E /
/// Escape puts the kept one back; both → Main.</item>
/// <item>Step (5): N next, P previous (head 0..13 and weapon 0..31 wrap; a colour nibble steps mod 16), K keeps (the
/// colour Keep keeps all six bytes), E / Escape puts the kept value back (all six colour bytes); both → the menu
/// before.</item>
/// </list>
/// The kept values ([bp-4] head, [bp-5] weapon, [bp-6] size, [bp-0x15] the six colour bytes) are read when the editor
/// starts and again after a No (0x1D1DE). The head's Keep reads the head from the party's first record (DS:5D92), the
/// record being edited in both callers.
/// </summary>
public sealed class IconEditor
{
    /// <summary>The editor's menus: the program's state byte.</summary>
    public enum Menu
    {
        Main = 1,
        Parts = 2,
        Colours = 3,
        Size = 4,
        Step = 5,
    }

    public const int Head = 0xBD, Weapon = 0xBE, Size = 0xC0, Colours = 0xC1;
    public const int HeadCount = 14, WeaponCount = 32;

    /// <summary>DS:07BF + 41·state: the menu lines (string[40] each).</summary>
    public const int MenuLines = 0x07BF, MenuLineSize = 41;

    /// <summary>The 1-based position (Pascal Delete / Insert at 0x1D3BD–0x1D3D4) of the four characters the colour line's
    /// third word replaces.</summary>
    public const int ColourWordAt = 13, ColourWordLength = 4;

    private readonly EclCharacter _c;
    private byte _head, _weapon, _size;
    private readonly byte[] _colours = new byte[6];

    public IconEditor(EclCharacter c)
    {
        _c = c;
        Restart();
    }

    public Menu State { get; private set; } = Menu.Main;

    /// <summary>[bp-0x1a]: the menu Step returns to (Parts or Colours), or Main.</summary>
    public Menu Parent { get; private set; } = Menu.Main;

    /// <summary>[bp-0x17]: the colour menu works on the high nibbles (color-2) rather than the low ones (color-1).</summary>
    public bool HighNibble { get; private set; }

    /// <summary>[bp-0x1b]: the part Step works on, 'H' or 'W' (Parts only).</summary>
    public char Part { get; private set; }

    /// <summary>[bp-0x16]: the colour byte Step works on, 1..6 (0xC0 + k).</summary>
    public int ColourByte { get; private set; } = 1;

    /// <summary>The DS offset of a menu's line.</summary>
    public static int MenuLine(Menu m) => MenuLines + MenuLineSize * (int)m;

    /// <summary>The size word shown before the Size menu's line (0x1D2F5): the other size than the record's.</summary>
    public bool OffersSmall => _c[Size] == 2;

    /// <summary>0x1D1DE: the kept values read again from the record; the menu starts at Main.</summary>
    public void Restart()
    {
        _head = _c[Head];
        _weapon = _c[Weapon];
        _size = _c[Size];
        for (int k = 0; k < 6; k++)
            _colours[k] = _c[Colours + k];
        State = Menu.Main;
    }

    private static bool IsExit(char k) => k is 'E' or '\0';

    /// <summary>
    /// One answer of the menu (a hotkey, '\0' for Escape). Returns true when the main menu is left: the kept values are
    /// back in the record (0x1D7D6–0x1D809), and the caller asks the IconOk question.
    /// </summary>
    public bool Key(char k)
    {
        switch (State)
        {
            case Menu.Main:
                Parent = Menu.Main;
                switch (k)
                {
                    case 'P':
                        State = Menu.Parts;
                        break;
                    case '1':
                    case '2':
                        State = Menu.Colours;
                        HighNibble = k == '2';
                        break;
                    case 'S':
                        State = Menu.Size;
                        break;
                }
                break;
            case Menu.Parts:
                Parent = Menu.Parts;
                if (IsExit(k))
                    State = Menu.Main;
                else
                {
                    Part = k;
                    State = Menu.Step;
                }
                return false;
            case Menu.Colours:
                Parent = Menu.Colours;
                ColourByte = k switch
                {
                    'W' => 6,
                    'B' => 1,
                    'H' or 'F' => 4,
                    'S' => 5,
                    'A' => 2,
                    'L' => 3,
                    _ => 1,
                };
                State = IsExit(k) ? Menu.Main : Menu.Step;
                return false;
            case Menu.Size:
                switch (k)
                {
                    case 'L':
                        _c[Size] = 2;
                        break;
                    case 'S':
                        _c[Size] = 1;
                        break;
                    case 'K':
                        _size = _c[Size];
                        State = Menu.Main;
                        return false;
                    case 'E' or '\0':
                        _c[Size] = _size;
                        State = Menu.Main;
                        return false;
                }
                break;
            case Menu.Step:
                Step(k);
                return false;
        }
        // 0x1D7BA: only while the menu before is Main (the Size menu leaves [bp-0x1a] at 1 too) does E / Escape end it.
        if (Parent != Menu.Main || !IsExit(k))
            return false;
        _c[Head] = _head;
        _c[Weapon] = _weapon;
        _c[Size] = _size;
        for (int j = 0; j < 6; j++)
            _c[Colours + j] = _colours[j];
        return true;
    }

    private void Step(char k)
    {
        if (Parent == Menu.Parts)
        {
            if (Part is not ('H' or 'W'))
                return; // 0x1D5E1: no other part; the key does nothing
            int at = Part == 'H' ? Head : Weapon, n = Part == 'H' ? HeadCount : WeaponCount;
            switch (k)
            {
                case 'P':
                    _c[at] = (byte)(_c[at] > 0 ? _c[at] - 1 : n - 1);
                    break;
                case 'N':
                    _c[at] = (byte)(_c[at] < n - 1 ? _c[at] + 1 : 0);
                    break;
                case 'K':
                    if (Part == 'H')
                        _head = _c[Head];
                    else
                        _weapon = _c[Weapon];
                    State = Parent;
                    break;
                case 'E' or '\0':
                    _c[at] = Part == 'H' ? _head : _weapon;
                    State = Parent;
                    break;
            }
            return;
        }
        if (Parent != Menu.Colours)
            return;
        int b = Colours - 1 + ColourByte;
        int lo = _c[b] & 0x0F, hi = _c[b] >> 4;
        switch (k)
        {
            case 'N':
                if (HighNibble)
                    hi = (hi + 1) % 16;
                else
                    lo = (lo + 1) % 16;
                _c[b] = (byte)(hi << 4 | lo);
                break;
            case 'P':
                if (HighNibble)
                    hi = (hi - 1) & 0x0F;
                else
                    lo = (lo - 1) & 0x0F;
                _c[b] = (byte)(hi << 4 | lo);
                break;
            case 'K':
                for (int j = 0; j < 6; j++)
                    _colours[j] = _c[Colours + j];
                State = Parent;
                break;
            case 'E' or '\0':
                for (int j = 0; j < 6; j++)
                    _c[Colours + j] = _colours[j];
                State = Parent;
                break;
        }
    }
}
