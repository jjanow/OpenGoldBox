using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The door menu's Bash / Pick / Knock model (docs/re/geo-format.md "Door menu"): rule tests with made-up parties,
/// and replays of the DOSBox captures <c>door_pick_fail</c> and <c>door_bash</c> from their RandSeed.
/// </summary>
public class DoorRulesTests
{
    private static DoorMember M(byte str, byte exc = 0, byte openLocks = 0, byte status = 0, sbyte mu = 0, sbyte thief = 0,
        byte[]? spells = null) =>
        new(str, exc, openLocks, status, mu, thief, spells ?? new byte[DoorMember.SpellSlots]);

    /// <summary>A generator whose next Random(n) is k (searches seeds; small n only).</summary>
    private static TurboPascalRandom Rigged(params (ushort N, int Value)[] rolls)
    {
        for (uint s = 0; ; s++)
        {
            var g = new TurboPascalRandom(s);
            if (rolls.All(r => g.Next(r.N) == r.Value))
                return new TurboPascalRandom(s);
        }
    }

    [Fact]
    public void Bash_locked_table()
    {
        // (strength, exceptional, sides, highest success)
        (byte, byte, int)[] rows = [(3, 0, 1), (7, 0, 1), (8, 0, 2), (15, 0, 2), (16, 0, 3), (17, 0, 3), (18, 0, 3),
            (18, 50, 3), (18, 51, 4), (18, 99, 4), (18, 100, 5)];
        foreach (var (str, exc, best) in rows)
        {
            for (int roll = 1; roll <= 6; roll++)
            {
                var flags = new DoorMenuFlags();
                var r = DoorRules.Bash(DoorBarrier.Locked, [M(str, exc)], Rigged((6, roll - 1)), flags);
                Assert.Equal(roll <= best, r.Opened);
                Assert.Equal(1, r.Draws);
                Assert.True(flags.Bash);
            }
        }
    }

    [Fact]
    public void Bash_locked_strength_outside_the_table_never_rolls()
    {
        var rng = new TurboPascalRandom(1);
        var r = DoorRules.Bash(DoorBarrier.Locked, [M(19), M(2), M(18, 101)], rng, new DoorMenuFlags());
        Assert.False(r.Opened);
        Assert.Equal(0, r.Draws);
    }

    [Fact]
    public void Bash_barred_weak_member_clears_the_flag_but_the_loop_goes_on()
    {
        var flags = new DoorMenuFlags();
        var r = DoorRules.Bash(DoorBarrier.Barred, [M(17), M(25)], new TurboPascalRandom(1), flags);
        Assert.True(r.Opened);        // strength 25: no roll
        Assert.Equal(1, r.Member);
        Assert.Equal(0, r.Draws);
        Assert.False(flags.Bash);     // cleared by the first member in the same try
    }

    [Fact]
    public void Bash_barred_table()
    {
        (byte, byte, ushort, int)[] rows = [(18, 91, 6, 1), (18, 99, 6, 1), (18, 100, 6, 2), (19, 0, 6, 3), (20, 0, 6, 3),
            (21, 0, 6, 4), (22, 0, 6, 4), (23, 0, 6, 5), (24, 0, 8, 7)];
        foreach (var (str, exc, sides, best) in rows)
            for (int roll = 1; roll <= sides; roll++)
            {
                var flags = new DoorMenuFlags();
                var r = DoorRules.Bash(DoorBarrier.Barred, [M(str, exc)], Rigged((sides, roll - 1)), flags);
                Assert.Equal(roll <= best, r.Opened);
                Assert.True(flags.Bash);
            }
        var f2 = new DoorMenuFlags();
        Assert.False(DoorRules.Bash(DoorBarrier.Barred, [M(18, 90)], new TurboPascalRandom(5), f2).Opened);
        Assert.False(f2.Bash);
    }

    [Fact]
    public void Bash_ignores_status()
    {
        var r = DoorRules.Bash(DoorBarrier.Locked, [M(10, status: 2)], Rigged((6, 0)), new DoorMenuFlags());
        Assert.True(r.Opened);
    }

    [Fact]
    public void Pick_rolls_for_every_member_and_needs_status_okay()
    {
        var flags = new DoorMenuFlags();
        var r = DoorRules.Pick(DoorBarrier.Locked, [M(10), M(10, openLocks: 99, status: 1), M(10)], Rigged((100, 0), (100, 0), (100, 98)), flags);
        Assert.False(r.Opened);   // member 0 has no skill, member 1 is not okay, member 2 rolls 99 > 0
        Assert.Equal(3, r.Draws);
        Assert.False(flags.Pick);
    }

    [Fact]
    public void Pick_barred_never_rolls()
    {
        var flags = new DoorMenuFlags();
        var r = DoorRules.Pick(DoorBarrier.Barred, [M(10, openLocks: 100, thief: 5)], new TurboPascalRandom(3), flags);
        Assert.Equal(new DoorAttempt(false, -1, 0), r);
        Assert.False(flags.Pick);
    }

    [Fact]
    public void Knock_takes_the_first_memorised_spell_31()
    {
        var pending = new byte[DoorMember.SpellSlots];
        pending[0] = 0x80 | DoorRules.KnockSpell;   // chosen, not memorised: does not count
        var memorised = new byte[DoorMember.SpellSlots];
        memorised[4] = DoorRules.KnockSpell;
        var party = new[] { M(10, spells: pending), M(10, mu: 3, spells: memorised) };
        Assert.Equal(new DoorAttempt(true, 1, 0, 4), DoorRules.Knock(party));
        Assert.False(DoorRules.Knock([M(10, spells: pending)]).Opened);
    }

    [Fact]
    public void Menu_choices()
    {
        var spells = new byte[DoorMember.SpellSlots];
        spells[0] = DoorRules.KnockSpell;
        var flags = new DoorMenuFlags();
        Assert.Equal([DoorAction.Bash], DoorRules.MenuChoices(DoorBarrier.Locked, [M(10, mu: 1)], flags));
        Assert.Equal([DoorAction.Bash, DoorAction.Knock], DoorRules.MenuChoices(DoorBarrier.Barred, [M(10, mu: 1)], flags));
        Assert.Equal([DoorAction.Bash, DoorAction.Pick, DoorAction.Knock],
            DoorRules.MenuChoices(DoorBarrier.Locked, [M(10, thief: 1), M(10, spells: spells)], flags));
        flags.Bash = flags.Pick = false;
        Assert.Empty(DoorRules.MenuChoices(DoorBarrier.Locked, [M(10, thief: 1)], flags));
        flags.Reset();
        Assert.True(flags.Bash && flags.Pick && flags.Knock);
    }

    [Fact]
    public void Map_edits()
    {
        var planes = new byte[1024];
        planes[2 * 16 + 10] = 0x10;                   // (10,2): N wall type 1
        planes[DoorMap.BarrierPlane + 2 * 16 + 10] = 0x02;  // N face locked
        Assert.Equal(DoorBarrier.Locked, DoorMap.FaceBarrier(planes, 10, 2, 0));
        Assert.Equal(DoorBarrier.Passable, DoorMap.FaceBarrier(planes, 10, 1, 4));  // no wall on the other side
        Assert.False(DoorMap.CanStep(planes, 10, 2, 0));
        Assert.True(DoorMap.CanStep(planes, 10, 1, 4));                        // one-way from the north
        DoorMap.Open(planes, 10, 2, 0);
        Assert.Equal(0x01, planes[DoorMap.BarrierPlane + 2 * 16 + 10]);
        Assert.Equal(0x10, planes[DoorMap.BarrierPlane + 1 * 16 + 10]);        // (10,1) S face set to 1
        DoorMap.Seal(planes, 10, 2, 0);
        Assert.Equal(DoorBarrier.Impassable, DoorMap.FaceBarrier(planes, 10, 2, 0));
        DoorMap.Open(planes, 0, 0, 6);                                          // neighbour off the map: own face only
        Assert.Equal(0x40, planes[DoorMap.BarrierPlane]);
    }

    // ---- DOSBox captures (private fixtures + game files) ----

    private static List<DoorMember> SaveAParty()
    {
        var game = PorData.Open();
        var party = PorParty.Load(PorSaveGame.Open(game.Find("SAVGAMA.DAT")!), game.Root);
        return party.Members.Select(DoorMember.FromCharacter).ToList();
    }

    [CaptureFact]
    public void Capture_door_pick_fail()
    {
        var before = new DosboxVmState(Path.Combine(Captures.Dir!, "door_pick_fail", "before"));
        var after = new DosboxVmState(Path.Combine(Captures.Dir!, "door_pick_fail", "after"));
        var (x, y, facing, _, _) = before.Position;
        var barrier = DoorMap.FaceBarrier(before.GeoPlanes!, x, y, facing);
        Assert.Equal(DoorBarrier.Locked, barrier);

        var party = SaveAParty();
        var flags = new DoorMenuFlags();
        Assert.Equal([DoorAction.Bash, DoorAction.Pick], DoorRules.MenuChoices(barrier, party, flags));
        var rng = new TurboPascalRandom(before.RandSeed);
        var r = DoorRules.Pick(barrier, party, rng, flags);
        Assert.False(r.Opened);
        Assert.Equal(party.Count, r.Draws);            // 6: everyone rolls, thief or not
        Assert.Equal([DoorAction.Bash], DoorRules.MenuChoices(barrier, party, flags));
        // The area loop then runs vector 1, whose RANDOM is the 7th draw (ecl-compare).
        Assert.Equal(r.Draws + 1, TurboPascalRandom.StepsBetween(before.RandSeed, after.RandSeed));
        Assert.Equal(before.GeoPlanes, after.GeoPlanes);
    }

    [CaptureFact]
    public void Capture_door_bash()
    {
        var before = new DosboxVmState(Path.Combine(Captures.Dir!, "door_bash", "before"));
        var after = new DosboxVmState(Path.Combine(Captures.Dir!, "door_bash", "after"));
        var (x, y, facing, _, _) = before.Position;
        var planes = before.GeoPlanes!.ToArray();
        var barrier = DoorMap.FaceBarrier(planes, x, y, facing);
        Assert.Equal(DoorBarrier.Locked, barrier);

        var party = SaveAParty();
        var rng = new TurboPascalRandom(before.RandSeed);
        var r = DoorRules.Bash(barrier, party, rng, new DoorMenuFlags());
        Assert.True(r.Opened);
        Assert.Equal(2, r.Member);                     // third member (Strength 15, needs ≤ 2 on 1d6)
        Assert.Equal(3, r.Draws);
        Assert.Equal(after.RandSeed, rng.Seed);        // vectors 0 and 1 drew nothing here

        DoorMap.Open(planes, x, y, facing);
        Assert.Equal(after.GeoPlanes, planes);
        var (ax, ay, _, _, _) = after.Position;
        Assert.Equal((x + DoorMap.DeltaX(facing), y + DoorMap.DeltaY(facing)), (ax, ay));
    }

    [CaptureFact]
    public void Capture_door_relocks_on_load_not_in_fight()
    {
        static byte Door(string capture, string side) =>
            new DosboxVmState(Path.Combine(Captures.Dir!, capture, side)).GeoPlanes![DoorMap.BarrierPlane + 2 * 16 + 10];
        Assert.Equal(0x15, Door("combat_win", "after"));     // the fight does not reload the map
        Assert.Equal(0x15, Door("load_saveB", "before"));    // nor camping and saving
        Assert.Equal(0x16, Door("load_saveB", "after"));     // loading re-reads the GEO block
    }

    /// <summary>
    /// Leaving the Slums and coming back (capture <c>door_relock</c>, s10): the door at (9,2) W bashed open stays open
    /// through a fight, is still open when the party steps off the east edge into New Phlan, and the map read on
    /// re-entry is the GEO block again, byte for byte.
    /// </summary>
    [CaptureFact]
    public void Capture_door_relocks_after_leaving_and_reentering_the_area()
    {
        DosboxVmState State(string side) => new(Path.Combine(Captures.Dir!, "door_relock", side));
        const int door = DoorMap.BarrierPlane + 2 * 16 + 9;
        var closed = State("closed");
        Assert.Equal((9, 2, 6), (closed.Position.X, closed.Position.Y, closed.Position.Facing));
        Assert.Equal(DoorBarrier.Locked, DoorMap.FaceBarrier(closed.GeoPlanes!, 9, 2, 6));

        var open = State("open");
        Assert.Equal(DoorBarrier.Passable, DoorMap.FaceBarrier(open.GeoPlanes!, 9, 2, 6));
        var expected = closed.GeoPlanes!.ToArray();
        DoorMap.Open(expected, 9, 2, 6);
        Assert.Equal(expected, open.GeoPlanes);

        var before = State("before");                        // at 15,4 facing E, after an orc fight
        Assert.Equal(open.GeoPlanes![door], before.GeoPlanes![door]);
        var away = State("away");                            // New Phlan, ECL3 block 0 / map 0
        Assert.Equal((3, 0), (away.Disk, away.Script_));
        var after = State("after");                          // back in the Slums
        Assert.Equal((2, 20), (after.Disk, after.Script_));
        Assert.Equal(closed.GeoPlanes![door], after.GeoPlanes![door]);

        var game = PorData.Open();
        var block = GoldBox.Data.Dax.DaxFile.Open(game.Find("GEO2.DAX")!).Block(20)!;
        Assert.Equal(block[2..], after.GeoPlanes);
    }

    /// <summary>
    /// One-way walls (captures <c>oneway_*</c>, patched saves E and F, s10): <see cref="DoorMap.CanStep"/> on the
    /// captured map predicts each step, and the step from the other side of the same edge is refused.
    /// </summary>
    [CaptureTheory]
    [InlineData("oneway_through", true)]
    [InlineData("oneway_blocked", false)]
    [InlineData("oneway_barrier_through", true)]
    [InlineData("oneway_barrier_blocked", false)]
    public void Capture_one_way_walls(string capture, bool moves)
    {
        var before = new DosboxVmState(Path.Combine(Captures.Dir!, capture, "before"));
        var after = new DosboxVmState(Path.Combine(Captures.Dir!, capture, "after"));
        var (x, y, f, _, _) = before.Position;
        Assert.Equal(moves, DoorMap.CanStep(before.GeoPlanes!, x, y, f));
        var expected = moves ? (x + DoorMap.DeltaX(f), y + DoorMap.DeltaY(f)) : (x, y);
        Assert.Equal(expected, (after.Position.X, after.Position.Y));
        // The far face of the edge reads the other way: a one-way edge.
        int nx = x + DoorMap.DeltaX(f), ny = y + DoorMap.DeltaY(f);
        Assert.Equal(!moves, DoorMap.CanStep(before.GeoPlanes!, nx, ny, (f + 4) % 8));
        // Both vectors ran once on the step, refused or not (counters $9700/$9701 of the test script).
        Assert.Equal(1, after.Area9700[0] - before.Area9700[0]);
        Assert.Equal(1, after.Area9700[1] - before.Area9700[1]);
    }
}
