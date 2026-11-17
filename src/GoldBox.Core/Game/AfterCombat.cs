using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>What the after-combat step found (GAME.OVR 0x5A47), for the summary and the loot step.</summary>
/// <param name="PartyDefeated">DS:49FF after the check: no party member left standing (never in a duel).</param>
/// <param name="PartyFled">DS:443B: a party member fled and none is okay or animated.</param>
/// <param name="Won">DS:84DA: a party member is okay or animated (in a duel: the dueller is okay).</param>
/// <param name="HostileFound">DS:443A: a hostile creature that did not flee was in the list (the summary then says the party won).</param>
/// <param name="ExperienceShare">DS:84D6: each character's share of the experience (0 when not won).</param>
/// <param name="MonstersDefeated">VM $6DC8 after the clean-up: removed monsters and hostiles that were out of play.</param>
public sealed record AfterCombatOutcome(bool PartyDefeated, bool PartyFled, bool Won, bool HostileFound, int ExperienceShare,
    int MonstersDefeated);

/// <summary>
/// The after-combat step (GAME.OVR 0x6AA5, docs/re/combat-format.md "After combat"): outcome, experience, removal
/// of the monsters from the party list, then the loot step and the one-fight settings reset. It works on the party
/// list (<see cref="EclParty.Members"/>, the program's DS:5D96 list) and the VM cells it writes.
/// </summary>
public sealed class AfterCombat
{
    /// <summary>VM cells: combat result ($6DC7: 0 won, 0x80 party destroyed, 0x81 party fled), monsters defeated, party size.</summary>
    public const ushort CombatResult = 0x6DC7, MonstersDefeated = 0x6DC8, PartySize = 0x6E3E, NoCombatItems = 0x6DE3,
        MirrorDuel = 0x6DE6;
    /// <summary>One-fight settings cleared at the end (0x6BAF–0x6BE6): $6E70–$6E72, $6DE3, $6DE6.</summary>
    public static readonly ushort[] ClearedAtEnd = [0x6E70, 0x6E71, 0x6E72, 0x6DE3, 0x6DE6];

    public const ushort ResultWon = 0, ResultDestroyed = 0x80, ResultFled = 0x81;

    private readonly EclMemory _memory;
    private readonly EncounterState _state;
    private readonly Func<ushort, ushort> _random;
    private readonly List<string> _diagnostics;

    /// <param name="random">Random(n), 0..n−1 (the program's dice are 1 + Random(n)).</param>
    public AfterCombat(EclMemory memory, EncounterState state, Func<ushort, ushort> random, List<string> diagnostics)
    {
        _memory = memory;
        _state = state;
        _random = random;
        _diagnostics = diagnostics;
    }

    private List<EclCharacter> List => _memory.Party?.Members ?? [];

    /// <summary>
    /// Combat record byte +0x13 (set at the fight start, GAME.OVR 0xD8B2, for list positions past $6E3E). Without a
    /// fight the records have no combat record and the program reads byte 0000:0013 instead ([?] machine-dependent;
    /// taken as "not 1", which makes every member count as party).
    /// </summary>
    private bool NotParty(EclCharacter c) => _state.NotParty.Contains(c);

    private static CreatureStatus StatusOf(EclCharacter c) => (CreatureStatus)c[CombatRules.Status];

    private static void SetOkay(EclCharacter c)
    {
        c[CombatRules.Status] = (byte)CreatureStatus.Okay;
        c[CombatRules.InPlay] = 1;
    }

    /// <summary>Runs 0x6AA5 up to the loot step; the caller runs the loot or defeat screen, then <see cref="Finish"/>.</summary>
    public AfterCombatOutcome Run()
    {
        _memory.Write(CombatResult, ResultWon);
        _hostileFound = false;
        var r = Results();
        int defeated = Cleanup();
        return r with { MonstersDefeated = defeated, HostileFound = _hostileFound };
    }

    /// <summary>After the loot (or the defeat screen): the one-fight settings are cleared (0x6BAF–0x6BE6).</summary>
    public void Finish(AfterCombatOutcome outcome)
    {
        if (outcome.PartyDefeated && !_state.Duel)
            _memory.Write(CombatResult, ResultDestroyed);
        else
            _state.TreasureItems.Clear(); // the treasure list is freed after the loot step (0x6AEA–0x6B32)
        foreach (var a in ClearedAtEnd)
            _memory.Write(a, 0);
    }

    private bool _hostileFound;

    /// <summary>GAME.OVR 0x5A47: who won, experience, what happens to the party members.</summary>
    private AfterCombatOutcome Results()
    {
        var list = List;
        int notInPlay = 0; // DS:84D5
        bool defeated = true; // DS:49FF
        bool fled = false; // DS:443B
        foreach (var c in list.TakeWhile(c => !NotParty(c)))
            if (StatusOf(c) == CreatureStatus.Fled)
                fled = true;
        if (_state.Duel)
            defeated = false;
        bool won = false; // DS:84DA
        int share = 0;

        if (!_state.Duel)
        {
            foreach (var c in list.TakeWhile(c => !NotParty(c)))
            {
                var s = StatusOf(c);
                if ((s is CreatureStatus.Fled or CreatureStatus.Animated or CreatureStatus.Okay || _memory.Read(MirrorDuel) == 1)
                    && c[CombatRules.Hostile] == 0 && c[CombatRules.Npc] < 0x80)
                    defeated = false;
                if (s is CreatureStatus.Animated or CreatureStatus.Okay)
                {
                    won = true;
                    fled = false;
                }
                if (c[CombatRules.InPlay] == 0 || s == CreatureStatus.Animated)
                    notInPlay++;
            }
            if (won)
            {
                share = Share(notInPlay);
                CombatRules.AwardExperience(list, share);
            }
            if (!defeated)
            {
                for (int i = 0; i < list.Count && !NotParty(list[i]);)
                {
                    var c = list[i];
                    if (!fled)
                    {
                        switch (StatusOf(c))
                        {
                            case CreatureStatus.Fled: SetOkay(c); break;
                            case CreatureStatus.Dying: c[CombatRules.Status] = (byte)CreatureStatus.Unconscious; break;
                            case CreatureStatus.Unconscious when c[CombatRules.HpCurrent] > 0: SetOkay(c); break;
                        }
                        i++;
                    }
                    else
                    {
                        _memory.Write(CombatResult, ResultFled);
                        if (StatusOf(c) == CreatureStatus.Fled)
                        {
                            SetOkay(c);
                            i++;
                        }
                        else
                        {
                            Remove(i, decrementPartySize: true); // members left behind leave the party
                        }
                    }
                }
            }
            else
            {
                while (list.Count > 0 && !NotParty(list[0]))
                    Remove(0, decrementPartySize: true);
                _memory.Write(PartySize, 0);
            }
        }
        else
        {
            foreach (var c in list.ToList())
            {
                if (c[CombatRules.InPlay] == 1 && StatusOf(c) == CreatureStatus.Okay && c[CombatRules.Hostile] != 1)
                {
                    won = true;
                    share = Share(notInPlay); // the program computes and awards a share for each such creature
                    CombatRules.AwardExperience(list, share);
                }
            }
            foreach (var c in list)
            {
                if (StatusOf(c) is CreatureStatus.Okay or CreatureStatus.Animated)
                    c[CombatRules.InPlay] = 1;
                if (StatusOf(c) == CreatureStatus.Dying)
                    c[CombatRules.Status] = (byte)CreatureStatus.Unconscious;
            }
        }
        return new AfterCombatOutcome(defeated, fled, won, false, share, 0);
    }

    /// <summary>
    /// GAME.OVR 0x557E: the share. Mirror duel (DS:84D4 and $6DE6 ≠ 0): 100 × level_highest of the current
    /// character. Else every hostile creature that did not flee adds its coins to the pool and its experience, and
    /// (unless $6DE3 = 1) its items go to the treasure list: an item with a value above 0 always, else while fewer
    /// than 8 were taken on 1d10 ≤ 3. Then the pool and the items' plus add experience, and the total is divided by
    /// ($6E3E − members out of play).
    /// </summary>
    private int Share(int notInPlay)
    {
        if (_state.Duel && _memory.Read(MirrorDuel) != 0)
        {
            var cur = _memory.Party?.Current;
            return cur is null ? 0 : (short)(ushort)((sbyte)cur[CombatRules.LevelHighest] * 100);
        }
        long total = 0;
        int taken = 0;
        foreach (var c in List)
        {
            if (c[CombatRules.Hostile] != 1 || StatusOf(c) == CreatureStatus.Fled)
                continue;
            _hostileFound = true;
            for (int k = 0; k < 7; k++)
                _state.Money[k] += c.Word(CombatRules.Money + 2 * k);
            total += CombatRules.CreatureExperience(c);
            if (_memory.Read(NoCombatItems) == 1)
                continue;
            foreach (var item in c.Items)
            {
                ushort value = (ushort)(item[CombatRules.ItemValue] | item[CombatRules.ItemValue + 1] << 8);
                if (value > 0 || (taken < 8 && _random(10) + 1 <= 3))
                {
                    taken++;
                    var copy = (byte[])item.Clone();
                    copy[CombatRules.ItemEquipped] = 0;
                    _state.TreasureItems.Insert(0, copy); // each copy becomes the new head
                }
            }
        }
        total += CombatRules.PoolExperience(_state.Money);
        // [?] The loop stops at the item DS:5D9A points to (set during the fight); no fight is modelled, so all items count.
        foreach (var item in _state.TreasureItems)
            total += CombatRules.ItemExperience(item);
        int divisor = (ushort)(_memory.Read(PartySize) - notInPlay);
        if (divisor == 0)
        {
            _diagnostics.Add("[?] after combat: experience divided by 0 (the program's long division halts it); share 0");
            return 0;
        }
        return (int)(total / divisor);
    }

    /// <summary>
    /// GAME.OVR 0x672A: every creature that is not a party member (+0x13) or is hostile leaves the list; those out of
    /// play (0x10D ≠ 1) are counted in $6DC8. Hostile party members also lower $6E3E. The current character becomes
    /// the head of the list.
    /// </summary>
    private int Cleanup()
    {
        var list = List;
        int count = 0;
        _memory.Write(MonstersDefeated, 0);
        for (int i = 0; i < list.Count;)
        {
            var c = list[i];
            bool notParty = NotParty(c);
            if (notParty || c[CombatRules.Hostile] == 1)
            {
                if (c[CombatRules.InPlay] != 1)
                    count++;
                Remove(i, decrementPartySize: !notParty);
            }
            else
            {
                i++;
            }
        }
        _memory.Write(MonstersDefeated, (ushort)count);
        _state.NotParty.Clear();
        if (_memory.Party is { } p)
            p.CurrentIndex = p.Members.Count > 0 ? 0 : -1;
        return count;
    }

    /// <summary>GAME.OVR 0x1CC10: unlink and free a creature; unless told otherwise, $6E3E − 1.</summary>
    private void Remove(int index, bool decrementPartySize)
    {
        var c = List[index];
        List.RemoveAt(index);
        _state.Monsters.Remove(c);
        _state.NotParty.Remove(c);
        if (decrementPartySize)
            _memory.Write(PartySize, (ushort)(_memory.Read(PartySize) - 1));
    }
}
