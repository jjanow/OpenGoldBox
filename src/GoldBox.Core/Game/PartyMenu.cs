using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>The party menu's entries in the program's order (DS:0619, 42 bytes each: a name, then the enabled byte).</summary>
public enum PartyMenuItem
{
    Create, Drop, Modify, Train, View, Add, Remove, Load, Save, Begin, Exit,
}

/// <summary>
/// The party menu (Pool of Radiance DOS v1.3, GAME.OVR 0x19AE0; the program's first screen after the title, and the
/// training hall's menu, PROGRAM 0): which entries it offers, and its party rules (Add 0x1C9EC, Remove 0x1CC10 after
/// the character file is written, Drop 0x1B8B6, the join 0x1F42C). Program read s19, docs/re/start-menu.md.
/// </summary>
public static class PartyMenu
{
    public const int Count = 11;

    /// <summary>VM cell of the training hall's class mask ($6DA8; the hall's script sets it): Train is offered when it is non-zero.</summary>
    public const ushort TrainMaskCell = 0x6DA8;

    public const int Npc = 0x84, IconOrder = 0xBF, RandomByte = 0xAB, InPlay = 0x10D;

    /// <summary>
    /// The entries shown, in order (0x19B2D–0x19BA3, drawn from row 12 at 0x19BB2). With a current character: Drop,
    /// Modify, View, Remove, Save and Begin on, Load off, Train on when the hall's mask is non-zero or the debug flag
    /// DS:470C is set (and it then stays on until no character is current: the program never clears it in that branch).
    /// Without one: those off, Load on. Create, Add and Exit keep their initial enabled bytes (all 1 in v1.3).
    /// </summary>
    public static bool[] Enabled(bool hasCurrent, bool trainOn, bool[]? previous = null)
    {
        var e = new bool[Count];
        e[(int)PartyMenuItem.Create] = e[(int)PartyMenuItem.Add] = e[(int)PartyMenuItem.Exit] = true;
        if (hasCurrent)
        {
            e[(int)PartyMenuItem.Drop] = e[(int)PartyMenuItem.Modify] = e[(int)PartyMenuItem.View] = true;
            e[(int)PartyMenuItem.Remove] = e[(int)PartyMenuItem.Save] = e[(int)PartyMenuItem.Begin] = true;
            e[(int)PartyMenuItem.Train] = trainOn || (previous?[(int)PartyMenuItem.Train] ?? false);
        }
        else
            e[(int)PartyMenuItem.Load] = true;
        return e;
    }

    /// <summary>A player character: the NPC byte below 0x80.</summary>
    public static bool IsPlayerCharacter(EclCharacter c) => c[Npc] < 0x80;

    /// <summary>
    /// The join 0x1F42C (also ADDNPC's): the record goes last in the list and becomes current; its icon slot 0xBF is the
    /// first of 0..7 no other member holds; the party size ($6E3E) goes up by one; then the level stats 0x2AA87 run on
    /// it (<paramref name="levelStats"/>).
    /// </summary>
    public static void Join(EclParty party, EclCharacter c, Action<EclCharacter>? levelStats)
    {
        c[IconOrder] = 0xFF;
        var used = party.Members.Select(m => (int)m[IconOrder]).Where(x => x < 8).ToHashSet();
        byte slot = 0;
        while (slot < 8 && used.Contains(slot))
            slot++;
        c[IconOrder] = slot;
        party.Members.Add(c);
        party.CurrentIndex = party.Members.Count - 1;
        levelStats?.Invoke(c);
    }

    /// <summary>
    /// One pick in Add (0x1CAA2–0x1CBC5). An empty party takes the character. Otherwise the party is walked, counting
    /// player characters, until a member with the same name and byte 0xAB; when none matches, a player character joins
    /// while fewer than 6 were counted, an NPC (0x84 ≥ 0x80) while the party has fewer than 8. Returns whether it
    /// joined, and the count the menu's loop tests (it leaves when that count is above 5 or the party is above 7).
    /// </summary>
    public static (bool Joined, int PlayerCharacters) TryAdd(EclParty party, EclCharacter c, Action<EclCharacter>? levelStats)
    {
        if (party.Members.Count == 0)
        {
            Join(party, c, levelStats);
            return (true, 0);
        }
        int pcs = 0;
        foreach (var m in party.Members)
        {
            if (m.Name == c.Name && m[RandomByte] == c[RandomByte])
                return (false, pcs);
            if (IsPlayerCharacter(m))
                pcs++;
        }
        bool pc = IsPlayerCharacter(c);
        if ((pc && pcs < 6) || (!pc && party.Members.Count < 8))
        {
            Join(party, c, levelStats);
            if (pc)
                pcs++;
            return (true, pcs);
        }
        return (false, pcs);
    }

    /// <summary>Add's loop end test (0x1CBD6–0x1CBF2): Exit, or more than 5 player characters counted, or more than 7 members.</summary>
    public static bool AddFull(EclParty party, int playerCharacters) => playerCharacters > 5 || party.Members.Count > 7;

    /// <summary>
    /// 0x1CC10 (Remove and Drop): the current character leaves the list. When it was the first, the next one becomes
    /// current (none when it was alone); otherwise the one before it.
    /// </summary>
    public static void RemoveCurrent(EclParty party)
    {
        int i = party.CurrentIndex;
        if (i < 0 || i >= party.Members.Count)
            return;
        party.Members.RemoveAt(i);
        party.CurrentIndex = i == 0 ? (party.Members.Count > 0 ? 0 : -1) : i - 1;
    }

    /// <summary>Home / End on the menu (0x19C9E → 0x23819): the previous / next member, wrapping [?] (the routine is shared with the camp).</summary>
    public static void Select(EclParty party, int step)
    {
        int n = party.Members.Count;
        if (n == 0)
            return;
        party.CurrentIndex = ((party.CurrentIndex + step) % n + n) % n;
    }
}
