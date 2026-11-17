namespace GoldBox.Core.Ecl;

/// <summary>
/// Thrown by a host when it has no answer for an input request (menu, input line). The VM ends the run with
/// <see cref="EclStopReason.AwaitingInput"/>, leaving the PC where the game's PC is while it waits (after the
/// instruction's operands). Used to stop at the same point as a capture taken while the game waits for a key.
/// </summary>
public sealed class EclAwaitingInput(string request) : Exception("awaiting input: " + request);

/// <summary>Result of CHKPARTY (0x1E): written to the four destination addresses (program read, GAME.OVR 0x217B).</summary>
public readonly record struct EclPartyCheck(ushort Min, ushort Max, ushort Average, ushort Any);

/// <summary>Arguments of ENCMENU (0x29), operand values as fetched. [?] Lee only: meanings of every field.</summary>
public sealed record EclEncounterMenu(
    ushort Monster, ushort Distance, ushort Picture, ushort DestinationAddress,
    ushort[] Reactions, string[] DistanceTexts, ushort Move1, ushort Move2);

/// <summary>
/// Everything an ECL script needs from the rest of the game (UI, combat, shops, files, sound, the party). The VM
/// passes decoded operand values and destination addresses and writes back only what a method returns, so no game
/// rule lives in the VM. Opcodes whose effect is from Lee only ("L" rows) are marked <c>[?]</c> with their spec row.
/// </summary>
public interface IEclHost
{
    // ---- Text box -----------------------------------------------------------------------------------------------
    /// <summary>PRINT (0x11) / PRINTCLR (0x12, <paramref name="clearFirst"/>). [?] L row 11/12.</summary>
    void Print(string text, bool clearFirst);
    /// <summary>PRINTRET (0x33): text cursor to column 1 of the next line (P, 0x3831).</summary>
    void PrintReturn();
    /// <summary>CLEARBOX (0x3D): clear the picture window (1,1)–(15,15) (P, 0x385A; the next PROTECT draws inside it).</summary>
    void ClearBox();
    /// <summary>DELAY (0x3A): pause for game speed (DS:49E2, 0–9) × 225 ms; speed 0 = none (P).</summary>
    void Delay();

    // ---- Input and menus ----------------------------------------------------------------------------------------
    /// <summary>VMENU (0x15): returns the choice index stored at dst. Shape P; meaning of the index [?].</summary>
    ushort VerticalMenu(string prompt, IReadOnlyList<string> choices);
    /// <summary>HMENU (0x2B): returns the choice index stored at dst. Shape P; meaning of the index [?].</summary>
    ushort HorizontalMenu(IReadOnlyList<string> choices);
    /// <summary>
    /// INPUTNUM (0x0F): the number typed (P: up to 6 characters, asked again until 0..65536; the VM stores the low
    /// 16 bits). The length operand is never fetched, so it is not passed.
    /// </summary>
    int InputNumber();
    /// <summary>
    /// INPUTSTR (0x10): the line typed. The VM applies the program's rules (characters 0x20–0x7A, at most 40,
    /// upper-cased, empty stored as one space). The length operand is never fetched, so it is not passed.
    /// </summary>
    string InputString();
    /// <summary>WHO (0x39): called by the default <see cref="PickCharacter"/> (display only).</summary>
    void Who(string prompt);
    /// <summary>PARLEY (0x2C): returns the value stored at dst. [?] L row 2C.</summary>
    ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive);
    /// <summary>
    /// ENCMENU (0x29): returns the value stored at the destination, or null for no write (P, GAME.OVR 0x2C3E: a
    /// reaction value of 5 or more ends the menu without writing). <see cref="GoldBox.Core.Game.EncounterHost"/>
    /// implements the program's rules.
    /// </summary>
    ushort? EncounterMenu(EclEncounterMenu menu);

    // ---- Party, monsters, encounters ----------------------------------------------------------------------------
    /// <summary>LOADCHAR (0x0A) when no party is attached (the VM applies the program's rule itself when one is).</summary>
    void LoadCharacter(ushort index);
    /// <summary>LOADMON (0x0B). [?] L row 0B.</summary>
    void LoadMonster(ushort monster, ushort count, ushort picture);
    /// <summary>SETUPMON (0x0C). [?] L row 0C.</summary>
    void SetupMonster(ushort monster, ushort distance, ushort picture);
    /// <summary>APPROACH (0x0D). [?] L row 0D.</summary>
    void Approach();
    /// <summary>CLRMON (0x1C). [?] L row 1C.</summary>
    void ClearMonsters();
    /// <summary>
    /// PARTYSTR (0x1D) when no party is attached to the VM memory (the VM computes the program's formula from the
    /// records when one is). Test hook.
    /// </summary>
    ushort PartyStrength();
    /// <summary>CHKPARTY (0x1E) when no party is attached (the VM computes it when one is).</summary>
    EclPartyCheck CheckParty(ushort field, ushort effect);
    /// <summary>COMBAT (0x24). [?] L row 24.</summary>
    void Combat();
    /// <summary>TREASURE (0x27): eight values. [?] L row 27.</summary>
    void Treasure(IReadOnlyList<ushort> values);
    /// <summary>ROB (0x28) when no party is attached (the VM applies it when one is).</summary>
    void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance);
    /// <summary>DAMAGE (0x2E) when no party is attached (the VM applies it when one is).</summary>
    void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra);
    /// <summary>SPRITEOFF (0x31). [?] L row 31.</summary>
    void SpriteOff();
    /// <summary>
    /// FINDITEM (0x32) when no party is attached (the VM searches the item records itself when one is): true when
    /// someone carries an item whose byte 0x2E is <paramref name="item"/>. Test hook.
    /// </summary>
    bool FindItem(ushort item);
    /// <summary>ADDNPC (0x36) when <see cref="CreateNpc"/> gives no record (log only).</summary>
    void AddNpc(ushort monster, ushort morale);
    /// <summary>SPELL (0x3B) when no party is attached: null = no write; else (index, count) for the two low-byte destinations.</summary>
    (ushort Slot, ushort Character)? Spell(ushort spell);

    // ---- World, files, programs ---------------------------------------------------------------------------------
    /// <summary>PICTURE (0x0E). [?] L row 0E.</summary>
    void Picture(ushort picture);
    /// <summary>LOADFILES (0x21), 3D view: load map <c>GEO&lt;disk&gt;.DAX</c> block <paramref name="map"/> (P; the VM has set $49C5).</summary>
    void LoadMap(int map);
    /// <summary>LOADFILES (0x21) outdoors ($49E6 = 0): load the fixed <c>BACPAC.DAX</c> picture (P; no operand passed).</summary>
    void LoadOutdoorPicture();
    /// <summary>
    /// LOADPIECES (0x37) and writes to $6E22/$6E24/$6E26: load <c>WALLDEF&lt;disk&gt;.DAX</c> set
    /// <paramref name="set"/> into wall slot <paramref name="slot"/> (1–3), or empty the slot when null (P).
    /// </summary>
    void LoadWallSet(int slot, int? set);
    /// <summary>
    /// CLOCK (0x34): add minutes to the clock $49C6–$49CC (P). [?] The amount is fetched with an uninitialised operand
    /// index in the program (stack residue); the VM passes operand 1's value.
    /// </summary>
    void Clock(ushort minutes);
    /// <summary>CALL (0x2D): built-in routine chosen by the raw address (ids P, effects [?]).</summary>
    void Call(ushort routine);
    /// <summary>PROGRAM (0x38): 0 training hall, 8 end game, 9 camp, else nothing (ids P, effects [?]). After 9 the VM ends the run (P).</summary>
    void Program(ushort program);
    /// <summary>PROTECT (0x3C): called by the default <see cref="DrawGlyphs"/>.</summary>
    void Protect(ushort address);
    /// <summary>
    /// Turbo Pascal Random(<paramref name="range"/>): a value in 0..range−1 (P). Used by RANDOM (range = v + 1, or
    /// 255 for v = 255) and by the dice of SURPRISE (range 6). <see cref="TurboPascalRandom"/> reproduces the game's
    /// generator.
    /// </summary>
    ushort Random(ushort range);
    /// <summary>NEWECL (0x20) was executed; the VM loads the script itself through its script source.</summary>
    void ScriptChanging(ushort script);

    // ---- Added in s11 (program read; default implementations keep older hosts working) -------------------------
    /// <summary>
    /// WHO (0x39, GAME.OVR 0x3879): the character pick menu with <paramref name="prompt"/>; returns the 0-based index of
    /// the chosen party-list member, which the VM makes current, or null for no change. [?] What Escape leaves.
    /// </summary>
    int? PickCharacter(string prompt)
    {
        Who(prompt);
        return null;
    }

    /// <summary>
    /// PROTECT (0x3C, GAME.OVR 0x3BC3): draw one line of glyph codes (VM bytes at <paramref name="address"/> + 1, up to
    /// a byte 0 or 255) at the text cursor; used for the rune texts.
    /// </summary>
    void DrawGlyphs(ushort address, IReadOnlyList<byte> glyphs) => Protect(address);

    /// <summary>
    /// ADDNPC (0x36, GAME.OVR 0x1F248) with room in the party: load monster <paramref name="monster"/> as a new party
    /// member (monster loader rolls, Random(256) to 0xAB, the portrait rolls). The VM appends it and makes it current.
    /// Null: the host cannot build it (then <see cref="AddNpc"/> is called instead).
    /// </summary>
    EclCharacter? CreateNpc(ushort monster) => null;

    /// <summary>DAMAGE (0x2E) hit one creature: the text-box line ("dies" / "is hit FOR n points of Damage").</summary>
    void DamageReport(EclDamageReport report) { }

    // ---- Added in s12 --------------------------------------------------------------------------------------------
    /// <summary>
    /// EXIT (0x00, and PROGRAM 9 which ends like it): the text cursor goes to column 1, row 17 (DS:5E24/5E25) without
    /// clearing the text box (ecl-format.md "PRINTRET and CLEARBOX"). Display only; the VM state does not change.
    /// </summary>
    void TextHome() { }
}
