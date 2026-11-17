using System.Globalization;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>
/// The script text window: PRINT appends, PRINTCLR and CLEARBOX clear, PRINTRET starts a new line. [?] The program's
/// text routine (wrapping, paging, colours) is not read: we wrap at word boundaries to the window width and keep the
/// last lines.
/// </summary>
public sealed class TextWindow(int columns, int rows)
{
    private readonly List<string> _lines = [""];

    public int Columns { get; } = columns;
    public int Rows { get; } = rows;

    public bool IsEmpty => _lines.Count == 1 && _lines[0].Length == 0;

    /// <summary>The lines to show (the last <see cref="Rows"/>).</summary>
    public IReadOnlyList<string> Visible => _lines.Count <= Rows ? _lines : _lines.GetRange(_lines.Count - Rows, Rows);

    public void Clear()
    {
        _lines.Clear();
        _lines.Add("");
    }

    public void NewLine() => _lines.Add("");

    public void Append(string text)
    {
        var parts = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                NewLine();
            if (parts[i].Length > 0)
                AppendWords(parts[i].ToUpperInvariant());
        }
    }

    private void AppendWords(string s)
    {
        foreach (var word in s.Split(' '))
        {
            var cur = _lines[^1];
            string add = cur.Length == 0 ? word : " " + word;
            if (cur.Length + add.Length > Columns && cur.Length > 0)
            {
                _lines.Add(word.Length > Columns ? word[..Columns] : word);
                continue;
            }
            _lines[^1] = cur + add;
        }
    }
}

/// <summary>
/// The Client's side of a running game, on the logic thread: the innermost ECL host (text, menus, input lines and the
/// opcodes whose effect is not read), the encounter screens, the stand-in fight, the door menu. Every question goes
/// to the UI thread through <see cref="ScriptRunner.Ask"/>; text and pictures change state the UI draws while the
/// logic is parked.
/// </summary>
public sealed class ClientPlayer(ScriptRunner runner, Func<EclMemory> memory) : IEclHost, IEncounterUi, ICombatRunner, IAreaUi
{
    public TextWindow Text { get; } = new(38, 6);

    /// <summary>What the 3D view shows instead of the walls while an encounter or picture is up (null = the view).</summary>
    public string[]? ViewOverlay { get; private set; }

    /// <summary>A one-line message (the area loop's own, or a stand-in notice) shown on the first text row.</summary>
    public string? Notice { get; set; }

    /// <summary>Host calls that have no player-facing effect yet, for the log.</summary>
    public List<string> Log { get; } = [];

    private int Choose(string? prompt, IReadOnlyList<string> items, bool vertical = false, int? cancel = null) =>
        runner.Ask(new ChoiceRequest(prompt, items, vertical, cancel)) is int i ? i : 0;

    /// <summary>A notice the player must acknowledge (stand-ins for screens not built yet).</summary>
    private void Acknowledge(string text)
    {
        Text.Clear();
        Text.Append(text);
        Choose(null, ["CONTINUE"], cancel: 0);
    }

    // ---- Text ----------------------------------------------------------------------------------------------------
    public void Print(string text, bool clearFirst)
    {
        if (clearFirst)
            Text.Clear();
        Text.Append(text);
    }
    public void PrintReturn() => Text.NewLine();
    public void ClearBox() => Text.Clear();

    /// <summary>DELAY: game speed × 225 ms. [?] The speed is read from $49FC (saved from the options), not DS:49E2.</summary>
    public void Delay()
    {
        int speed = memory().Read(0x49FC) & 0xFF;
        if (speed is > 0 and <= 9)
            runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(225 * speed)));
    }

    // ---- Menus and input -----------------------------------------------------------------------------------------
    public ushort VerticalMenu(string prompt, IReadOnlyList<string> choices) => (ushort)Choose(prompt, Upper(choices), vertical: true);
    public ushort HorizontalMenu(IReadOnlyList<string> choices) => (ushort)Choose(null, Upper(choices));

    private static string[] Upper(IReadOnlyList<string> s) => [.. s.Select(c => c.ToUpperInvariant())];

    public int InputNumber()
    {
        var s = runner.Ask(new TextRequest("NUMBER:", true, 6)) as string ?? "";
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    public string InputString() => runner.Ask(new TextRequest("", false, 40)) as string ?? "";

    /// <summary>WHO: [?] Lee only (the player picks a character, who becomes current).</summary>
    public void Who(string prompt)
    {
        var party = memory().Party;
        if (party is null || party.Members.Count == 0)
            return;
        int n = party.Members.Count;
        var names = party.Members.Select(m => m.Name.ToUpperInvariant()).ToList();
        int i = Choose(prompt.ToUpperInvariant(), names, vertical: true);
        party.CurrentIndex = Math.Clamp(i, 0, n - 1);
    }

    public ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive) => 0; // EncounterHost answers
    public ushort? EncounterMenu(EclEncounterMenu menu) => null;                                       // EncounterHost answers

    // ---- Opcodes the encounter host or the VM answers; the rest are Lee-only stand-ins ----------------------------
    public void LoadCharacter(ushort index) => Log.Add($"LOADCHAR {index} [?]");
    public void LoadMonster(ushort monster, ushort count, ushort picture) { }
    public void SetupMonster(ushort monster, ushort distance, ushort picture) { }
    public void Approach() { }
    public void ClearMonsters() { }
    public ushort PartyStrength() => 0;
    public EclPartyCheck CheckParty(ushort field, ushort effect)
    {
        Log.Add($"CHKPARTY {field} {effect} [?] answered 0");
        return default;
    }
    public void Combat() { }
    public void Treasure(IReadOnlyList<ushort> values) { }
    public void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance) => Log.Add($"ROB {wholeParty} {moneyPercent} {itemChance} [?] not applied");
    public void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra) =>
        Log.Add($"DAMAGE {attack} {dice}d{sides}+{bonus} {extra} [?] not applied");
    public void SpriteOff() { }
    public bool FindItem(ushort item) => false;
    public void AddNpc(ushort monster, ushort morale) => Log.Add($"ADDNPC {monster} {morale} [?] not applied");
    public (ushort Slot, ushort Character)? Spell(ushort spell) => null;
    public void Picture(ushort picture) { }
    public void LoadMap(int map) { }
    public void LoadOutdoorPicture() => Log.Add("LOADFILES outdoors (wilderness picture)");
    public void LoadWallSet(int slot, int? set) { }
    public void Clock(ushort minutes) { }
    public void Call(ushort routine) => Log.Add($"CALL {routine:X4}");
    public void Protect(ushort address) { } // the code wheel: imported modules skip it (PLAN 6)
    public ushort Random(ushort range) => 0; // AreaHost draws from the game's generator
    public void ScriptChanging(ushort script) => Log.Add($"NEWECL {script}");

    /// <summary>PROGRAM: 0 training hall, 8 end game, 9 camp (ids P). None of these screens is built yet.</summary>
    public void Program(ushort program)
    {
        string what = program switch { 0 => "THE TRAINING HALL", 8 => "THE END GAME", 9 => "THE CAMP MENU", _ => "" };
        if (what.Length > 0)
            Acknowledge(what + " IS NOT IN THIS BUILD YET.");
    }

    // ---- IEncounterUi ---------------------------------------------------------------------------------------------
    public void ShowEncounter(byte sprite, byte picture, int distance) =>
        ViewOverlay = distance == 0
            ? ["CLOSE-UP", $"PIC {picture}", "", "(ART NOT", "IN YET)"]
            : ["MONSTER", $"SPRITE {sprite}", $"DISTANCE {distance}", "", "(ART NOT", "IN YET)"];

    public void ShowPicture(byte picture, byte? head) =>
        ViewOverlay = head is { } h ? ["PICTURE", $"HEAD {h}", $"BODY {picture}"] : ["PICTURE", $"{picture}"];

    public void RestoreView() => ViewOverlay = null;

    public int ChooseEncounterAction(EncounterMenuKind kind, string? text, int distance)
    {
        if (text is not null)
        {
            Text.Clear();
            Text.Append(text);
        }
        string[] items = kind == EncounterMenuKind.CombatWaitFleeAdvance
            ? ["COMBAT", "WAIT", "FLEE", "ADVANCE"]
            : ["COMBAT", "WAIT", "FLEE", "PARLEY"];
        return Choose(null, items);
    }

    public void ShowEncounterMessage(EncounterMessage message)
    {
        Text.Clear();
        // Our wording; the program's two fixed lines (GAME.OVR unit offsets 0x1ED4 / 0x1EE5) are not reproduced.
        Text.Append(message == EncounterMessage.BothSidesWait ? "BOTH SIDES WAIT." : "THE MONSTERS FLEE.");
        runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(900)));
    }

    public int ChooseParleyAttitude() => Choose(null, ["HAUGHTY", "SLY", "NICE", "MEEK", "ABUSIVE"]);

    public void LoadCombatIcon(byte picture, int slot) { }

    // ---- ICombatRunner: stand-ins until the fight is built ---------------------------------------------------------
    /// <summary>
    /// The fight is not built: a notice, then the creatures keep their status as loaded, so the after-combat step that
    /// follows treats the fight as won (monsters removed, experience shared). A stand-in, not the game's outcome.
    /// </summary>
    public void Fight(EncounterState state) =>
        Acknowledge($"A FIGHT WITH {state.Monsters.Count} CREATURES. THE FIGHT IS NOT IN THIS BUILD YET: IT COUNTS AS WON.");

    public void Loot(EncounterState state, AfterCombatOutcome outcome)
    {
        long coins = state.Money.Sum(m => (long)m);
        Acknowledge($"AFTER THE FIGHT: {outcome.ExperienceShare} XP EACH. TREASURE: {coins} COINS/GEMS, " +
                    $"{state.TreasureItems.Count} ITEMS. THE LOOT MENU IS NOT IN THIS BUILD YET.");
    }

    public void PartyDestroyed(EncounterState state) => Acknowledge("THE PARTY IS DESTROYED.");
    public void Shop(EncounterState state) => Acknowledge("A SHOP. SHOPS ARE NOT IN THIS BUILD YET.");
    public void Temple(EncounterState state) => Acknowledge("A TEMPLE. TEMPLES ARE NOT IN THIS BUILD YET.");

    /// <summary>[?] The item maker (GAME.OVR 0x25C99) is not read: a blank item record of the type, as the headless runner.</summary>
    public byte[] MakeRandomItem(byte type)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2E] = type;
        return r;
    }

    // ---- IAreaUi ---------------------------------------------------------------------------------------------------
    public DoorAction? ChooseDoorAction(DoorBarrier barrier, IReadOnlyList<DoorAction> choices)
    {
        var items = choices.Select(c => c.ToString().ToUpperInvariant()).Append("EXIT").ToList();
        // Our wording for the menu's title; the program's (GAME.OVR cs:0xD64) is not reproduced.
        int i = Choose(barrier == DoorBarrier.Barred ? "A BARRED DOOR." : "A LOCKED DOOR.", items, cancel: items.Count - 1);
        return i >= 0 && i < choices.Count ? choices[i] : null;
    }

    public void Bump() { }

    public void Message(AreaMessage message)
    {
        if (message == AreaMessage.NoAreaMapHere)
            Notice = "NO AREA MAP HERE.";
    }

    /// <summary>[?] The camp menu is not built: vector 2 has run; a notice; no rest, so no interruption.</summary>
    public bool Camp()
    {
        Camping = true;
        try
        {
            Acknowledge("CAMP: REST AND MEMORISE ARE NOT IN THIS BUILD YET.");
        }
        finally
        {
            Camping = false;
        }
        return false;
    }

    /// <summary>The camp menu is up (the position line says CAMPING, DS:49F3 = 2).</summary>
    public bool Camping { get; private set; }
}
