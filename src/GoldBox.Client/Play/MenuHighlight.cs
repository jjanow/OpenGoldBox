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

    /// <summary>An entry was chosen (hotkey, Return on the highlight, a click).</summary>
    public void Chosen(int entry)
    {
        if (entry >= 0)
            Index = entry;
    }
}
