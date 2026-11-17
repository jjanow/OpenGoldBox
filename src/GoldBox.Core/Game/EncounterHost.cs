using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Game-side host for the encounter opcodes, read from the program (docs/re/ecl-format.md "Encounters"):
/// SETUPMON (0C), APPROACH (0D), LOADMON (0B), CLRMON (1C), ENCMENU (29), COMBAT (24) entry, PARLEY (2C),
/// TREASURE (27), PICTURE (0E) and SPRITEOFF (31). It wraps another host (<see cref="Inner"/>), which still answers
/// everything else (text, other menus, files, random numbers). Player-facing parts go to <see cref="IEncounterUi"/>,
/// the fight, shops and temples to <see cref="ICombatRunner"/>. Headless: no graphics, no game text.
/// </summary>
public sealed class EncounterHost : IEclHost
{
    /// <summary>VM cells the encounter opcodes use (Lee 12.4.2 names; uses P).</summary>
    public const ushort RequestedDistance = 0x6DC0, Distance = 0x6DC1, SearchMode = 0x6DCA, HeadPicture = 0x6DE1,
        TempleFlag = 0x6DE2, ShopFlag = 0x6E6C, OutdoorsFlag = 0x49E6;

    public EncounterHost(EclMemory memory, IEclHost inner, IEncounterUi ui, ICombatRunner combat, IEncounterData? data = null)
    {
        Memory = memory;
        Inner = inner;
        Ui = ui;
        CombatRunner = combat;
        Data = data;
    }

    public EclMemory Memory { get; }
    public IEclHost Inner { get; }
    public IEncounterUi Ui { get; }
    public ICombatRunner CombatRunner { get; }
    public IEncounterData? Data { get; set; }
    public EncounterState State { get; } = new();

    /// <summary>Walls of the current map, for the view distance. Null = no walls.</summary>
    public IWallMap? Map { get; set; }
    /// <summary>The current ECL block (DS:84DC), for the wall accessor's edge rule. Default: none (-1).</summary>
    public Func<int> CurrentBlock { get; set; } = () => -1;

    /// <summary>Problems met (missing data files, …), for tools and tests.</summary>
    public List<string> Diagnostics { get; } = [];

    private bool Outdoors => Memory.Read(OutdoorsFlag) == 0;

    /// <summary>
    /// The view routine 0x7955 for the party's square and facing. Outdoors it also stores $6DC1 = 2 itself, as the
    /// program does.
    /// </summary>
    public int ViewDistance()
    {
        var p = Memory.Position;
        if (Outdoors)
        {
            Memory.Write(Distance, 2);
            return 2;
        }
        return EncounterRules.ViewDistance(Map, false, p.X, p.Y, p.Facing, CurrentBlock());
    }

    /// <summary>
    /// Shared start of SETUPMON and ENCMENU (0x10FA / 0x2C72): sprite, $6DC0 = requested distance (16 bits),
    /// picture, $6DC1 = min(requested, view), then draw.
    /// </summary>
    private void StartEncounter(ushort sprite, ushort distance, ushort picture)
    {
        State.Sprite = (byte)sprite;
        Memory.Write(RequestedDistance, distance);
        State.Picture = (byte)picture;
        int view = ViewDistance();
        Memory.Write(Distance, (ushort)Math.Min(distance, view));
        Draw();
    }

    private void Draw()
    {
        State.SpriteShown = true;
        Ui.ShowEncounter(State.Sprite, State.Picture, Memory.Read(Distance));
    }

    private void CloseIn()
    {
        Memory.Write(Distance, (ushort)(Memory.Read(Distance) - 1));
        Draw();
    }

    // ---- Encounter opcodes (program read) ----------------------------------------------------------------------

    public void SetupMonster(ushort monster, ushort distance, ushort picture) => StartEncounter(monster, distance, picture);

    public void Approach()
    {
        if (Memory.Read(Distance) > 0)
            CloseIn();
    }

    public void ClearMonsters() => State.Clear();

    public void LoadMonster(ushort monster, ushort count, ushort picture)
    {
        if (State.MonsterCount >= EncounterState.MaxMonsters)
            return;
        var first = LoadMonsterRecord((byte)monster);
        int n = (byte)count == 0 ? 1 : (byte)count;
        Ui.LoadCombatIcon((byte)picture, State.IconSlot);
        Append(first);
        for (int k = 2; k <= n && State.MonsterCount < EncounterState.MaxMonsters; k++)
        {
            var copy = new EclCharacter(first.Record);
            copy.Items.AddRange(first.Items.Select(i => (byte[])i.Clone()));
            copy.Effects.AddRange(first.Effects.Select(e => (byte[])e.Clone()));
            Append(copy);
        }
        State.IconSlot++;
        State.MonstersLoaded = true;
    }

    private void Append(EclCharacter c)
    {
        c[0xBF] = (byte)State.IconSlot;
        State.Monsters.Add(c);
        Memory.Party?.Members.Add(c); // the program appends to the party list itself (DS:5D96)
        State.MonsterCount++;
    }

    /// <summary>The monster loader (0x1EEAA): the record, icon size by race, five 1d6 icon colours, items and effects.</summary>
    private EclCharacter LoadMonsterRecord(byte id)
    {
        var data = Data?.Monster(Memory.Disk, id);
        if (data is null)
            Diagnostics.Add($"LOADMON: no monster {id} for disk {Memory.Disk} (the game quits to DOS)");
        var c = new EclCharacter(data?.Record);
        byte race = c[0x2E];
        if (race is 1 or 3 or 5)
            c[0xC0] = 1;
        else if (race is 2 or 4 or 6 or 7)
            c[0xC0] = 2;
        for (int k = 1; k <= 3; k++)
            c[0xC0 + k] = EncounterRules.IconColour(Random(6) + 1);
        c[0xC4] = 0xC4;
        for (int k = 5; k <= 6; k++)
            c[0xC0 + k] = EncounterRules.IconColour(Random(6) + 1);
        if (data is not null)
        {
            c.Effects.AddRange(data.Effects.Select(e => (byte[])e.Clone()));
            c.Items.AddRange(data.Items.Select(i => (byte[])i.Clone()));
        }
        return c;
    }

    public ushort? EncounterMenu(EclEncounterMenu menu)
    {
        State.InEncounterMenu = true;
        try
        {
            var list = (IReadOnlyList<EclCharacter>?)Memory.Party?.Members ?? [];
            var (fastest, slowest) = EncounterRules.PartySpeed(list);
            StartEncounter(menu.Monster, menu.Distance, menu.Picture);
            byte move1 = (byte)menu.Move1, move2 = (byte)menu.Move2;
            while (true)
            {
                int d = Memory.Read(Distance);
                var kind = EncounterRules.MenuKind(d, Outdoors);
                int index = Ui.ChooseEncounterAction(kind, EncounterRules.PickText(menu.DistanceTexts, d), d);
                var choice = EncounterRules.Choice(index, kind);
                int reaction = (byte)menu.Reactions[(int)choice];
                var step = EncounterRules.Resolve(reaction, choice, d, fastest, slowest, move1, move2);
                if (step.Approach)
                    CloseIn();
                if (step.Message is { } m)
                    Ui.ShowEncounterMessage(m);
                if (step.Result is { } r)
                    return (ushort)r;
                if (!step.Repeat)
                    return null;
            }
        }
        finally
        {
            State.InEncounterMenu = false;
        }
    }

    public ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive)
    {
        ushort[] v = [haughty, sly, nice, meek, abusive];
        int c = Ui.ChooseParleyAttitude();
        return (byte)v[Math.Clamp(c, 0, 4)];
    }

    public void Combat()
    {
        if (!State.MonstersLoaded && !State.Duel)
        {
            if (Memory.Read(ShopFlag) == 1)
            {
                Memory.Write(ShopFlag, 0);
                CombatRunner.Shop(State);
            }
            else if (Memory.Read(TempleFlag) == 1)
            {
                Memory.Write(TempleFlag, 0);
                CombatRunner.Temple(State);
            }
            else
            {
                CombatRunner.AfterCombat(State);
            }
        }
        else
        {
            int view = ViewDistance();
            if (view < Memory.Read(Distance))
                Memory.Write(Distance, (ushort)view);
            CombatRunner.Fight(State);
            CombatRunner.AfterCombat(State);
            State.Duel = false;
        }
        Memory.Write(SearchMode, (ushort)(Memory.Read(SearchMode) & 1));
        State.SpriteShown = false;
        State.PictureShown = false;
    }

    public void Treasure(IReadOnlyList<ushort> values)
    {
        for (int i = 0; i < 7; i++)
            State.Money[i] = values[i]; // set, not added (0x270B)
        byte id = (byte)values[7];
        if (id < 128)
        {
            var items = Data?.ItemList(Memory.Disk, id);
            if (items is null)
            {
                Diagnostics.Add($"TREASURE: no item list {id} for disk {Memory.Disk} (the game quits to DOS)");
                return;
            }
            foreach (var it in items)
                State.TreasureItems.Insert(0, (byte[])it.Clone()); // each record becomes the new head
        }
        else if (id < 255)
        {
            byte type = 0; // [?] the program's variable is unset before the first item
            for (int k = 1; k <= id - 128; k++)
            {
                type = EncounterRules.RandomItemType(n => Random((ushort)n), type);
                State.TreasureItems.Insert(0, CombatRunner.MakeRandomItem(type));
            }
            // [?] Then START 00BA:0442 runs on every treasure item (not read).
        }
    }

    public void Picture(ushort picture)
    {
        byte v = (byte)picture;
        if (v != 255)
        {
            State.PictureShown = true;
            ushort head = Memory.Read(HeadPicture);
            Ui.ShowPicture(v, head == 255 ? null : (byte)head);
            return;
        }
        if (State.PictureShown || State.SpriteShown)
            Ui.RestoreView();
        State.PictureShown = false;
        State.SpriteShown = false;
    }

    public void SpriteOff()
    {
        if (!State.SpriteShown)
            return;
        Ui.RestoreView();
        State.SpriteShown = false;
        State.PictureShown = false;
    }

    // ---- Everything else goes to the inner host ----------------------------------------------------------------

    public void Print(string text, bool clearFirst) => Inner.Print(text, clearFirst);
    public void PrintReturn() => Inner.PrintReturn();
    public void ClearBox() => Inner.ClearBox();
    public void Delay() => Inner.Delay();
    public ushort VerticalMenu(string prompt, IReadOnlyList<string> choices) => Inner.VerticalMenu(prompt, choices);
    public ushort HorizontalMenu(IReadOnlyList<string> choices) => Inner.HorizontalMenu(choices);
    public int InputNumber() => Inner.InputNumber();
    public string InputString() => Inner.InputString();
    public void Who(string prompt) => Inner.Who(prompt);
    public void LoadCharacter(ushort index) => Inner.LoadCharacter(index);
    public ushort PartyStrength() => Inner.PartyStrength();
    public EclPartyCheck CheckParty(ushort field, ushort effect) => Inner.CheckParty(field, effect);
    public void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance) => Inner.Rob(wholeParty, moneyPercent, itemChance);
    public void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra) => Inner.Damage(attack, dice, sides, bonus, extra);
    public bool FindItem(ushort item) => Inner.FindItem(item);
    public void AddNpc(ushort monster, ushort morale) => Inner.AddNpc(monster, morale);
    public (ushort Slot, ushort Character)? Spell(ushort spell) => Inner.Spell(spell);
    public void LoadMap(int map) => Inner.LoadMap(map);
    public void LoadOutdoorPicture() => Inner.LoadOutdoorPicture();
    public void LoadWallSet(int slot, int? set) => Inner.LoadWallSet(slot, set);
    public void Clock(ushort minutes) => Inner.Clock(minutes);
    public void Call(ushort routine) => Inner.Call(routine);
    public void Program(ushort program) => Inner.Program(program);
    public void Protect(ushort address) => Inner.Protect(address);
    public ushort Random(ushort range) => Inner.Random(range);
    public void ScriptChanging(ushort script) => Inner.ScriptChanging(script);
}
