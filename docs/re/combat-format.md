# Combat, after-combat, shop and temple (Pool of Radiance, DOS v1.3)

What the ECL COMBAT opcode hands over to (`ecl-format.md` "Encounters", COMBAT 0x24E1): the fight (GAME.OVR
0x9881), the after-combat step (0x6AA5), the shop (0x7212) and the temple (0x5280). Read from the program in
session 10 (2026-11-17); addresses are GAME.OVR file offsets unless marked START (offsets in the unpacked
`START.EXE` image). Far calls resolve through the overlay stubs as `ecl-format.md` "Program references" describes.

Status marks as elsewhere: **P** read in the program, **D** checked against DOSBox-X, `[?]` not settled. Prior art
(talonius' source, Lee's FAQ) was used only to find routines; every rule below is from the disassembly.

Code: `src/GoldBox.Core/Game/` — `CombatRules` (pure rules), `AfterCombat` (0x6AA5), `EncounterHost` (COMBAT, fight
start bookkeeping, CALL ids). The fight's actions, the loot menus, shops and temples are behind `ICombatRunner`.

## Data the combat code uses

- **Combat list** = the party list (DS:5D96, next pointer at record +0x104). LOADMON appends the monsters to it.
- **Party size** = VM $6E3E (word at [DS:49D6]+0x67C).
- **Combat record**: 22 bytes, far pointer at creature record +0x108, allocated and zeroed for every creature at the
  fight start (0xD8B2). Fields used here: +0x03 initiative (signed byte), +0x07, +0x0F, +0x12 cleared at each turn
  (0x99FD), +0x09 facing, +0x0E rounds spent dying, **+0x13 = 1 for list positions past $6E3E** ("not a party
  member": the monsters LOADMON appended, or a creature added later in the list).
- Creature record bytes (`character-format.md`): status 0x10C (0 okay, 1 animated, 3 fled, 4 unconscious, 5 dying,
  6 dead …), in play 0x10D, hostile 0x10E, computer-controlled 0x10F, NPC/morale 0x84, current HP 0x11B.
- Team counts DS:6814 and DS:6815, set by START 0x2F7B (6814 is the party side: the go-on question comes when 6814 > 0 and 6815 = 0).

## The fight (0x9881) — P, top level only

1. DS:49F3 = 5 (combat screen); the spell-target handler DS:6B84 points at the combat version; fight start
   (0xE402, below).
2. Done at once if DS:6814 = 0 or DS:6815 = 0. Else, per round until done:
   - START 0x2F7B (count the teams); for every creature 0x127FB (start of round `[?]` not read); VM $6DCB = 0.
   - Repeat: pick the next creature (0x9938) and let it act (0x99FD) until no creature is picked.
   - End of round (0xA087).
3. Clean-up (0x9807): frees the list at DS:6DBB (19-byte records) and the arena DS:6716 (0x4E9 bytes), frees the
   picture at DS:6E36, resets the palette (0x3083F) and the spell-target handler. **It does not touch the combat
   list**: the monsters are removed by the after-combat step.

**Fight start** (0xE402), the parts that later rules read: VM $6E33 = 0; VM $6DC6 (morale threshold) limited to
100; DS:5D9A (an item pointer the experience routine stops at) = nil; then 0xD8B2 for every creature in list order:
stat recalculation (START 0x1758; this is where combat monsters get their movement recalculated, see
`character-format.md` "Movement recalculation"), a zeroed combat record, +0x13 = 1 when the 1-based list position is
> $6E3E, facing from the party's facing (+4 mod 8 for hostiles), and a creature that is not hostile, has +0x13 = 1
and morale (0x84 AND 0x7F) 0 or > 102 gets 0x84 = $6DC6 + 0x80. Placement, the arena and the turn-start effects
are not read `[?]`.

**Next creature** (0x9938, P): walks the whole list and rolls **1d100 for every creature on every pick** (dice
0x2BCCD). With `best` = 0 and `bestRoll` = 0 at the start: if the creature's initiative > best, bestRoll = its roll;
then if initiative ≥ best and roll ≥ bestRoll, it becomes the pick (best = initiative, bestRoll = roll). A full tie
goes to the later creature. No pick when best stays 0. So the number of random draws per round depends on the list
length and the number of turns; a fight replay must follow this order.

**A creature's turn** (0x99FD, P structure): combat record +0x0F, +0x12, +0x07 = 0; effects with trigger 7
(0x2B1E4); if initiative > 0: initiative 20 becomes 19, the creature becomes current (DS:5D92), stats are
recalculated, effects with trigger 15; then if initiative is still > 0 the AI (0xABCF) acts when 0x10F ≠ 0, else the
player's menu (0x9B21). The actions, the AI, attacks and spells are not read `[?]`.

**End of round** (0xA087, P): one minute passes (clock routine 0x241A1, unit 1); DS:6DE3 + 1; 0x15074 `[?]`; for every
creature effects with trigger 0x13 (0x2B1E4) and 0x2B9D1 `[?]`; a creature with status 5 (dying) adds 1 to combat
record +0x0E and becomes dead (6) when that exceeds 9; a message when a party member is dying (0xA806); team counts
again; the fight is done when either count is 0. When the party side is left and the enemy side is empty and DS:49F4
= 0, a yes/no question offers to go on fighting (yes = not done).

DOSBox (`combat_win`, the Slums goblin fight from save A, 4 rounds): the clock moved 4 minutes ($49C7 4 → 8), as
one minute per round gives.

## After combat (0x6AA5) — P; D where marked

Called after every fight and alone when COMBAT has no monsters, shop or temple.

1. VM $6DC7 (combat result) = 0, DS:443A = 0, DS:49F3 = 6.
2. **Results** (0x5A47), below.
3. **Removal** (0x672A), below.
4. If DS:49FF = 0 (party not destroyed) or DS:84D4 (duel): NPC treasure share (0x6866 `[?]` not read), summary screen
   (0x5E94, shows the share DS:84D6), loot menu (0x644B), then the treasure item list DS:6810 is freed.
   Else: $6DC7 = 0x80 and the "party destroyed" screen.
5. DS:4A00 = 1; VM $6E70, $6E71, $6E72, $6DE3 and $6DE6 = 0.

**Results** (0x5A47). "Party" = the list from its head up to the first creature with combat record +0x13 = 1.
- DS:84D5 (members out of play) = 0, DS:49FF (party destroyed) = 1, DS:443B (fled) = 1 if a party member has
  status 3. In a duel DS:49FF = 0.
- Outside a duel, for each party member: DS:49FF = 0 if (status 0, 1 or 3, or $6DE6 = 1) and hostile = 0 and
  0x84 < 0x80; status 0 or 1 sets DS:84DA (won) and clears DS:443B; in play = 0 or status 1 counts in DS:84D5.
  - Won: the share (0x557E) into DS:84D6, then the award (0x58D3).
  - Party not destroyed: per party member, if not fled: 3 → 0 (and in play = 1), 5 → 4, 4 with HP > 0 → 0 (in
    play = 1). If fled: $6DC7 = 0x81; a member with status 3 → 0 (in play = 1), **every other member leaves the
    party** (0x1CC10, $6E3E − 1).
  - Party destroyed: every party member leaves (0x1CC10), $6E3E = 0.
- In a duel: for every creature in the list with in play = 1, status 0 and hostile ≠ 1: won, and a share is computed
  and awarded **for each such creature** (so the pool money is added again each time). Then status 0/1 → in play = 1,
  5 → 4, for every creature.

**Share** (0x557E): `character-format.md` "XP award" (verified by DOSBox: 545 / 6 = 90 in `combat_win`; the code
runs the whole step on the real records of that fight and gets share 90, $6DC8 = 9 and save B's experience, for
either extreme of the 1d10 item rolls: `CombatRulesTests.After_combat_on_the_slums_goblin_fight_matches_the_capture_and_save_B`). Details
read this session: the creature loop also sets DS:443A; items are copied with equipped (0x34) = 0 and each copy
becomes the new head of the treasure list; an item is taken if its value (word 0x3A) is above 0, else, while fewer
than 8 were taken, on 1d10 ≤ 3 (the roll is made only then); with VM $6DE3 = 1 no item is taken. The item loop for
the plus bonus stops at the item DS:5D9A points to (nil at the fight start; set during the fight `[?]`). The divisor
is ($6E3E − DS:84D5) as an unsigned word; 0 would halt in the long division `[?]`. Mirror duel (DS:84D4 and $6DE6 ≠
0): the current character's 0x73 (signed) × 100, 16-bit product sign-extended.

**Award** (0x58D3), **D**: every creature in the list with in play ≠ 0 and status ≠ 1 gets, added to its experience
dword 0xAC, by class byte 0x2F:

| Class | Gets |
|---|---|
| 0 cleric | share + share / 10 if Wisdom (0x12) > 15, else the share |
| 2 fighter | same with Strength (0x10) |
| 5 magic-user | same with Intelligence (0x11) |
| 6 thief | same with Dexterity (0x13) |
| 8, 10–14, 16 (two classes) | share / 2 |
| 9, 15 (three classes) | share / 3 |
| any other (druid, paladin, ranger, monk) | the share |

DOSBox: in `combat_win` the share was 90; save B (made after it) holds +99 (fighter, Str 17), +30 (class 9), +45
(class 13), +99 (cleric, Wis 17), +99 (magic-user, Int 18), +99 (thief, Dex 18) over save A
(`CombatRulesTests.Award_of_90_reproduces_the_save_after_the_goblin_fight`).

**Removal of the monsters** (0x672A), P + D: VM $6DC8 = 0; then for every creature in the list: if its combat
record +0x13 = 1 **or** it is hostile (0x10E = 1), $6DC8 + 1 when in play ≠ 1, and it is removed (0x1CC10: unlinked,
its icon freed, record, items and effects freed); $6E3E − 1 only when +0x13 ≠ 1 (a hostile party member). Every
other creature's combat record is freed and its pointer cleared. Then the head of the list becomes the current
character. DOSBox: `combat_win` ends with $6DC8 = 9 (8 goblin guards + the leader, all killed), $6DC7 = 0.

**Without a fight** (COMBAT with no monsters, shop or temple): 0x6AA5 still runs. The combat record pointers were
cleared by the previous removal step, so the +0x13 tests read byte 0000:0013 (the interrupt vector table) `[?]`
(machine-dependent; when it is not 1 every member counts as party). The share then comes from the treasure pool
alone (TREASURE money), which is how a script gives experience for treasure. The code takes "not 1".

## Shop (0x7212) — P, structure only

DS:49F3 = 1, the 28 bytes of the money pool DS:67F4 are zeroed, DS:6DC0 (a byte, not the VM cell) = 0, and every item
of the treasure list DS:6810 is named (START 0xFE2): **the shop's stock is the treasure list**, filled by TREASURE
before COMBAT. Menu loop (0x2CC8C): Buy, View, Take, Pool, Share, Appraise, Exit while the pool holds money or items
(0x25C07), else Buy, View, Pool, Appraise, Exit. Keys: B buy (0x7035), V view the character (0x21AED), T take
(0x25974), P pool (0x25191), S share (0x252C3), A appraise (0x2663B), E exit (with money left in the pool a yes/no
question first; leaving keeps it), G / O previous / next character (0x23819).

Buy (0x7035): the price is the item's value word 0x3A. If the current character's money (0x238F8 `[?]` how coins
convert) covers it, the item is copied to the character (0x6F0D) and the money reduced (0x24DB9); else if the pool
total (0x24D31) covers it the pool pays (0x24E0E); else a "not enough" message. Appraise, sell rules and the coin
conversion are not read `[?]`.

## Temple (0x5280) — P, structure only

Same frame as the shop (DS:49F3 = 1, pool zeroed, DS:6DC0 = 0). Menu: Heal, View, Take, Pool, Share, Appraise, Exit
(or Heal, View, Pool, Appraise, Exit with an empty pool). H opens the heal list (0x4F4E, 10 entries):

| Entry | Routine | Price (gold) | Effect |
|---|---|---|---|
| 0 | 0x47C0 | 1000 | cure blindness `[?]` effect ids |
| 1 | 0x4864 | 1000 | cure disease `[?]` effect ids |
| 2 | 0x4978(1) | 100 | heal 1d8 (0x2C647) |
| 3 | 0x4978(2) | 350 | heal 2d8 + 1 (low byte) |
| 4 | 0x4978(3) | 600 | heal 3d8 + 3 (low byte) |
| 5 | 0x4CC8 | 1000 | `[?]` (neutralize poison per prior art) |
| 6 | 0x4A87 | 5500 | raise dead `[?]` details |
| 7 | 0x4DB1 | 3500 | `[?]` (remove curse per prior art) |
| 8 | 0x4E9F | 2000 | `[?]` (stone to flesh per prior art) |
| 9 | – | – | leave the list |

Dice are the dice routine 0x2BCCD (`ecl-format.md` "RANDOM"). Payment (0x4636): after a yes, the current
character's money pays if it covers the price (0x238F8 / 0x24DB9), else the pool (0x24D31 / 0x24E0E, only the low
word of the remaining pool is passed `[?]`), else a message and the answer becomes no; the effect runs only on yes.

## CALL ids (handler 0x3A15) — P

The handler subtracts 0x7FFF from the raw operand address and compares:

| Address | Routine | What |
|---|---|---|
| $2C90 | 0x3A44 | event byte DS:6AB1 read again (0x2EE71; modelled); with DS:4431 set and any redraw flag (84E0/84E3/84E5/84E6/84E8) set, redraw the view and clear the flags |
| $8000 / $8001 | 0x8F42(1 / 0) | duel set-up (`character-format.md` "XP award"); with 1 the copy also gets in play = 1, 0x10F = 1, 0xBF = DS:6F83, a fixed program name, no effect list, and combat icon `CPIC` 11 is loaded into slot DS:6F83 (DS:6F83 not advanced) |
| $BA03 | 0x3AC6 | sound by DS:84CC (8 or 10) |
| **$C01E** | **0x8EAE** | **one square forward**: facing 0: y − 1 (0 → 15), 2: x + 1 (15 → 0), 4: y + 1 (15 → 0), 6: x − 1 (0 → 15), other facings no move; then the event byte DS:6AB1 (0x2EE71) and the wall type ahead DS:6AB0 (0x2ED72) are read again |
| $C018 | 0x3AFA | in the 3D view (DS:49FA = 1) the wall type ahead DS:6AB0 is read again |

DOSBox (`edge_exit_east`, s10): from save A facing east at 15,4, one step off the edge left the party at 0,4 (one
$C01E move, wrapped; the program takes no step of its own after a vector 0 that switched scripts), and the replay
matches cell for cell, including the wall type ahead and the event byte (the comparison checks $C04E/$C04F since
s10). **D** for $C01E. The view redraw (0x2DCF4) re-reads only the event byte, not the wall ahead.

$C01E is the town scripts' edge exit: vector 0 of ECL2 block 20 ($9945) runs it when the step leaves the map
($6DD5 ≠ 0), right before NEWECL. **It is not the encounter flee move**: in block 20 the flee path ($B225) moves
the party itself, with `RANDOM #3, [$C04D]` (facing) and two GETTABLEs from $B6F2 / $B6F7 into $C04B / $C04C, then
CALL $2C90 and the search vector. The `encmenu_flee` capture is replayed with no CALL model at all.

## Open `[?]`

- Start of round (0x127FB), the actions (0x9B21 menu, 0xABCF AI), placement, attacks, spells, effects (0x2B1E4
  triggers), 0x15074 and 0x2B9D1 in the end of round.
- NPC treasure share (0x6866), summary (0x5E94) and loot menu (0x644B) beyond their order.
- What sets DS:5D9A during a fight; byte 0000:0013 in the no-fight case.
- Shop/temple: the coin conversion (0x238F8, 0x24DB9, 0x24D31, 0x24E0E), appraise, take/pool/share, and the
  effects of temple entries 0, 1 and 5–8.
