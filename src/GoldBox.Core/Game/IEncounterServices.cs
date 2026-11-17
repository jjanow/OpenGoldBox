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
    /// <summary>PICTURE: a picture (<paramref name="head"/> null) or a head ($6DE1) + body composite.</summary>
    void ShowPicture(byte picture, byte? head);
    /// <summary>Back to the normal view (PICTURE 255, SPRITEOFF).</summary>
    void RestoreView();
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
/// What COMBAT hands over to (GAME.OVR 0x24E1). The fight itself, shops and temples are out of this host's scope.
/// </summary>
public interface ICombatRunner
{
    /// <summary>
    /// The fight (0x9881), entered with $6DC1 already limited to the view. Owns the combat list: it must remove
    /// the monsters <see cref="EncounterState.Monsters"/> from the party list when it is done ([?] where the
    /// program does it is not read).
    /// </summary>
    void Fight(EncounterState state);
    /// <summary>After-combat step (0x6AA5), run after a fight and when COMBAT has no monsters, shop or temple (treasure, per Lee).</summary>
    void AfterCombat(EncounterState state);
    /// <summary>Shop ($6E6C was 1; the host has cleared it) (0x7212).</summary>
    void Shop(EncounterState state);
    /// <summary>Temple ($6DE2 was 1; the host has cleared it) (0x5280).</summary>
    void Temple(EncounterState state);
    /// <summary>
    /// [?] One random treasure item of <paramref name="type"/> (item maker GAME.OVR 0x25C99, not read; it may draw
    /// random numbers).
    /// </summary>
    byte[] MakeRandomItem(byte type);
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

/// <summary>An <see cref="ICombatRunner"/> that only records calls (no fight happens; monsters stay listed).</summary>
public sealed class RecordingCombatRunner : ICombatRunner
{
    public List<string> Calls { get; } = [];
    public void Fight(EncounterState state) => Calls.Add($"Fight {state.Monsters.Count}");
    public void AfterCombat(EncounterState state) => Calls.Add("AfterCombat");
    public void Shop(EncounterState state) => Calls.Add("Shop");
    public void Temple(EncounterState state) => Calls.Add("Temple");
    public byte[] MakeRandomItem(byte type)
    {
        Calls.Add($"MakeRandomItem {type}");
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2E] = type; // [?] where the maker stores the type; FINDITEM reads byte 0x2E
        return r;
    }
}
