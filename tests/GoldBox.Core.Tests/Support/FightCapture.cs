using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests.Support;

/// <summary>
/// One DOSBox-X fight state (<c>dosbox_state.py fight</c>: <c>fight.json</c>, <c>arena.bin</c>, plus the VM cells a
/// fight reads in <c>vm.json</c>) loaded into a fight wired as <see cref="FightKit.Create"/> wires it (combat map,
/// moves, effect engine, spells, AI, menu), but on the captured arena and squares instead of a new placement. The
/// creature list, records, items, effects, combat records, round counter DS:6DE3, enemy HP percentage DS:6E2F, the
/// current creature DS:5D92 and RandSeed come from the capture. Used by the s13 capture tests (fixtures
/// <c>fight_casts</c>, <c>fight_mirror</c>, <c>fight_undead</c>; README in the fixtures folder).
/// </summary>
public sealed class FightCapture
{
    public required Fight Fight { get; init; }
    public required TurboPascalRandom Rng { get; init; }
    public required CombatMap Map { get; init; }
    public required StopAtMenu Controller { get; init; }
    public required MenuController Menu { get; init; }
    public EclCharacter? Current { get; init; }
    public List<EclCharacter> List => Fight.List;

    /// <summary>The private fixtures folder <paramref name="name"/>, or null when absent.</summary>
    public static string? Folder(string name) =>
        PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d && Directory.Exists(Path.Combine(d, name))
            ? Path.Combine(d, name) : null;

    public static JsonDocument Json(string dir) => JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "fight.json")));

    public static uint Seed(string dir)
    {
        using var j = Json(dir);
        return j.RootElement.GetProperty("vars").GetProperty("randseed").GetUInt32();
    }

    public static byte[] Hex(JsonElement e) => Convert.FromHexString(e.GetString()!);

    private static FightKit? _kit;

    /// <summary>The program's tables (one per test run).</summary>
    public static FightKit Kit => _kit ??= PorFight.Kit(PorData.Open()) ?? throw new InvalidOperationException("no fight kit");

    /// <summary>Loads the state in <paramref name="dir"/>; the menu's questions go to <paramref name="chooser"/>.</summary>
    public static FightCapture Load(string dir, IFightMenuChooser chooser)
    {
        var kit = Kit;
        using var doc = Json(dir);
        var root = doc.RootElement;
        var ds = File.ReadAllBytes(Path.Combine(dir, "ds.bin"));
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var rng = new TurboPascalRandom(root.GetProperty("vars").GetProperty("randseed").GetUInt32());
        var trace = new List<string>();
        Fight? fight = null;
        var hooks = new DrawHooks();
        ushort Random(ushort range)
        {
            hooks.BeforeDraw?.Invoke(rng.Draws);
            ushort v = rng.Next(range);
            if (TraceDraws)
                trace.Add($"{rng.Draws} d{range}={v + 1} cur {(fight?.State.Current is { } c ? fight.List.IndexOf(c) : -1)} " +
                          string.Join("<", new System.Diagnostics.StackTrace().GetFrames().Skip(1)
                              .Select(f => f.GetMethod()?.Name).Where(n => n is not null && !n.StartsWith('<') && n != "Dice").Take(5)));
            return v;
        }
        var map = new CombatMap(kit.Tables);
        Hex(root.GetProperty("arena")).CopyTo(map.Arena.Bytes, 0);
        var effects = new EffectHandlers(Random, kit.Names) { ExperienceTable = kit.Experience };
        FightKit.WireLevels(effects, kit.LevelTables);
        var ai = new MonsterAi(kit.AiSteps, null, kit.TurnUndead);
        var menu = new MenuController(chooser, ai, chooser is IItemScreenChooser ic ? new FightItemScreens(ic) : null, kit.TurnUndead);
        var controller = new StopAtMenu(menu);
        var encounter = new EncounterState();
        fight = new Fight(memory, encounter, kit.Items, Random, controller, map, effects) { Moves = new FightMoves(map) };
        map.State = fight.State;
        effects.LeaveMap = map.Remove;
        fight.Recalculate = c => StatRecalc.Recalculate(c, kit.Items);
        effects.Recalculate = fight.Recalculate;
        fight.Spells = new FightSpells(kit.Spells, effects, Random);
        effects.Attach(fight);
        FightKit.WireClock(fight, effects, new CampState());

        long cur = root.GetProperty("pointers").GetProperty("current_5d92").GetProperty("linear").GetInt64();
        EclCharacter? current = null;
        foreach (var e in root.GetProperty("creatures").EnumerateArray())
        {
            var c = new EclCharacter(Hex(e.GetProperty("record")));
            foreach (var it in e.GetProperty("items").EnumerateArray())
                c.Items.Add(Hex(it.GetProperty("record")));
            foreach (var ef in e.GetProperty("effects").EnumerateArray())
                c.Effects.Add(Hex(ef.GetProperty("record")));
            memory.Party.Members.Add(c);
            Hex(e.GetProperty("combat")).CopyTo(fight.Record(c).Bytes, 0);
            if (e.GetProperty("index").GetInt32() >= 6)
            {
                encounter.NotParty.Add(c);
                encounter.Monsters.Add(c);
            }
            if (e.GetProperty("linear").GetInt64() == cur)
                current = c;
        }
        var list = memory.Party.Members;
        // +0x0A: the creature attacked last, a far pointer (offset, segment) to its record.
        var linear = root.GetProperty("creatures").EnumerateArray().Select(e => e.GetProperty("linear").GetInt64()).ToList();
        foreach (var c in list)
        {
            var b = fight.Record(c).Bytes;
            long p = b[0x0C] * 16L + b[0x0D] * 4096L + b[0x0A] + b[0x0B] * 256L;
            if (p != 0 && linear.IndexOf(p) is >= 0 and var k)
                fight.Record(c).Other = list[k];
        }
        int slots = 0;
        foreach (var l in root.GetProperty("locations_lee").EnumerateArray())
        {
            if (l.GetProperty("list_index").ValueKind != JsonValueKind.Number)
                continue;
            int k = l.GetProperty("slot").GetInt32();
            map.SetSlotCreature(k, list[l.GetProperty("list_index").GetInt32()]);
            map.SetSlotPosition(k, l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32());
            map.SetSlotSize(k, l.GetProperty("size").GetInt32());
            slots = Math.Max(slots, k);
        }
        map.SlotCount = slots + 1;
        map.Rebuild();
        var vm = Path.Combine(dir, "vm.json");
        if (File.Exists(vm))
            foreach (var p in JsonDocument.Parse(File.ReadAllText(vm)).RootElement.EnumerateObject())
                memory.Write(Convert.ToUInt16(p.Name, 16), (ushort)p.Value.GetInt32());
        fight.State.RoundCounter = ds[0x6DE3];
        fight.State.HostileHpPercent = ds[0x6E2F];
        FightRules.CountTeams(list, fight.State);
        fight.State.Current = current;
        return new FightCapture { Fight = fight, Rng = rng, Map = map, Controller = controller, Menu = menu, Current = current, Trace = trace, Hooks = hooks };
    }

    /// <summary>Called before every draw with the number of draws made since the load.</summary>
    public sealed class DrawHooks
    {
        public Action<long>? BeforeDraw { get; set; }
    }

    public DrawHooks Hooks { get; init; } = new();

    /// <summary>Set GOLDBOX_FIGHT_TRACE to record every draw (number, range, value, current creature, callers).</summary>
    public static bool TraceDraws { get; set; } = Environment.GetEnvironmentVariable("GOLDBOX_FIGHT_TRACE") is not null;

    /// <summary>The draws made since the load (only with <see cref="TraceDraws"/>).</summary>
    public List<string> Trace { get; init; } = [];

    /// <summary>The menu of the current creature (the capture waits at it).</summary>
    public void RunMenu(EclCharacter? c = null) => Menu.Act(Fight, c ?? Current!, false);

    /// <summary>What <see cref="AfterFight"/> found, or null before it ran.</summary>
    public AfterCombatOutcome? After { get; private set; }

    /// <summary>
    /// The after-combat step (GAME.OVR 0x6AA5) once the fight is over, as <see cref="EncounterHost"/> runs it: results,
    /// share and award (0x5A47), removal (0x672A), then <see cref="AfterCombat.Finish"/>. The loot / defeat screens and
    /// the NPC treasure share (0x6866) are left out (no draws). The captures' vm.json does not hold $6E3E, so the party
    /// size is taken as the number of creatures whose combat record +0x13 is not 1 (the fight start set +0x13 from
    /// $6E3E); for the same reason $6DC7 and $6DC8 are not compared. The current creature becomes the list's head.
    /// </summary>
    public AfterCombatOutcome AfterFight()
    {
        var memory = Fight.Memory;
        memory.Write(AfterCombat.PartySize, (ushort)List.Count(c => Fight.Record(c)[CombatRecord.NotParty] != 1));
        var step = new AfterCombat(memory, Fight.Encounter, Fight.Random, Fight.Diagnostics);
        var outcome = step.Run();
        step.Finish(outcome);
        Fight.State.Current = memory.Party?.Current;
        return (After = outcome);
    }

    /// <summary>
    /// The fight goes on as <see cref="Fight.Round"/> does (picks, turns, end of round, next round's start) until a
    /// party member not on Quick reaches its menu (returned; its turn-start bookkeeping done, as in a capture taken at
    /// the menu), or the fight ends (null).
    /// </summary>
    public EclCharacter? ContinueToNextMenu(int maxRounds = 20)
    {
        Controller.Armed = true;
        try
        {
            for (int r = 0; r < maxRounds; r++)
            {
                while (Fight.NextCreature() is { } c)
                    Fight.Turn(c);
                Fight.EndOfRound(out bool done);
                if (done)
                    return null;
                FightRules.CountTeams(List, Fight.State);
                foreach (var c in List)
                    FightRules.BeginRound(c, Fight.Record(c), Fight.State, Fight.Items, Fight.Memory, Fight.Random, Fight.Effects);
                Fight.Memory.Write(FightRules.SurpriseCell, 0);
            }
            throw new InvalidOperationException("no menu within " + maxRounds + " rounds");
        }
        catch (StopAtMenu.Stop s)
        {
            return s.Creature;
        }
        finally
        {
            Controller.Armed = false;
        }
    }

    /// <summary>
    /// The compared fields: RandSeed, round, current creature, and per creature its square and icon size, HP, status,
    /// in play, Quick (0x10F), memorised spells (0x17–0x2B), effect records (first 5 bytes), the 22-byte combat record
    /// (without the far pointer +0x0A–0x0D) and the creature that pointer names.
    /// </summary>
    public Dictionary<string, string> Fields()
    {
        var f = new Dictionary<string, string>
        {
            ["randseed"] = Rng.Seed.ToString("x8"),
            ["round"] = Fight.State.RoundCounter.ToString(),
            ["current"] = (Fight.State.Current is { } oc ? List.IndexOf(oc) : -1).ToString(),
        };
        if (After is { } a)
            AfterFields(f, a.PartyDefeated, a.PartyFled, a.Won, a.HostileFound, a.ExperienceShare, Fight.Encounter.Money,
                List.Count);
        for (int i = 0; i < List.Count; i++)
        {
            var c = List[i];
            f[$"#{i} at"] = Map.IconSize(c) > 0 ? $"{Map.Position(c)} size {Map.IconSize(c)}" : "off";
            Creature(f, i, c.Record, c.Effects, Fight.Record(c).Bytes);
            f[$"#{i} other"] = (Fight.Record(c).Other is { } o ? List.IndexOf(o) : -1).ToString();
        }
        return f;
    }

    /// <summary>The same fields of a captured state.</summary>
    public static Dictionary<string, string> Fields(string dir)
    {
        using var doc = Json(dir);
        var cap = doc.RootElement;
        var f = new Dictionary<string, string>
        {
            ["randseed"] = cap.GetProperty("vars").GetProperty("randseed").GetUInt32().ToString("x8"),
        };
        var cc = cap.GetProperty("creatures").EnumerateArray().ToList();
        if (cc.Count == 0)
        {
            // After the fight: the after-combat step's DS cells (0x5A47) and the pool; the list is empty.
            var ds = File.ReadAllBytes(Path.Combine(dir, "ds.bin"));
            var pool = cap.GetProperty("pool_67f4").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
            AfterFields(f, ds[0x49FF] != 0, ds[0x443B] != 0, ds[0x84DA] != 0, ds[0x443A] != 0,
                BitConverter.ToInt32(ds, 0x84D6), pool, 0);
            return f;
        }
        f["round"] = cap.GetProperty("ds_bytes").GetProperty("6DE3").GetByte().ToString();
        long cur = cap.GetProperty("pointers").GetProperty("current_5d92").GetProperty("linear").GetInt64();
        f["current"] = cc.FindIndex(c => c.GetProperty("linear").GetInt64() == cur).ToString();
        for (int i = 0; i < cc.Count; i++)
            f[$"#{i} at"] = "off";
        foreach (var l in cap.GetProperty("locations_lee").EnumerateArray())
        {
            if (l.GetProperty("list_index").ValueKind != JsonValueKind.Number)
                continue;
            int size = l.GetProperty("size").GetInt32();
            if (size > 0)
                f[$"#{l.GetProperty("list_index").GetInt32()} at"] = $"{(l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32())} size {size}";
        }
        var linear = cc.Select(c => c.GetProperty("linear").GetInt64()).ToList();
        for (int i = 0; i < cc.Count; i++)
        {
            var comb = Hex(cc[i].GetProperty("combat"));
            Creature(f, i, Hex(cc[i].GetProperty("record")),
                cc[i].GetProperty("effects").EnumerateArray().Select(e => Hex(e.GetProperty("record"))).ToList(), comb);
            long p = comb[0x0C] * 16L + comb[0x0D] * 4096L + comb[0x0A] + comb[0x0B] * 256L;
            f[$"#{i} other"] = (p == 0 ? -1 : linear.IndexOf(p)).ToString();
        }
        return f;
    }

    /// <summary>The after-combat cells: DS:49FF, DS:443B, DS:84DA, DS:443A, the share DS:84D6, the pool DS:67F4, the list's length.</summary>
    private static void AfterFields(Dictionary<string, string> f, bool defeated, bool fled, bool won, bool hostile, int share,
        IReadOnlyList<uint> pool, int listCount)
    {
        f["after defeated"] = defeated.ToString();
        f["after fled"] = fled.ToString();
        f["after won"] = won.ToString();
        f["after hostile found"] = hostile.ToString();
        f["after share"] = share.ToString();
        f["after pool"] = string.Join(" ", pool);
        f["after list"] = listCount.ToString();
    }

    private static void Creature(Dictionary<string, string> f, int i, byte[] rec, IEnumerable<byte[]> effects, byte[] comb)
    {
        f[$"#{i} hp"] = rec[FightRules.Hp].ToString();
        f[$"#{i} status"] = rec[FightRules.Status].ToString();
        f[$"#{i} inplay"] = rec[FightRules.InPlay].ToString();
        f[$"#{i} quick"] = rec[CombatRules.AutoCombat].ToString();
        f[$"#{i} spells"] = Convert.ToHexString(rec, 0x17, 21);
        f[$"#{i} effects"] = string.Join(" ", effects.Select(e => Convert.ToHexString(e, 0, 5)));
        for (int b = 0; b < comb.Length; b++)
            if (b is < 0x0A or > 0x0D)
                f[$"#{i} +{b:X2}"] = comb[b].ToString();
    }

    /// <summary>"key ours vs captured" for every field that differs.</summary>
    public static List<string> Diff(Dictionary<string, string> ours, Dictionary<string, string> captured) =>
        captured.Where(kv => ours.GetValueOrDefault(kv.Key, "?") != kv.Value)
            .Select(kv => $"{kv.Key} {ours.GetValueOrDefault(kv.Key, "?")} vs {kv.Value}").ToList();

    /// <summary>Draws from one state's RandSeed to another's (null if not within 100000).</summary>
    public static long? Draws(string fromDir, string toDir) => TurboPascalRandom.StepsBetween(Seed(fromDir), Seed(toDir), 100_000);
}

/// <summary>The menu controller, but while <see cref="Armed"/> a party member's menu (not on Quick) stops the run.</summary>
public sealed class StopAtMenu(MenuController inner) : IFightController
{
    public sealed class Stop(EclCharacter c) : Exception
    {
        public EclCharacter Creature { get; } = c;
    }

    public bool Armed { get; set; }

    public void Act(Fight fight, EclCharacter creature, bool auto)
    {
        if (Armed && !auto && !fight.Encounter.NotParty.Contains(creature))
            throw new Stop(creature);
        inner.Act(fight, creature, auto);
    }

    public bool ContinueBattle() => inner.ContinueBattle();
}

/// <summary>A scripted player: main-menu commands in order, a spell id for Cast, the aim on one creature, the
/// attack-ally question answered yes, Quit in the Done menu; Quick when the script is used up.</summary>
public sealed class ScriptedChooser(params FightCommand[] commands) : IFightMenuChooser, IItemScreenChooser
{
    /// <summary>The item the Use screen uses (once; then Exit).</summary>
    public byte[]? UseItem { get; set; }

    public ViewEntry ChooseView(EclCharacter c, IReadOnlyList<ViewEntry> entries) => ViewEntry.Exit;

    public ItemChoice ChooseItem(EclCharacter c, IReadOnlyList<ItemsEntry> entries, IReadOnlyList<byte[]> items)
    {
        var it = UseItem;
        UseItem = null;
        return it is null ? new ItemChoice(ItemsEntry.Exit, null) : new ItemChoice(ItemsEntry.Use, it);
    }

    private readonly Queue<FightCommand> _commands = new(commands);
    public int? Spell { get; init; }
    public EclCharacter? AimAt { get; set; }
    public List<EclCharacter> AimSeen { get; } = [];
    public List<FightNotice> Notices { get; } = [];

    public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries) =>
        _commands.Count > 0 ? _commands.Dequeue() : FightCommand.Quick;

    public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries) => DoneCommand.Quit;
    public MoveInput ChooseMove(FightMenu menu, int movementLeft) => MoveInput.Escape;

    /// <summary>Cursor keys for Manual; when set, the next aim menu answers Manual (0x155F5) and the cursor follows
    /// these keys (Exit when they run out). Setting it again arms Manual for a later aim.</summary>
    public Queue<ManualInput>? Manual
    {
        get => _manual;
        set
        {
            _manual = value;
            _manualArmed = value is not null;
        }
    }

    private Queue<ManualInput>? _manual;
    private bool _manualArmed;
    public List<ManualView> ManualSeen { get; } = [];

    public ManualInput ChooseManual(FightMenu menu, ManualView view)
    {
        ManualSeen.Add(view);
        return Manual is { Count: > 0 } q ? q.Dequeue() : ManualInput.Exit;
    }

    public AimKey ChooseAim(FightMenu menu, AimView view)
    {
        if (_manualArmed)
        {
            _manualArmed = false;
            return AimKey.Manual;
        }
        AimSeen.Add(view.Candidate);
        if (ReferenceEquals(view.Candidate, AimAt))
            return AimKey.Target;
        return AimSeen.Count > 40 ? AimKey.Exit : AimKey.Next;
    }

    public bool ConfirmFlee(FightMenu menu) => false;
    public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally) => true;
    public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) => Spell;
    public void Notice(FightNotice notice, EclCharacter? who) => Notices.Add(notice);
}
