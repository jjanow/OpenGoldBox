using System.Globalization;

namespace GoldBox.Core.Ecl;

/// <summary>
/// A headless host for tests and tools: records every call and answers from queues (menu choices, numbers, texts,
/// random values, FINDITEM results). An empty queue gives the default answer (0, "", false, no spell), or ends the
/// run when <see cref="StopWhenInputMissing"/> is set.
/// </summary>
public sealed class ScriptedEclHost : IEclHost
{
    public List<string> Calls { get; } = [];

    public Queue<ushort> MenuChoices { get; } = new();
    public Queue<int> Numbers { get; } = new();
    public Queue<string> Strings { get; } = new();
    public Queue<ushort> RandomValues { get; } = new();
    public Queue<bool> FindItemResults { get; } = new();
    public Queue<ushort> ParleyResults { get; } = new();
    public Queue<ushort> EncounterResults { get; } = new();

    /// <summary>
    /// The game's generator: when set, Random(n) draws from it once <see cref="RandomValues"/> is empty. Seed it from a
    /// capture's RandSeed to reproduce that run's rolls.
    /// </summary>
    public TurboPascalRandom? Rng { get; set; }

    /// <summary>Answer for Random(n) when the queue is empty and <see cref="Rng"/> is null: null = 0, else a .NET generator.</summary>
    public Random? RandomFallback { get; set; }

    /// <summary>
    /// When set, a menu or input request with an empty queue throws <see cref="EclAwaitingInput"/> instead of
    /// answering by default, so the run ends where the game would wait for the player.
    /// </summary>
    public bool StopWhenInputMissing { get; set; }

    /// <summary>
    /// Called for every Random(n) request before it is answered; returning a value answers it (used by the trace
    /// compare's roll search). Null or a null return = the normal sources.
    /// </summary>
    public Func<ushort, ushort?>? RandomOverride { get; set; }

    /// <summary>
    /// Exploration mode: when set, empty queues are answered from this generator instead of the defaults (menu
    /// index within the choices, FINDITEM yes/no, PARLEY/ENCMENU 0..4). For coverage runs only; the answers mean
    /// nothing about the game.
    /// </summary>
    public Random? AnswerFallback { get; set; }

    public ushort PartyStrengthValue { get; set; }
    public EclPartyCheck PartyCheckValue { get; set; }

    /// <summary>Printed text, concatenated (PRINTCLR starts a new box).</summary>
    public System.Text.StringBuilder TextBox { get; } = new();

    private void Log(string s) => Calls.Add(s);

    private ushort Pick(int count, string request)
    {
        if (StopWhenInputMissing)
            throw new EclAwaitingInput(request);
        return AnswerFallback is null || count <= 0 ? (ushort)0 : (ushort)AnswerFallback.Next(count);
    }

    public void Print(string text, bool clearFirst)
    {
        if (clearFirst)
            TextBox.Clear();
        TextBox.Append(text);
        Log((clearFirst ? "PrintClear " : "Print ") + text);
    }
    public void PrintReturn() { TextBox.Append('\n'); Log("PrintReturn"); }
    public void ClearBox() { TextBox.Clear(); Log("ClearBox"); }
    public void Delay() => Log("Delay");

    public ushort VerticalMenu(string prompt, IReadOnlyList<string> choices)
    {
        Log($"VerticalMenu {prompt} [{string.Join("|", choices)}]");
        return MenuChoices.TryDequeue(out var c) ? c : Pick(choices.Count, "VerticalMenu");
    }
    public ushort HorizontalMenu(IReadOnlyList<string> choices)
    {
        Log($"HorizontalMenu [{string.Join("|", choices)}]");
        return MenuChoices.TryDequeue(out var c) ? c : Pick(choices.Count, "HorizontalMenu");
    }
    public int InputNumber()
    {
        Log("InputNumber");
        if (Numbers.TryDequeue(out var n))
            return n;
        Pick(0, "InputNumber");
        return 0;
    }
    public string InputString()
    {
        Log("InputString");
        if (Strings.TryDequeue(out var s))
            return s;
        Pick(0, "InputString");
        return "";
    }
    public void Who(string prompt) => Log("Who " + prompt);
    public ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive)
    {
        Log($"Parley {haughty} {sly} {nice} {meek} {abusive}");
        return ParleyResults.TryDequeue(out var r) ? r : Pick(5, "Parley");
    }
    public ushort? EncounterMenu(EclEncounterMenu menu)
    {
        Log($"EncounterMenu {menu.Monster} {menu.Distance} {menu.Picture}");
        return EncounterResults.TryDequeue(out var r) ? r : Pick(5, "EncounterMenu");
    }

    public void LoadCharacter(ushort index) => Log($"LoadCharacter {index}");
    public void LoadMonster(ushort monster, ushort count, ushort picture) => Log($"LoadMonster {monster} {count} {picture}");
    public void SetupMonster(ushort monster, ushort distance, ushort picture) => Log($"SetupMonster {monster} {distance} {picture}");
    public void Approach() => Log("Approach");
    public void ClearMonsters() => Log("ClearMonsters");
    public ushort PartyStrength() { Log("PartyStrength"); return PartyStrengthValue; }
    public EclPartyCheck CheckParty(ushort field, ushort effect) { Log($"CheckParty {field} {effect}"); return PartyCheckValue; }
    public void Combat() => Log("Combat");
    public void Treasure(IReadOnlyList<ushort> values) => Log("Treasure " + string.Join(" ", values));
    public void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance) => Log($"Rob {wholeParty} {moneyPercent} {itemChance}");
    public void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra) => Log($"Damage {attack} {dice} {sides} {bonus} {extra}");
    public void SpriteOff() => Log("SpriteOff");
    public bool FindItem(ushort item)
    {
        Log($"FindItem {item}");
        if (FindItemResults.TryDequeue(out var r))
            return r;
        return AnswerFallback is not null && AnswerFallback.Next(2) == 1;
    }
    public void AddNpc(ushort monster, ushort morale) => Log($"AddNpc {monster} {morale}");
    public (ushort Slot, ushort Character)? Spell(ushort spell) { Log($"Spell {spell}"); return null; }

    public void Picture(ushort picture) => Log($"Picture {picture}");
    /// <summary>Called on LOADFILES with a map (the 3D view's area entry), after the call is logged.</summary>
    public Action<int>? MapLoaded { get; set; }
    /// <summary>Called on LOADPIECES / wall-set writes: (slot 1..3, WALLDEF id or null = empty), after logging.</summary>
    public Action<int, int?>? WallSetLoaded { get; set; }

    public void LoadMap(int map)
    {
        Log($"LoadMap {map}");
        MapLoaded?.Invoke(map);
    }
    public void LoadOutdoorPicture() => Log("LoadOutdoorPicture");
    public void LoadWallSet(int slot, int? set)
    {
        Log($"LoadWallSet {slot} {(set is { } s ? s.ToString(CultureInfo.InvariantCulture) : "empty")}");
        WallSetLoaded?.Invoke(slot, set);
    }
    public void Clock(ushort minutes) => Log($"Clock {minutes}");
    public void Call(ushort routine) => Log($"Call {routine:X4}");
    public void Program(ushort program) => Log($"Program {program}");
    public void Protect(ushort address) => Log($"Protect {address:X4}");
    /// <summary>Every Random(n) answer given, as (range, value).</summary>
    public List<(ushort Range, ushort Value)> RandomLog { get; } = [];

    public ushort Random(ushort range)
    {
        Log($"Random {range}");
        ushort r;
        if (RandomOverride?.Invoke(range) is { } o)
            r = o;
        else if (RandomValues.TryDequeue(out var q))
            r = q;
        else if (Rng is not null)
            r = Rng.Next(range);
        else
            r = RandomFallback is null || range == 0 ? (ushort)0 : (ushort)RandomFallback.Next(range);
        RandomLog.Add((range, r));
        return r;
    }
    public void ScriptChanging(ushort script) => Log($"ScriptChanging {script}");

    /// <summary>WHO answers: party-list index picked (empty queue: no change, or awaiting input when set).</summary>
    public Queue<int> CharacterPicks { get; } = new();
    public int? PickCharacter(string prompt)
    {
        Log("PickCharacter " + prompt);
        if (CharacterPicks.TryDequeue(out var k))
            return k;
        if (StopWhenInputMissing)
            throw new EclAwaitingInput("PickCharacter");
        return null;
    }
    public void DrawGlyphs(ushort address, IReadOnlyList<byte> glyphs) => Log($"DrawGlyphs {address:X4} {string.Join(" ", glyphs)}");
    /// <summary>ADDNPC record builder for tests (null = none built).</summary>
    public Func<ushort, EclCharacter?>? NpcFactory { get; set; }
    public EclCharacter? CreateNpc(ushort monster)
    {
        Log($"CreateNpc {monster}");
        return NpcFactory?.Invoke(monster);
    }
    public void DamageReport(EclDamageReport report) => Log($"Damage {report.Target.Name} {report.Damage}{(report.Dies ? " dies" : "")}");
    public void DamageEnd(bool partyKilled) => Log(partyKilled ? "DamageEnd killed" : "DamageEnd");
}
