using GoldBox.Core.Ecl;

namespace GoldBox.Core.Explore;

/// <summary>
/// The host calls the explore loop owns, in front of a player-facing host (<see cref="Inner"/>): LOADFILES puts the
/// map in the loop's memory, CLOCK advances the clock (0x241A1, unit minutes), Random draws from the loop's one
/// generator. Everything else, and every call after its own effect, goes to <see cref="Inner"/>. Place it inside
/// <see cref="GoldBox.Core.Game.EncounterHost"/> (which wraps it) so the encounter opcodes keep their own rules.
/// </summary>
public sealed class AreaHost(AreaLoop loop, IEclHost inner) : IEclHost
{
    public AreaLoop Loop { get; } = loop;
    public IEclHost Inner { get; } = inner;

    /// <summary>LOADPIECES / wall-set writes: (disk, slot 1..3, WALLDEF id or null = empty).</summary>
    public Action<int, int, int?>? WallSetLoaded { get; set; }

    public void LoadMap(int map)
    {
        Loop.LoadMap(map);
        Inner.LoadMap(map);
    }

    public void LoadWallSet(int slot, int? set)
    {
        WallSetLoaded?.Invoke(Loop.Memory.Disk, slot, set);
        Inner.LoadWallSet(slot, set);
    }

    public void Clock(ushort minutes)
    {
        Loop.AdvanceClock(GameClock.Minutes, minutes);
        Inner.Clock(minutes);
    }

    public ushort Random(ushort range) => Loop.Rng.Next(range);

    public void Print(string text, bool clearFirst) => Inner.Print(text, clearFirst);
    public void PrintReturn() => Inner.PrintReturn();
    public void ClearBox() => Inner.ClearBox();
    public void Delay() => Inner.Delay();
    public ushort VerticalMenu(string prompt, IReadOnlyList<string> choices) => Inner.VerticalMenu(prompt, choices);
    public ushort HorizontalMenu(IReadOnlyList<string> choices) => Inner.HorizontalMenu(choices);
    public int InputNumber() => Inner.InputNumber();
    public string InputString() => Inner.InputString();
    public void Who(string prompt) => Inner.Who(prompt);
    public ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive) => Inner.Parley(haughty, sly, nice, meek, abusive);
    public ushort? EncounterMenu(EclEncounterMenu menu) => Inner.EncounterMenu(menu);
    public void LoadCharacter(ushort index) => Inner.LoadCharacter(index);
    public void LoadMonster(ushort monster, ushort count, ushort picture) => Inner.LoadMonster(monster, count, picture);
    public void SetupMonster(ushort monster, ushort distance, ushort picture) => Inner.SetupMonster(monster, distance, picture);
    public void Approach() => Inner.Approach();
    public void ClearMonsters() => Inner.ClearMonsters();
    public ushort PartyStrength() => Inner.PartyStrength();
    public EclPartyCheck CheckParty(ushort field, ushort effect) => Inner.CheckParty(field, effect);
    public void Combat() => Inner.Combat();
    public void Treasure(IReadOnlyList<ushort> values) => Inner.Treasure(values);
    public void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance) => Inner.Rob(wholeParty, moneyPercent, itemChance);
    public void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra) => Inner.Damage(attack, dice, sides, bonus, extra);
    public void SpriteOff() => Inner.SpriteOff();
    public bool FindItem(ushort item) => Inner.FindItem(item);
    public void AddNpc(ushort monster, ushort morale) => Inner.AddNpc(monster, morale);
    public (ushort Slot, ushort Character)? Spell(ushort spell) => Inner.Spell(spell);
    public void Picture(ushort picture) => Inner.Picture(picture);
    public void LoadOutdoorPicture() => Inner.LoadOutdoorPicture();
    public void Call(ushort routine) => Inner.Call(routine);
    public void Program(ushort program) => Inner.Program(program);
    public void Protect(ushort address) => Inner.Protect(address);
    public void ScriptChanging(ushort script) => Inner.ScriptChanging(script);
    public int? PickCharacter(string prompt) => Inner.PickCharacter(prompt);
    public void DrawGlyphs(ushort address, IReadOnlyList<byte> glyphs) => Inner.DrawGlyphs(address, glyphs);
    public EclCharacter? CreateNpc(ushort monster) => Inner.CreateNpc(monster);
    public void DamageReport(EclDamageReport report) => Inner.DamageReport(report);
    public void DamageEnd(bool partyKilled) => Inner.DamageEnd(partyKilled);
    public void TextHome() => Inner.TextHome();
}
