using GoldBox.Client.Play;
using GoldBox.Core.Ecl;

namespace GoldBox.Client.Tests;

/// <summary>
/// ECL DELAY under the Amiga pages (s60): the Amiga program's handler (hunk 16 +0x43DA) steps the VM's PC and calls
/// hunk 25 +0xD7A, Delay(speed · 5 ticks of 1/50 s) = 100 ms a step; DOS (START 0x7E4E) waits speed × 225 ms. The land's
/// recovery's two pauses (hunk 13 +0x2AC8) are the same call.
/// </summary>
[Collection(AmigaViewStatic.Name)]
public class AmigaDelayTests
{
    private static List<TimeSpan> Delays(bool amiga, int speed, Action<ClientPlayer> act)
    {
        bool was = AmigaView.On;
        try
        {
            AmigaView.On = amiga;
            var memory = new EclMemory { TypesText = false, GameSpeed = (byte)speed };
            using var runner = new ScriptRunner();
            var player = new ClientPlayer(runner, () => memory);
            runner.Start(() => act(player));
            var seen = new List<TimeSpan>();
            for (int guard = 0; runner.Pending is { } p && guard < 200; guard++)
            {
                if (p is DelayRequest { Animation: false } d)
                    seen.Add(d.Duration);
                runner.Answer(null);
            }
            return seen;
        }
        finally
        {
            AmigaView.On = was;
        }
    }

    [Fact]
    public void Delay_waits_100_ms_a_step_under_the_Amiga_pages_and_225_under_DOS()
    {
        Assert.Equal([TimeSpan.FromMilliseconds(400)], Delays(true, 4, p => p.Delay()));
        Assert.Equal([TimeSpan.FromMilliseconds(900)], Delays(false, 4, p => p.Delay()));
        Assert.Empty(Delays(true, 0, p => p.Delay()));
    }

    private static readonly GoldBox.Core.Explore.OverlandRecoveryTables Tables = new([0x10], [0x11],
        [.. Enumerable.Range(0, 16).Select(i => (byte)i)], [.. Enumerable.Range(0, 16).Select(i => (byte)i)],
        [.. Enumerable.Range(0, 16).Select(i => (byte)i)], (1, 1), (2, 2), [(1, 1), (2, 2)]);

    [Fact]
    public void The_recovery_pauses_twice_at_the_Amiga_step()
    {
        Assert.Equal([TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300)],
            Delays(true, 3, p => p.OverlandRecovery(Tables, true, _ => 0)));
        Assert.Equal([TimeSpan.FromMilliseconds(675), TimeSpan.FromMilliseconds(675)],
            Delays(false, 3, p => p.OverlandRecovery(Tables, true, _ => 0)));
    }
}
