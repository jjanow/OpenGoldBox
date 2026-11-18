using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The camp's Alter menu rules on the party list (GAME.OVR 0x18CDA, 0x18E32, 0x19116) and its settings (0x192C8,
/// 0x1948A). The current character (DS:5D92) stays the same record through a move.
/// </summary>
public static class CampParty
{
    /// <summary>
    /// 0x18CDA (Place, Home): the current character changes places with the one before it; the first one goes to the
    /// end of the list (alone, it stays).
    /// </summary>
    public static void MoveUp(EclParty party)
    {
        var m = party.Members;
        int i = party.CurrentIndex;
        if (i < 0 || i >= m.Count || m.Count < 2)
            return;
        var c = m[i];
        if (i == 0)
        {
            m.RemoveAt(0);
            m.Add(c);
        }
        else
            (m[i - 1], m[i]) = (m[i], m[i - 1]);
        party.SetCurrent(c);
    }

    /// <summary>
    /// 0x18E32 (Place, End): the current character changes places with the one after it; the last one goes to the
    /// head of the list.
    /// </summary>
    public static void MoveDown(EclParty party)
    {
        var m = party.Members;
        int i = party.CurrentIndex;
        if (i < 0 || i >= m.Count || m.Count < 2)
            return;
        var c = m[i];
        if (i == m.Count - 1)
        {
            m.RemoveAt(i);
            m.Insert(0, c);
        }
        else
            (m[i + 1], m[i]) = (m[i], m[i + 1]);
        party.SetCurrent(c);
    }

    /// <summary>
    /// Drop (0x19116 → 0x1CC10 with "keep the size" off and "free the icon" on): the current character leaves the
    /// list, the one before it (or the new head) becomes current, and $6E3E goes down by one. The last member alone
    /// is not dropped (the program offers to quit to DOS instead). Returns the dropped record.
    /// </summary>
    public static EclCharacter? Drop(EclParty party, EclMemory memory)
    {
        var m = party.Members;
        int i = party.CurrentIndex;
        if (i < 0 || i >= m.Count || m.Count == 1)
            return null;
        var c = m[i];
        m.RemoveAt(i);
        party.CurrentIndex = i > 0 ? i - 1 : 0;
        memory.Write(EclPartyRules.PartySizeCell, (ushort)(memory.Read(EclPartyRules.PartySizeCell) - 1));
        return c;
    }

    /// <summary>0x19116: an enabled character (record 0x10D ≠ 0) gets the farewell message, another the other one.</summary>
    public static bool LeavesWillingly(EclCharacter c) => c[0x10D] != 0;
}

/// <summary>
/// The camp's game settings (Alter: Speed 0x192C8, Pics 0x1948A). The game speed DS:49E2 (0 fastest … 9 slowest)
/// and the picture flags are program variables; the save writes them into $49FC and $49FF (bit 1 monsters, bit 0
/// portraits). The speed is <see cref="EclMemory.GameSpeed"/> (the save builder copies it into $49FC); the picture
/// flags are kept in the VM cell itself, the value the save would write.
/// </summary>
public static class CampSettings
{
    public const ushort PicturesCell = 0x49FF;

    public static int Speed(EclMemory m) => m.GameSpeed;

    /// <summary>
    /// 0x192C8: F (offered while the speed is above 0) and Down lower it, S (offered below 9) and Up raise it. The
    /// letters are not bounded by the routine: they only act when offered, which keeps them in 0..9.
    /// </summary>
    public static void Faster(EclMemory m)
    {
        if (m.GameSpeed > 0)
            m.GameSpeed--;
    }

    public static void Slower(EclMemory m)
    {
        if (m.GameSpeed < 9)
            m.GameSpeed++;
    }

    public static bool MonsterPictures(EclMemory m) => (m.Read(PicturesCell) & 2) != 0;
    public static bool Portraits(EclMemory m) => (m.Read(PicturesCell) & 1) != 0;

    /// <summary>0x1948A's Pics menu: M toggles the monster pictures, P the portraits.</summary>
    public static void ToggleMonsters(EclMemory m) => m.Write(PicturesCell, (ushort)(m.Read(PicturesCell) ^ 2));
    public static void TogglePortraits(EclMemory m) => m.Write(PicturesCell, (ushort)(m.Read(PicturesCell) ^ 1));
}
