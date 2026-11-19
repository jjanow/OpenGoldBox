using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// The camp Cast's effect message (0x2C540 → START 0x2B8B outside a fight → START 0x16EC's out-of-fight branch, read
/// s27): the name at (1, 18), the text from (1, 19) in green, the game-speed pause, then rows 18–22 cleared. Our
/// wordings (no game files).
/// </summary>
public class CampEffectMessageTests
{
    /// <summary>Scripted answers; the text window's rows are kept at every delay.</summary>
    private sealed class Script(TextWindow w, params object?[] answers)
    {
        private readonly Queue<object?> _answers = new(answers);
        public List<(TimeSpan Wait, IReadOnlyList<string> Rows, byte NameColour)> Delays { get; } = [];

        public object? Ask(PlayerRequest r)
        {
            if (r is DelayRequest d)
            {
                Delays.Add((d.Duration, w.Lines, w.Cell(18, 1).Colour));
                return null;
            }
            if (_answers.Count == 0)
                throw new InvalidOperationException($"no answer left for {r}");
            return _answers.Dequeue();
        }

        public int Left => _answers.Count;
    }

    private static CampContext Camp(Script s, TextWindow w, EclCharacter member, byte speed)
    {
        var p = new EclParty();
        p.Members.Add(member);
        p.CurrentIndex = 0;
        var t = new TradeContext
        {
            Dialog = new Dialog(s.Ask, w),
            Text = new GameText(),
            Party = p,
            Pool = new uint[7],
            Treasure = [],
            Random = n => 0,
        };
        var m = new EclMemory { Party = p, GameSpeed = speed };
        m.Write(EclPartyRules.PartySizeCell, 1);
        // Spell 5: cleric 1, target the caster (mode 1), effect 0x0E for 1 + 0 rounds (the routine 0x28101 passes
        // its text to the resolution).
        var table = new SpellTable(new Dictionary<int, SpellInfo>
        {
            [5] = new(0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0x0E, 1, 0, 0, 0, 0),
        });
        var spells = new CampSpells(table, _ => false);
        var state = new CampState();
        var fx = new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current };
        return new CampContext
        {
            Trade = t,
            State = state,
            Spells = spells,
            Rest = new CampRest(m, state, spells, _ => 0, (c, e) => c.Effects.Remove(e), c => fx.RestoreHitPoints(c, 1, false)),
            Memory = m,
            Cast = new CampCast(new FightSpells(table, fx, _ => 0), p),
        };
    }

    private static EclCharacter Member()
    {
        var c = new EclCharacter { Name = "Anna" };
        c[0x10C] = 0;
        c[0x10D] = 1;
        c[0x17] = 5; // spell 5 memorised
        return c;
    }

    [Fact]
    public void The_effect_text_shows_under_the_name_with_the_game_speed_pause()
    {
        var w = new TextWindow();
        var a = Member();
        var s = new Script(w,
            2, 0,                 // MAGIC, CAST
            new ListAnswer(0, 1), // spell 5 (row 0 is the heading)
            null,                 // nothing memorised now: message key
            5, 5);
        var c = Camp(s, w, a, speed: 2);
        VenueScreens.Camp(c, null);
        Assert.Equal(0, s.Left);
        // The cast message (DOSBox-X s29): name and the casts word on row 19, the spell on row 20, the game-speed
        // pause and no key; then the effect message.
        Assert.Equal(2, s.Delays.Count);
        var cast = s.Delays[0];
        Assert.Equal(TimeSpan.FromMilliseconds(450), cast.Wait);
        Assert.StartsWith("ANNA ", cast.Rows[2].Trim());
        Assert.NotEqual("", cast.Rows[3].Trim());
        Assert.Equal("", cast.Rows[1].Trim());
        var d = s.Delays[1];
        Assert.Equal(TimeSpan.FromMilliseconds(450), d.Wait); // PauseOnly: DS:49E2 × 225 ms
        Assert.Equal("", d.Rows[0].Trim());                  // row 17 (index 0) cleared
        Assert.Equal("ANNA", d.Rows[1].Trim());              // the name at (1, 18)
        Assert.Equal(11, d.NameColour);                      // in play, not hostile: cyan
        Assert.Equal(c.Resources.CombatTexts.Effect(EffectTexts.ForSpell(5)!), d.Rows[2].Trim());
        Assert.NotNull(EffectHandlers.FindEffect(a, 0x0E));
    }

    [Fact]
    public void Speed_0_does_not_wait()
    {
        var w = new TextWindow();
        var s = new Script(w, 2, 0, new ListAnswer(0, 1), null, 5, 5);
        VenueScreens.Camp(Camp(s, w, Member(), speed: 0), null);
        Assert.Equal(0, s.Left);
        Assert.Empty(s.Delays);
    }
}
