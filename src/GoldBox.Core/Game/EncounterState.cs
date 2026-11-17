using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Encounter state the program keeps in its data segment, not in VM memory (docs/re/ecl-format.md "Encounters").
/// The VM cells the encounter opcodes touch ($6DC0/$6DC1, $6DCA, $6DE2, $6E6C, menu destinations) stay in
/// <see cref="EclMemory"/>.
/// </summary>
public sealed class EncounterState
{
    /// <summary>Most monster records LOADMON adds before it stops (DS:4435 &lt; 63).</summary>
    public const int MaxMonsters = 63;
    /// <summary>First combat-icon slot for monsters after CLRMON (DS:6F83 = 8).</summary>
    public const int FirstMonsterIconSlot = 8;

    /// <summary>Sprite id of the encounter (DS:6F81): operand 1 of SETUPMON / ENCMENU, a <c>SPRIT&lt;d&gt;.DAX</c> block.</summary>
    public byte Sprite { get; set; }
    /// <summary>Close-up picture (DS:6F82): operand 3 of SETUPMON / ENCMENU, shown at distance 0.</summary>
    public byte Picture { get; set; }

    /// <summary>Monster records added since the last CLRMON (DS:4435).</summary>
    public int MonsterCount { get; set; }
    /// <summary>Next combat-icon slot (DS:6F83). [?] Its value before the first CLRMON is not read.</summary>
    public int IconSlot { get; set; } = FirstMonsterIconSlot;
    /// <summary>LOADMON ran since the last CLRMON (DS:84E7); COMBAT fights only when set (or for a duel).</summary>
    public bool MonstersLoaded { get; set; }
    /// <summary>Duel flag (DS:84D4, set by CALL $8000 / $8001, GAME.OVR 0x8F42); COMBAT fights and then clears it.</summary>
    public bool Duel { get; set; }

    /// <summary>
    /// Monster records LOADMON appended to the combat list (the same list as the party, DS:5D96), in order. CLRMON
    /// does not remove them; the after-combat step does (GAME.OVR 0x672A, <see cref="AfterCombat"/>).
    /// </summary>
    public List<EclCharacter> Monsters { get; } = [];

    /// <summary>
    /// Creatures whose combat record has byte +0x13 = 1: list positions past the party size $6E3E when the fight
    /// started (GAME.OVR 0xD8B2). The after-combat step removes them without lowering $6E3E.
    /// </summary>
    public HashSet<EclCharacter> NotParty { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>TREASURE operands 1–7 (DS:67F4, seven dwords; Lee: copper, silver, electrum, gold, platinum, gems, jewellery).</summary>
    public uint[] Money { get; } = new uint[7];
    /// <summary>Treasure items (DS:6810 list, 63-byte records), head first.</summary>
    public List<byte[]> TreasureItems { get; } = [];

    /// <summary>
    /// DS:5D9A: the copy of the last thrown weapon a fight dropped into <see cref="TreasureItems"/> (GAME.OVR 0x14279,
    /// <see cref="FightRules.DropSpentWeapon"/>); nil at the fight start (0xE476). The experience for the items' plus
    /// stops at this item (0x5865). Compared by reference, like the program's far pointer.
    /// </summary>
    public byte[]? LastThrownItem { get; set; }

    /// <summary>Display bookkeeping (DS:84E3 sprite on screen, DS:84E0 picture on screen). Not compared with captures.</summary>
    public bool SpriteShown { get; set; }
    public bool PictureShown { get; set; }

    /// <summary>The ENCMENU loop is running (DS:84E9).</summary>
    public bool InEncounterMenu { get; set; }

    /// <summary>CLRMON: counts, flags and treasure reset; the monster records stay.</summary>
    public void Clear()
    {
        MonsterCount = 0;
        MonstersLoaded = false;
        IconSlot = FirstMonsterIconSlot;
        Array.Clear(Money);
        TreasureItems.Clear();
    }
}
