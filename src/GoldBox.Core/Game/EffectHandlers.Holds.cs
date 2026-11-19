using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// Effect handlers 40, 73–77, 81–86, 88 and 121 (table DS:6828; GAME.OVR file offsets; 73 and 74 sit in the unit at
// 0x161CD, 88 in the spell unit at 0x27018, the rest in the handler unit at 0xEC5B): the stinking cloud's arena cells,
// the rake, the holds and the blood drain (combat slots), the fear auras, the gaze, the charm gaze, level drain, the
// lightning breath and the acid breath. docs/re/spells-effects.md "Handlers 73–139". Handler 88's entry is written by
// the spell table builder (0x2A7B9, DS:6988), not by 0x11DFD.

/// <summary>
/// One stinking cloud of the list DS:6DBB (19-byte nodes from GetMem): +0 caster, +4 next, +7 + i the terrain under
/// cell i, +0x0B + i "cell i was clouded" (i = 1..4), +0x10 / +0x11 the aimed square, +0x12 the caster's cloud number.
/// </summary>
public sealed class StinkingCloud
{
    public required EclCharacter Caster { get; init; }
    public int Index { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>+7 + i: the terrain the cloud covered (index 1..4).</summary>
    public byte[] Under { get; } = new byte[5];
    /// <summary>+0x0B + i: the cell was on the arena and passable (cost below 0xFF), so it was clouded.</summary>
    public bool[] Used { get; } = new bool[5];

    /// <summary>DS:290D + i (i = 1..4): the directions of the four cells from the aimed square (centre, east,
    /// south-east, south: a 2 × 2 block).</summary>
    public static readonly int[] CellDirections = [0, 8, 2, 3, 4];

    public (int X, int Y) Cell(int i) =>
        (X + CombatTables.DirDx[CellDirections[i]], Y + CombatTables.DirDy[CellDirections[i]]);
}

public sealed partial class EffectHandlers
{
    /// <summary>DS:6DBB: the stinking clouds on the arena, oldest first (spell 34 adds, handler 40 removes).</summary>
    public List<StinkingCloud> Clouds { get; } = [];

    /// <summary>
    /// DS:409B + 0x38·class + 4·level: experience for a class level (8 classes × 14 levels, signed dwords, read from
    /// the player's program by <c>PorSpellData.ExperienceTable</c>); flat, index class × 14 + level. Level drain
    /// (handlers 85/86) and Restoration (spell 56) read it.
    /// </summary>
    public int[]? ExperienceTable { get; set; }

    /// <summary>0x2AA87 (after a level changes): <see cref="Game.LevelStats.Apply"/> with the program's tables, set by
    /// <see cref="FightKit.WireLevels"/>; without it nothing happens and a diagnostic is noted.</summary>
    public Action<EclCharacter>? LevelStats { get; set; }

    /// <summary>START 0x3AF7 (creature, class): true when the race's level limit stops that class
    /// (<see cref="Game.LevelStats.RaceLimitStops"/>, set by <see cref="FightKit.WireLevels"/>); without it no class is
    /// stopped and a diagnostic is noted.</summary>
    public Func<EclCharacter, int, bool>? RaceLevelLimit { get; set; }

    private Fight? _attached;

    /// <summary>Experience for <paramref name="cls"/> at <paramref name="level"/> (DS:409B), or 0 without the table.</summary>
    public int Experience(int cls, int level)
    {
        int i = cls * 14 + level;
        if (ExperienceTable is { } t && i >= 0 && i < t.Length)
            return t[i];
        Diag("[?] the experience table (DS:409B) is not loaded");
        return 0;
    }

    private void CallLevelStats(EclCharacter c)
    {
        if (LevelStats is { } f)
            f(c);
        else
            Diag("[?] level stats 0x2AA87 not modelled");
    }

    private CombatMap? Map => Geometry as CombatMap;

    /// <summary>0x31399: the combat slot of a creature (0 when none or without the combat map).</summary>
    private int SlotOf(EclCharacter? c)
    {
        if (c is null)
            return 0;
        if (Map is { } m)
            return m.Slot(c);
        Diag("[?] combat slots need the combat map");
        return 0;
    }

    /// <summary>DS:65B9 + 4·slot: the creature in a combat slot.</summary>
    private EclCharacter? SlotCreature(int slot) => slot > 0 && Map is { } m ? m.SlotCreature(slot) : null;

    partial void CallHoldHandlers(byte id, EclCharacter c, byte[]? node, bool removed, ref bool handled)
    {
        handled = true;
        var rec = F.Record(c);
        byte param = node is { Length: > 3 } ? node[3] : (byte)0;
        switch (id)
        {
            case 40: // 0xF91E: the stinking cloud ends (runs only as the removal call: the record's byte 4 is set)
                CloudGone(c, param);
                return;
            case 73: // 0x161CD rake: two hits in phase 1 and the last target still in play → two 2d4 attacks now
            {
                if (F.HitsMade[1] != 2 || rec.Other is not { } o || o[InPlay] == 0)
                    return;
                rec[CombatRecord.AttackPhase] = 2;
                c[0x114] = 2;
                c[0x116] = 2;
                c[0x118] = 4;
                c[0x113] = 0;
                Emit(c, EffectEvent.Other);
                MakeAttack(c, o, false);
                FightRules.EndActions(rec);
                return;
            }
            case 74: // 0x16281 hold (from 77)
                Hold(c, node!, removed, rec);
                return;
            case 75: // 0x103CA blood drain (from 76)
                BloodDrain(c, node!, removed, rec);
                return;
            case 76: // 0x10510: attach to the last target: 0x4B on the creature (207 minutes, param = the target's
                     // combat slot, removal on), 0x3A on the target (permanent, param 255) and its handler
            {
                var o = rec.Other;
                Cells.Other = o;
                if (o is null)
                    return;
                AddEffect(c, 0x4B, 0xCF, (byte)SlotOf(o), true);
                AddEffect(o, 0x3A, 0, 0xFF, false);
                CallHandler(0x3A, o, null, false);
                return;
            }
            case 77: // 0x10590: a last target with Strength below 19 is held: 0x4A on the creature (permanent, the
                     // target's slot, removal on), 0x3A on the target and its handler; message
            {
                var o = rec.Other;
                Cells.Other = o;
                if (o is null || o[Str] >= 0x13)
                    return;
                AddEffect(c, 0x4A, 0, (byte)SlotOf(o), true);
                AddEffect(o, 0x3A, 0, 0xFF, false);
                CallHandler(0x3A, o, null, false);
                Emit(o, EffectEvent.Other);
                return;
            }
            case 81: // 0x10705 fear aura (fight start)
                FearAura(c);
                return;
            case 82: // 0x10803 paralysing aura (fight start)
                ParalysingAura(c);
                return;
            case 83: // 0x10908 petrifying gaze
                Gaze(c, rec);
                return;
            case 84: // 0x10AD2 charm gaze
                CharmGaze(c, rec);
                return;
            case 85: // 0x10FFF: the last target loses one level (0x10D7D)
                if (rec.Other is { } t85)
                    LevelDrain(t85, 1);
                return;
            case 86: // 0x1101D: two levels
                if (rec.Other is { } t86)
                    LevelDrain(t86, 2);
                return;
            case 88: // 0x2A0F1 lightning breath (entry written at 0x2A7B9 by the spell table builder)
                LightningBreath(c, node!, rec);
                return;
            case 121: // 0x11877 acid breath
                AcidBreath(c, node!, rec);
                return;
        }
        handled = false;
    }

    private void MakeAttack(EclCharacter attacker, EclCharacter defender, bool noTurn)
    {
        if (_attached is { } f)
            f.MakeAttack(attacker, defender, noTurn);
        else
            Diag("[?] handler attacks (0x1407F) need the fight");
    }

    /// <summary>
    /// 0xF91E: the cloud of this creature with number param ÷ 16 (the first match in DS:6DBB); none → nothing. Else
    /// (message) each clouded cell gets back a body's terrain 0x1F when a body (DS:66D6 + 7k, k = 1..DS:6715) lies on it,
    /// else the terrain it covered; the node leaves the list (FreeMem) and every remaining cloud's cells are clouded
    /// again (0x1E), so overlapping clouds stay.
    /// </summary>
    private void CloudGone(EclCharacter c, byte param)
    {
        int n = param >> 4;
        var cloud = Clouds.FirstOrDefault(k => ReferenceEquals(k.Caster, c) && k.Index == n);
        if (cloud is null)
            return;
        Emit(c, EffectEvent.Other);
        var map = Map;
        if (map is null)
            Diag("[?] stinking cloud cells need the combat map");
        for (int i = 1; i <= 4 && map is not null; i++)
        {
            if (!cloud.Used[i])
                continue;
            var (x, y) = cloud.Cell(i);
            bool body = map.Bodies.Any(b => b is not null && b.X == x && b.Y == y);
            map.Arena[x, y] = body ? (byte)0x1F : cloud.Under[i];
        }
        Clouds.Remove(cloud);
        foreach (var k in Clouds)
            for (int i = 1; i <= 4 && map is not null; i++)
                if (k.Used[i])
                {
                    var (x, y) = k.Cell(i);
                    map.Arena[x, y] = 0x1E;
                }
    }

    /// <summary>
    /// 0x16281: the held creature is DS:65B9[param]. Removed, or either out of play: its 0x3A goes and, when not the
    /// removal call, this record goes too (byte 4 cleared first). Else one 3d4 attack in phase 1 (0x113 = 1, 0x114 = 0,
    /// 0x115 = 3, 0x117 = 4), MakeAttack without the defender turning, actions end; a held creature now out of play
    /// ends the hold (its removal call runs).
    /// </summary>
    private void Hold(EclCharacter c, byte[] node, bool removed, CombatRecord rec)
    {
        var o = SlotCreature(node[3]);
        Cells.Other = o;
        if (removed || c[InPlay] == 0 || o is null || o[InPlay] == 0)
        {
            if (o is not null)
                RemoveEffect(o, 0x3A);
            if (!removed)
            {
                node[4] = 0;
                RemoveEffect(c, 0x4A, node);
            }
            return;
        }
        c[0x113] = 1;
        c[0x114] = 0;
        c[0x115] = 3;
        c[0x117] = 4;
        MakeAttack(c, o, true);
        FightRules.EndActions(rec);
        if (o[InPlay] == 0)
            RemoveEffect(c, 0x4A, node);
    }

    /// <summary>
    /// 0x103CA: the victim is DS:65B9[param]. Removed, or either out of play: the victim's 0x3A goes; when not the
    /// removal call this record goes (byte 4 cleared first); with both still in play the creature's combat record
    /// +0x10 = 1. Else (message) 1d4 (count to DS:681C), element 0, non-weapon damage to the victim, actions end;
    /// victim still in play and the record's minutes ÷ 16 above the damage → minutes − damage × 16; otherwise the record
    /// goes (its removal call frees the victim).
    /// </summary>
    private void BloodDrain(EclCharacter c, byte[] node, bool removed, CombatRecord rec)
    {
        var o = SlotCreature(node[3]);
        Cells.Other = o;
        if (removed || c[InPlay] == 0 || o is null || o[InPlay] == 0)
        {
            if (o is not null)
                RemoveEffect(o, 0x3A);
            if (!removed)
            {
                node[4] = 0;
                RemoveEffect(c, 0x4B, node);
            }
            if (c[InPlay] != 0 && o is not null && o[InPlay] != 0)
                rec[CombatRecord.Turned] = 1;
            return;
        }
        Emit(c, EffectEvent.Other);
        byte d = DamageDiceRoll(1, 4);
        F.DamageFlags = 0;
        NonWeaponDamage(o, d, 0, false);
        FightRules.EndActions(rec);
        ushort minutes = (ushort)(node[1] | (node[2] << 8));
        if (o[InPlay] != 0 && (minutes >> 4) > d)
        {
            minutes = (ushort)(minutes - (d << 4));
            node[1] = (byte)minutes;
            node[2] = (byte)(minutes >> 8);
        }
        else
            RemoveEffect(c, 0x4B, node);
    }

    /// <summary>START 0x2F57: the other side's hostile byte (1 for side 0, else 0).</summary>
    private static byte OtherSide(EclCharacter c) => c[Hostile] == 0 ? (byte)1 : (byte)0;

    /// <summary>
    /// 0x10705 (class 8): every creature in play on the other side saves vs. type 4 (one draw each); hit-dice byte 0x73
    /// 0..3 → paralysis 0x34, 4 or 5 → Curse 2; both permanent, param 12, no removal call, a save negates (0x2C540).
    /// </summary>
    private void FearAura(EclCharacter c)
    {
        byte other = OtherSide(c);
        foreach (var p in List().ToList())
        {
            if (p[InPlay] == 0 || p[Hostile] != other)
                continue;
            bool saved = SavingThrow(p, 4, 0);
            sbyte hd = (sbyte)p[0x73];
            if (hd is >= 0 and <= 3)
                AddEffectWithMessage(p, 0x34, 0, 0x0C, false, 1, saved, EffectEvent.Other, EffectTexts.FearParalysis); // 0x10794
            else if (hd is 4 or 5)
                AddEffectWithMessage(p, 0x02, 0, 0x0C, false, 1, saved, EffectEvent.Other, EffectTexts.FearWeakened); // 0x107CB
        }
    }

    /// <summary>
    /// 0x10803 (class 8): for every creature in the list its first 0x52 goes (the carrier's too, so it acts once); each
    /// one in play on the other side saves vs. type 0 with +1 when its side's team count (DS:6814 + side) is at least 6
    /// and +2 for race 7, then paralysis 0x34 for 1d4 minutes (rolled after the save), param 12, a save negates.
    /// </summary>
    private void ParalysingAura(EclCharacter c)
    {
        byte other = OtherSide(c);
        foreach (var p in List().ToList())
        {
            RemoveEffect(p, 0x52);
            if (p[InPlay] == 0 || p[Hostile] != other)
                continue;
            int mod = 0;
            if (p[Hostile] < 2 && F.TeamCount[p[Hostile]] >= 6)
                mod++;
            if (p[Race] == 7)
                mod += 2;
            bool saved = SavingThrow(p, 0, mod);
            ushort minutes = Dice(1, 4);
            AddEffectWithMessage(p, 0x34, minutes, 0x0C, false, 1, saved, EffectEvent.Other, EffectTexts.AuraParalysis); // 0x108C7
        }
    }

    /// <summary>
    /// The moving icon (START 0x25C9) from <paramref name="from"/>'s square to <paramref name="to"/>'s, both on the
    /// combat map (squares 0x31321 / 0x31349), through <see cref="FightState.Flight"/>. A display hook only.
    /// </summary>
    private void Fly(EclCharacter from, EclCharacter? to, Func<int, int, int, int, FlightAnimation> flight)
    {
        if (F.Flight is { } fly && to is not null && Map is { } m && m.OnMap(from) && m.OnMap(to))
        {
            var (fx, fy) = m.Position(from);
            var (tx, ty) = m.Position(to);
            fly(flight(fx, fy, tx, ty));
        }
    }

    /// <summary>
    /// 0x10908 (class 0x0E): (message) the gaze at the last target, the moving icon from the gazer to it (0x10987).
    /// When the gazer carries 0x7F, any readied item (byte 0x34) of the target with name word 0x76 at 0x2F, 0x30 or
    /// 0x31 reflects it (message, the icon back from the target to the gazer, 0x10A59): the gazer becomes the victim.
    /// (DOSBox-X s27: the word on the armour and shield alone, not on the weapon read first, reflected.)
    /// The victim saves vs. type 1; failing, it turns to stone (0xECB3, status 7).
    /// </summary>
    private void Gaze(EclCharacter c, CombatRecord rec)
    {
        var o = rec.Other;
        Cells.Other = o;
        Emit(c, EffectEvent.Gazes); // 0x10930: START 0x16EC from row 10, no pause
        // 0x10945–0x10987: 0x257E(cell 0x12), the moving icon from the gazer to the target, 4 frames, 45 ms.
        Fly(c, o, FlightAnimation.Gaze);
        if (o is null)
            return;
        if (FindEffect(c, 0x7F) is not null)
            foreach (var it in o.Items)
            {
                if (it.Length > 0x34 && it[0x34] != 0 && (it[0x2F] == 0x76 || it[0x30] == 0x76 || it[0x31] == 0x76))
                {
                    Emit(o, EffectEvent.GazeReflected); // 0x10A0A: START 0x16EC from row 12, no pause
                    // 0x10A1F–0x10A59: the same sprite back from the target to the gazer.
                    Fly(o, c, FlightAnimation.Gaze);
                    o = c;
                    Cells.Other = c;
                    break;
                }
            }
        if (!SavingThrow(o, 1, 0))
            InflictStatus(o, 7, EffectEvent.Stoned); // 0x10AAA: 0xECB3's message, row 10, no pause
    }

    /// <summary>
    /// 0x10AD2 (class 0x0E): the last target must pass the target check (0x13880) and be in sight (0x2FF74 from the
    /// gazer's square; [?] the range passed is an uninitialised stack byte, taken as 255 here). Then (message, the moving
    /// icon 0x257E(cell 0x12) from the gazer to the target, 4 frames, 45 ms, 0x10BC6) DS:681B =
    /// 10 (Charm Person; not reset afterwards), the target saves vs. type 4 with −2 and gets charm 0x0B (permanent,
    /// param = gazer's hostile × 128 + 12, removal on, a save negates); with a charm now on it its handler runs.
    /// </summary>
    private void CharmGaze(EclCharacter c, CombatRecord rec)
    {
        var o = rec.Other;
        Cells.Other = o;
        if (_attached is not { } f)
        {
            Diag("[?] handler 84 needs the fight (target check 0x13880)");
            return;
        }
        if (!FightMoves.CanTarget(f, c, o))
            return;
        if (Map is { } m)
        {
            var (cx, cy) = m.Position(c);
            var (ox, oy) = m.Position(o!);
            int dist = 0xFF;
            Diag("[?] handler 84's sight range is an uninitialised byte; 255 taken");
            if (!m.WalkLine(cx, cy, ref ox, ref oy, ref dist))
                return;
        }
        else
            Diag("[?] handler 84's line of sight (0x2FF74) needs the combat map");
        Emit(c, EffectEvent.CharmGazes); // 0x10B6F: START 0x16EC from row 10, no pause
        Fly(c, o, FlightAnimation.Gaze); // 0x10B84–0x10BC6
        Cells.Spell = 0x0A;
        bool saved = SavingThrow(o!, 4, -2);
        AddEffectWithMessage(o!, 0x0B, 0, (byte)(((sbyte)c[Hostile] << 7) + 0x0C), true, 1, saved, EffectEvent.Charmed,
            EffectTexts.CharmGaze); // 0x10C20
        if (FindEffect(o!, 0x0B) is { } n)
            CallHandler(0x0B, o!, n, false);
    }

    /// <summary>
    /// 0x2A0F1 (class 0x0E, handler 88; its DS:6828 entry is written by the spell table builder at 0x2A7B9, not by
    /// 0x11DFD): after the first round (round counter DS:6DE3 ≠ 0) a 1d100 ≤ 50 skips it. Else (message, START 0x16EC
    /// from row 10 with its pause) the breath (<see cref="FightSpells.LightningBreath"/>: a Lightning Bolt pick, the
    /// aim one square from the breather, damage = its byte 0x32); then the record's byte 3 counts down while above
    /// 0xFD (so 0xFF gives three breaths), else the record goes; the actions end (START 0x2637).
    /// </summary>
    private void LightningBreath(EclCharacter c, byte[] node, CombatRecord rec)
    {
        if (F.RoundCounter != 0 && Dice(1, 100) <= 0x32)
            return;
        Emit(c, EffectEvent.Breathes); // 0x2A137: START 0x16EC from row 10 with the pause
        if (_attached is { Spells: FightSpells spells } f)
            spells.LightningBreath(f, c);
        else
            Diag("[?] handler 88's breath needs the fight's spells (FightSpells)");
        if (node[3] > 0xFD)
            node[3]--;
        else
            RemoveEffect(c, 0x58, node);
        FightRules.EndActions(rec);
    }

    /// <summary>
    /// 0x11877 (class 0x0E): 1d100 ≤ 25 and the last target nearer than 4 squares (START 0x30FB): actions end,
    /// (message) the moving icon 0x257E(cell 0x17) with a frame count of 1 from the breather to the target (30 ms,
    /// 0x11930), 8d4 (count to DS:681C), then the target saves vs. type 3 (breath) and takes it as non-weapon damage
    /// (halved when saved); the record goes and so does the creature's first 0x50 (its acid bite).
    /// </summary>
    private void AcidBreath(EclCharacter c, byte[] node, CombatRecord rec)
    {
        var o = rec.Other;
        Cells.Other = o;
        if (Dice(1, 100) > 0x19 || o is null)
            return;
        if (Geometry is null)
        {
            Diag("[?] handler 121's distance needs the combat map");
            return;
        }
        if (Geometry.Distance(c, o) >= 4)
            return;
        FightRules.EndActions(rec);
        Emit(c, EffectEvent.SpitsAcid); // 0x118D9: START 0x16EC from row 10 with the pause
        Fly(c, o, FlightAnimation.AcidBreath); // 0x118EE–0x11930
        byte d = DamageDiceRoll(8, 4);
        bool saved = SavingThrow(o, 3, 0);
        NonWeaponDamage(o, d, 2, saved);
        RemoveEffect(c, 0x79, node);
        RemoveEffect(c, 0x50);
    }

    /// <summary>
    /// 0x10D7D (handlers 85, 86): (message) an animated creature (status 1) is destroyed (0xECB3 status 8, max HP 0).
    /// Else per level, unless already gone (status 8): drained count 0x74 + 1; n = max HP ÷ the sum of the eight class
    /// levels 0x96..0x9D; n times max HP, rolled HP 0xB1 and HP − 1 (not below 0) and HP drained 0x75 + 1. Hit-dice byte
    /// 0x73 below 2 → gone (status 8, max HP 0), and with two levels drained its top class level becomes 0. Else the top
    /// class (0x10C5D) loses a level, HP 0 and not dead → dead (status 6), level stats (0x2AA87). Then 0x73 = the top
    /// class's level, experience 0xAC = that level's threshold (DS:409B), and the stats are recalculated (START 0x1758).
    /// </summary>
    public void LevelDrain(EclCharacter t, int count)
    {
        Emit(t, EffectEvent.Other);
        if (t[Status] == 1)
        {
            InflictStatus(t, 8, EffectEvent.StatusChange);
            t[HpMax] = 0;
            return;
        }
        for (int k = 1; k <= count; k++)
        {
            if (t[Status] == 8)
                continue;
            t[0x74]++;
            byte total = 0;
            for (int i = 0; i < 8; i++)
                total += t[ClassLevels + i];
            int per = 0;
            if (total == 0)
                Diag("[?] level drain with no class levels divides by 0");
            else
                per = (byte)(t[HpMax] / total);
            for (int j = 1; j <= per; j++)
            {
                if (t[HpMax] > 0)
                    t[HpMax]--;
                if (t[HpRolled] > 0)
                    t[HpRolled]--;
                if (t[Hp] > 0)
                    t[Hp]--;
                t[0x75]++;
            }
            if ((sbyte)t[0x73] < 2)
            {
                InflictStatus(t, 8, EffectEvent.StatusChange);
                t[HpMax] = 0;
                int cls = TopClass(t, out _);
                if ((sbyte)t[ClassLevels + cls] > 0 && count == 2)
                    t[ClassLevels + cls] = 0;
            }
            else
            {
                int cls = TopClass(t, out _);
                t[ClassLevels + cls]--;
                if (t[Hp] == 0 && t[Status] != 6)
                    InflictStatus(t, 6, EffectEvent.StatusChange);
                CallLevelStats(t);
            }
            int top = TopClass(t, out int xp);
            t[0x73] = t[ClassLevels + top];
            t[0xAC] = (byte)xp;
            t[0xAD] = (byte)(xp >> 8);
            t[0xAE] = (byte)(xp >> 16);
            t[0xAF] = (byte)(xp >> 24);
            RecalculateStats(t);
        }
    }

    /// <summary>
    /// 0x10C5D: over classes 0..7, threshold = DS:409B[class][level] for a level above 1, else 0; a class whose level is
    /// at least the best so far and whose threshold is at least the best threshold (signed) or −1 becomes the result.
    /// </summary>
    private int TopClass(EclCharacter t, out int threshold)
    {
        threshold = 0;
        int best = 0, res = 0;
        for (int cls = 0; cls < 8; cls++)
        {
            int lvl = (sbyte)t[ClassLevels + cls];
            int thr = lvl > 1 ? Experience(cls, lvl) : 0;
            if (lvl < best)
                continue;
            if (thr >= threshold || thr == -1)
            {
                threshold = thr;
                res = cls;
                best = lvl;
            }
        }
        return res;
    }
}
