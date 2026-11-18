namespace GoldBox.Core.Ecl;

/// <summary>What DAMAGE (0x2E) did to one creature, for the text box ("dies" / "is hit FOR n points").</summary>
public readonly record struct EclDamageReport(EclCharacter Target, int Damage, bool Dies);

/// <summary>
/// The party opcodes the spec reads from the program (docs/re/ecl-format.md "Party, damage and text-box opcodes"):
/// LOADCHAR (GAME.OVR 0x1026), CHKPARTY (0x217B), SPELL (0x3949), ROB (0x2AF7) and DAMAGE (0x34BB). They work on the
/// party list (<see cref="EclParty.Members"/>, which also holds the creatures LOADMON appended) and draw their dice
/// from <see cref="Random"/> (Turbo Pascal Random(n), 0..n−1), in the program's order.
/// </summary>
public sealed class EclPartyRules(EclMemory memory, Func<int, int> random)
{
    /// <summary>VM cell holding the party size (DS:49D6 + 0x67C).</summary>
    public const ushort PartySizeCell = 0x6E3E;

    /// <summary>Record offsets used here (docs/re/character-format.md).</summary>
    public const int Hp = 0x11B, Status = 0x10C, InPlay = 0x10D, Hostile = 0x10E, Ac = 0x111, SaveBonus = 0x101,
        Saves = 0x6D, Morale = 0x84, Money = 0x88, MoneyWords = 7, ItemWeight = 0x37;

    /// <summary>Effect ids whose handlers run for trigger class 12 (saving throw) and 16 (to-hit) (0x2B1E4).</summary>
    public static readonly byte[] SaveTriggerEffects = [8, 9, 10, 0x11, 0x14, 0x21, 0x24, 0x2D, 0x2E, 0x31, 0x3D, 0x6F, 0x7D, 0x5A, 0x61];
    public static readonly byte[] HitTriggerEffects = [0x19, 0x47, 0x25, 0x2F, 0x30, 0x59];

    public EclMemory Memory { get; } = memory;
    public Func<int, int> Random { get; } = random;

    /// <summary>
    /// [?] The effect handlers of trigger classes 12 and 16 (not read): (creature, class, roll) → roll. Null: the roll
    /// is left as it is, and a creature carrying one of the listed effects is reported through <see cref="Diagnostic"/>.
    /// </summary>
    public Func<EclCharacter, int, int, int>? EffectTrigger { get; set; }

    /// <summary>DS:682A: the save type of the saving throw being rolled (read by class-12 handlers).</summary>
    public int LastSaveType { get; private set; }

    public event Action<string>? Diagnostic;
    /// <summary>DAMAGE applied to one creature (0x959F), for the text box.</summary>
    public event Action<EclDamageReport>? Damaged;

    private EclParty Party => Memory.Party ?? throw new InvalidOperationException("no party attached");

    /// <summary>Dice routine 0x2BCCD: count × (Random(sides) + 1), summed as a byte; count 0 gives 0.</summary>
    public int Dice(int count, int sides)
    {
        int sum = 0;
        for (int k = 0; k < (byte)count; k++)
            sum += Random((byte)sides) + 1;
        return sum & 0xFF;
    }

    // ---- LOADCHAR ----------------------------------------------------------------------------------------------

    /// <summary>
    /// LOADCHAR (0x1026): DS:4430 set; the (v AND 0x7F)-th list member (0-based walk from the head) becomes current
    /// when it exists. With bit 0x80 and both DS:84D2 and DS:84D3 set, the current character is removed from the list
    /// (0x1CC10): the predecessor (or the new head) becomes current, $6E3E − 1, and both flags are cleared.
    /// Returns the removed record, if any.
    /// </summary>
    public EclCharacter? LoadCharacter(int v)
    {
        var party = Party;
        party.ChangedByScript = true;
        int n = v & 0x7F;
        if (n < party.Members.Count)
            party.CurrentIndex = n;
        if ((v & 0x80) == 0 || !Memory.Ds84D2 || !Memory.Ds84D3)
            return null;
        var cur = party.Current;
        if (ReferenceEquals(cur, party.Selected))
            party.ChangedByScript = false;
        EclCharacter? removed = null;
        if (cur is not null)
        {
            int i = party.CurrentIndex;
            party.Members.RemoveAt(i);
            removed = cur;
            // 0x1CC10: the predecessor becomes current; when the head went, the new head (or none).
            party.CurrentIndex = i > 0 ? i - 1 : party.Members.Count > 0 ? 0 : -1;
            Memory.Write(PartySizeCell, (ushort)(Memory.Read(PartySizeCell) - 1));
        }
        Memory.Ds84D2 = false;
        Memory.Ds84D3 = false;
        return removed;
    }

    // ---- CHKPARTY ----------------------------------------------------------------------------------------------

    /// <summary>
    /// CHKPARTY (0x217B): selector 0 → (0, 0, 0, anyone has effect e); $6BA7 / $6C1B → (min, max, sum div count, 0) of
    /// record byte 0x79 / 0x11C over the list; any other selector → null (nothing written).
    /// </summary>
    public EclPartyCheck? CheckParty(ushort selector, int effect)
    {
        var members = Party.Members;
        if (selector == 0)
        {
            bool any = members.Any(m => m.HasEffect((byte)effect));
            return new EclPartyCheck(0, 0, 0, (ushort)(any ? 1 : 0));
        }
        int offset = selector switch { 0x6BA7 => 0x79, 0x6C1B => 0x11C, _ => -1 };
        if (offset < 0)
            return null;
        int min = 255, max = 0, sum = 0;
        foreach (var m in members)
        {
            min = Math.Min(min, m[offset]);
            max = Math.Max(max, m[offset]);
            sum += m[offset];
        }
        if (members.Count == 0)
            throw new EclVmFault("CHKPARTY over an empty party list: division by zero in the program");
        return new EclPartyCheck((ushort)min, (ushort)max, (ushort)((sum / members.Count) & 0xFF), 0);
    }

    // ---- SPELL -------------------------------------------------------------------------------------------------

    /// <summary>
    /// SPELL (0x3949): (slot index, member count). The member loop runs only while the uninitialised "found" byte is 0
    /// (<paramref name="foundResidue"/>, stack residue: non-zero in both DOSBox samples); it then scans the first member's record bytes 0x18.. for
    /// the spell: index i of the match (record 0x17 + i), or 255 after i = 0x52, and the count is 1. When the loop does
    /// not run: (1, 0).
    /// </summary>
    public (byte Index, byte Count) Spell(int spell, byte foundResidue)
    {
        var members = Party.Members;
        if (foundResidue != 0 || members.Count == 0)
            return (1, 0);
        var m = members[0];
        int i = 1;
        while (true)
        {
            if (m[0x17 + i] == (byte)spell)
                break;
            if (i > 0x51)
            {
                i = 0xFF;
                break;
            }
            i++;
        }
        return ((byte)i, 1);
    }

    // ---- ROB ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// ROB (0x2AF7): v1 = 0 the current character, else every list member. Money words 0x88..0x94 × (100 − p) / 100,
    /// truncated ([?] six-byte Real rounding; exact rational here). Items (0x92C3): per item the chance is reduced
    /// first (by 90 for weight &gt; 255, by 50 for weight &gt; 24, floor 0; the reduction carries over to the next
    /// items), then 1d100 ≤ chance loses it.
    /// </summary>
    public void Rob(int who, int percent)
    {
        var party = Party;
        int p = (byte)percent;
        IEnumerable<EclCharacter> targets = (byte)who == 0
            ? party.Current is { } c ? [c] : []
            : party.Members.ToList();
        foreach (var m in targets)
        {
            for (int k = 0; k < MoneyWords; k++)
            {
                int w = m.Word(Money + 2 * k);
                m.SetWord(Money + 2 * k, (ushort)(w * (100 - p) / 100));
            }
            int chance = p;
            foreach (var item in m.Items.ToList())
            {
                int weight = item.Length > ItemWeight + 1 ? item[ItemWeight] | (item[ItemWeight + 1] << 8) : 0;
                if (weight > 0xFF)
                    chance = chance > 90 ? chance - 90 : 0;
                else if (weight > 0x18)
                    chance = chance > 50 ? chance - 50 : 0;
                if (Dice(1, 100) <= chance)
                    m.Items.Remove(item);
            }
        }
    }

    // ---- DAMAGE ------------------------------------------------------------------------------------------------

    /// <summary>
    /// DAMAGE f, n, s, b, t (0x34BB, all low bytes). Returns true when no list member is left in play (DS:49FF set).
    /// </summary>
    public bool Damage(int f, int n, int s, int b, int t)
    {
        var party = Party;
        var members = party.Members;
        int saved = party.CurrentIndex;
        int d = Dice(n, s) + b;
        bool wholeParty = (f & 0x40) != 0;
        int r = 0;
        if (!wholeParty)
            r = Dice(1, Memory.Read(PartySizeCell) & 0xFF);
        if ((f & 0x80) != 0)
        {
            int bonus = f & 0x1F, type = t & 7;
            if (wholeParty)
            {
                foreach (var m in members.ToList())
                    if ((f & 0x20) != 0 || !SavingThrow(m, type, bonus))
                        Apply(m, d);
            }
            else if ((t & 0x80) != 0)
            {
                if (party.Current is { } cur && (type == 0 || !SavingThrow(cur, type - 1, bonus)))
                    Apply(cur, d);
            }
            else if (Member(r) is { } m && !SavingThrow(m, type, bonus))
                Apply(m, d);
        }
        else
        {
            for (int k = 1; k <= f; k++)
            {
                r = Dice(1, Memory.Read(PartySizeCell) & 0xFF);
                if (MemberFromHead(r) is { } m && Hits(m, t))
                    Apply(m, d);
                d = Dice(n, s) + b;
            }
        }
        party.CurrentIndex = saved < members.Count ? saved : -1;
        return !members.Any(m => m[InPlay] != 0);
    }

    /// <summary>The r-th list member for the save form: walk from the head, r ≤ 1 = the head (0x3624 loop from 2).</summary>
    private EclCharacter? Member(int r)
    {
        var members = Party.Members;
        int i = Math.Max(r, 1) - 1;
        if (members.Count == 0)
            return null;
        return i < members.Count ? members[i] : null;
    }

    /// <summary>The r-th member for the attack form (walk while index &lt; r, starting at 1).</summary>
    private EclCharacter? MemberFromHead(int r) => Member(r);

    /// <summary>Saving throw 0x2BC4D: 1d20; 1 fails, 20 saves; else roll + 0x101 + bonus (signed bytes) ≥ save byte.</summary>
    public bool SavingThrow(EclCharacter m, int type, int bonus)
    {
        int roll = Dice(1, 20);
        if (roll == 1)
            return false;
        if (roll == 20)
            return true;
        int total = (byte)(roll + (sbyte)m[SaveBonus] + (sbyte)(byte)bonus);
        LastSaveType = type;
        total = Trigger(m, 12, total, SaveTriggerEffects);
        return m[Saves + type] <= (byte)total;
    }

    /// <summary>To-hit 0x2BB39: 1d20 (≤ 1 misses, 20 counts as 100), then hits when roll + t &gt; AC byte 0x111.</summary>
    public bool Hits(EclCharacter m, int t)
    {
        int roll = Dice(1, 20);
        if (roll <= 1)
            return false;
        if (roll == 20)
            roll = 100;
        roll = (sbyte)(byte)Trigger(m, 16, roll, HitTriggerEffects);
        if (roll < 0)
            return false;
        return roll + (byte)t > m[Ac];
    }

    private int Trigger(EclCharacter m, int cls, int roll, byte[] ids)
    {
        if (EffectTrigger is { } hook)
            return hook(m, cls, roll);
        if (m.Effects.Any(e => e.Length > 0 && ids.Contains(e[0])))
            Diagnostic?.Invoke($"[?] effect handlers of trigger class {cls} not modelled ({m.Name})");
        return roll;
    }

    /// <summary>
    /// 0x959F → START.EXE 0x2DC8: nothing for status 6; else (damage as a byte) HP − d or HP 0 with "over" = d − HP;
    /// status 6 when over &gt; 9 or (HP 0 and status 1), 5 when over &gt; 0, 4 when HP 0; a status other than 0/1 then
    /// takes the creature out of play with HP 0.
    /// </summary>
    public void Apply(EclCharacter m, int damage)
    {
        if (m[Status] == 6)
            return;
        Damaged?.Invoke(new EclDamageReport(m, damage, m[Hp] + 10 < damage));
        int d = damage & 0xFF, hp = m[Hp], over = 0, left = 0;
        if (hp >= d)
            left = hp - d;
        else
            over = (d - hp) & 0xFF;
        if (over > 9 || (left == 0 && m[Status] == 1))
            m[Status] = 6;
        else if (over > 0)
            m[Status] = 5;
        else if (left == 0)
            m[Status] = 4;
        if (m[Status] is 0 or 1)
            m[Hp] = (byte)left;
        else
        {
            m[InPlay] = 0;
            m[Hp] = 0;
        }
    }
}
