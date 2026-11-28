using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>
/// The cast 0x27C38 outside a fight (read whole s49) and the Items page's Use 0x22AC4 outside a fight: the combat-only
/// branch with its spell ("Lose it?") and item ("Use it?") questions, the announce flag, the pick 0x27AA9 and the
/// item's charge or scroll spell spent when done. Also the spell globals DS:4844 / DS:4845 (Sleep, Enlarge).
/// </summary>
public class CampCastTests
{
    // Spell 5: target mode 1 (the caster). Spell 9: target mode 0 (combat only). Spell 3: target mode 2 (one member).
    private static readonly SpellTable Table = new(new Dictionary<int, SpellInfo>
    {
        [3] = new(0, 1, 0, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 0),
        [5] = new(0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0x0E, 1, 0, 0, 0, 0),
        [9] = new(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
        [0x22] = new(1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
        [58] = new(0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0x0E, 1, 0, 0, 0, 0), // as 5: the caster
    });

    private static readonly BaseItemTable Items = MakeItems();

    private static BaseItemTable MakeItems()
    {
        var t = new byte[BaseItemTable.EntrySize * BaseItemTable.Count];
        t[1 * BaseItemTable.EntrySize + BaseItemTable.Slot] = 11; // type 1: a scroll
        t[2 * BaseItemTable.EntrySize + BaseItemTable.Slot] = 2;  // type 2: not a scroll
        return new BaseItemTable(t);
    }

    private static (CampCast Cast, EclParty Party, EclCharacter Caster) Make()
    {
        var p = new EclParty();
        var a = new EclCharacter { Name = "Anna" };
        a[0x10C] = 0;
        a[0x10D] = 1;
        a[0x96] = 3;
        var b = new EclCharacter { Name = "Bo" };
        p.Members.Add(a);
        p.Members.Add(b);
        p.SetCurrent(a);
        var fx = new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current };
        return (new CampCast(new FightSpells(Table, fx, _ => 0), p), p, a);
    }

    private static byte[] Wand(byte power, byte charges)
    {
        var it = new byte[63];
        it[0x2E] = 2;
        it[FightSpells.ItemPower] = power;
        it[FightSpells.ItemCharges] = charges;
        it[FightSpells.ItemCount] = 1;
        return it;
    }

    private static byte[] Scroll(byte a, byte b, byte c)
    {
        var it = new byte[63];
        it[0x2E] = 1;
        (it[0x3C], it[0x3D], it[0x3E]) = (a, b, c);
        it[0x30] = 0xD4;
        return it;
    }

    [Fact]
    public void Spell_globals_start_as_the_unpacker_leaves_them()
    {
        // DS:4844 / DS:4845 before their first writer: 100 / 11 (s60: the EXEPACK unpacker under emulation, DOSBox-X).
        var g = new SpellGlobals();
        Assert.Equal(100, g.Ds4844);
        Assert.Equal(11, g.Ds4845);
    }

    [Fact]
    public void A_combat_only_spell_asks_lose_it_and_yes_removes_the_memorised_copy()
    {
        foreach (bool yes in new[] { false, true })
        {
            var (cast, _, a) = Make();
            a[FightSpells.Memorised] = 9;
            int ends = 0;
            (int Spell, bool Item)? asked = null;
            bool done = false;
            var r = cast.Run(9, announce: true, new CampCast.Hooks
            {
                CombatOnly = (s, item) => { asked = (s, item); return yes; },
                Announce = (_, _) => Assert.Fail("no cast message for a combat-only spell"),
                End = () => ends++,
            }, ref done);
            Assert.Equal(CampCast.Outcome.CombatOnly, r);
            Assert.Equal((9, false), asked);
            Assert.Equal(yes ? 0 : 9, a[FightSpells.Memorised]);
            Assert.False(done); // a spell's yes never sets done
            Assert.Equal(1, ends); // 0x27F44 on every way out
        }
    }

    [Fact]
    public void A_combat_only_item_asks_use_it_and_yes_spends_a_charge_with_no_effect()
    {
        foreach (bool yes in new[] { false, true })
        {
            var (cast, _, a) = Make();
            var wand = Wand(9, 2);
            a.Items.Add(wand);
            int messages = 0, ends = 0;
            (int Spell, bool Item)? asked = null;
            bool done = cast.UseItem(Items, wand, () => 0, (_, _) => messages++, new CampCast.Hooks
            {
                CombatOnly = (s, item) => { asked = (s, item); return yes; },
                End = () => ends++,
            });
            Assert.Equal(yes, done);
            Assert.Equal((9, true), asked);
            Assert.Equal(1, messages); // a power item's message comes first (0x22B6C–0x22BF7)
            Assert.Equal(yes ? 1 : 2, wand[FightSpells.ItemCharges]);
            Assert.Equal(1, ends);
            Assert.False(cast.Spells.UsingItem); // DS:6DBF back to 0
        }
    }

    [Fact]
    public void A_scroll_with_a_combat_only_spell_gets_the_item_question_too()
    {
        var (cast, _, a) = Make();
        var scroll = Scroll(9, 5, 0);
        a.Items.Add(scroll);
        bool? item = null;
        bool done = cast.UseItem(Items, scroll, () => 9, (_, _) => Assert.Fail("no power-item message for a scroll"),
            new CampCast.Hooks { CombatOnly = (_, i) => { item = i; return true; } });
        Assert.True(done);
        Assert.True(item); // DS:6DBF = 1 for scrolls too (0x22BFC)
        Assert.Equal(0, scroll[0x3C]); // 0x2A2A2: the spell leaves the scroll
        Assert.Equal(5, scroll[0x3D]);
    }

    [Fact]
    public void An_item_cast_is_not_announced_and_keeps_the_memorised_copy()
    {
        var (cast, _, a) = Make();
        a[FightSpells.Memorised] = 5;
        var wand = Wand(5, 1);
        a.Items.Add(wand);
        bool announced = false;
        bool done = cast.UseItem(Items, wand, () => 0, null, new CampCast.Hooks { Announce = (_, _) => announced = true });
        Assert.True(done);
        Assert.False(announced);
        Assert.Equal(5, a[FightSpells.Memorised]);
        Assert.DoesNotContain(wand, a.Items); // the last charge: the item leaves the list
    }

    [Fact]
    public void A_memorised_cast_is_announced_once_when_asked_and_uses_the_copy()
    {
        var (cast, _, a) = Make();
        a[FightSpells.Memorised] = 5;
        var seen = new List<int>();
        bool done = false;
        var r = cast.Run(5, announce: true, new CampCast.Hooks { Announce = (_, s) => seen.Add(s) }, ref done);
        Assert.Equal(CampCast.Outcome.Cast, r);
        Assert.True(done);
        Assert.Equal([5], seen);
        Assert.Equal(0, a[FightSpells.Memorised]);

        // the loot menu's D passes announce 0 (0x6605): no message
        a[FightSpells.Memorised] = 5;
        seen.Clear();
        cast.Run(5, announce: false, new CampCast.Hooks { Announce = (_, s) => seen.Add(s) }, ref done);
        Assert.Empty(seen);
    }

    /// <summary>Spell 58 (0x29E65) with two records of poison 0x37: the program's handler 22 finds the second on the
    /// 0x16's removal and kills; the s51 fix (user, not an option) removes every 0x37 and 0x16 under the lock.</summary>
    [Fact]
    public void Spell_58_removes_every_poison_record_and_never_kills()
    {
        foreach (var ids in new[] { new byte[] { 0x37, 0x16 }, [0x37, 0x37, 0x16], [0x37, 0x37, 0x16, 0x16, 33] })
        {
            var (cast, _, a) = Make();
            a[0x32] = 20;
            a[0x11B] = 11;
            foreach (var id in ids)
                EffectHandlers.AddEffect(a, id, 10, 0, onRemove: true);
            a[FightSpells.Memorised] = 58;
            bool done = false;
            Assert.Equal(CampCast.Outcome.Cast, cast.Run(58, announce: false, new CampCast.Hooks(), ref done));
            Assert.Equal(0, a[0x10C]);
            Assert.Equal(11, a[0x11B]);
            Assert.Equal(ids.Contains((byte)33) ? [33] : Array.Empty<byte>(), a.Effects.Select(e => e[0]));
        }
    }

    [Fact]
    public void A_member_pick_given_up_loses_nothing_and_spends_no_charge()
    {
        var (cast, _, a) = Make();
        a[FightSpells.Memorised] = 3;
        bool done = true;
        var r = cast.Run(3, announce: false, new CampCast.Hooks { PickMember = _ => null }, ref done);
        Assert.Equal(CampCast.Outcome.NoTarget, r);
        Assert.False(done);
        Assert.Equal(3, a[FightSpells.Memorised]);
        Assert.Null(cast.Remembered);

        var wand = Wand(3, 2);
        a.Items.Add(wand);
        Assert.False(cast.UseItem(Items, wand, () => 0, null, new CampCast.Hooks { PickMember = _ => null }));
        Assert.Equal(2, wand[FightSpells.ItemCharges]);
    }

    [Fact]
    public void No_spell_from_the_scroll_list_does_nothing()
    {
        var (cast, _, a) = Make();
        var scroll = Scroll(5, 0, 0);
        a.Items.Add(scroll);
        int ends = 0;
        Assert.False(cast.UseItem(Items, scroll, () => 0, null, new CampCast.Hooks { End = () => ends++ }));
        Assert.Equal(0, ends); // 0x22B30: no spell → straight out, the cast routine is not called
        Assert.Equal(5, scroll[0x3C]);
    }

    [Fact]
    public void Power_above_0x38_casts_power_minus_0x17()
    {
        var (cast, _, a) = Make();
        var wand = Wand(0x39, 1); // power 0x39 → spell 0x22
        var asked = new List<int>();
        a.Items.Add(wand);
        cast.UseItem(Items, wand, () => 0, null, new CampCast.Hooks { CombatOnly = (s, _) => { asked.Add(s); return false; } });
        Assert.Equal([0x22], asked);
    }

    [Fact]
    public void The_spell_globals_are_shared_through_the_game_memory()
    {
        var m = new EclMemory();
        var fx = new EffectHandlers(_ => 0);
        var one = new FightSpells(Table, fx, _ => 0) { Globals = m.SpellGlobals };
        var two = new FightSpells(Table, fx, _ => 0) { Globals = m.SpellGlobals };
        one.Ds4845 = 6;
        Assert.Equal(6, two.Ds4845);
    }

    /// <summary>Sleep leaves its last target's cost in DS:4845 and what is left of the budget in DS:4844 (0x2857F–
    /// 0x2861B); an Enlarge whose casting level is outside 1–6 (a class-2 caster: 12) keeps that DS:4845 and sets
    /// DS:4844 := 18 (0x282C3).</summary>
    [Fact]
    public void Sleep_feeds_a_later_enlarge_outside_levels_1_to_6()
    {
        var table = new SpellTable(new Dictionary<int, SpellInfo>
        {
            [12] = new(2, 1, 0, 0, 1, 0, 0, 2, 0, 0, 0x0C, 1, 0, 0, 0, 0),
            [21] = new(1, 1, 0, 0, 1, 0, 0, 4, 0, 0, 0x35, 1, 0, 0, 0, 0),
        });
        var (_, p, a) = Make();
        var t1 = new EclCharacter { Name = "T1" };
        var t2 = new EclCharacter { Name = "T2" };
        t1[FightSpells.HitDice] = 2;
        t2[FightSpells.HitDice] = 5; // race byte 0 → 10
        var m = new EclMemory();
        var fx = new EffectHandlers(_ => 3) { List = () => p.Members, CurrentCharacter = () => p.Current, InCombat = false };
        var spells = new FightSpells(table, fx, _ => 3) { Globals = m.SpellGlobals };
        spells.Targets.AddRange([t1, t2]);
        spells.Routine(21, a);
        Assert.Equal(10, m.SpellGlobals.Ds4845);
        byte budget = m.SpellGlobals.Ds4844;

        var other = new FightSpells(table, fx, _ => 3) { Globals = m.SpellGlobals };
        other.Targets.Add(t1);
        other.Routine(12, a);
        Assert.Equal(10, m.SpellGlobals.Ds4845);
        Assert.Equal(18, m.SpellGlobals.Ds4844);
        Assert.NotEqual(18, budget); // the budget was Sleep's own
    }
    /// <summary>
    /// DS:6DB5 (the remembered target) is a program global: 0x27AA9 defaults it to the caster only when it is nil
    /// (0x27AAF–0x27AC2) and the member pick writes its answer back through the pointer (0x27B11 → START 0x37E8). A second
    /// cast object on the same game memory (the command line's next View / Use) starts its pick there; giving up leaves
    /// it nil.
    /// </summary>
    [Fact]
    public void The_remembered_target_lives_on_the_game_memory()
    {
        var m = new EclMemory();
        var p = new EclParty();
        var a = new EclCharacter { Name = "Anna" };
        var b = new EclCharacter { Name = "Bo" };
        p.Members.AddRange([a, b]);
        p.SetCurrent(a);
        var fx = new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current };
        CampCast New() => new(new FightSpells(Table, fx, _ => 0) { Globals = m.SpellGlobals }, p);
        var starts = new List<EclCharacter?>();
        bool done = false;
        a[FightSpells.Memorised] = 3;
        New().Run(3, false, new CampCast.Hooks { PickMember = s => { starts.Add(s); return b; }, Routine = _ => { } }, ref done);
        Assert.Same(b, m.SpellGlobals.Ds6DB5);
        a[FightSpells.Memorised] = 3;
        var second = New();
        Assert.Same(b, second.Remembered);
        second.Run(3, false, new CampCast.Hooks { PickMember = s => { starts.Add(s); return null; }, Routine = _ => { } }, ref done);
        Assert.Equal([a, b], starts);
        Assert.Null(m.SpellGlobals.Ds6DB5);
        Assert.False(done);
        a[FightSpells.Memorised] = 3;
        New().Run(3, false, new CampCast.Hooks { PickMember = s => { starts.Add(s); return s; }, Routine = _ => { } }, ref done);
        Assert.Same(a, starts[^1]); // nil again: the caster
    }
    /// <summary>
    /// The remembered target DS:6DB5 goes when its member leaves the party (original bug fixed, user s51; camp.md "The
    /// remembered target DS:6DB5"): the party menu's Remove / Drop (0x1CC10), a script's LOADCHAR / DUMP
    /// (<see cref="EclPartyRules.RemoveCurrent"/>); another member leaving keeps it. The next pick starts on the caster.
    /// </summary>
    [Fact]
    public void A_member_leaving_the_party_clears_the_remembered_target()
    {
        var m = new EclMemory();
        var p = new EclParty();
        m.Party = p;
        var a = new EclCharacter { Name = "Anna" };
        var b = new EclCharacter { Name = "Bo" };
        var c = new EclCharacter { Name = "Cy" };
        p.Members.AddRange([a, b, c]);
        var fx = new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current };
        CampCast New() => new(new FightSpells(Table, fx, _ => 0) { Globals = m.SpellGlobals }, p);
        var starts = new List<EclCharacter?>();
        bool done = false;
        void Cast(EclCharacter? answer)
        {
            p.SetCurrent(a);
            a[FightSpells.Memorised] = 3;
            New().Run(3, false, new CampCast.Hooks { PickMember = s => { starts.Add(s); return answer ?? s; }, Routine = _ => { } }, ref done);
        }

        // Remove (party menu): another member leaving keeps it, its own member leaving clears it.
        Cast(b);
        p.SetCurrent(c);
        PartyMenu.RemoveCurrent(p, m.SpellGlobals);
        Assert.Same(b, m.SpellGlobals.Ds6DB5);
        p.SetCurrent(b);
        PartyMenu.RemoveCurrent(p, m.SpellGlobals);
        Assert.Null(m.SpellGlobals.Ds6DB5);
        Cast(null);
        Assert.Same(a, starts[^1]); // as with no target: the caster

        // LOADCHAR with bit 0x80 / DUMP (EclPartyRules.RemoveCurrent).
        p.Members.Add(b);
        Cast(b);
        Assert.Same(b, m.SpellGlobals.Ds6DB5);
        p.SetCurrent(b);
        new EclPartyRules(m, _ => 0).RemoveCurrent();
        Assert.Null(m.SpellGlobals.Ds6DB5);

        // A party replaced by a Load over the running game.
        p.Members.Add(b);
        Cast(b);
        m.SpellGlobals.PartyReplaced();
        Assert.Null(m.SpellGlobals.Ds6DB5);
    }
}
