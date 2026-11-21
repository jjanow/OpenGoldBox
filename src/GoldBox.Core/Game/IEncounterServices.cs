using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>A creature record with its item and effect records, as the monster files hold it.</summary>
public sealed record MonsterData(byte[] Record, IReadOnlyList<byte[]> Items, IReadOnlyList<byte[]> Effects);

/// <summary>Game data the encounter opcodes load (GoldBox.Data implements it from the DAX files).</summary>
public interface IEncounterData
{
    /// <summary><c>MON&lt;disk&gt;CHA/ITM/SPC.DAX</c> block <paramref name="id"/>, or null when absent (the program quits to DOS).</summary>
    MonsterData? Monster(int disk, int id);
    /// <summary><c>ITEM&lt;disk&gt;.DAX</c> block <paramref name="id"/> as 63-byte item records, or null when absent (the program quits to DOS).</summary>
    IReadOnlyList<byte[]>? ItemList(int disk, int id);
    /// <summary>
    /// The random item array of the player's program from DS:0970 (at least <see cref="TreasureMaker.TableWords"/>
    /// words; docs/re/item-format.md), or null when not available.
    /// </summary>
    IReadOnlyList<ushort>? RandomItemTable => null;
    /// <summary>
    /// A byte of the NPC portrait tables in START.EXE's data segment (DS:08C7 heads, DS:08D7/08E1/08EB/08F5 bodies;
    /// ADDNPC, GAME.OVR 0x1F248), or null when not available.
    /// </summary>
    byte? NpcPortraitByte(int dsOffset) => null;
}

/// <summary>
/// The player-facing side of the encounter opcodes (pictures, menus). A game client implements it; headless runs
/// use <see cref="ScriptedEncounterUi"/>. No method returns game text: the host passes ids and enums.
/// </summary>
public interface IEncounterUi
{
    /// <summary>
    /// Draw the encounter at <paramref name="distance"/> squares (0x7A21): the sprite (<c>SPRIT</c> block
    /// <paramref name="sprite"/>) at distance 1–2, the close-up <paramref name="picture"/> at 0.
    /// </summary>
    void ShowEncounter(byte sprite, byte picture, int distance);
    /// <summary>
    /// The encounter draw drew the sprite (<c>SPRIT</c> block <paramref name="sprite"/>, the one whose frames are loaded)
    /// and no close-up (0x7A98–0x7AB7): at slot <paramref name="distance"/> + 1 in the 3D view (also at distance 0, when
    /// the close-up is not drawn), at slot 2 on the overland.
    /// </summary>
    void ShowEncounterSprite(byte sprite, byte picture, int distance) => ShowEncounter(sprite, picture, distance);
    /// <summary>PICTURE: a picture (<paramref name="head"/> null) or a head ($6DE1) + body composite.</summary>
    void ShowPicture(byte picture, byte? head);
    /// <summary>Back to the normal view (PICTURE 255, SPRITEOFF).</summary>
    void RestoreView();
    /// <summary>CALL $2C90's redraw (0x3A7E–0x3A83): the view and the position line (START 0x34AB) drawn again.</summary>
    void ViewRedrawn() { }
    /// <summary>
    /// The encounter menu: show <paramref name="text"/> (null = none) and return the item picked, 0–3. Throw
    /// <see cref="EclAwaitingInput"/> to end the run where the game waits.
    /// </summary>
    int ChooseEncounterAction(EncounterMenuKind kind, string? text, int distance);
    /// <summary>One of the two fixed encounter messages.</summary>
    void ShowEncounterMessage(EncounterMessage message);
    /// <summary>PARLEY: the attitude picked, 0–4 (haughty, sly, nice, meek, abusive).</summary>
    int ChooseParleyAttitude();
    /// <summary>LOADMON: load combat icon <c>CPIC</c> <paramref name="picture"/> into icon slot <paramref name="slot"/>.</summary>
    void LoadCombatIcon(byte picture, int slot);
}

/// <summary>
/// What COMBAT hands over to (GAME.OVR 0x24E1, docs/re/combat-format.md). The fight's actions, the loot menus, shops
/// and temples are player-facing and stay outside the host; the after-combat rules (outcome, experience, removal of
/// the monsters) run in the host (<see cref="AfterCombat"/>).
/// </summary>
public interface ICombatRunner
{
    /// <summary>
    /// The fight (0x9881), entered with $6DC1 already limited to the view and the fight-start bookkeeping the host
    /// models done (<see cref="EncounterState.NotParty"/>, $6DC6 ≤ 100, $6E33 = 0). It leaves each creature's status
    /// (0x10C), in-play byte (0x10D) and hit points in the records; the host's after-combat step reads them.
    /// </summary>
    void Fight(EncounterState state);
    /// <summary>
    /// The fight built by <see cref="EncounterHost.FightBuilder"/> (arena and placement done): the runner shows it and
    /// runs it to its end. Default: <see cref="Game.Fight.Run"/>, headless.
    /// </summary>
    void Fight(EncounterState state, Fight fight) => fight.Run();
    /// <summary>
    /// The loot part of the after-combat step (0x6AD6–0x6AE7), after the host has taken the NPCs' treasure share
    /// (0x6866, <see cref="EncounterHost.NpcsTakingShare"/>): the summary screen (0x5E94) and the loot menu (0x644B),
    /// where the player takes money and items from
    /// <see cref="EncounterState.Money"/> / <see cref="EncounterState.TreasureItems"/>. Not run when the party was
    /// destroyed outside a duel.
    /// </summary>
    void Loot(EncounterState state, AfterCombatOutcome outcome);
    /// <summary>The "party destroyed" screen (0x6B37–0x6BAA).</summary>
    void PartyDestroyed(EncounterState state);
    /// <summary>Shop ($6E6C was 1; the host has cleared it) (0x7212). Rules: <see cref="ShopRules"/>, <see cref="Money"/>.</summary>
    void Shop(EncounterState state);
    /// <summary>Temple ($6DE2 was 1; the host has cleared it) (0x5280). Rules: <see cref="TempleRules"/>.</summary>
    void Temple(EncounterState state);
}

/// <summary>
/// A headless <see cref="IEncounterUi"/>: menu answers from a queue (shared with <see cref="ScriptedEclHost"/>'s
/// menu queue in the capture replay), parley answers from another, a log of calls.
/// </summary>
public sealed class ScriptedEncounterUi(Queue<ushort>? choices = null) : IEncounterUi
{
    public Queue<ushort> Choices { get; } = choices ?? new();
    public Queue<ushort> ParleyChoices { get; } = new();
    public List<string> Calls { get; } = [];
    /// <summary>Throw <see cref="EclAwaitingInput"/> when a queue is empty (else answer 0).</summary>
    public bool StopWhenInputMissing { get; set; }

    public void ShowEncounter(byte sprite, byte picture, int distance) => Calls.Add($"ShowEncounter {sprite} {picture} {distance}");
    public void ShowPicture(byte picture, byte? head) => Calls.Add($"ShowPicture {picture} {head?.ToString() ?? "-"}");
    public void RestoreView() => Calls.Add("RestoreView");
    public void ViewRedrawn() => Calls.Add("ViewRedrawn");
    public int ChooseEncounterAction(EncounterMenuKind kind, string? text, int distance)
    {
        Calls.Add($"EncounterMenu {kind} {distance}");
        if (Choices.TryDequeue(out var c))
            return c;
        if (StopWhenInputMissing)
            throw new EclAwaitingInput("EncounterMenu");
        return 0;
    }
    public void ShowEncounterMessage(EncounterMessage message) => Calls.Add($"Message {message}");
    public int ChooseParleyAttitude()
    {
        Calls.Add("Parley");
        if (ParleyChoices.TryDequeue(out var c))
            return c;
        if (StopWhenInputMissing)
            throw new EclAwaitingInput("Parley");
        return 0;
    }
    public void LoadCombatIcon(byte picture, int slot) => Calls.Add($"LoadCombatIcon {picture} {slot}");
}

/// <summary>
/// An <see cref="ICombatRunner"/> that only records calls. No fight happens: every status stays as loaded, so the
/// after-combat step sees the party okay and the monsters not fled (a won fight).
/// </summary>
public sealed class RecordingCombatRunner : ICombatRunner
{
    public List<string> Calls { get; } = [];
    public void Fight(EncounterState state) => Calls.Add($"Fight {state.Monsters.Count}");
    public void Loot(EncounterState state, AfterCombatOutcome outcome) => Calls.Add($"Loot {outcome.ExperienceShare}");
    public void PartyDestroyed(EncounterState state) => Calls.Add("PartyDestroyed");
    public void Shop(EncounterState state) => Calls.Add("Shop");
    public void Temple(EncounterState state) => Calls.Add("Temple");
}
