namespace GoldBox.Client.Play;

/// <summary>
/// The program's menu highlight (DS:6B2B, 1-based in the program, read s18 in the menu routine GAME.OVR 0x2CC8C): one
/// cell shared by every menu the routine draws (area, camp and combat menus, the fight's Done, Speed, Aim and View
/// menus). A menu opens with the entry the cell names, or the first when the cell is past its last entry (0x2CCDE);
/// a hotkey moves the cell to its entry (0x2CF48) and ',' / '.' move it; Escape leaves it. So the highlight carries
/// over from the last choice in any menu. DOSBox-X (s18, save K's fight): Load B → the first combat menu highlights
/// View; View → the View menu highlights Spells; its Exit (4th) → the main menu's 7th (Done) → the Done menu (4 entries)
/// starts on its first; Quit (2nd) → the next creature's menu highlights View, its View menu Drop (2nd).
/// Read s19: only three routines write the cell, all in the menu unit. The menu routine itself (above; past the last
/// entry it also stores 1 back, 0x2CCE7); the item list routine 0x2D669 (Items screen, drop money's coin list, VMENU)
/// stores 1 once on entry (0x2D6B9) and then runs its menu through 0x2CC8C, so the item lists open on their first entry
/// (DOSBox-X: first entry each time) and leave the cell where their menu left it; the yes/no routine 0x2D9A0 stores 2
/// (0x2D9B9) before its "Yes No" menu, so it opens on No and leaves the cell on the answer.
/// In a fight (read s29) the item list routine is also behind the coin list of drop money and the spell list 0x239D6
/// (Cast, View's Spells, a scroll's spells, through 0x270A2); the aim menu is the menu routine itself, so an item's
/// aim opens where the Items list left the cell (docs/re/combat-menu.md "Menus").
/// </summary>
public sealed class MenuHighlight
{
    /// <summary>The cell, 0-based.</summary>
    public int Index { get; set; }

    /// <summary>
    /// The entry a menu of <paramref name="count"/> entries opens on; a cell past the last entry goes back to the first
    /// (0x2CCDE–0x2CCE7 stores 1).
    /// </summary>
    public int Start(int count)
    {
        if (Index >= count)
            Index = 0;
        return Index;
    }

    /// <summary>The item list routine 0x2D669 on entry (0x2D6B9): the cell to the first entry.</summary>
    public void ItemList() => Index = 0;

    /// <summary>The yes/no routine 0x2D9A0 (0x2D9B9): the cell to the second entry, No.</summary>
    public void YesNo() => Index = 1;

    /// <summary>
    /// The item list routine's window top (DOS DS:6B29, Amiga hunk 32 +0x17D6): one global word beside the cell, kept
    /// from list to list. Start-up sets it to 1 on DOS (0xE9DA) and to 0 on the Amiga (hunk 25 +0x1540), so a list
    /// whose entry 0 is a label (creation's "Pick …" lists, position 1) hides that label on DOS until a list has taken
    /// the top to 0 (DOSBox-X s44: fresh start, the race list from row 2 with no label; after camp Display, position
    /// 0, the label on row 2 and the races from row 3) and shows it on the Amiga from the start (FS-UAE s43).
    /// </summary>
    public int ListTop { get; set; } = DosListTop;

    /// <summary>The window top after start-up: DOS 1 (0xE9DA), Amiga 0 (hunk 25 +0x1540).</summary>
    public const int DosListTop = 1, AmigaListTop = 0;

    /// <summary>
    /// The list routine's entry (DOS 0x2D766–0x2D792, Amiga hunk 13 +0x2C74–0x2CA4): the top goes up to the caller's
    /// position when below it, and to 0 when past the entry count (labels included). Returns the new top.
    /// `[?]` The Client carries it through the creation lists, Add's roster list (position 0, 0x1CA19) and the
    /// explore screen's item lists only: the party menu's View pages (Items, the coin list), VMENU, the fight's lists
    /// and every Next / Prev paging (the spell lists keep their own top, combat-menu.md "Spell lists' Next / Prev") do
    /// not move it yet.
    /// </summary>
    public int ListEntered(int position, int count) => ListTop = EnterList(ListTop, position, count);

    /// <summary><see cref="ListEntered"/>'s rule on a given top.</summary>
    public static int EnterList(int top, int position, int count)
    {
        if (top > position)
            top = position;
        if (top > count)
            top = 0;
        return top;
    }

    /// <summary>An entry was chosen (hotkey, Return on the highlight, a click).</summary>
    public void Chosen(int entry)
    {
        if (entry >= 0)
            Index = entry;
    }
}
