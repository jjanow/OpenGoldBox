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
        ushort distance = Memory.Read(Distance);
        // 0x7AD9–0x7B77: the close-up at distance 0 in the 3D view (DS:49FA = 1), not inside ENCMENU (DS:84E9) with the
        // monster pictures off (DS:49F6), sets DS:84E0; with a head ($6DE1 ≠ 255) the head + body loader 0x79E4 and
        // 0x7B77 clear DS:84E1. [?] Not modelled: the skip when the close-up is already up with the same head (DS:84C9
        // and DS:84EA = $6DE1, 0x7ABC–0x7AD6).
        if (distance == 0 && Memory.ViewMode == 1 && !(State.InEncounterMenu && !CampSettings.MonsterPictures(Memory)))
        {
            Memory.PictureShown = true;
            if (Memory.Read(HeadPicture) != 255)
                Memory.PictureArmed = false;
        }
        Ui.ShowEncounter(State.Sprite, State.Picture, distance);
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
        Memory.TypesText = true; // DS:4A00 = 1 (0x2C4F): its texts and messages type out
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
            Memory.TypesText = false; // 0x32E0, the one way out (after row 24 is cleared, 0x32DB)
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
                AfterCombatStep();
            }
        }
        else
        {
            int view = ViewDistance();
            if (view < Memory.Read(Distance))
                Memory.Write(Distance, (ushort)view);
            StartFight();
            if (FightBuilder?.Invoke(State) is { } fight)
                CombatRunner.Fight(State, fight);
            else
                CombatRunner.Fight(State);
            AfterCombatStep();
            State.Duel = false;
        }
        Memory.Write(SearchMode, (ushort)(Memory.Read(SearchMode) & 1));
        State.SpriteShown = false;
        Memory.PictureShown = false; // 0x2630
    }

    /// <summary>
    /// Builds the fight after the fight start's VM-cell part (<see cref="FightKit.Create"/>: arena, placement, map,
    /// moves, spells, effects, AI and menu); the host then hands it to <see cref="ICombatRunner.Fight(EncounterState, Fight)"/>.
    /// Null, or a builder that returns null: <see cref="ICombatRunner.Fight(EncounterState)"/> runs the fight itself.
    /// </summary>
    public Func<EncounterState, Fight?>? FightBuilder { get; set; }

    /// <summary>VM cells the fight start writes (GAME.OVR 0xE48C–0xE4AD): $6E33 = 0, morale threshold $6DC6 ≤ 100.</summary>
    public const ushort AttackedAllies = 0x6E33, MoraleThreshold = 0x6DC6;

    /// <summary>
    /// The parts of the fight start (GAME.OVR 0xE402) that later rules read: $6E33 = 0, $6DC6 limited to 100, and per
    /// creature (0xD8B2) the combat-record flag +0x13 for list positions past $6E3E; a friendly creature past the
    /// party whose morale (0x84 AND 0x7F) is 0 or above 102 becomes an NPC with 0x84 = $6DC6 + 0x80. [?] The rest
    /// (stat recalculation, facing, placement, the arena) belongs to the fight.
    /// </summary>
    private void StartFight()
    {
        Memory.Write(AttackedAllies, 0);
        if (Memory.Read(MoraleThreshold) > 100)
            Memory.Write(MoraleThreshold, 100);
        State.LastThrownItem = null; // DS:5D9A = nil (0xE476)
        State.NotParty.Clear();
        var list = Memory.Party?.Members ?? [];
        int partySize = Memory.Read(AfterCombat.PartySize);
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (i + 1 > partySize)
                State.NotParty.Add(c);
            int morale = c[CombatRules.Npc] & 0x7F;
            if (c[CombatRules.Hostile] == 0 && State.NotParty.Contains(c) && (morale == 0 || morale > 102))
                c[CombatRules.Npc] = (byte)(Memory.Read(MoraleThreshold) + 0x80);
        }
    }

    /// <summary>NPCs the last NPC treasure share (GAME.OVR 0x6866) named as taking their share (empty: no message).</summary>
    public List<EclCharacter> NpcsTakingShare { get; private set; } = [];

    /// <summary>Called when the after-combat step leaves DS:49FF set (party destroyed): the VM's run loop stops.</summary>
    public Action? PartyDefeated { get; set; }

    /// <summary>
    /// The VM whose run loop reads DS:49FF (<see cref="EclVm.ExternalStop"/>). The after-combat check (GAME.OVR 0x5A47)
    /// writes DS:49FF on every fight: 1 when no party member is left standing, else 0 (always 0 in a duel); the run loop
    /// 0x3EB7 then stops before the next instruction while it is set. The area loop clears it when it ends (0x4245);
    /// that is the caller's part.
    /// </summary>
    public EclVm? Vm { get; set; }

    /// <summary>The after-combat step (GAME.OVR 0x6AA5): <see cref="AfterCombat"/>, then the loot or defeat screen.</summary>
    private void AfterCombatStep()
    {
        var step = new AfterCombat(Memory, State, Random, Diagnostics);
        var outcome = step.Run();
        if (!outcome.PartyDefeated || State.Duel)
        {
            // 0x6866: the NPCs take their shares of the coins before the summary.
            NpcsTakingShare = FightRules.NpcTreasureShare(Memory.Party?.Members ?? [], State.Money);
            CombatRunner.Loot(State, outcome);
        }
        else
            CombatRunner.PartyDestroyed(State);
        step.Finish(outcome);
        if (Vm is not null)
            Vm.ExternalStop = outcome.PartyDefeated;
        if (outcome.PartyDefeated)
            PartyDefeated?.Invoke();
    }

    // ---- CALL ids the game program implements (handler GAME.OVR 0x3A15) -------------------------------------------

    /// <summary>CALL ids handled here; others go to the inner host.</summary>
    public const ushort CallStepForward = 0xC01E, CallMirrorDuel = 0x8000, CallSoloFight = 0x8001, CallRedraw = 0x2C90,
        CallWallAhead = 0xC018;

    /// <summary>Combat icon (<c>CPIC</c> block) the mirror duel loads for the copy (GAME.OVR 0x8FC3).</summary>
    public const byte MirrorIcon = 11;

    public void Call(ushort routine)
    {
        switch (routine)
        {
            case CallStepForward:
                StepForward();
                break;
            case CallWallAhead:
                // 0x3AFA: in the 3D view (DS:49FA = 1) the wall type ahead DS:6AB0 is read again (0x2ED72).
                if (Memory.ViewMode == 1 && Map is null)
                    Diagnostics.Add("CALL $C018: no map; wall ahead left as it is");
                else if (Memory.ViewMode == 1)
                {
                    var p = Memory.Position;
                    p.WallAhead = (byte)EncounterRules.WallAt(Map, (sbyte)p.X, (sbyte)p.Y, p.Facing, CurrentBlock());
                }
                break;
            case CallMirrorDuel:
                SetUpDuel(true);
                break;
            case CallSoloFight:
                SetUpDuel(false);
                break;
            case CallRedraw:
                // 0x3A44: the event byte is read again from the map. 0x3A54: with DS:4431 set (files loaded since the
                // last NEWECL) and a redraw flag up, the view is redrawn and the flags cleared; of the flags, the
                // picture (DS:84E0) and sprite (DS:84E3) ones take the picture off the view (DOSBox-X s21: the Tyr
                // temple's "then you must leave" moves the party back with $C04B/$C04C and CALL $2C90, and the 3D view
                // replaces the priest). The other flags (84E5 backdrop, 84E6 position, 84E8 sky) redraw what the view
                // shows anyway; every redraw also draws the position line (0x3A83 → START 0x34AB).
                Memory.Position.EventByte = EventByteHere();
                if (Memory.FilesLoadedSinceSwitch
                    && (Memory.PictureShown || State.SpriteShown || Memory.ViewRedrawFlags != ViewRedraw.None))
                {
                    if (Memory.PictureShown || State.SpriteShown)
                        Ui.RestoreView();
                    Memory.PictureShown = false; // 0x3A97 (DS:84E1 is not touched)
                    State.SpriteShown = false;
                    Memory.ViewRedrawFlags = ViewRedraw.None;
                    Explore.Overland.Redrawn(Memory); // 0x3A7E: on the overland the marker too (DS:6DDC + 1)
                    Ui.ViewRedrawn();
                }
                Inner.Call(routine);
                break;
            default:
                Inner.Call(routine);
                break;
        }
    }

    /// <summary>
    /// CALL $C01E (GAME.OVR 0x8EAE): one square forward in the facing direction, wrapping at the map edge (0 ↔ 15);
    /// a facing other than 0/2/4/6 does not move. Then the event byte ($C04F, plane 2 at the new square, accessor
    /// 0x2EE71) and the wall type ahead ($C04E, accessor 0x2ED72) are read again. Used by vector 0 of the town
    /// scripts when the step leaves the map ($6DD5 set), before NEWECL.
    /// </summary>
    private void StepForward()
    {
        var p = Memory.Position;
        int x = (sbyte)p.X, y = (sbyte)p.Y;
        switch (p.Facing)
        {
            case 0: y = y > 0 ? y - 1 : 15; break;
            case 2: x = x < 15 ? x + 1 : 0; break;
            case 4: y = y < 15 ? y + 1 : 0; break;
            case 6: x = x > 0 ? x - 1 : 15; break;
        }
        p.X = (byte)x;
        p.Y = (byte)y;
        p.EventByte = EventAt(x, y);
        p.WallAhead = (byte)EncounterRules.WallAt(Map, x, y, p.Facing, CurrentBlock());
    }

    /// <summary>The event byte of the party's square on <see cref="Map"/> (accessor 0x2EE71), e.g. for <see cref="EclVm.ScriptSwitchEventByte"/>.</summary>
    public byte EventByteHere() => EventAt((sbyte)Memory.Position.X, (sbyte)Memory.Position.Y);

    /// <summary>The event accessor 0x2EE71 for an on-map square: plane 2 of the map. [?] Null map or no plane 2: 0.</summary>
    private byte EventAt(int x, int y) =>
        Map is MapPlanes { Planes.Length: >= 768 } m && x is >= 0 and <= 15 && y is >= 0 and <= 15 ? m.Planes[512 + y * 16 + x] : (byte)0;

    /// <summary>
    /// CALL $8000 / $8001 (GAME.OVR 0x8F42, docs/re/character-format.md "XP award"): DS:84D4 = 1 (the next COMBAT
    /// fights even without LOADMON), $6DE6 = 1 for $8000 / 0 for $8001, and every party member whose name differs from
    /// the current character's is taken out of the fight (0x10D = 0). With $8000 combat icon <c>CPIC</c> 11 is loaded
    /// into icon slot DS:6F83 and a copy of the current character is appended as the enemy: in play, computer
    /// controlled, hostile, 0x84 = 0xB2, icon slot DS:6F83, items copied, no effects. [?] The program also renames the
    /// copy (a fixed program string, not reproduced here); DS:6F83 is not advanced.
    /// </summary>
    private void SetUpDuel(bool mirror)
    {
        State.Duel = true;
        Memory.Write(AfterCombat.MirrorDuel, (ushort)(mirror ? 1 : 0));
        var party = Memory.Party;
        var cur = party?.Current;
        if (party is null || cur is null)
        {
            Diagnostics.Add("CALL duel: no current character");
            return;
        }
        foreach (var c in party.Members)
            if (c.Name != cur.Name)
                c[CombatRules.InPlay] = 0;
        if (mirror)
        {
            Ui.LoadCombatIcon(MirrorIcon, State.IconSlot);
            var copy = new EclCharacter(cur.Record);
            copy.Items.AddRange(cur.Items.Select(i => (byte[])i.Clone()));
            copy[CombatRules.InPlay] = 1;
            copy[CombatRules.AutoCombat] = 1;
            copy[CombatRules.Hostile] = 1;
            copy[CombatRules.Npc] = 0xB2;
            copy[0xBF] = (byte)State.IconSlot;
            party.Members.Add(copy);
        }
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
            // The type variable ([bp-0x65] of the handler, GAME.OVR 0x26F7) is written only by the type roll, so a
            // second roll of 48/49 keeps the previous item's type; for the first item of a TREASURE it holds whatever
            // an earlier routine left in that stack cell (path-dependent). FirstItemTypeResidue stands in for it.
            byte type = FirstItemTypeResidue;
            ItemMaker ??= new TreasureMaker(Data?.RandomItemTable);
            for (int k = 1; k <= id - 128; k++)
            {
                type = EncounterRules.RandomItemType(n => Random((ushort)n), type);
                State.TreasureItems.Insert(0, ItemMaker.Make(type, n => Random((ushort)n)));
            }
            // Then the naming routine (START 0x0FE2) runs over the whole treasure list with the current character
            // (0x2AAC–0x2AF1): it only writes the name cache, which the engine composes when it shows an item.
        }
    }

    /// <summary>
    /// The random item maker (GAME.OVR 0x25C99). Null until the first random TREASURE, which builds it from
    /// <see cref="IEncounterData.RandomItemTable"/>.
    /// </summary>
    public TreasureMaker? ItemMaker { get; set; }

    /// <summary>
    /// The type a random TREASURE's first item gets when its second roll is 48 or 49: stack residue in the program,
    /// left by whatever ran before (docs/re/item-format.md "Random treasure"). DOSBox-X read 0 at all seven v1.3 sites
    /// on every sample (Slums s18, wilderness s24); 0 makes a blank item of type 0. [?] Only for modules.
    /// </summary>
    public byte FirstItemTypeResidue { get; set; }

    public void Picture(ushort picture)
    {
        byte v = (byte)picture;
        if (v != 255)
        {
            Memory.PictureShown = true; // 0x15F8
            ushort head = Memory.Read(HeadPicture);
            if (head != 255)
                Memory.PictureArmed = false; // the head + body loader 0x79E4 (0x79E7)
            Ui.ShowPicture(v, head == 255 ? null : (byte)head);
            return;
        }
        // 0x1659–0x1684: only in the 3D view or with DS:49F9 > 1 (not right after a switch from the 3D view to the
        // overland), and only while a picture or the sprite is up: the view is redrawn, both flags cleared and the
        // animation armed (DS:84E1 := 1). Otherwise nothing changes.
        if (Memory.PreviousViewMode <= 1 && Memory.ViewMode != 1)
            return;
        if (!Memory.PictureShown && !State.SpriteShown)
            return;
        Ui.RestoreView();
        Memory.PictureShown = false;
        State.SpriteShown = false;
        Memory.PictureArmed = true;
    }

    public void SpriteOff()
    {
        if (!State.SpriteShown)
            return;
        Explore.Overland.Redrawn(Memory); // 0x37F6: the redraw 0x2DCF4 (on the overland, DS:6DDC + 1)
        Ui.RestoreView();
        State.SpriteShown = false;
        Memory.PictureShown = false; // 0x3800
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
    public void Program(ushort program) => Inner.Program(program);
    public void Protect(ushort address) => Inner.Protect(address);
    public int? PickCharacter(string prompt) => Inner.PickCharacter(prompt);
    public void DrawGlyphs(ushort address, IReadOnlyList<byte> glyphs) => Inner.DrawGlyphs(address, glyphs);
    public void DamageReport(EclDamageReport report) => Inner.DamageReport(report);
    public void DamageEnd(bool partyKilled) => Inner.DamageEnd(partyKilled);
    public void TextHome() => Inner.TextHome();

    /// <summary>
    /// ADDNPC's record (GAME.OVR 0x1F248, docs/re/ecl-format.md "Party, damage and text-box opcodes"): the monster
    /// loader (five 1d6), record 0xAB = Random(256), then the portrait: one of the classes with a level above 0
    /// (bytes 0x96..0x9C, signed; none: class 2) picked by 1d(count); body 0xBC from the DS table for that class
    /// [5 × gender + 1d5], head 0xBB from DS:08C7 [8 × gender + 1d8] (<see cref="IEncounterData.NpcPortraitByte"/>;
    /// without the tables the rolls are still drawn and the bytes stay as loaded). The VM appends it and makes it current.
    /// </summary>
    public EclCharacter? CreateNpc(ushort monster)
    {
        var c = LoadMonsterRecord((byte)monster);
        c[0xAB] = (byte)Random(256);
        var classes = new List<int>();
        for (int k = 0; k <= 6; k++)
            if ((sbyte)c[0x96 + k] > 0)
                classes.Add(k);
        if (classes.Count == 0)
            classes.Add(2);
        int pick = classes[Dice(classes.Count) - 1];
        int gender = (sbyte)c[0x9E];
        int table = pick switch { 0 => 0x8D7, 5 => 0x8EB, 6 => 0x8F5, _ => 0x8E1 };
        int body = Dice(5);
        SetPortrait(c, 0xBC, table + 5 * gender + body);
        int head = Dice(8);
        SetPortrait(c, 0xBB, 0x8C7 + 8 * gender + head);
        return c;
    }

    private int Dice(int sides) => Random((ushort)(byte)sides) + 1;

    private void SetPortrait(EclCharacter c, int offset, int dsOffset)
    {
        if (Data?.NpcPortraitByte(dsOffset) is { } b)
            c[offset] = b;
        else
            Diagnostics.Add($"ADDNPC: no portrait table byte DS:{dsOffset:X4}; record byte {offset:X2} left as loaded");
    }
    public ushort Random(ushort range) => Inner.Random(range);
    public void ScriptChanging(ushort script) => Inner.ScriptChanging(script);
}
