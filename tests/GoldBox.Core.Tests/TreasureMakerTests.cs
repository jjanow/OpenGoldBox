using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>TREASURE's random item maker (GAME.OVR 0x25C99, docs/re/item-format.md "Random treasure").</summary>
public class TreasureMakerTests
{
    /// <summary>Dice answers in order (each is the die's face; Random(n) gets face − 1), recording the sides asked.</summary>
    private sealed class Dice(params int[] faces)
    {
        private int _k;
        public List<int> Sides { get; } = [];
        public int Roll(int n)
        {
            Sides.Add(n);
            int f = faces[_k++];
            Assert.InRange(f, 1, n);
            return f - 1;
        }
        public bool AllUsed => _k == faces.Length;
    }

    /// <summary>A synthetic array: word k = 1000 + k, so each copied field shows which index it came from.</summary>
    private static ushort[] Table() => Enumerable.Range(0, TreasureMaker.TableWords).Select(k => (ushort)(1000 + k)).ToArray();

    private static int U16(byte[] r, int o) => r[o] | r[o + 1] << 8;

    [Fact]
    public void Plain_weapon_rolls_the_plus_and_gets_weight_and_value()
    {
        var d = new Dice(15); // plus +2
        var r = new TreasureMaker(null).Make(18, d.Roll);
        Assert.Equal([20], d.Sides);
        Assert.Equal(18, r[0x2E]);
        Assert.Equal(18, r[0x31]);
        Assert.Equal(0xA3, r[0x30]);
        Assert.Equal(0, r[0x2F]);
        Assert.Equal(2, r[0x32]);
        Assert.Equal(6, r[0x35]);
        Assert.Equal(175, U16(r, 0x37));
        Assert.Equal(0, r[0x39]);
        Assert.Equal(4000, U16(r, 0x3A));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(14, 1)]
    [InlineData(15, 2)]
    [InlineData(19, 2)]
    [InlineData(20, 3)]
    public void Plus_roll(int d20, int plus) => Assert.Equal(plus, TreasureMaker.PlusRoll(d20));

    [Fact]
    public void Armour_puts_the_plus_word_first_and_hides_only_it()
    {
        var r = new TreasureMaker(null).Make(58, new Dice(20).Roll);
        Assert.Equal(58, r[0x31]);
        Assert.Equal(0x30, r[0x30]);
        Assert.Equal(0xA4, r[0x2F]);
        Assert.Equal(4, r[0x35]);
        Assert.Equal(450, U16(r, 0x37));
        Assert.Equal(15000, U16(r, 0x3A));
        Assert.Equal(0x31, new TreasureMaker(null).Make(50, new Dice(1).Roll)[0x30]);
        Assert.Equal(0x32, new TreasureMaker(null).Make(52, new Dice(1).Roll)[0x30]);
    }

    [Fact]
    public void Bracers_double_the_plus_and_pick_a_word()
    {
        var r = new TreasureMaker(null).Make(77, new Dice(20).Roll); // +3 → AC 8
        Assert.Equal(8, r[0x32]);
        Assert.Equal(0xDC, r[0x2F]);
        Assert.Equal(0xA7, r[0x30]);
        Assert.Equal(0x4F, r[0x31]);
        Assert.Equal(10, U16(r, 0x37));
        Assert.Equal(24000, U16(r, 0x3A));
        Assert.Equal(0xDD, new TreasureMaker(null).Make(77, new Dice(1).Roll)[0x2F]);
    }

    [Fact]
    public void Ammunition_and_darts_come_in_stacks()
    {
        var arrows = new TreasureMaker(null).Make(73, new Dice(1).Roll);
        Assert.Equal(0x3D, arrows[0x31]);
        Assert.Equal(10, arrows[0x39]);
        Assert.Equal(40, U16(arrows, 0x37));
        Assert.Equal(150, U16(arrows, 0x3A));
        var darts = new TreasureMaker(null).Make(9, new Dice(1).Roll);
        Assert.Equal(5, darts[0x39]);
        Assert.Equal(25, U16(darts, 0x37));
    }

    [Fact]
    public void Javelin_on_a_five_copies_across_two_array_entries()
    {
        var d = new Dice(1, 5);
        var r = new TreasureMaker(Table()).Make(21, d.Roll);
        Assert.Equal([20, 5], d.Sides); // the plus first, then the 1d5
        Assert.Equal(21, r[0x2E]);
        Assert.Equal(unchecked((byte)(1000 + 0x90)), r[0x2F]);
        Assert.Equal(unchecked((byte)(1000 + 0x92)), r[0x31]);
        Assert.Equal(1000 + 0x93, U16(r, 0x37));
        Assert.Equal(1000 + 0x94, U16(r, 0x3A));
        Assert.Equal(unchecked((byte)(1000 + 0x97)), r[0x3E]);
        Assert.Equal(1, r[0x32]);
        Assert.Equal(1, r[0x33]);
        Assert.Equal(0, r[0x39]);

        var plain = new TreasureMaker(Table()).Make(21, new Dice(1, 4).Roll);
        Assert.Equal(20, U16(plain, 0x37));
        Assert.Equal(2000, U16(plain, 0x3A));
    }

    [Fact]
    public void Scroll_rolls_count_then_level_and_id_per_spell()
    {
        // 3 spells: level 1 (1d13 = 13 → 21), level 2 (1d7 = 1 → 29), level 3 (1d11 = 11 → 55)
        var d = new Dice(3, 1, 13, 2, 1, 3, 11);
        var r = new TreasureMaker(null).Make(61, d.Roll);
        Assert.True(d.AllUsed);
        Assert.Equal([3, 3, 13, 3, 7, 3, 11], d.Sides);
        Assert.Equal(0xD1, r[0x31]);
        Assert.Equal(0xD4, r[0x30]);
        Assert.Equal(1, r[0x32]);
        Assert.Equal(25, U16(r, 0x37));
        Assert.Equal([21, 29, 55], r[0x3C..0x3F]);
        Assert.Equal(1800, U16(r, 0x3A));
        Assert.Equal(6, r[0x35]);

        var c = new TreasureMaker(null).Make(62, new Dice(1, 3, 9).Roll); // cleric level 3: 1d9 + 35
        Assert.Equal(0xD0, c[0x31]);
        Assert.Equal(44, c[0x3C]);
        Assert.Equal(900, U16(c, 0x3A));
    }

    [Theory]
    [InlineData(63, new int[0], 0x49)]
    [InlineData(67, new int[0], 0x91)]
    [InlineData(69, new[] { 1 }, 0x29)]
    [InlineData(69, new[] { 6 }, 0x79)]
    [InlineData(69, new[] { 7 }, 0x81)]
    [InlineData(78, new[] { 1 }, 0x41)]
    [InlineData(78, new[] { 2 }, 0x71)]
    [InlineData(79, new int[0], 0x39)]
    [InlineData(84, new int[0], 0x09)]
    [InlineData(92, new int[0], 0x61)]
    [InlineData(71, new[] { 5 }, 0x11)]
    [InlineData(71, new[] { 8 }, 0x01)]
    [InlineData(71, new[] { 9 }, 0x19)]
    [InlineData(71, new[] { 10 }, 0x51)]
    [InlineData(70, new[] { 7 }, 0x59)]
    [InlineData(70, new[] { 10 }, 0x89)]
    public void Other_types_copy_an_array_entry(byte type, int[] dice, int magic)
    {
        var d = new Dice(dice);
        var r = new TreasureMaker(Table()).Make(type, d.Roll);
        Assert.True(d.AllUsed);
        Assert.Equal(type, r[0x2E]);
        Assert.Equal((byte)(1000 + magic), r[0x2F]);
        Assert.Equal((byte)(1000 + magic + 1), r[0x30]);
        Assert.Equal((byte)(1000 + magic + 2), r[0x31]);
        Assert.Equal(1000 + magic + 3, U16(r, 0x37));
        Assert.Equal(1000 + magic + 4, U16(r, 0x3A));
        Assert.Equal([(byte)(1000 + magic + 5), (byte)(1000 + magic + 6), (byte)(1000 + magic + 7)], r[0x3C..0x3F]);
        int p = magic == 0x61 ? 2 : 1;
        Assert.Equal(p, r[0x32]);
        Assert.Equal(p, r[0x33]);
        Assert.Equal(6, r[0x35]);
    }

    [Theory]
    [InlineData(78, 3)] // 1 + 1d3 = 4: nothing
    [InlineData(71, 11)]
    public void A_dead_branch_leaves_a_blank_item(byte type, int face)
    {
        var r = new TreasureMaker(Table()).Make(type, new Dice(face).Roll);
        var blank = new byte[EclCharacter.ItemRecordSize];
        blank[0x2E] = type;
        blank[0x35] = 6;
        Assert.Equal(blank, r);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(99)]
    public void Unknown_types_draw_nothing(byte type)
    {
        var r = new TreasureMaker(Table()).Make(type, _ => throw new InvalidOperationException("no roll expected"));
        Assert.Equal(type, r[0x2E]);
        Assert.Equal(6, r[0x35]);
        Assert.Equal(0, U16(r, 0x3A));
    }

    [Fact]
    public void Without_the_array_the_copy_is_reported()
    {
        var m = new TreasureMaker(null);
        m.Make(79, _ => 0);
        Assert.Single(m.Diagnostics);
    }

    [PorFact]
    public void Player_program_holds_the_random_item_array()
    {
        var prog = PorProgram.Open(PorData.Open())!;
        Assert.Equal(0x0970, prog.RandomItemBase());
        var t = prog.RandomItemTable()!;
        for (int magic = 1; magic <= 145; magic += 8)
        {
            Assert.True(t[magic + 2] is > 0 and < 256, $"entry {magic}: first word");
            Assert.True(t[magic + 4] > 0, $"entry {magic}: value");
        }
    }
}
