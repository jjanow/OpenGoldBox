using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Sound;

namespace GoldBox.Client.Play;

/// <summary>
/// The effect engine's handler messages outside a fight (DS:49F3 ≠ 5: exploring, the camp, its rest), read s30
/// (docs/re/spells-effects.md "Status changes"). Every one goes through START 0x16EC(creature, text, row, pause flag),
/// whose out-of-fight branch ignores the row: while the rest loop runs (DS:6DDA = 1, <paramref name="resting"/>) the box
/// (1, 18)–(38, 22) is cleared, the name goes on row 19 and the text from row 20; otherwise the text box's frame is drawn
/// again (the Client's is always there), (1, 17)–(38, 22) cleared, the name on row 18 and the text from row 19, in green.
/// With the pause flag: PauseOnly (DS:49E2 × 225 ms), then START 0x2383, which outside a fight clears (1, 18)–(38, 22).
/// 0xECB3's own text passes no pause flag; its end (<see cref="EffectEvent.StatusDone"/>) is CreatureDowned 0x3154B's
/// out-of-fight branch (0x31560: the body cue DS:261E and PauseOnly) when the creature is still out of play, then
/// PauseOnly, START 0x2383 and START 0x0767 (the party list, which the Client draws from memory every frame). START
/// 0x1693 (handlers 40, 135) is the row-24 message in green with the pause.
/// </summary>
public sealed class OutsideEffectMessages(TextWindow window, Func<PlayerRequest, object?> ask, Func<int> speed,
    CombatText texts, Func<bool> resting, Action<SoundCue>? cue = null, Action? framed = null,
    Action<EclCharacter>? revived = null, Action? listDrawn = null)
{
    /// <summary>
    /// Shows the event's message as the program does outside a fight; false for an event this class does not draw (the
    /// caller's own messages: 0x2C540's texts, damage, healing).
    /// </summary>
    public bool Show(EclCharacter c, EffectEvent e, EffectHandlers? fx = null)
    {
        var t = texts;
        switch (e)
        {
            // 0xECB3's callers: the text without the pause flag (0xECDD).
            case EffectEvent.PoisonDeath: Message(c, t[CombatMsg.StatusPoisonDeath], false); return true;
            case EffectEvent.Collapses: Message(c, t[CombatMsg.StatusCollapses], false); return true;
            case EffectEvent.RotsAway: Message(c, t[CombatMsg.StatusRotsAway], false); return true;
            case EffectEvent.PoisonKilled: Message(c, t[CombatMsg.StatusPoisonKilled], false); return true;
            case EffectEvent.DeadForever: Message(c, t[CombatMsg.StatusDeadForever], false); return true;
            case EffectEvent.DrainKilled: Message(c, t[CombatMsg.StatusDrainKilled], false); return true;
            case EffectEvent.FallsDead: Message(c, t[CombatMsg.StatusFallsDead], false); return true;
            case EffectEvent.Stoned: Message(c, t[CombatMsg.HandlerStoned], false); return true;
            case EffectEvent.StatusDone:
                // 0xED2E–0xED5D outside a fight.
                if (c[FightRules.InPlay] == 0)
                {
                    cue?.Invoke(SoundCue.Body);
                    Pause();
                }
                Pause();
                Clear();
                listDrawn?.Invoke(); // START 0x0767
                return true;
            // The messages before them and the other handler texts: with the pause flag.
            case EffectEvent.Rots: Message(c, t[CombatMsg.Rots], true); return true;
            case EffectEvent.Drained:
            {
                int n = fx?.DrainCount ?? 1;
                Message(c, t[CombatMsg.Drained] + (n > 1
                    ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) + t[CombatMsg.DrainedLevels]
                    : t[CombatMsg.DrainedOneLevel]), true);
                return true;
            }
            case EffectEvent.Revived:
                // 0x2C753 outside a fight: no view (0x319D2 only in a fight), the text by the hostile byte 0x10E. No party
                // list is drawn: the name stays light red until the next redraw (D s38: the camp's rest, its menu, the
                // area after the camp; the camp's View redraws it).
                revived?.Invoke(c);
                Message(c, t[c[FightRules.Hostile] == 1 ? CombatMsg.StatusRevivedEnemy : CombatMsg.StatusRevived], true);
                return true;
            case EffectEvent.Diseased: Message(c, t[CombatMsg.HandlerDiseased], true); return true;
            case EffectEvent.HeldFast: Message(c, t[CombatMsg.HandlerHeldFast], true); return true;
            case EffectEvent.SucksBlood: Message(c, t[CombatMsg.HandlerSucksBlood], true); return true;
            case EffectEvent.Rakes: Message(c, t[CombatMsg.HandlerRakes], true); return true;
            case EffectEvent.Stronger: Message(c, t[CombatMsg.HandlerStronger], true); return true;
            case EffectEvent.Escapes: Message(c, t[CombatMsg.StatusEscapes], true); return true;
            case EffectEvent.AirClears: BottomLine(t[CombatMsg.HandlerAirClears]); return true;
            case EffectEvent.NeedsGiantStrength: BottomLine(t[CombatMsg.HandlerNeedsGiantStrength]); return true;
            // START 0x2EEA (00BA:234A): the healed words by HP 0x11B = 0x32, START 0x16EC with the pause, then outside a
            // fight the party list (00BA:23AE). Handlers 50 / 62 and Cure Light Wounds (0x28083).
            case EffectEvent.Healed: Healed(c); return true;
            // Handlers 15 / 44 after their damage: the party list (0xF299 / 0xFE19), no text.
            case EffectEvent.HandlerDamageListed: listDrawn?.Invoke(); return true;
        }
        return false;
    }

    /// <summary>START 0x2EEA outside a fight: "fully" when HP 0x11B equals the maximum 0x32 (00BA:2353), else "partially".</summary>
    public void Healed(EclCharacter c)
    {
        Message(c, texts[c[0x11B] == c[0x32] ? CombatMsg.FullyHealed : CombatMsg.PartiallyHealed], true);
        listDrawn?.Invoke();
    }

    /// <summary>START 0x16EC's out-of-fight branch (0x22F7–0x237D).</summary>
    public void Message(EclCharacter c, string text, bool pause)
    {
        int r = resting() ? TextWindow.Top + 1 : TextWindow.Top;
        if (r == TextWindow.Top)
            framed?.Invoke(); // the box's frame drawn again (03F1:0000)
        window.ClearRect(TextWindow.Left, r, TextWindow.Right, TextWindow.Bottom);
        window.Put(r + 1, TextWindow.Left, AmigaView.Case(c.Name), CombatChooser.NameColour(c));
        window.PrintIn(TextWindow.Left, r + 2, TextWindow.Right, TextWindow.Bottom, text, true, TextWindow.ScriptColour);
        if (pause)
        {
            Pause();
            Clear();
        }
    }

    /// <summary>START 0x2383 outside a fight: (1, 18)–(38, 22) cleared.</summary>
    private void Clear() => window.ClearRect(TextWindow.Left, TextWindow.Top + 1, TextWindow.Right, TextWindow.Bottom);

    /// <summary>PauseOnly (START 0x709:0x0DBE): DS:49E2 × 225 ms; speed 0 waits nothing. The Amiga's creature message
    /// routine (hunk 13 +0x4A7C): speed · 100 ms (<see cref="AmigaView.MessagePause"/>).</summary>
    private void Pause()
    {
        if (AmigaView.MessagePause(speed()) is { Ticks: > 0 } pause)
            ask(new DelayRequest(pause));
    }

    /// <summary>START 0x1693 (0x2233): row 24 cleared, the text at column 0 in green, PauseOnly, row 24 cleared (the
    /// Amiga's hunk 13 +0x4928: speed · 100 ms).</summary>
    private void BottomLine(string text)
    {
        if (AmigaView.MessagePause(speed()) is { Ticks: > 0 } pause)
            ask(new DelayRequest(pause) { BottomLine = (text, TextWindow.ScriptColour) });
    }
}
