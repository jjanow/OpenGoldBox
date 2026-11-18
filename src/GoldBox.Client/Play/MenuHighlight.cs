namespace GoldBox.Client.Play;

/// <summary>
/// The program's menu highlight (DS:6B2B, 1-based in the program, read s18 in the menu routine GAME.OVR 0x2CC8C): one
/// cell shared by every menu the routine draws (area, camp and combat menus, the fight's Done, Speed, Aim and View
/// menus). A menu opens with the entry the cell names, or the first when the cell is past its last entry (0x2CCDE);
/// a hotkey moves the cell to its entry (0x2CF48) and ',' / '.' move it; Escape leaves it. So the highlight carries
/// over from the last choice in any menu. DOSBox-X (s18, save K's fight): Load B → the first combat menu highlights
/// View; View → the View menu highlights Spells; its Exit (4th) → the main menu's 7th (Done) → the Done menu (4 entries)
/// starts on its first; Quit (2nd) → the next creature's menu highlights View, its View menu Drop (2nd).
/// [?] The item list routine 0x2D669 (Items screen, drop money's coin list) keeps its own highlight (DOSBox-X: first
/// entry each time); it is not this cell.
/// </summary>
public sealed class MenuHighlight
{
    /// <summary>The cell, 0-based.</summary>
    public int Index { get; set; }

    /// <summary>The entry a menu of <paramref name="count"/> entries opens on.</summary>
    public int Start(int count) => Index < count ? Index : 0;

    /// <summary>An entry was chosen (hotkey, Return on the highlight, a click).</summary>
    public void Chosen(int entry)
    {
        if (entry >= 0)
            Index = entry;
    }
}
