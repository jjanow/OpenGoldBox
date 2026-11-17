using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The sweep attack (GAME.OVR 0x1367D, docs/re/combat-menu.md "Sweep attacks"): a fighter next to several enemies of
/// less than one hit die attacks each of them once. Called by the menu's move-attack (0xA5BD), Aim's Target (0x154F2)
/// and the AI (0xBA3C) before a normal attack. No draws besides the attacks'.
/// </summary>
public static class SweepAttack
{
    /// <summary>Record 0x73: hit dice (monsters) or highest level (characters); the sweep takes only 0.</summary>
    public const int HitDice = CombatRules.LevelHighest;

    /// <summary>
    /// 0x1367D. Applies only when the attacker's phase-1 attacks left (0x113) are below its sweep budget (+5), the
    /// defender's 0x73 is 0, the distance is 1, and the attacker's adjacent enemies (START 0x2FD3 range 1) with 0x73 = 0
    /// are more than 0x113. Then count = min(that number, +5); the defender is put first in the list (it swaps places
    /// with the first entry); each entry in order with 0x73 = 0, while count &gt; 0, gets 0x113 = 1 and the note-direction
    /// + MakeAttack pair (<see cref="Fight.Attack"/>, no ammunition). Returns true when the sweep was made.
    /// </summary>
    public static bool Try(Fight fight, EclCharacter attacker, EclCharacter defender,
        Action<FightNotice, EclCharacter?>? notice = null)
    {
        var aRec = fight.Record(attacker);
        if (attacker[0x113] >= aRec[CombatRecord.SweepBudget])
            return false;
        if (defender[HitDice] != 0)
            return false;
        if (fight.Geometry.Distance(attacker, defender) != 1)
            return false;
        var list = FightSearch.EnemiesNear(fight, attacker, 1);
        int pos = list.FindIndex(x => ReferenceEquals(x, defender));
        int count = list.Count(x => x[HitDice] == 0);
        if (count <= attacker[0x113])
            return false;
        if (count > aRec[CombatRecord.SweepBudget])
            count = aRec[CombatRecord.SweepBudget];
        notice?.Invoke(FightNotice.Sweeps, attacker);
        if (list.Count > 0 && !ReferenceEquals(list[0], defender))
        {
            // [?] A defender missing from the list (an ally) makes the program write the old first entry to an index it
            // never set; here the defender just replaces the first entry.
            if (pos >= 0)
                list[pos] = list[0];
            list[0] = defender;
        }
        foreach (var e in list)
        {
            if (count == 0)
                break;
            if (e[HitDice] != 0)
                continue;
            attacker[0x113] = 1;
            fight.Attack(attacker, e);
            count--;
        }
        return true;
    }
}
