using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>The camp's rules (docs/re/camp.md) on hand-made records: rest time, effect timeout, spells, order.</summary>
public class CampTests
{
    /// <summary>A spell table with the given class, level and effect bytes.</summary>
    private static SpellTable Table(params (int Id, byte Class, byte Level, byte Effect)[] spells)
    {
        var d = new Dictionary<int, SpellInfo>();
        foreach (var (id, cls, lvl, eff) in spells)
            d[id] = new SpellInfo(cls, lvl, 0, 0, 0, 0, 0, 0, 0, 0, eff, 0, 0, 0, 0, 0);
        return new SpellTable(d);
    }

    private static EclCharacter Member(string name = "A")
    {
        var c = new EclCharacter { Name = name };
        c[0x10D] = 1;
        return c;
    }

    private static EclMemory Memory(params EclCharacter[] members)
    {
        var m = new EclMemory();
        var p = new EclParty();
        p.Members.AddRange(members);
        p.CurrentIndex = members.Length > 0 ? 0 : -1;
        m.Party = p;
        m.Write(EclPartyRules.PartySizeCell, (ushort)members.Length);
        return m;
    }

    private static byte[] Effect(byte id, ushort minutes)
    {
        var e = new byte[EclCharacter.EffectRecordSize];
        e[0] = id;
        e[1] = (byte)minutes;
        e[2] = (byte)(minutes >> 8);
        return e;
    }

    // ---- Rest time ----------------------------------------------------------------------------------------------

    [Fact]
    public void Rest_menu_increments_minutes_by_five_and_carries()
    {
        var s = new CampState();
        var menu = new RestTimeMenu(s, null);
        for (int i = 0; i < 12; i++)
            menu.Press(RestTimeMenu.Key.Up);                               // Up = Inc on the minutes
        Assert.Equal((0, 1, 0), menu.Shown);
        menu.Press(RestTimeMenu.Key.Down);
        Assert.Equal((0, 0, 55), menu.Shown);                              // borrowed from the hour
        menu.Press(RestTimeMenu.Key.Left);                                 // minutes → hours
        Assert.Equal(CampTime.Hours, menu.Selected);
        menu.Press(RestTimeMenu.Key.Left);
        menu.Press(RestTimeMenu.Key.Left);                                 // days wrap to minutes
        Assert.Equal(CampTime.TenMinutes, menu.Selected);
        menu.Press(RestTimeMenu.Key.Right);                                // minutes wrap to days
        Assert.Equal(CampTime.Days, menu.Selected);
        Assert.False(menu.Press(RestTimeMenu.Key.Exit));
        Assert.True(menu.Press(RestTimeMenu.Key.Rest));
    }

    [Fact]
    public void Days_fold_from_months_and_cap_at_99()
    {
        var s = new CampState();
        var menu = new RestTimeMenu(s, null) { Selected = CampTime.Days };
        for (int i = 0; i < 120; i++)
            menu.Press(RestTimeMenu.Key.Inc);
        Assert.Equal(99, s.RestTime[CampTime.Days]);
        Assert.Equal(0, s.RestTime[CampTime.Months]);
    }

    [Fact]
    public void Decrement_with_nothing_above_clears_the_rest_time()
    {
        var s = new CampState();
        s.RestTime[CampTime.Minutes] = 3;
        CampTime.Decrement(s, 5, CampTime.Minutes, null);                 // nothing to borrow from: all cleared
        Assert.False(s.RestTimeLeft);
        CampTime.Decrement(s, 5, CampTime.Minutes, null);                 // nothing left: no change
        Assert.All(s.RestTime, w => Assert.Equal(0, w));
    }

    // ---- Effect timeout 0x23DCC -----------------------------------------------------------------------------------

    [Fact]
    public void Effects_age_in_ten_minute_steps_and_expire()
    {
        var c = Member();
        c.Effects.Add(Effect(1, 25));
        c.Effects.Add(Effect(2, 0));                                       // permanent: skipped
        var m = Memory(c);
        var removed = new List<byte>();
        var s = new CampState();
        CampTime.TimeOutEffects(m, s, 2, CampTime.TenMinutes, (x, e) => { removed.Add(e[0]); x.Effects.Remove(e); });
        Assert.Equal(5, c.Effects[0][1]);                                  // 25 − 10 − 10
        Assert.Equal(1, s.EffectFlags[0]);
        CampTime.TimeOutEffects(m, s, 5, CampTime.Minutes, (x, e) => { removed.Add(e[0]); x.Effects.Remove(e); });
        Assert.Equal([1], removed);
        Assert.Single(c.Effects);
        Assert.Equal(0, s.EffectFlags[0]);                                 // only a permanent effect left
    }

    [Fact]
    public void Removing_the_heads_successor_ages_the_head_again_in_that_step()
    {
        var c = Member();
        c.Effects.Add(Effect(1, 30));
        c.Effects.Add(Effect(2, 5));
        c.Effects.Add(Effect(3, 30));
        var m = Memory(c);
        CampTime.TimeOutEffects(m, new CampState(), 1, CampTime.TenMinutes, (x, e) => x.Effects.Remove(e));
        Assert.Equal(2, c.Effects.Count);
        Assert.Equal(10, c.Effects[0][1]);                                 // 30 − 10 − 10: aged twice
        Assert.Equal(20, c.Effects[1][1]);
    }

    [Fact]
    public void In_camp_unflagged_creatures_are_not_aged()
    {
        var c = Member();
        c.Effects.Add(Effect(1, 30));
        var m = Memory(c);
        var s = new CampState { Camping = true };
        CampTime.TimeOutEffects(m, s, 1, CampTime.TenMinutes, (x, e) => x.Effects.Remove(e));
        Assert.Equal(30, c.Effects[0][1]);
        s.EffectFlags[0] = 1;
        CampTime.TimeOutEffects(m, s, 1, CampTime.TenMinutes, (x, e) => x.Effects.Remove(e));
        Assert.Equal(20, c.Effects[0][1]);
    }

    // ---- Spells ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Preparation_time_is_base_hours_plus_15_minutes_a_level()
    {
        var t = Table((3, 0, 1, 0), (22, 0, 2, 0), (45, 1, 3, 0));
        var sp = new CampSpells(t, it => it[0x2E] == 99);
        var c = Member();
        c[0x17] = 3 + 0x80;
        c[0x18] = 22 + 0x80;
        Assert.Equal(4 * 60 + 15 * 3, sp.PrepMinutes(c));
        Assert.Equal(4, c[0x2C]);
        var scroll = new byte[EclCharacter.ItemRecordSize];
        scroll[0x2E] = 99;
        scroll[0x3C] = 45 + 0x80;
        c.Items.Add(scroll);
        Assert.Equal(6 * 60 + 15 * 3 + 15 * 3, sp.PrepMinutes(c));        // a level-3 spell: 6 hours
        CampSpells.ResetMemorise(c);
        sp.ResetScribe(c);
        Assert.Equal(0, sp.PrepMinutes(c));
        Assert.Equal(45, scroll[0x3C]);
    }

    [Fact]
    public void Memorise_marks_pending_into_the_first_empty_slot_and_sorts()
    {
        var t = Table((3, 0, 1, 0), (5, 0, 1, 0), (15, 1, 1, 0));
        var sp = new CampSpells(t, _ => false);
        var c = Member();
        c[0xB2] = 2;                                                       // cleric level 1: two
        c[0x17] = 5;
        Assert.True(sp.MarkMemorise(c, 3));
        Assert.Equal(0, sp.SlotsLeft(c, 1, 0));
        Assert.False(sp.MarkMemorise(c, 5));
        Assert.False(sp.MarkMemorise(c, 15));                              // no magic-user slot
        var slots = Enumerable.Range(0x17, 21).Select(o => c[o]).ToList();
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3 + 0x80, 5], slots);
        var rows = sp.List(c, CampSpells.ListKind.ToMemorise);
        Assert.Equal([new CampSpellRow(3, true, null, 1)], rows);
    }

    [Fact]
    public void Resting_finishes_pending_spells_after_the_base_hours()
    {
        var t = Table((3, 0, 1, 0), (5, 0, 1, 0));
        var sp = new CampSpells(t, _ => false);
        var c = Member();
        c[0xB2] = 2;
        sp.MarkMemorise(c, 3);
        sp.MarkMemorise(c, 5);
        var m = Memory(c);
        var state = new CampState { Camping = true };
        var rest = new CampRest(m, state, sp, _ => 0, (x, e) => x.Effects.Remove(e), _ => false);
        var ui = new ScriptUi();
        Assert.False(rest.RestForPreparation(ui));                         // 4 h + 2 × 15 min
        Assert.Equal([(c, 3), (c, 5)], ui.Done);
        Assert.Equal(3, c[0x2A]);
        Assert.Equal(5, c[0x2B]);
        Assert.Equal("04:30", GameClock.Format(m));
    }

    private sealed class ScriptUi : ICampRestUi
    {
        public List<(EclCharacter, int)> Done { get; } = [];
        public bool ChooseRestTime(RestTimeMenu menu) => true;
        public bool StopAsked() => false;
        public void ShowRestTime() { }
        public void PartyHealed(bool anyone) { }
        public void Memorised(EclCharacter c, int spell) => Done.Add((c, spell));
        public void Scribed(EclCharacter c, int spell) => Done.Add((c, spell));
        public void Interrupted() { }
    }

    [Fact]
    public void Scribing_refuses_known_and_pending_spells_and_finishing_drops_an_empty_scroll()
    {
        var t = Table((15, 1, 1, 0));
        var sp = new CampSpells(t, it => it[0x2E] == 99);
        var c = Member();
        c[0xB5] = 1;                                                       // magic-user level 1
        var scroll = new byte[EclCharacter.ItemRecordSize];
        scroll[0x2E] = 99;
        scroll[0x30] = 0xD2;                                               // one spell
        scroll[0x3C] = 15;
        c.Items.Add(scroll);
        Assert.Equal(CampSpells.ScribeResult.Marked, sp.MarkScribe(c, 15));
        Assert.Equal(15 + 0x80, scroll[0x3C]);
        Assert.Equal(CampSpells.ScribeResult.AlreadyScribing, sp.MarkScribe(c, 15));
        bool done = false;
        var got = new List<int>();
        Assert.Equal(0, sp.FinishScribe(c, ref done, (_, s) => got.Add(s)));
        Assert.Equal([15], got);
        Assert.Equal(1, c[0x32 + 15]);
        Assert.Empty(c.Items);
        Assert.Equal(CampSpells.ScribeResult.AlreadyKnown, sp.MarkScribe(c, 15));
    }

    [Fact]
    public void Healing_comes_every_288_ticks()
    {
        var c = Member();
        var m = Memory(c);
        var state = new CampState { Camping = true };
        int heals = 0;
        var rest = new CampRest(m, state, new CampSpells(Table(), _ => false), _ => 0, (x, e) => x.Effects.Remove(e),
            _ => { heals++; return true; });
        state.RestTime[CampTime.Days] = 2;
        var ui = new ScriptUi();
        Assert.False(rest.Run(ui));
        Assert.Equal(2, heals);                                            // 576 ticks = 2 days
        Assert.Equal(0, state.HealTicks);
    }

    [Fact]
    public void Display_names_spell_effects_and_drops_unlisted_ones()
    {
        var sp = new CampSpells(Table((1, 0, 1, 1), (8, 1, 1, 0x30)), _ => false);
        var c = Member();
        c.Effects.Add(Effect(1, 10));    // Bless: spell 1
        c.Effects.Add(Effect(9, 10));    // listed, no spell has it: Funky
        c.Effects.Add(Effect(55, 0));    // poisoned: fixed label
        c.Effects.Add(Effect(3, 0));     // not shown
        var lines = sp.Display(c);
        Assert.Equal([new CampEffectLine(1, 1, false), new CampEffectLine(0, 9, true), new CampEffectLine(0, 55, false)], lines);
    }

    // ---- Party order and drop -------------------------------------------------------------------------------------

    [Fact]
    public void Order_moves_wrap_at_the_ends_and_keep_the_current_record()
    {
        var a = Member("A"); var b = Member("B"); var c = Member("C");
        var m = Memory(a, b, c);
        var p = m.Party!;
        CampParty.MoveUp(p);                                               // A first → last
        Assert.Equal("BCA", string.Concat(p.Members.Select(x => x.Name)));
        Assert.Same(a, p.Current);
        CampParty.MoveDown(p);                                             // A last → first
        Assert.Equal("ABC", string.Concat(p.Members.Select(x => x.Name)));
        p.SetCurrent(c);
        CampParty.MoveUp(p);
        Assert.Equal("ACB", string.Concat(p.Members.Select(x => x.Name)));
        Assert.Same(c, p.Current);
    }

    [Fact]
    public void Drop_makes_the_previous_member_current_and_shrinks_the_party_size()
    {
        var a = Member("A"); var b = Member("B");
        var m = Memory(a, b);
        var p = m.Party!;
        p.SetCurrent(b);
        Assert.Same(b, CampParty.Drop(p, m));
        Assert.Same(a, p.Current);
        Assert.Equal(1, m.Read(EclPartyRules.PartySizeCell));
        Assert.Null(CampParty.Drop(p, m));                                 // the last one: quit-to-DOS instead
    }
}
