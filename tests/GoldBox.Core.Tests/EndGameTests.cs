using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>The end game (GAME.OVR 0x20941) as steps, and the hall's and end game's texts from the program.</summary>
public class EndGameTests
{
    [Theory]
    [InlineData(1, new int[0])]
    [InlineData(2, new[] { 4 })]
    [InlineData(3, new[] { 4, 5 })]
    [InlineData(6, new[] { 4, 5, 6 })]
    public void Party_figures_follow_the_party_size(int size, int[] figures)
    {
        var steps = EndGame.Steps(size, 1);
        var pictures = steps.OfType<EndGamePicture>().Select(p => p.Block).ToList();
        Assert.Equal(new[] { 1, 3 }.Concat(figures), pictures);
        Assert.Equal([2, 7, 8], steps.OfType<EndGameAnimation>().Select(a => a.Block));
        Assert.Equal(13, steps.OfType<EndGameLine>().Count());
        Assert.Equal(3, steps.OfType<EndGamePause>().Count());
    }

    [Fact]
    public void Positions_and_loops_are_the_programs()
    {
        var steps = EndGame.Steps(6, 1);
        Assert.Contains(new EndGamePicture(5, 14, 10), steps);
        Assert.Contains(new EndGameAnimation(2, 3, 5, 22), steps);   // ((10 − 1) × 5) shr 1
        Assert.Contains(new EndGameAnimation(7, 3, 1, 22), steps);
        Assert.Contains(new EndGameAnimation(8, 3, 1, 1), steps);
        Assert.Equal(25, EndGame.Loops(0));
        Assert.Equal(2, EndGame.Loops(9));
        Assert.Equal(DosClock.Ticks(2), EndGame.FrameTime(1, 2));     // 8 hundredths: 2 ticks (10.99)
        Assert.Equal("FINAL5", EndGame.FileName(5));
    }

    [Fact]
    public void The_amiga_sequence_is_its_programs()
    {
        // hunk 13 +0x26AE: the same 13 lines, blocks 2, 7 forwards and 8 backwards at (3, 3), nothing else.
        var steps = EndGame.AmigaSteps();
        Assert.Equal([new EndGameFrames(2, 3, 3, false), new EndGameFrames(7, 3, 3, false), new EndGameFrames(8, 3, 3, true)],
            steps.OfType<EndGameFrames>());
        Assert.Equal(Enumerable.Range(0, 13), steps.OfType<EndGameLine>().Select(l => l.Text));
        Assert.Equal([17, 18, 19, 17, 18, 19, 20, 21, 22, 17, 18, 19, 20], steps.OfType<EndGameLine>().Select(l => l.Row));
        Assert.Equal(3, steps.OfType<EndGameClearText>().Count());
        Assert.Empty(steps.OfType<EndGamePicture>());
        Assert.Empty(steps.OfType<EndGamePause>());
        Assert.Equal(EndGame.AmigaBlocks, steps.OfType<EndGameFrames>().Select(f => f.Block));
        Assert.Equal([5, 4, 3, 2, 1, 0], EndGame.FrameOrder(new EndGameFrames(8, 3, 3, true), 6));
        Assert.Equal([0, 1, 2, 3], EndGame.FrameOrder(new EndGameFrames(2, 3, 3, false), 4));
        Assert.Equal(TimeSpan.FromSeconds(2), EndGame.AmigaFrameTime);   // Delay(100) ticks of 1/50 s
    }

    [Fact]
    public void Frames_hold_whole_bios_ticks_as_dosbox_showed()
    {
        // DOSBox-X s24, speed 4: dragon delays 2, 1, 2, 1 held 3, 2, 3, 2 ticks; the spirit's 4, 2, 2, 3, 2, 2 held
        // 6, 3, 3, 4, 3, 3; one loop 0.550 s and 1.199 s measured.
        long Ticks(params uint[] delays) => delays.Sum(d => EndGame.FrameTime(4, d).Ticks) / DosClock.Tick.Ticks;
        Assert.Equal(10, Ticks(2, 1, 2, 1));
        Assert.Equal(22, Ticks(4, 2, 2, 3, 2, 2));
        Assert.Equal(549, (int)DosClock.Ticks(10).TotalMilliseconds);
        Assert.Equal(TimeSpan.FromTicks(549254), DosClock.Tick);
        Assert.Equal(3, DosClock.TicksAbove(11));                     // 2 ticks read 10 or 11, never above 11
        Assert.Equal(2, DosClock.TicksAbove(10));
        Assert.Equal(4, DosClock.TicksAtLeast(20));                   // the picture window's ≥ (0.22 s for delay 2)
        Assert.Equal(0, DosClock.TicksAtLeast(0));
    }

    [Fact]
    public void Menu_flags_follow_the_current_member_and_the_hall()
    {
        var none = TrainingHall.MenuFlags(false, 116, false);
        Assert.Equal([true, false, false, false, false, true, false, true, false, false, true], none);
        var hall = TrainingHall.MenuFlags(true, 116, false);
        Assert.True(hall[(int)TrainingHall.Entry.Train]);
        Assert.False(hall[(int)TrainingHall.Entry.Load]);
        Assert.False(TrainingHall.MenuFlags(true, 0, false)[(int)TrainingHall.Entry.Train]);
        Assert.True(TrainingHall.MenuFlags(true, 0, true)[(int)TrainingHall.Entry.Train]);
    }

    [Fact]
    public void Texts_come_from_the_program()
    {
        if (!PorData.Present)
            return;
        var p = PorProgramData.Open(PorData.Open())!;
        for (int i = 0; i <= 12; i++)
            Assert.False(string.IsNullOrEmpty(p.Text($"End.{i}")), $"End.{i}");
        foreach (var k in PorTextCatalog.Overlay.Keys.Where(k => k.StartsWith("Hall.", StringComparison.Ordinal)))
            Assert.False(string.IsNullOrEmpty(p.Text(k)), k);
        Assert.Equal("FINAL", p.Text("End.File"));
        Assert.All(Enum.GetValues<TrainingHall.Entry>(), e => Assert.False(string.IsNullOrEmpty(PorTrainingTables.MenuEntry(p, e))));
        Assert.Equal([true, true, true], new[] { TrainingHall.Entry.Create, TrainingHall.Entry.Add, TrainingHall.Entry.Exit }
            .Select(e => PorTrainingTables.MenuDefault(p, e)!.Value));
    }

    /// <summary>The payment 0x1DC6C on a purse of every coin (the DOSBox thief): small coins first, change in electrum and gold.</summary>
    [Fact]
    public void Payment_takes_small_coins_first_and_gives_change()
    {
        var ds = new byte[0x5000];
        int[] values = [1, 10, 100, 200, 1000];
        for (int k = 0; k < 5; k++)
            BitConverter.GetBytes(values[k]).CopyTo(ds, 0xD24 + 4 * k);
        var t = new TrainingTables
        {
            Data = ds,
            Levels = new LevelStatsTables
            {
                Data = ds, Thac0 = 0, ClericSlots = 0, MageSlots = 0, SpellTable = 0, Saves = 0, ThiefBase = 0,
                ThiefRace = 0, ThiefDex = 0,
            },
        };
        var c = new EclCharacter();
        ushort[] coins = [500, 300, 20, 900, 20, 3, 1];
        for (int k = 0; k < 7; k++)
            c.SetWord(0x88 + 2 * k, coins[k]);
        TrainingHall.Pay(c, 1000, t);
        Assert.Equal(new ushort[] { 0, 0, 1, 2, 5, 3, 1 }, Enumerable.Range(0, 7).Select(k => c.Word(0x88 + 2 * k)));
    }
}
