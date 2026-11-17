using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>What the rest loop shows or asks (the Client's screens; tests script it).</summary>
public interface ICampRestUi
{
    /// <summary>The rest-time menu (0x244ED, <see cref="RestTimeMenu"/>): true when the player chose Rest.</summary>
    bool ChooseRestTime(RestTimeMenu menu);

    /// <summary>
    /// Polled once per rest tick (0x24B1D): a key waiting? Then the rest time is shown and the stop question asked; true
    /// = stop. Front ends without a key buffer answer here when input arrived since the last poll.
    /// </summary>
    bool StopAsked();

    /// <summary>The rest time redrawn (0x243BC with no highlight): every 5 ticks and before each message.</summary>
    void ShowRestTime();

    /// <summary>Every 288 ticks (0x2463D): the heal message (always), then a key; <paramref name="anyone"/>: the party list is redrawn.</summary>
    void PartyHealed(bool anyone);

    /// <summary>A pending spell memorised (0x246F1: its message and the spell name, then a key).</summary>
    void Memorised(EclCharacter c, int spell);

    /// <summary>A pending scroll spell scribed (0x247B3: its message and the spell name, then a key).</summary>
    void Scribed(EclCharacter c, int spell);

    /// <summary>The interruption (0x24BD6: the interruption message in white, then a key).</summary>
    void Interrupted();
}

/// <summary>
/// The rest-time menu (GAME.OVR 0x244ED): the highlighted word of the rest time (2 minutes, 3 hours, 4 days; starts
/// at 2) and the keys. Rest (or Enter) rests; Y / H / M pick days / hours / minutes; I adds one to the highlighted
/// word, 5 to the minutes word when minutes are highlighted; D takes one off (5 minutes) through the borrow 0x24246;
/// both then carry (0x2416F). The extended keys: Up = I, Down = D, Left = the next word up (days wrap to minutes),
/// Right = the next word down (minutes wrap to days). Escape or Exit leaves without resting.
/// </summary>
public sealed class RestTimeMenu(CampState state, EclParty? party)
{
    public CampState State { get; } = state;

    /// <summary>The highlighted rest-time word: <see cref="CampTime.TenMinutes"/> (minutes), Hours or Days.</summary>
    public int Selected { get; set; } = CampTime.TenMinutes;

    public enum Key { Rest, Days, Hours, Minutes, Inc, Dec, Exit, Up, Down, Left, Right }

    /// <summary>Applies a key; returns true (rest), false (leave) or null (the menu stays).</summary>
    public bool? Press(Key key)
    {
        key = key switch { Key.Up => Key.Inc, Key.Down => Key.Dec, _ => key };
        switch (key)
        {
            case Key.Left:
                if (++Selected > CampTime.Days) Selected = CampTime.TenMinutes;
                return null;
            case Key.Right:
                if (--Selected < CampTime.TenMinutes) Selected = CampTime.Days;
                return null;
            case Key.Rest:
                return true;
            case Key.Exit:
                return false;
            case Key.Days:
                Selected = CampTime.Days;
                return null;
            case Key.Hours:
                Selected = CampTime.Hours;
                return null;
            case Key.Minutes:
                Selected = CampTime.TenMinutes;
                return null;
            case Key.Inc:
                if (Selected == CampTime.TenMinutes)
                    State.RestTime[CampTime.Minutes] += 5;
                else
                    State.RestTime[Selected]++;
                CampTime.Validate(State, party);
                return null;
            case Key.Dec:
                if (Selected == CampTime.TenMinutes)
                    CampTime.Decrement(State, 5, CampTime.Minutes, party);
                else
                    CampTime.Decrement(State, 1, Selected, party);
                CampTime.Validate(State, party);
                return null;
        }
        return null;
    }

    /// <summary>Days, hours and minutes as 0x243BC shows them: two digits each, minutes = 10 × ten-minutes + minutes.</summary>
    public (int Days, int Hours, int Minutes) Shown =>
        (State.RestTime[CampTime.Days], State.RestTime[CampTime.Hours],
         10 * State.RestTime[CampTime.TenMinutes] + State.RestTime[CampTime.Minutes]);
}

/// <summary>
/// Resting in camp (GAME.OVR 0x24A66, reached from the camp's Rest and the Magic menu's Rest through 0x17FAB). Rolls
/// come from the game's generator through <see cref="FightRules.Dice"/>.
/// </summary>
public sealed class CampRest(EclMemory memory, CampState state, CampSpells spells, Func<ushort, ushort> random,
    Action<EclCharacter, byte[]> removeEffect, Func<EclCharacter, bool> heal)
{
    /// <summary>VM cells (zone $6B00 + 0x5A4 / 0x5A6): rest ticks between interruption rolls, and the roll's percent.</summary>
    public const ushort SafeTicksCell = 0x6DD2, InterruptPercentCell = 0x6DD3;

    /// <summary>Rest ticks per whole-party heal (0x2463D: 0x120, a day of 5-minute ticks).</summary>
    public const int HealTicks = 288;

    public EclMemory Memory { get; } = memory;
    public CampState State { get; } = state;
    public CampSpells Spells { get; } = spells;

    private IReadOnlyList<EclCharacter> Party => Memory.Party?.Members ?? [];

    /// <summary>
    /// 0x17FAB (the camp's and the Magic menu's Rest): the rest time is set to the longest preparation time in the
    /// party (<see cref="CampSpells.PrepMinutes"/>, which also stores each member's base hours): hours = m div 60,
    /// ten minutes = (m − 60·hours) div 10, minutes = m mod 10 (days untouched: 0 after camp entry); then the rest
    /// (<see cref="Run"/>); afterwards the rest time is cleared. Returns whether the rest was interrupted.
    /// </summary>
    public bool RestForPreparation(ICampRestUi ui)
    {
        ushort max = 0;
        foreach (var c in Party)
        {
            ushort m = Spells.PrepMinutes(c);
            if (max < m)
                max = m;
        }
        var t = State.RestTime;
        t[CampTime.Hours] = (ushort)(max / 60);
        t[CampTime.TenMinutes] = (ushort)((max - 60 * t[CampTime.Hours]) / 10);
        t[CampTime.Minutes] = (ushort)(max % 10);
        bool interrupted = Run(ui);
        Array.Clear(t);
        return interrupted;
    }

    /// <summary>
    /// 0x24A66: preparation timers of the party positions := 0, every effect flag DS:471E := 1, the rest-time menu.
    /// Unless left, while rest time remains: a key offers to stop; else one tick: rest time − 5 minutes (0x24246);
    /// every 5th tick the rest time is shown; the clock + 5 minutes with the effect timeout (0x241A1); the heal
    /// count (0x2463D); the preparation timers (0x248D6, 0x2497E); then, when $6DD2 &gt; 0, DS:49FB + 1 and at
    /// $6DD2 or more it restarts at 0 and 1d100 ≤ $6DD3 interrupts the rest. Returns whether it was interrupted.
    /// </summary>
    public bool Run(ICampRestUi ui)
    {
        for (int i = 0; i < Party.Count && i < State.PrepTimers.Length; i++)
            State.PrepTimers[i] = 0;
        Array.Fill(State.EffectFlags, (byte)1);
        byte tick = 0, shown = 0;
        bool interrupted = false;
        bool stop = !ui.ChooseRestTime(new RestTimeMenu(State, Memory.Party));
        while (!stop && State.RestTimeLeft)
        {
            if (ui.StopAsked())
                stop = true;
            if (stop)
                break;
            CampTime.Decrement(State, 5, CampTime.Minutes, Memory.Party);
            if (++shown >= 5)
            {
                ui.ShowRestTime();
                shown = 0;
            }
            CampTime.Advance(Memory, State, CampTime.Minutes, 5, removeEffect);
            HealTick(ui);
            PrepTick(ui);
            PrepBaseTick(ref tick, ui);
            ushort safe = Memory.Read(SafeTicksCell);
            if (safe > 0)
            {
                State.IntervalTicks++;
                if (State.IntervalTicks >= safe)
                {
                    State.IntervalTicks = 0;
                    if (FightRules.Dice(random, 1, 100) <= Memory.Read(InterruptPercentCell))
                    {
                        ui.Interrupted();
                        stop = true;
                        interrupted = true;
                    }
                }
            }
        }
        return interrupted;
    }

    /// <summary>
    /// 0x2463D: DS:6DD8 + 1; at 288 each party member is healed 1 HP (0x2C647 without bypass), the message shows
    /// whoever was healed, and the count restarts.
    /// </summary>
    private void HealTick(ICampRestUi ui)
    {
        State.HealTicks++;
        if (State.HealTicks < HealTicks)
            return;
        bool anyone = false;
        foreach (var c in Party)
            if (heal(c))
                anyone = true;
        ui.PartyHealed(anyone);
        State.HealTicks = 0;
    }

    /// <summary>
    /// 0x248D6: each member's timer DS:6DD0 − 1 (a byte: 0 wraps to 255). At 0 with record 0x2C = 0 a pending spell is
    /// finished, scribing first (0x247B3), else memorising (0x246F1), and the timer is reloaded with 3 × the level of
    /// the next pending one (0 when none).
    /// </summary>
    private void PrepTick(ICampRestUi ui)
    {
        for (int i = 0; i < Party.Count && i < State.PrepTimers.Length; i++)
        {
            var c = Party[i];
            State.PrepTimers[i]--;
            if (State.PrepTimers[i] != 0 || c[CampSpells.PrepHours] != 0)
                continue;
            bool done = false;
            int lvl = Spells.FinishScribe(c, ref done, ui.Scribed);
            if (lvl == 0)
                lvl = Spells.FinishMemorise(c, ref done, ui.Memorised);
            State.PrepTimers[i] = (byte)(lvl * 3);
        }
    }

    /// <summary>
    /// 0x2497E: every 12th tick (an hour), each member with record 0x2C above 0 (signed) loses one; reaching 0, its
    /// timer is set to 3 × the level of its first pending scroll spell, else of its first pending memorised spell.
    /// </summary>
    private void PrepBaseTick(ref byte tick, ICampRestUi ui)
    {
        if (++tick < 12)
            return;
        tick = 0;
        for (int i = 0; i < Party.Count && i < State.PrepTimers.Length; i++)
        {
            var c = Party[i];
            if ((sbyte)c[CampSpells.PrepHours] <= 0)
                continue;
            c[CampSpells.PrepHours]--;
            if (c[CampSpells.PrepHours] != 0)
                continue;
            bool done = true;
            int lvl = Spells.FinishScribe(c, ref done, ui.Scribed);
            if (lvl == 0)
                lvl = Spells.FinishMemorise(c, ref done, ui.Memorised);
            State.PrepTimers[i] = (byte)(lvl * 3);
        }
    }
}
