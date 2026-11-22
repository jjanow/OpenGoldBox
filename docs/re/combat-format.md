# Combat, after-combat, shop and temple (Pool of Radiance, DOS v1.3)

What the ECL COMBAT opcode hands over to (`ecl-format.md` "Encounters", COMBAT 0x24E1): the fight (GAME.OVR
0x9881), the after-combat step (0x6AA5), the shop (0x7212) and the temple (0x5280). Read from the program in
session 10 (2026-11-17); addresses are GAME.OVR file offsets unless marked START (offsets in the unpacked
`START.EXE` image). Far calls resolve through the overlay stubs as `ecl-format.md` "Program references" describes.

Status marks as elsewhere: **P** read in the program, **E** also run under emulation (unicorn oracle), **D** checked
against DOSBox-X, `[?]` not settled. Prior art
(talonius' source, Lee's FAQ) was used only to find routines; every rule below is from the disassembly.

Code: `src/GoldBox.Core/Game/` — `CombatRules` (pure rules), `FightRules` (start of round, attacks, damage, NPC
share), `FightState` (combat records and the fight's DS cells), `Fight` (headless round loop), `AfterCombat` (0x6AA5),
`EncounterHost` (COMBAT, fight start bookkeeping, CALL ids). The menu, the AI, the arena, the loot menus, shops and
temples are behind `IFightController`, `IFightGeometry` and `ICombatRunner`.

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
   - START 0x2F7B (count the teams); for every creature in list order 0x127FB (start of round, below); VM $6DCB = 0.
   - Repeat: pick the next creature (0x9938) and let it act (0x99FD) until no creature is picked.
   - End of round (0xA087).
3. Clean-up (0x9807): frees the list at DS:6DBB (19-byte records) and the arena DS:6716 (0x4E9 bytes), frees the
   picture at DS:6E36, resets the palette (0x3083F) and the spell-target handler. **It does not touch the combat
   list**: the monsters are removed by the after-combat step.

**Fight start** (0xE402), the parts that later rules read: VM $6E33 = 0; VM $6DC6 (morale threshold) limited to
100; DS:5D9A (the last thrown weapon dropped into the treasure, below; the experience routine stops at it) = nil
(0xE476); then 0xD8B2 for every creature in list order:
stat recalculation (START 0x1758; this is where combat monsters get their movement recalculated, see
`character-format.md` "Movement recalculation"), a zeroed combat record, +0x13 = 1 when the 1-based list position is
> $6E3E, facing from the party's facing (+4 mod 8 for hostiles), and a creature that is not hostile, has +0x13 = 1
and morale (0x84 AND 0x7F) 0 or > 102 gets 0x84 = $6DC6 + 0x80. Placement, the arena and the turn-start effects
are not read `[?]`.

**Next creature** (0x9938, P, E, D below): walks the whole list and rolls **1d100 for every creature on every pick** (dice
0x2BCCD). With `best` = 0 and `bestRoll` = 0 at the start: if the creature's initiative > best, bestRoll = its roll;
then if initiative ≥ best and roll ≥ bestRoll, it becomes the pick (best = initiative, bestRoll = roll). A full tie
goes to the later creature. The best is kept as an unsigned byte and each initiative is compared sign-extended, so
a creature with initiative 0x80–0xFF (negative) is never picked. The result pointer is written only on a pick, and set
to nil at the end when best stayed 0 (a creature with initiative 0 can be the pick along the way, then nil).
No pick when best stays 0. So the number of random draws per round depends on the list
length and the number of turns; a fight replay must follow this order.

**A creature's turn** (0x99FD, P structure): combat record +0x0F, +0x12, +0x07 = 0; effects with trigger 7
(0x2B1E4); if initiative > 0: initiative 20 becomes 19, the creature becomes current (DS:5D92), stats are
recalculated, effects with trigger 15; then if initiative is still > 0 the AI (0xABCF) acts when 0x10F ≠ 0, else the
player's menu (0x9B21). The stat recalculation is START 0x1758 (`character-format.md` "Stat recalculation";
`StatRecalc`), called at 0x9A8A.

**Program units of the fight** (overlay stubs, `ecl-format.md` "Program references"): stub 0x35 (0x9807–0xAA86)
the loop, menu and end of round; stub 0x38 (0xABC0–0xC4E7) the AI; stub 0x68 (0x127FB–0x1646D) round start, moves,
attacks, spells' targets; stub 0xB0 (0x2AEEA–0x2C821) effects, attack and saving rolls, dice; START 0x1713–0x3B40
helpers (helpless, damage, equipment).

### Start of round (0x127FB) — P, E, D

For every creature in list order, before the first pick (the loop then sets VM $6DCB = 0):

1. Combat record: +0 (spell being cast) = 0, +1 (cast allowed) = 1, +2 (item use allowed) = 1, +8 (attacked this
   round) = 0, +4 (attack phase) = 2.
2. Attacks left in phase 1 (0x1351E, below) into record 0x113.
3. Phase 2: DS:681A = 0xA2, DS:681D = 0, effects with trigger 0x12, then 0x114 = whole attacks (0x1364D) of DS:681A.
4. +5 (sweep budget) = record 0x6B.
5. In play (0x10D ≠ 0): initiative +3 = 1d6 + Dex adjustment (START 0x1D08 on 0x13: 0–2 → −4, 3–5 → Dex − 6,
   16–18 → Dex − 15, 19–20 → 3, 21–23 → 4, 24–25 → 5, else 0), stored as a byte and compared signed: below 1 → 1;
   then − 6 when the low byte of ((hostile, sign-extended) + 1) AND VM $6DCB is not 0 ($6DCB = surprise: bit 0 the
   party, bit 1 the monsters); then below 0 or above 20 → 0. Out of play: 0.
6. Movement (0x1291E) into +6: 0x11C, plus VM $6E72 (low byte of the sum) on the party side (hostile 0); outside
   1–96 it becomes 1; DS:681A = 2 × that, DS:681D = 1, effects with trigger 0x12, DS:681D = 0; the result is DS:681A.

**Draws:** exactly one (the 1d6) per creature in play, in list order; nothing else in the round start draws (the
effect handlers excepted `[?]`). With the picks (0x9938) that gives the RNG contract of a round's start.

**DOSBox** (s11, `fight_rounds/`, a random goblin encounter in the Slums from save A: 6 party + 6 goblin guards,
the party surprised them, $6DCB = 2): states at the first player menu of rounds 0–4. Stepping the captured RandSeed
back by 12 (1d100 picks) + 12 (1d6) draws and replaying 0x127FB and 0x9938 forward reproduces, in all five rounds,
every creature's initiative (60 values; the surprised goblins' round-0 rolls of 1–4 give 0), movement +6, phase-2
attacks 0x114, the creature whose menu showed and the RandSeed (`FightRoundCaptureTests`). In round 0 the second
and third menus (two and three picks with no draws in between: the turns were ended with Done, Quit) also follow
the pick rule, including a tie of two initiatives 8 settled by the 1d100. **D** for the round start and the pick.

**Checked under emulation** (s49, `tools/scripts/por_round_oracle.py`, fixture
`tests/fixtures-private/por/program-v1.3/round.json`, `RoundProgramTests`): 160 rounds of 1–14 creatures (Dex 0–25 and
stray bytes, in and out of play, hostile 0/1/2/0xFF, 0x11C 0/1/96/97/255, $6E72 and $6DCB words, DS:6DE3 odd and
even, combat record +8 set or clear, melee, bows with arrows, crossbows with bolts, thrown and flags-0x0A weapons with
counts 0–20, effects 39 haste, 42 slow and 58 run for real (haste's first-time ageing, two records of one id)): every
creature's record, effect list and all 22 combat-record bytes after 0x127FB, the RandSeed, then the picks 0x9938 to
the end of the round (each pick's initiative set to 0); and 200 runs of 0x9938 alone on initiative bytes 0x00–0xFF
(the signed path, ties). All equal to `FightRules.BeginRound` and `CombatRules.NextCreature`. The whole routine was
read (s49): nothing in it is left out.

**Whole attacks** (0x1364D): (rate + 1 when the round counter DS:6DE3 is odd, byte add) shr 1.

**Attacks left** (0x1351E): `previous` = 0x113; 0x113 = 0xA1 (the base rate, in halves); with a ranged weapon
(START 0x39A3: weapon slot set and its base range, byte 12, > 1) that has ammunition (START 0x3A2B, below) the rate is
the weapon's rate of fire (base byte 5, at least 2), else 0x113; DS:681D = 0, trigger 0x12; attacks = whole attacks
of DS:681A; ranged with ammunition: at most max(1, ammunition count 0x39). 0x113 = attacks when +8 = 0, or attacks <
`previous`, or (attacks < 2 × `previous` and not ranged); otherwise 0x113 keeps the base rate written at the start.

**Equipment** (START 0x1758–0x18D7): the 13 pointers at 0xCC are cleared and rebuilt from the readied items (0x34 ≠
0) in list order: base slot 0–8 → that slot (a later item wins); 9 → ring 1 if empty, else ring 2 if empty; and,
whatever the slot, type 0x49 → arrows (slot 11) and type 0x1C → bolts (slot 12). **Ammunition** (START 0x3A2B): with
weapon flags (base byte 14) bit 4 (thrown) the weapon; then if bit 3: bit 0 → the arrows slot, bit 7 → the bolts slot
(each replacing the previous pick, nil included); found when an item was picked or the flags are exactly 0x0A.
Throwable (START 0x39E6): ranged and flags AND 0x14 = 0x14.

### Attacks (0x1407F, 0x13C01) — P

**Melee attack** (far calls to MakeAttack, stub 0x68 entry 12, at 0xA5DB in the menu unit and 0xBA62 in the AI;
near calls inside unit 0x68, e.g. the guards 0x12E22, not listed): the defender notes the attack (0x13FF1: +0x0F + 1; angle = (direction
from the defender to the attacker − facing +9, + 8) mod 8, folded to 8 − angle when > 4; +0x12 = (+0x12 + angle) mod
8), then MakeAttack (0x1407F): a defender with +0x0F < 3, not attacked from behind, turns to face the attacker; the
attacker faces the defender (0x31232 `[?]`) and +0x0A = the defender; if 0x113 or 0x114 > 0 the attacker becomes
current and the attack routine runs; then the thrown-weapon rule; stats recalculated; actions done → START 0x31D7
(initiative, +0, +7, +6 = 0).

**Attack routine** (0x13C01): DS:6823/6824 (hits per phase), DS:6E2D/6E2E (attacks per phase), DS:6818 (damage) = 0;
attacker +8 = 1.
- Helpless defender (START 0x1713: carries effect 0x33, 0x34, 0x35 or 0x1F, list DS:286C; which spells `[?]`): the
  phase (+4) steps down while 0x112 + phase is 0 (no lower bound `[?]`), one attack counted, the defender takes
  HP + 5 (with the slay-helpless message), invisibility goes, 0x113 = 0x114 = 0, actions done. **No draw.**
- Else: with a weapon and a defender whose size byte 0x6C is above 0x80 or has (0x6C AND 7) > 1, the phase-1 damage
  becomes the weapon's large dice: 0x115 = base byte 2, 0x117 = byte 3, 0x119 = 0x119 − byte 11 + byte 4 (signed).
  (So a size byte of exactly 0x80 is not large: `character-format.md` says "bit 7".) The defender's stats are
  recalculated and its trigger-0x0B effects run. Defense byte: backstab → rear AC 0x112 − 2; else AC 0x111, or the
  rear AC 0x112 when attacked from the rear (argument, or +0x0F > 1 and the attacker in the defender's facing direction and
  +0x12 > 4). Then the range penalty.
- Phases from +4 down to 1; in each, while 0x112 + phase > 0 and the defender is not down: 0x112 + phase − 1,
  +4 = phase, attacks + 1, **attack roll**; a hit when it hits, or (after a miss) the defender is helpless. A hit
  counts, rolls damage, applies it, and runs trigger phase + 1 on the attacker while the defender is in play; a
  defender out of play ends the loop. Phase 2 (0x114, the secondary attacks) therefore comes before phase 1.
- No hit at all: the miss message. Done when 0x113 = 0x114 = 0; +5 = 0; done → 0x31D7.
- **Draw order per attack:** 1d20, then (on a hit) the damage dice.

**Attack roll** (0x2BBA1): invisibility (effect 0x19, every instance: a loop of START 0x2199 find and 0x2AF10) removed (0x2BEBC); 1d20 into DS:6822 (signed);
≤ 1 misses; 20 becomes 100; trigger 0x0A on the attacker and 0x10 on the defender; hit when roll ≥ 0 and roll +
THAC0 byte 0x110 + m ≥ defense byte, as 16-bit signed sums, where m = low byte of VM $6E71 (party side) or $6E70
(hostile side), signed. In the 60 − x encoding of both bytes this is exactly "1d20 ≥ THAC0 − AC − m", with 1 always
a miss and 20 always a hit unless the defense is above 100 + 0x110 + m. The roll without an attacker (0x2BB39; callers
not read `[?]`): roll + given value **>** the defender's 0x111 (strictly).

**Damage** (0x1298C, phase p): dice count 0x114 + p (DS:681C) of sides 0x116 + p (0x2BD18), plus the signed bonus
0x118 + p, **as a byte**: the program's "below 0 → 0" test is an unsigned compare and never fires, so 1d2 − 2
rolling 1 deals 255. Backstab: × ((thief level 0x9C, sign-extended) shr 2 + 2), low byte. DS:6819 = 0, triggers 4
(attacker) and 5 (defender).

**Backstab** (0x14D6F): thief level 0x9C > 0 (signed), off hand empty or type 0x32, weapon slot empty or type 7, 8,
0x23, 0x24 or 0x25, the defender attacked more than once this turn (+0x0F > 1) and the attacker in the defender's
facing direction (0x14E38 = +9).

**Range penalty** (0x14319): d = distance (START 0x30FB); band = (weapon range − 1) div 3 with a ranged weapon, else
d; if d − band > 0: d −= band, defense + 2; if again d − band > 0: defense + 3. So melee adds nothing; a ranged weapon
of range ≤ 3 adds both at any distance > 0. `[?]` A ranged weapon with range 0 would be a divide overflow; none.

**What a hit does** (0x12AF4, the non-message part; START 0x2DC8): over = damage − HP when damage > HP, else remain =
HP − damage. Over > 9, or remain 0 on an animated (1) creature → dead (6); else over > 0 → dying (5) and, in combat
(DS:49F3 = 5), combat record +0x0E = over; else remain 0 → unconscious (4). A status not okay/animated (set at START
0x2208: {0, 1}): in play 0, HP 0, in combat the team count of its side − 1 and initiative 0; else HP = remain. Then
any damage > 0: the defender's +1 (cast allowed) = 0 and a spell it was casting is lost: START 0x202B (call at
0x12D2C) removes the first memorised copy of that spell from the **current** character (DS:5D92, the attacker while
it attacks), then +0 = 0. A defender out of play loses the first instance of each effect in DS:0C14 (7, 11, 30, 31, 32, 51, 52, 53, 54,
58, 59, 95, 98, 137, 74, 75; 0x2BEF4, their removal handlers run `[?]`), runs trigger 0x0D, and if still out of play
leaves the map (0x3154B).

**Thrown weapons and DS:5D9A** (0x141DF–0x14280): after an attack made with an ammunition item, its count 0x39 (when
> 0) drops by DS:6E2D (phase-1 attacks made, byte). At 0 the item leaves the attacker (START 0x20A9); when the attacker
has a throwable weapon and the item's byte 0x3E ≠ 0x89, first a 63-byte copy with readied = 0 becomes the head of
the treasure list DS:6810 and **DS:5D9A = that copy** (0x14279). The two writers of DS:5D9A in GAME.OVR are this one
and the fight start (0xE476, nil); none in START.EXE. Effect: the after-combat plus experience stops at the last
dropped weapon, so it, earlier dropped weapons and the TREASURE items placed before the fight give no plus
experience; the monsters' items (added at the head after the fight) still do.

**Enemy hit points** (0x15074, end of round): over hostile creatures (0x10E = 1), cur = Σ HP of those in play, max =
Σ 0x32 of all; when max > 0, DS:6E2F = low byte of ((20 × cur, 16 bits) div max) × 5. Read by the AI `[?]`.

**Menu** (0x9B21, P structure): a spell begun earlier (+0 > 0) is cast now (0x27C38) and the actions end; else a
loop over the menu (0x9E6D): M move (0xA1E6), V view (0x21AED, then 0x1351E), A aim (0x15D53), U use (0x21F32,
then 0x1351E), C cast (0x14C0F), T turn undead (0x13965, then 0x31D7), Q quick (0xAA32, then the AI 0xABCF), D done
(0xA64C), Alt-M quick magic, space (party back to manual), Alt-Q (all to quick, initiative 20), Alt-X (0x1638E).
Read in full in s12: `combat-menu.md` (menu, move, aim, Done menu, cast frame, sweep attacks, turn undead).
**AI** (0xABCF): read in full in s12, `combat-ai.md` (opening dice, morale 0xBC97, item 0xAFA4, turn undead 0xADC3,
spell 0xB105, weapon 0xBF8A, target search 0x15FDF, move/attack 0xB6FC, step 0xB3B2).

**End of round** (0xA087, P): one minute passes (clock routine 0x241A1, count 1, unit 1, which runs the effect
timeout 0x23DCC over the whole list: camp.md "Effect timeout"; D: `fight_casts`); DS:6DE3 + 1; 0x15074 (enemy hit
points, below); for every
creature effects with trigger 0x13 (0x2B1E4) and the terrain effects 0x2B9D1 (`spells-effects.md` "Terrain"); a creature with status 5 (dying) adds 1 to combat
record +0x0E and becomes dead (6) when that exceeds 9; a message when a party member is dying (0xA806); team counts
again; the fight is done when either count is 0. When the party side is left and the enemy side is empty and DS:49F4
= 0, a yes/no question offers to go on fighting (yes = not done). DS:49F4 is cleared at start-up (0xE99E) and never
set (no other instruction names it; VM $49F4 is a different cell), so every won fight asks (s45, D: also with the
whole party on Quick; training.md "The picture under $4AFF on other pages").

DOSBox (`combat_win`, the Slums goblin fight from save A, 4 rounds): the clock moved 4 minutes ($49C7 4 → 8), as
one minute per round gives.

## After combat (0x6AA5) — P; D where marked

Called after every fight and alone when COMBAT has no monsters, shop or temple.

1. VM $6DC7 (combat result) = 0, DS:443A = 0, DS:49F3 = 6.
2. **Results** (0x5A47), below.
3. **Removal** (0x672A), below.
4. If DS:49FF = 0 (party not destroyed) or DS:84D4 (duel): NPC treasure share (0x6866, below), summary screen
   (0x5E94, shows the share DS:84D6), loot menu (0x644B), then the treasure item list DS:6810 is freed.
   Else: $6DC7 = 0x80 and the "party destroyed" screen; the treasure list DS:6810 is **not** freed.
5. DS:4A00 = 1; VM $6E70, $6E71, $6E72, $6DE3 and $6DE6 = 0.

With DS:49FF set the ECL run and the game loop 0x403E end and the program quits to DOS (start-menu.md §8: no return
to the party menu in v1.3).

**D** (s18, `fight_casts` q33 → q34_the_end): "won" and "destroyed" are separate tests and can both hold. The won
test (status 0 or 1) does not look at the hostile byte or 0x84, the destroyed test does: a charmed party member (on
the enemy side) and still okay when the rest of the party fell, sets DS:84DA = 1 while DS:49FF stays 1 (at q33 both
Brother Sean and Darkstar are charmed, okay and hostile = 1). So the share and the award run: the pool gets every
hostile creature's coins, the two charmed members' included (q34: copper 40, silver 126 = 2 × 36 + 3 × 18,
electrum 4); items of value above 0 are always taken and count toward the 8, the rest are rolled 1d10 while fewer
than 8 were taken: three rolls, the 3 draws between the fight's end and the end screen; share 225 with DS:84D5 = 4. Then
every party member leaves and the "party destroyed" screen comes, DS:6810 still set. The replay runs `AfterCombat`
after the fight and matches RandSeed, DS:49FF, 443A, 443B, 84DA, 84D6 and the pool (`FightMonsterCastReplayTests`).

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
the plus bonus stops at the item DS:5D9A points to (nil at the fight start; the last thrown weapon a fight dropped,
see "Thrown weapons"). The divisor
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

**NPC treasure share** (0x6866), P: runs after the removal, so over the party. NPCs (0x84 > 0x7F) with status
okay add (0x85 AND 7) to the NPC shares and to the total; every other creature adds 1 to the total (byte counters).
With NPC shares > 0, each of the seven pool dwords (DS:67F4) that is above 0 (signed) loses cut × NPC shares, where
cut = **low byte** of (pool div total) (signed long division, START 0xB9F6) and the product is 16 bits, sign-extended.
So a pile whose per-share cut is ≥ 256 loses only the low byte of it per share. When any coin was taken a window lists
each okay NPC with 0x85 ≠ 0 as taking its share, and waits for a key.
**The window (0x695E), P + D (s22):** a full-screen window (START 03F1:02E8 with 1, 1, 38, 22, empty title CS 0x12A8);
for the k-th such NPC (k from 0) the name and the share text through the text routine in the box (5, 5 + 2k)–(34, 22),
colour 10, cleared first (so each starts at column 5 on rows 5, 7, 9, … and wraps at column 34); then the press-enter
text through the menu routine (03C5:002A) with colours 15, 15, 15, as after the summary (white, the shared cell back
to the first). The lines go through START 0x77F8 (0x69F0), so they type while DS:4A00 is set, which here is the value
the fight began with (0x6BAF sets it later); the summary's lines are fixed-cell START 0x76B6 and never type (P, s30;
view3d.md "Typing state"). DOSBox-X: a test save whose step runs ADDNPC 73 (a gnoll, share 7), TREASURE 100 copper and COMBAT
with no monsters; the gnoll's line wraps after the fifth word. Then the summary (found treasure), then the loot
screen with the gnoll in the party list (cyan). The Client's three screens equal DOSBox-X pixel for pixel apart from
its help lines (`VenueScreens.Loot`; before s22 it printed into the text window over the loot screen).
**Row 24 under the window** (read s39, P + D): 0x6866 → 0x695E calls only the window 03F1:02E8 (0x697F, rows 0–23),
the lines 0709:0768 (0x69F0) and then the menu routine 03C5:002A (0x6A40); before it the after-combat step's 0x5A47
and 0x672A call no screen unit (0709, 03F1, 0464). So row 24 keeps what the fight left there while the lines type,
and the press-enter menu is drawn over it word by word (not cleared first). **D** (s39, `fight_save.py BASE OUT
1C36005E00640B00020001000224 --monpatch 2:0x32=1,0x11B=1`: ADDNPC 94, the level 3 magic-user with share 7, then one
goblin guard with 1 HP whose coins make the pool; seed 12345, every member Quick, the continue-battle question
answered No): the continue-battle question (No lit) stayed on row 24 under the NPC's share line as it
typed (~1.3 s) until the menu line. Client: `ClientPlayer.KeepsCombatRow24` as for the party-destroyed window; 23 of
126 Client frames equal before, 49 after: every share-window frame (26) now equal. The other misses (3–78) were the
fight: the joined NPC's icon, drawn on the map in DOSBox-X, was missing in the Client (fixed, combat-menu.md
"Icons"): 155 of 159 frames equal after that fix, the misses the help line and three frames where the share
window first shows. There the Client's first three typed states ("L", "LE", "LEV") were caught on screen and
DOSBox-X showed none of them: its first window frame already held "LEVE", 0.68 s after the last fight frame (the
Client: 0.43 s). **Settled s41** (P + D): from the continue-battle answer to the window the program calls no Delay,
pause or key wait (0x6AA5 → 0x5A47, 0x672A with 0x1CC10 and the frees, 0x6866's division; then 0x695E's window
03F1:02E8 and the lines). Recaptured with the grab that keeps grabbing while a key is held (same save and seed, all
Quick, No): the window's first frame 0.07 s after N, its frame drawn over another 0.07 s, then "L", "LE", "LEV",
"LEVE"… one by one ~43 ms apart. So the missing states and the 0.25 s were the old blocking grab; the ~0.14 s
DOSBox-X spends ending the fight and drawing the frame at 3000 cycles is machine time (not copied, decisions
s39 / s40). The Client: 146 of 148 frames equal (every share-window frame; the misses the run's first frame and the
summary after DOSBox-X's capture ended).
An earlier set-up stopped with "UNABLE TO FIND ITEM FILE" and quit to DOS. That was the test save's fault, not a
path to match: its hand-written TREASURE had seven 2-byte operands and a bare `FF`, so the eighth operand's type byte
was 0xFF (an undefined type, `ecl.py`), followed by LOADMON's opcode byte, and the code after it was misread. v8 was
then not 255, and a v8 below 128 loads `ITEM<d>.DAX` block v8, whose absence "quits to DOS" (ecl-format.md
TREASURE; GAME.OVR string "Unable to find item file"). With well-formed operands (`por_test_saves.py` save E) TREASURE
runs; the capture above used no TREASURE (the goblin's coins fill the pool). After a COMBAT with no monsters the Client keeps row 24 blank
under the window (no fight row; not compared) `[?]`.

**Between a loot and the next venue** (P + D s47). The loot menu ends without touching the text box when the player
leaves (its exit question stays there); the after-combat step frees the treasure list and returns; COMBAT's handler
then sets DS:49F3 to 4 / 3 and calls START 0x3379 (0x2635), whose 00BA:272A draws the four windows through 03F1:02E8,
which blanks each inside (03F1:0137): the text box is empty, the 3D view (0x2DCF4), the party list. The next COMBAT's
shop or temple entry (0x721E / 0x528B: DS:49F3 = 1, START 0x3379 case 1) blanks it again and draws the venue box.
DOSBox-X (test save `loot-shop` of `por_test_saves.py`: TREASURE 100 copper, COMBAT, TREASURE, SAVE 1 → $6E6C,
COMBAT; 175 grabs a second): after N at the loot's question the screen became the explore layout (frames, then the
3D view and the party list, ~0.3 s), then the shop's frames, the empty box and the menu; row 24 kept "YES NO" until
the shop's menu. The Client equal on the shop's first frame and the explore screen after it (but the help lines). It
draws the in-between explore layout too (the redraw runs) but the shop's screen follows at once: the ~0.3 s DOSBox-X
spends drawing the windows and the view at 3000 cycles is machine time (the grabs changed every 12–20 ms throughout,
the drawing itself; decisions s39 / s40), not copied.

**Loot menu** (0x644B), P structure: View (0x21AED, called at 0x65B4; `camp.md` "View"), Take (0x62B6), Pool (0x25191), Share (0x252C3), D (0x27C38),
Exit, G / O previous / next character (0x23819) with the party list after it; the item list comes from 0x25C07. On
entry START 0x3379 case 6 (0x645C); nothing else in the loop redraws.
Menu line (s12): with money in the pool View, Take, Pool, Share; with items only View, Take, Pool; then Detect and
Exit when there are items and the current character has spell 5 or 11 among its memorised bytes 0x17–0x2B, else
Exit; with nothing left View, Pool, Exit. P pools only for a non-extended key (0x50 is also Down's scan code). E (or
the key 0) with money or items left (0x25C07 asked again): the exit question (0x6636–0x66DB, P + D s47): the first
text in colour 10 with the box cleared, the second in 15 running on after it (0709:0768), then "~Yes ~No" through the
menu helper 0x8B57 (`shop-format.md` "Exit"; in the Client the keyboard's Return answers the lit word too, a
deliberate difference, s48). Answer 1 (No) ends the menu with the question left in the box; any
other answer blanks the box (0709:0DD3 with 1, 17, 38, 22) and the menu runs again. Nothing left: the menu ends.
**Take** (0x62B6): money and items → a "Take:" menu (Money, Items, Exit) until Exit; money only → the coin take
0x25974; items only → the item take 0x6184 (`shop-format.md`). **Summary** (0x5E94, s12): the text box is cleared, then one
result line: with DS:443A (a hostile that did not flee) or a duel (DS:84D4): in a duel the duel-lost text when
DS:84DA = 0, else the duel-won text; outside a duel the party-won text. Otherwise the party-fled text when DS:443B is
set, else the treasure-found text (a COMBAT with no monsters). Then the experience line (the duelist's or each character's share,
DS:84D6) and a key prompt through the menu routine. **Rows (s21, read and DOSBox-X):** the screen is a full-screen window
(START 03F1:02E8 with 1, 1, 38, 22, no title); colour 10 at column 1: the result on row 3, the experience text and the
share on row 5, the "experience points" text on row 7; then the menu routine 0x2CC8C on the lower-case press-enter text
with colours 15, 15, 15 (all white). The routine starts an entry at each digit or capital (set at its CS:00AB, 0x2C99A),
so that text has none and the shared cell goes back to 1 (0x2CCE7): the loot menu opens on View. The loot menu (0x644B)
first redraws the screen for DS:49F3 = 6 (START 0x3379 case 6): the explore frame, PIC block 1 (the treasure chest) in
the view, the party list, no position line, an empty text window. The "continue battle" question (0xA1A5) is the
yes/no routine 0x2D9A0 (opens on No).

## Shop (0x7212) — P

**Screen (D, s22):** the shop and the temple run in interface state 1; their redraw (START 0x3379 case 1: 0x32CA(1),
the frames and the inner view box, then 03D0:003E with DS:49FD / 49FE and 03D0:0039 (3, 3, 1): the head + body last
given to 0x79E4, view3d.md "The shop and temple box", none here) leaves the **view box
empty** and draws **no position line**; the party list is there, the text box empty, the menu on row 24. A PICTURE
before CLRMON (as the New Phlan shop's script, ECL3 block 0 $A248) is not shown. Leaving with Exit: the 3D view and
the position line come back with the step, and the command line opens on the shared cell left by the shop menu
(Exit, entry 4: Search). Client: the view box blank and no position line while `Venue.Shop`; pixel-equal apart
from its help lines. **With a picture still armed** (DOSBox-X s24, test saves: PICTURE 2 then the shop or the temple
with no CLRMON): the view box is just as empty on entry and again after View → Exit, and Exit shows the 3D view with
the step, as with CLRMON (settled s24). Between menus: View's menu offers only the entries 0x21AED builds
(a fighter with items and money: Items, Trade, Drop, Exit), so its Exit leaves the shared highlight on the shop menu's
4th entry, Appraise (DOSBox-X); the Client's View page uses the same entries since s24 (it offered Spells always).
As the first menu after a Load (vector 4 opening the shop) with $4AFF set, the menu runs a loaded picture in the box
until the loop draws its screen again (P + D s44, training.md "The picture under $4AFF on other pages").

DS:49F3 = 1, the 28 bytes of the money pool DS:67F4 are zeroed, DS:6DC0 (a byte, not the VM cell) = 0, and every item
of the treasure list DS:6810 is named (START 0xFE2): **the shop's stock is the treasure list**, filled by TREASURE
before COMBAT. Menu loop (0x2CC8C): Buy, View, Take, Pool, Share, Appraise, Exit while the pool holds money or items
(0x25C07), else Buy, View, Pool, Appraise, Exit. Keys: B buy (0x7035), V view the character (0x21AED), T take
(0x25974), P pool (0x25191), S share (0x252C3), A appraise (0x2663B), E exit (with money left in the pool a yes/no
question first; leaving keeps it), G / O previous / next character (0x23819).

Buy, sell, identify, take, pool, share, appraise and the coin rules: `shop-format.md` (s11).

## Temple (0x5280) — P

Same frame as the shop (DS:49F3 = 1, pool zeroed, DS:6DC0 = 0). Menu: Heal, View, Take, Pool, Share, Appraise, Exit
(or Heal, View, Pool, Appraise, Exit with an empty pool). H opens the heal list (0x4F4E, 10 entries):

The entries, their conditions, prices, payment and the heal and raise-dead rules: `shop-format.md` "Temple" (s11).

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

- Placement, the arena, direction/distance and moves (fight start 0xE402, 0x12E22–0x13465, 0x14E38, START 0x30FB)
  are read in `combat-map.md`; the menu, sweep and turn undead in `combat-menu.md`; the AI in its own spec.
- Spells (0x14C0F, 0x27C38); the effect handlers (0x2B1E4 triggers); the stat recalculation's effect on THAC0/AC
  during a fight.
- Summary screen (0x5E94): byte 0000:0013 in the no-fight case (rows settled s21).
- Shop/temple: see `shop-format.md` "Open".
