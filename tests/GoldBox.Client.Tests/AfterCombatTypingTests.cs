using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// Which after-combat windows type (P, s30): the NPC share's lines go through the text routine START 0x77F8 (0x69F0)
/// and the loot Exit question too (0x666C / 0x6693), so they type while DS:4A00 is set; the summary is fixed-cell
/// strings (START 0x76B6) and the menu routine, never typed.
/// </summary>
public class AfterCombatTypingTests
{
    private static EclCharacter Member(string name)
    {
        var c = new EclCharacter { Name = name };
        c[0x10C] = 0;
        c[0x10D] = 1;
        return c;
    }

    /// <summary>Runs <paramref name="work"/> on a player's trade context; every request is answered (menus on their last entry).</summary>
    private static List<PlayerRequest> Run(bool typing, Action<TradeContext> work, List<bool>? rowBlank = null)
    {
        var party = new EclParty();
        party.Members.Add(Member("ANNA"));
        party.CurrentIndex = 0;
        var memory = new EclMemory { TypesText = typing, GameSpeed = 2, Party = party };
        using var runner = new ScriptRunner();
        var player = new ClientPlayer(runner, () => memory);
        var t = player.Trade(null)!;
        runner.Start(() => work(t));
        var seen = new List<PlayerRequest>();
        for (int guard = 0; runner.Pending is { } p && guard < 2000; guard++)
        {
            seen.Add(p);
            if (p is DelayRequest { Chained: true })
                rowBlank?.Add(player.WindowAfterFight && player.Window is not null);
            runner.Answer(p is ChoiceRequest c ? c.Items.Count - 1 : null);
        }
        Assert.False(runner.Busy);
        return seen;
    }

    private static int Typed(List<PlayerRequest> requests) => requests.Count(r => r is DelayRequest { Chained: true });

    private static readonly AfterCombatOutcome Won = new(false, false, true, true, 90, 9);

    [Fact]
    public void The_summary_never_types()
    {
        Assert.Equal(0, Typed(Run(typing: true, t => VenueScreens.Summary(t, Won, duel: false))));
    }

    [Fact]
    public void The_npc_share_window_types_while_ds_4a00_is_set()
    {
        void Share(TradeContext t) => VenueScreens.Loot(t, Won, duel: false, [Member("GNOLL")]);
        var rowBlank = new List<bool>();
        var typed = Run(typing: true, Share, rowBlank).OfType<DelayRequest>().Where(d => d.Chained).ToList();
        Assert.NotEmpty(typed);
        // While it types, the window is up with row 24 blank (WindowAfterFight: as the party-destroyed window at the
        // same point of 0x6AA5), not the area command line.
        Assert.All(rowBlank, Assert.True);
        Assert.All(typed, d => Assert.Equal(TimeSpan.FromMilliseconds(20), d.Duration)); // DS:49E2 × 10 ms
        Assert.Equal(0, Typed(Run(typing: false, Share)));
        Assert.Equal(0, Typed(Run(typing: true, t => VenueScreens.Loot(t, Won, duel: false, [])))); // no share: summary + menu only
    }

    [Fact]
    public void The_loot_exit_question_types_while_ds_4a00_is_set()
    {
        static void Exit(TradeContext t)
        {
            t.Pool[0] = 5; // money left: Exit asks first (the last entry, "NO", leaves)
            VenueScreens.LootMenu(t);
        }
        Assert.NotEqual(0, Typed(Run(typing: true, Exit)));
        Assert.Equal(0, Typed(Run(typing: false, Exit)));
    }
}
