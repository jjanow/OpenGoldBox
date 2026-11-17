# Creature record and monster files (Pool of Radiance, DOS)

Status: **layout verified** (2026-11-17) on every creature record shipped with PoR DOS v1.3: 172 monster
records in `MON1CHA.DAX`..`MON8CHA.DAX` and the 12 pregenerated characters `CHRDATA1-6.SAV` / `CHRDATJ1-6.SAV`.
Field meanings come from three independent readings of the original code (below) and were checked against the data
where the data can show them. Anything that only one source claims, or that sources disagree on, is `[?]`.

One 285-byte (0x11D) record describes every creature: player characters, NPCs who join the party, and monsters. The
same bytes are used on disk and in memory, so the file also holds runtime fields (pointers, combat state). The
63-byte item record and the 9-byte effect record that hang off a creature are specified in
`docs/re/item-format.md` (other lane); here they are only counted.

Tool: `tools/scripts/gbchar.py` (`char`, `monsters`, `check`). Save-game files: `docs/re/save-format.md`.

## Sources

- Stephen S. Lee, *Pool of Radiance – Exhaustive Game Information* v2.00, sections 12.2.2.1 "Creature Format" and
  10.2 "Detailed Bestiary" (per-monster listing). CC BY-NC 4.0. Written for PoR DOS v1.3.
  (Local copy: `scratch-priorart/web/porfaq.txt`; URL in `docs/re/dax-format.md`.)
- talonius/pool-of-radiance, `src/types.pas` (`TCharRecord`), `src/saveload.pas` (`LoadCreatureData`, `LoadChar`),
  `src/postcbt.pas` (experience award), `src/includes/c_*.pas` (enum constants). Turbo Pascal reconstruction that
  claims byte-identical v1.3 builds. **No license: read only, nothing copied.**
- DrEvil-TitaniumHelix/pool-of-radiance-remake, `decode/data/SAVES.md` §3 and `MONSTERS.md` (field table with code
  addresses from the v1.3 overlays). No license in tree: read only.
- Our own decode of the files (this document's "Verification").

Field names in this document and in the JSON are ours.

## Conventions

- All integers little-endian. "60−x" fields store `60 − value` in an unsigned byte (THAC0 20 → 0x28, AC 10 → 0x32,
  AC −2 → 0x3E).
- `s8` = signed byte. Level bytes are signed: 0xFF (−1) means "no level in this class" on two monster records.
- **stale**: far pointers and per-combat values that the game rebuilds after loading. Their disk bytes are whatever
  was in memory when the file was written and must be ignored by an importer.
- **derived**: recomputed from the base fields and equipped items whenever the game recalculates the character
  (talonius `ClassCal.RecalcClassStats`, DrEvil "RecalcCharacter"). Stored, but an importer should recompute them.

## Layout (285 bytes)

| Off | Size | JSON key | Meaning | Notes / confidence |
|---|---|---|---|---|
| 0x00 | 16 | `name` | Pascal string: length byte (≤ 15) + characters, rest zero-padded | all 3 sources; padding checked (no stray bytes in any record) |
| 0x10 | 1 | `abilities.str` | Strength 3..18 (monsters up to 22 seen) | all sources; the stat order STR INT WIS DEX CON CHA is confirmed by the bestiary check |
| 0x11 | 1 | `abilities.int` | Intelligence | |
| 0x12 | 1 | `abilities.wis` | Wisdom | |
| 0x13 | 1 | `abilities.dex` | Dexterity | |
| 0x14 | 1 | `abilities.con` | Constitution | |
| 0x15 | 1 | `abilities.cha` | Charisma | |
| 0x16 | 1 | `abilities.str_exc` | Exceptional strength 1..100 when STR = 18 (100 = "18/00"); 0 otherwise | checked: never non-zero with STR ≠ 18 |
| 0x17 | 21 | `spells.memorized` | One spell id per slot, 0 = empty. Bit 7 set = chosen but not yet memorised (pending until the next rest) | Lee, talonius, DrEvil agree. 27 monster lists equal Lee's bestiary (see Verification) |
| 0x2C | 1 | `spells.camp_hours_code` | Rest-time code written by the memorise menu (0, 4 or 6 per talonius / DrEvil) | 0 in every shipped record |
| 0x2D | 1 | `thac0_base` | Base THAC0, 60−x | |
| 0x2E | 1 | `race` | enum, see below | |
| 0x2F | 1 | `class` | enum, see below | |
| 0x30 | 2 | `age` | Age in years (u16) | |
| 0x32 | 1 | `hp.max` | Maximum hit points | |
| 0x33 | 56 | `spells.known` | Spell book: byte `0x32 + id` ≠ 0 means spell id (1..56) is known | Lee, talonius; `known` lists the ids |
| 0x6B | 1 | `derived.sweep_level` | Fighter level used for sweep attacks against weak foes (= fighter level, minimum 1; 0 for most monsters) | derived; Lee "sweep attack maximum targets", talonius `attackLevel` |
| 0x6C | 1 | `size` | bits 0-6: combat icon footprint (1 = 1×1, 2 = 1×2, 3 = 2×1, 4 = 2×2); bit 7: counts as Large | Lee. "Large" for weapon damage = bit 7 **or** footprint > 1 (DrEvil code reading); matches Lee's bestiary size on all 172 |
| 0x6D | 5 | `saves.*` | Base saving-throw targets in AD&D order: paralyzation/poison/death, petrification/polymorph, rod/staff/wand, breath weapon, spell | see "Saving-throw order" |
| 0x72 | 1 | `movement_base` | Base movement | |
| 0x73 | 1 | `level_highest` | Monsters: hit dice. Characters: highest class level | Lee "hit dice", talonius `levelHighest`; equals Lee's hit dice on all 172 monsters |
| 0x74 | 1 | `drain.levels` | Levels lost to energy drain | 0 for PCs, 255 for every monster |
| 0x75 | 1 | `drain.hp` | Max HP lost to energy drain | 0 for PCs, 255 for every monster |
| 0x76 | 1 | `monster.undead_turn_index` | Row of the turn-undead table, 0 = cannot be turned | non-zero exactly on the 16 undead (creature type 4) records |
| 0x77 | 8 | `thief_skills.*` | Percentages incl. all modifiers: pick pockets, open locks, find/remove traps, move silently, hide in shadows, hear noise, climb walls, read languages | Lee, talonius; a level-1 halfling thief PC has 45 45 30 35 35 15 70 0 |
| 0x7F | 4 | – | Far pointer to the effect list | **stale** |
| 0x83 | 1 | `reserved["0x83"]` | Unused | 0 in PCs, 255 in all monsters |
| 0x84 | 1 | `npc` | Bit 7 set = not a player character. Low bits: morale | Lee: morale = (value − 128) / 2; talonius: low 7 bits. Code: the ECL ADD NPC command stores `0x80 | (operand / 2)` (OVR 0x38E9–0x390A; the scripts pass 99 or 100, giving 0xB1/0xB2). How the low bits are read back is `[?]`; seen: 0 (PCs), 255 (163 monsters), 0xB2 (9 monsters) |
| 0x85 | 1 | `npc.treasure_share` | bits 0-2: share of treasure an NPC party member takes; bits 3-7 unused | Lee, talonius. 1 in all PCs |
| 0x86 | 2 | `reserved` | Unused | 0 everywhere |
| 0x88 | 14 | `money.*` | 7 × u16: copper, silver, electrum, gold, platinum, gems, jewelry | all sources |
| 0x96 | 8 | `levels.*` | s8 level per base class: cleric, druid, fighter, paladin, ranger, magic-user, thief, monk | |
| 0x9E | 1 | `gender` | enum, see below | |
| 0x9F | 1 | `monster.creature_type` | Category used by spells (charm/hold need humanoid, snake charm needs snake, animate dead sets undead …) | codes from Lee; meaning of each code partly `[?]` (Lee notes miscategorised monsters) |
| 0xA0 | 1 | `alignment` | enum, see below | checked against Lee's bestiary on all 172 |
| 0xA1 | 1 | `attacks.primary.attacks_x2` | Base primary attacks per round × 2 | |
| 0xA2 | 1 | `attacks.secondary.attacks_x2` | Base secondary attacks per round × 2 | Lee: secondary attacks are used before primary ones |
| 0xA3 | 2 | `attacks.*.dice` | Base damage dice count, primary / secondary (unarmed or natural attack) | |
| 0xA5 | 2 | `attacks.*.sides` | Base damage die sides, primary / secondary | |
| 0xA7 | 2 | `attacks.*.bonus` | Base damage bonus (s8), primary / secondary | |
| 0xA9 | 1 | `ac_base` | Base armour class, 60−x | |
| 0xAA | 1 | `attacks.str_bonus_allowed` | Non-zero: strength to-hit/damage bonus applies | Lee, talonius. Data: of the itemless records with 0xAA = 1 and STR ≥ 17, Lee lists a STR to-hit bonus only for the two human (race 7) ones; four race-0 monsters with the flag and 18/xx show none. Whether the monster recalculation applies 0xAA is `[?]` |
| 0xAB | 1 | `byte_0xAB` | Random byte set at character creation; the roster uses (name, this byte) to detect duplicates | talonius (`chargen.pas`); Lee: "randomized combat icon, not believed used"; DrEvil: unknown. `[?]`. 255 in every monster, varied in PCs |
| 0xAC | 4 | `xp` | Experience points (u32) | |
| 0xB0 | 1 | `item_usability_mask` | Class bits for "can use item": bit 0 magic-user, 1 cleric, 2 thief, 3 fighter (other bits for druid/paladin/ranger/monk unconfirmed `[?]`) | DrEvil code reading; data: equals the bits of the class code (0x2F) on 179 of 184 records; the 5 exceptions are monster records whose class byte does not match their levels (e.g. class cleric with fighter levels) |
| 0xB1 | 1 | `hp.rolled` | Max HP without CON bonus (and without changes made by the Modify menu) | Lee, talonius. Code writers: character creation (OVR 0x1B6C0, 0x1B774), level-up (0x1C959), energy drain (0x10EEB), drain restore (0x29CB0); none in the monster loader, so monsters keep the disk value. Used by the XP award |
| 0xB2 | 6 | `spells.slots` | Spells per day: cleric levels 1-3, then magic-user levels 1-3 | |
| 0xB8 | 2 | `monster.xp_base` | Experience awarded for defeating this creature (u16) | |
| 0xBA | 1 | `monster.xp_per_rolled_hp` | Extra experience per hit point (see "XP award") | |
| 0xBB | 1 | `portrait.head` | Portrait head picture number (HEAD?.DAX) | |
| 0xBC | 1 | `portrait.body` | Portrait body picture number (BODY?.DAX) | |
| 0xBD | 1 | `icon.head` | Combat icon head part (CHEAD.DAX) | Lee range 0..13 |
| 0xBE | 1 | `icon.body` | Combat icon body/weapon part (CBODY.DAX) | Lee calls it "weapon" with range 0..15, but PCs hold values up to 29 here; talonius `iconBody`. `[?]` exact mapping |
| 0xBF | 1 | `icon.order` | Combat-icon animation slot, assigned when the creature joins | not meaningful on disk (PCs hold their party position 0..5, monsters 0) |
| 0xC0 | 1 | `icon.size` | 1 = short icon, 2 = tall icon | for monsters the loader overwrites it (see MONxCHA) |
| 0xC1 | 6 | `icon.colors` | 6 bytes, one colour pair (two nibbles) each for the icon's parts | PC default `91 A2 B3 C4 E6 F7`; monsters are recoloured on load |
| 0xC7 | 1 | `item_count` | Number of items in the inventory list | checked: equals the .ITM record count for all 12 PCs |
| 0xC8 | 4 | – | Far pointer to the item list | **stale** |
| 0xCC | 52 | – | 13 far pointers to equipped items: weapon, shield, body armour, gauntlets, helm, belt, robe, cloak, boots, ring 1, ring 2, arrows, bolts | **stale/derived**; rebuilt from the items' "readied" flag. All zero in monster records |
| 0x100 | 1 | `derived.hands_used` | Hands occupied by readied items | derived |
| 0x101 | 1 | `derived.save_bonus` | Saving-throw bonus from items (s8) | derived. Code: recalculation zeroes it (START image 0x19DC) and adds item byte +33 of each readied item whose base AC code is exactly 128 (START 0xE22–0xEE5; types 76, 92, 93). The saving-throw routine (OVR 0x2BC4D) rolls 1d20: 1 fails, 20 succeeds, else roll + 0x101 + caller bonus (+ effect handlers of trigger class 12) ≥ save byte 0x6D+type succeeds. Lee/talonius right, DrEvil's "AC bonus" wrong |
| 0x102 | 2 | `derived.encumbrance` | Carried weight in coins (u16) | derived |
| 0x104 | 4 | – | Far pointer to the next creature (party / combat list) | **stale** |
| 0x108 | 4 | – | Far pointer to the combat record | **stale** |
| 0x10C | 1 | `combat.status` | enum, see below | |
| 0x10D | 1 | `combat.in_play` | Takes part in play (≈ status okay or animated) | 1 in every record |
| 0x10E | 1 | `combat.hostile` | 0 = party side, 1 = enemy side | 0 for PCs; 1 for 171 monsters (one friendly monster has 0) |
| 0x10F | 1 | `combat.autocombat` | Computer-controlled | 0 for PCs, 1 for monsters |
| 0x110 | 1 | `derived.thac0` | Current THAC0, 60−x | derived (monster disk values like 0xA6 are garbage) |
| 0x111 | 1 | `derived.ac` | Current AC, 60−x | derived |
| 0x112 | 1 | `derived.ac_rear` | Current AC against attacks from behind, 60−x | derived |
| 0x113 | 2 | `derived.attacks.*.attacks_x2` | Attacks left this round × 2, primary / secondary | derived |
| 0x115 | 2 | `derived.attacks.*.dice` | Current damage dice count | derived (weapon) |
| 0x117 | 2 | `derived.attacks.*.sides` | Current damage die sides | derived |
| 0x119 | 2 | `derived.attacks.*.bonus` | Current damage bonus (s8) | derived |
| 0x11B | 1 | `hp.current` | Current hit points | real state, not derived |
| 0x11C | 1 | `derived.movement` | Current movement | derived; rule in "Movement recalculation" |

Total 0x11D = 285 bytes. Every byte is covered above.

## Enumerations

Codes as stored. Names are AD&D rules terms, not game text.

| Field | Codes |
|---|---|
| race (0x2E) | 0 monster, 1 dwarf, 2 elf, 3 gnome, 4 half-elf, 5 halfling, 6 half-orc, 7 human |
| class (0x2F) | 0 cleric, 1 druid, 2 fighter, 3 paladin, 4 ranger, 5 magic-user, 6 thief, 7 monk, 8 cleric/fighter, 9 cleric/fighter/magic-user, 10 cleric/ranger, 11 cleric/magic-user, 12 cleric/thief, 13 fighter/magic-user, 14 fighter/thief, 15 fighter/magic-user/thief, 16 magic-user/thief, 17 monster |
| gender (0x9E) | 0 male, 1 female |
| alignment (0xA0) | 0 lawful good, 1 lawful neutral, 2 lawful evil, 3 neutral good, 4 true neutral, 5 neutral evil, 6 chaotic good, 7 chaotic neutral, 8 chaotic evil |
| status (0x10C) | 0 okay, 1 animated (raised as undead), 2 temporarily removed, 3 fled, 4 unconscious, 5 dying, 6 dead, 7 petrified, 8 gone (removed for good) |
| creature type (0x9F) | 0 humanoid, 1 monstrous humanoid, 2 giant, 3 dragon, 4 undead, 7 extra-dimensional, 8 fire-based, 9 cold-based, 10 regenerating, 11 reptile/amphibian, 12 avian, 14 snake/scorpion, 15 quadruped, 16 insect, 17 generic monster. Codes 5, 6, 13 unused in PoR |
| base-class level slots (0x96..0x9D) | cleric, druid, fighter, paladin, ranger, magic-user, thief, monk |

Races 6 (half-orc) and classes 1, 3, 4, 7, 10, 12 exist in the tables but cannot be created in PoR (Lee). Half-orc
does occur in two monster records. Monsters normally have race 0 and class 2 (fighter); 72 monster records are human
NPCs with real races, classes and levels.

### Spell ids

Spell ids 1..56 index the spell table. The tool reports each id with its caster class and spell level, using the id
ranges from Lee 12.2.2.1: 1-8 cleric 1, 9-21 magic-user 1, 22-28 cleric 2, 29-35 magic-user 2, 36-44 cleric 3,
45-55 magic-user 3, 56 cleric 7 (only castable as a temple service). Spell names and rules belong in a future
spell spec, not here.

## Saving-throw order

Bytes 0x6D..0x71 follow the AD&D table columns: paralyzation/poison/death, petrification/polymorph, rod/staff/wand,
breath weapon, spell. This is the order in Lee 12.2.2.1 (0x6E petrification, 0x6F rod) and talonius
(`C_Save`: 0 paralyzation, 1 petrification, 3 breath, 4 spell). The data agrees: fighter level 1 stores
14 15 16 17 17 and every magic-user stores its best value (11 at level 1-5) in the third byte, as in the AD&D
tables.

Lee's bestiary (10.2) prints the second byte on the "Rod, Staff, or Wand" row and the third on the
"Petrification or Polymorph" row. The values match our bytes one for one, so this is a labelling slip in the
bestiary listing, not a different layout. We follow the offset table and the code.

## XP award (verified: code read + one DOSBox fight)

Post-combat routine at OVR 0x557E (v1.3), static read:

1. Walk the combat list (DS:5D96, next = creature +104h). For every creature with `combat.hostile` (0x10E) = 1
   and `combat.status` (0x10C) ≠ 3 (fled), add `xp_base (0xB8) + xp_per_rolled_hp (0xBA) × hp.rolled (0xB1)`
   (OVR 0x5645–0x5673). The product is a 16-bit multiply that is then sign-extended, so a product ≥ 32768 would
   count as negative; the largest product in the shipped data is 1540 (set 6 id 102), so this never happens.
   The same loop adds the creature's 7 money words (0x88) to the treasure pool (DS:67F4) and moves its items to
   the treasure list (always if the item value > 0, else on 1d10 ≤ 3 while fewer than 8 were taken).
2. Add the treasure pool (OVR 0x57B1–0x5845): copper / 200 + silver / 20 + electrum / 2 + gold + platinum × 5 +
   gems × 250 + jewelry × 2200 (integer divisions on the pool totals).
3. Add 400 × `plus` for every item in the treasure list with `plus` > 0 (OVR 0x5848–0x589E). The walk **stops**
   at the item DS:5D9A points to (s10 correction: not "skips"); the fight start sets DS:5D9A = nil (0xE475), so
   without a pointer set during the fight every item counts. What sets it during a fight is not read `[?]`.
4. Divide by ($6E3E − byte DS:84D5) (OVR 0x58A0–0x58BE; $6E3E = party size, word [DS:49D6]+67Ch), unsigned 16-bit
   divisor: the per-character share. DS:84D5 is counted by the result check 0x5A47 (s10): party members with in
   play (+10Dh) = 0 or status 1 (animated). So the divisor is the party members still in play and not animated.

Before step 1 a separate branch (OVR 0x5584) returns `level_highest` (0x73) × 100 of the current character
(DS:5D92) as the share, with no loot and no division, when the byte DS:84D4 and VM $6DE6 (word [DS:49D6]+5CCh) are
both non-zero. What sets them (program read 2026-11-17, not tried in DOSBox-X):

- ECL **CALL $8000** and **CALL $8001** (handler 0x3A15: $8000 → routine 0x8F42 with 1, $8001 → with 0). 0x8F42
  sets DS:84D4 = 1 and $6DE6 = its argument, and takes out of the fight (+10Dh = 0) every party member whose name
  differs from the current character's (string compare at 0x8F86; a member with the same name stays in). With 1 it also appends a copy of the current character to the party list as the enemy
  (hostile, +84h = 0xB2, items copied, effects cleared). So $8000 is a one-on-one duel against the character's own
  copy (Lee: "mirror duel"), and $8001 is the current character fighting the loaded monsters alone.
- DS:84D4 is cleared by COMBAT after the fight (0x25F2) and at start-up (0xE9E0). $6DE6 is cleared at 0x6BE6
  (Lee: combat results).
- So the branch fires only after CALL $8000, and only the dueller gets XP (the award loop 0x58D3 skips members
  with +10Dh = 0). v1.3 calls $8000 once (ECL3 block 11, $9CA6, then COMBAT) and $8001 twice (ECL1 block 18,
  ECL8 block 16); each is followed by LOADMON and COMBAT, so DS:84D4 never outlives the fight. One script writes $6DE6 directly (ECL3 block 0, `SAVE #1, [$6DE6]` at $A76D, with $6DE3 = 1
  before a COMBAT). DS:84D4 is 0 there, so the branch does not fire and the XP is normal.
- After the fight (0x5AA5–0x5B2D), DS:84D4 set skips the "party defeated" loop and clears DS:49FF. Otherwise a
  non-hostile player character (+84h < 0x80) with status 0, 1 or 3 clears DS:49FF; with $6DE6 = 1 any status
  does. So the ECL3 block 0 fight never counts as a party wipe. (These loops stop at the first creature whose combat
  record, pointer +108h, has byte +13h = 1: the fight start (0xD8B2) sets it for every list position past the party
  size $6E3E, i.e. the monsters LOADMON appended. `combat-format.md`.)

So the multiplier is the disk byte 0xB1, not 0x32 (talonius `postcbt.pas` and DrEvil agree; Lee's bestiary totals
use 0x32 and are off wherever the two differ: 14 monster records, e.g. goblin guards 5 vs 4, set 3 id 109 23 vs 35,
set 7 id 68 0 vs 30). No code writes 0xB1 for monsters (see the table row). DOSBox check (2026-11-17, shipped
save A, ECL2 block 20 fight: 8 × set 2 id 2 and 1 × set 2 id 12, party of 6): monsters 8 × (10 + 1 × 5) +
(10 + 1 × 7) = 137, silver 8 × 18 + 24 = 168 → 8, the leader's armour with plus 1 → 400; 545 / 6 = 90, the
share shown on screen. With 0x32 the result would be 537 / 6 = 89. The split over classes is the award loop
0x58D3 (`combat-format.md` "After combat"; checked against save B: +99/+30/+45/+99/+99/+99 for a share of 90).

## Movement recalculation (code read)

The recalculation routine (START image 0x1758, far address 00BA:0BB8; 22 call sites in GAME.OVR) sets
movement as follows:

1. `movement` (0x11C) := `movement_base` (0x72) (START 0x19F5).
2. For every readied item whose base slot is 2 (body armour) (START 0xD9B, called per readied item at 0x1AA9):
   item weight ≤ 150 → base movement; 151–399 → 9; ≥ 400 → 6. Then, if the item's `plus` is non-zero (any sign)
   and movement ≤ 9, add 3. The value is set, not capped: a base-6 creature in 200-coin armour gets 9.
3. Encumbrance cap (START 0xF40): excess = encumbrance (0x102) − strength allowance (START 0x1F4B, indexed by a
   strength code from START 0x1D8A; −350 … 15,000 coins), floored at 0. Excess ≤ 512 → no cap; 513–768 → 9;
   769–1024 → 6; more → 3. Movement := min(movement, cap).
   Encumbrance (START 0x17B4–0x1948) = Σ item weight × max(count, 1) + the 7 money words; a carried item of the
   bag-of-holding kind subtracts 5000 (START 0x1AEC) `[?]` which flag selects it.

The ECL ADD NPC command (OVR 0x38C4) loads the monster record, sets 0x84 and 0x10E, then runs the recalculation
(OVR 0x3933). **Combat monsters are recalculated too** (DOSBox-X, 2026-11-17, RAM read at the start of the Slums
goblin fight from save A): the 8 goblin guards (set 2 id 2, readied 200-coin armour) and the goblin leader (id 12,
150-coin armour with plus 1) hold 0x72 = 6 and 0x11C = 6 on disk but 0x11C = 9 in memory, which is exactly the rule
above (200 → 9; 150 → 6, plus → +3 = 9). The monster loader (OVR 0x1EEAA, called from 0x7917) does not call it.
It happens at the fight start: OVR 0xD8B2, called from the fight-start routine 0xE402 (program read 2026-11-17, s10;
`combat-format.md`), walks the whole combat list (DS:5D96) and recalculates every entry before it gives each one a
combat record. The other combat-side call sites (e.g. OVR 0xC35D, 0x13D84, 0x14299) were not followed.

## MONnCHA.DAX, MONnITM.DAX, MONnSPC.DAX

- `n` is the data-set ("disk") number 1..8, the same digit as in `ECLn.DAX` and `GEOn.DAX`. The game loads monsters
  only from the set of the current disk (save byte 0, `docs/re/save-format.md`).
- **Block id = monster number.** The ECL script's load-monster command passes this id. Ids are global across sets:
  the same id in different sets is the same monster, and each set carries only the monsters its scripts use. Lee's
  bestiary uses the same numbers (#000..#124). 116 distinct ids, 172 records in total. Checked: all copies of an id
  have identical CHA bytes except id 41 (0x84/0x85 differ in set 5, plus stale pointer bytes), and identical
  ITM/SPC blocks except ids 22 and 23, which have no effects in set 1 because `MON1SPC.DAX` does not exist.
- `MONnCHA.DAX` block = one 285-byte creature record (every block in all 8 files is exactly 285 bytes).
- `MONnITM.DAX` block with the same id = the monster's inventory, k × 63-byte item records (k = 1..6); optional.
- `MONnSPC.DAX` block with the same id = the monster's built-in effects (special attacks, immunities,
  regeneration …), k × 9-byte effect records (k = 1..10); optional. `MON1SPC` and `MON3SPC` do not exist.
- Every ITM/SPC id has a CHA block with the same id (checked; `orphan_item_or_effect_ids` is empty for all sets).
- Item and effect record contents: `docs/re/item-format.md`.

What the loader does after copying the 285 bytes (talonius `LoadCreatureData`, DrEvil `MONSTERS.md`; the two
agree):

1. Clears the pointers at 0x7F, 0xC8, 0x104, 0x108.
2. For races 1, 3, 5 sets `icon.size` (0xC0) = 1; for races 2, 4, 6, 7 sets it to 2; race 0 keeps the disk value
   (0 in every record).
3. Randomises the icon colours 0xC1..0xC3 and 0xC5..0xC6 (each a colour pair built from one 1d6 roll) and sets
   0xC4 = 0xC4.
4. Builds the effect list from the SPC block and the item list from the ITM block, if present.

The ECL command then copies the record for every monster in the group; the in-memory records are recalculated
(derived fields) before combat. So in MONnCHA blocks the derived fields, 0xBF..0xC6 and the item count (0 in all
172) are not meaningful. Use the ITM block's record count instead of 0xC7.

## JSON (`gbchar.py char FILE.SAV --json`)

Top level: `{"format": "gbchar/1", "kind": "character", "file", "size", "record": {…}, "items": {"file", "size",
"records": [hex…]}, "effects": {…}}`. `items.records` / `effects.records` are the raw 63- / 9-byte records as hex
strings, in file order, for the item decoder. `--raw` adds `record.raw` (the 285 bytes as hex).

`record` (trimmed example, pregenerated character A2):

```json
{
 "name": "…", "abilities": {"str": 18, "str_exc": 90, "int": 16, "wis": 17, "dex": 14, "con": 12, "cha": 12},
 "race": {"id": 4, "name": "half-elf"}, "class": {"id": 9, "name": "cleric/fighter/magic-user"},
 "gender": {"id": 0, "name": "male"}, "alignment": {"id": 6, "name": "chaotic good"}, "age": 48,
 "levels": {"cleric": 1, "druid": 0, "fighter": 1, "paladin": 0, "ranger": 0, "magic-user": 1, "thief": 0, "monk": 0},
 "level_highest": 1, "xp": 10, "hp": {"current": 7, "max": 7, "rolled": 5}, "drain": {"levels": 0, "hp": 0},
 "thac0_base": 20, "ac_base": 10,
 "saves": {"paralyzation_poison_death": 10, "petrification_polymorph": 13, "rod_staff_wand": 11,
           "breath_weapon": 15, "spell": 12},
 "movement_base": 12,
 "attacks": {"primary": {"attacks_x2": 2, "dice": 1, "sides": 2, "bonus": 0},
             "secondary": {"attacks_x2": 0, "dice": 0, "sides": 0, "bonus": 0}, "str_bonus_allowed": 1},
 "spells": {"memorized": [{"slot": 17, "spell": 3, "caster": "cleric", "level": 1, "pending": false}, "…"],
            "known": [1, 2, 3, 4, 5, 6, 7, 8, 11, 18, 19, 21],
            "slots": {"cleric": [3, 0, 0], "magic-user": [1, 0, 0]}, "camp_hours_code": 0},
 "thief_skills": {"pick_pockets": 0, "…": 0},
 "money": {"copper": 0, "silver": 36, "electrum": 0, "gold": 0, "platinum": 2, "gems": 0, "jewelry": 0},
 "item_usability_mask": 11, "item_count": 2,
 "npc": {"raw": 0, "is_npc": false, "morale_bits": 0, "treasure_share": 1, "share_raw": 1},
 "monster": {"creature_type": {"id": 0, "name": "humanoid"}, "xp_base": 0, "xp_per_rolled_hp": 0,
             "undead_turn_index": 0},
 "size": {"raw": 1, "icon_dims": 1, "large": false},
 "portrait": {"head": 11, "body": 5},
 "icon": {"head": 9, "body": 17, "order": 1, "size": 2, "colors": [145, 162, 179, 196, 230, 247]},
 "combat": {"status": {"id": 0, "name": "okay"}, "in_play": 1, "hostile": 0, "autocombat": 0},
 "derived": {"thac0": 18, "ac": 4, "ac_rear": 6,
             "attacks": {"primary": {"attacks_x2": 0, "dice": 1, "sides": 10, "bonus": 4}, "secondary": {"…": 0}},
             "movement": 9, "sweep_level": 1, "hands_used": 2, "save_bonus": 0, "encumbrance": 638},
 "byte_0xAB": 179, "reserved": {"0x83": 0, "0x86": 0, "0x87": 0}
}
```

`gbchar.py monsters MONnCHA.DAX --json`: `{"format", "kind": "monsters", "file", "set", "item_file",
"effect_file", "orphan_item_or_effect_ids": [], "monsters": [{"id", "block_size", "record": {…as above…},
"items": [hex…], "effects": [hex…]}, …]}`, sorted by id.

Stability rules: keys are only added, never renamed, within `gbchar/1`; enums always come as `{"id", "name"}` with
`name: null` for codes we do not know; values are raw numbers (AC/THAC0 already converted from 60−x).

## Verification (2026-11-17)

Inputs: PoR DOS v1.3 files (WizardWorks image `por/v1.3_1992_wizardworks-ce-vol1_3.5in-1.44M`, see
`reverse-engineering/dos/original-images/SOURCES.txt`). The v1.1 360K set has byte-identical `CHRDATA1-6.*` (checked
against its `SAVE.ARC`) and `MON*.DAX` files (checked against an unpacked v1.1 set), so everything below holds for
v1.1 too.

1. `gbchar.py check <gamedir>`: decodes all 172 monster records and 12 character files; checks names printable,
   STR..CHA in 3..18 (PCs) or 0..25 (monsters), exceptional strength only with STR 18, every enum code known, PC
   current HP ≤ max HP and max HP > 0, item count = .ITM records, .ITM/.SPC sizes multiples of 63 / 9, CHA block
   size 285, no orphan ITM/SPC ids. **0 problems.**
2. Cross-check against Lee's bestiary (scratch script, not shipped, because it parses Lee's text): for all 172
   monster records, name, hit dice (0x73), hit points (0x32), movement (0x72), the six abilities incl. 18/xx,
   all five saving throws (in record order, see above), alignment, Large/not Large, class levels, XP base and XP per
   HP agree (Lee's entry for an id is compared with every set's copy), except:
   - movement: Lee lists 9 (one record: 6) where the record holds 12 in both 0x72 and 0x11C, for 7 NPC records
     (set 1 id 107; set 3 ids 36, 103, 107, 108, 109, 112). **Explained** (code + data, 2026-11-17): these are
     NPCs that join the party through ADD NPC (ECL3 block 0 `ADDNPC #107`; ECL3 block 11 picks from the table at
     $A170 = ids 108, 103, 122, 36, 111, 112, 109, 110), and ADD NPC runs the movement recalculation (above).
     With their readied armour from `MON3ITM`/`MON1ITM` the rule gives exactly Lee's values: 300-coin armour → 9
     (ids 36, 103, 107, 108), 450-coin armour with plus 1 → 6 + 3 = 9 (id 109), 450-coin armour with plus 0 → 6
     (id 112); the encumbrance cap does not lower any of them further. The other ADD NPC ids (24, 25, 27, 88,
     104, 110, 111, 122) come out unchanged, as Lee lists. The same rule would change 18 other monster ids
     (32 set copies; e.g. goblin guards 6 → 9, set 6 id 100 12 → 6, hill giants 12 → 3 through the encumbrance
     cap) where Lee lists the base value; combat monsters do get the recalculation (DOSBox check under
     "Movement recalculation"), so in play they move as the rule says and Lee's figures are the base values.
   - one record (set 8 id 95) only because the check script read Lee's "18(00)" as 0; the record holds 100. Not a
     data difference.
3. Base THAC0 (0x2D) and base AC (0xA9) against Lee for the 67 records without items (Lee's figures are
   equipment-adjusted): equal on 61. The other 6 are exactly the DEX adjustment to AC (DEX 15-19) and, for two human
   records with 0xAA = 1 (STR 17 and 18/81), the strength to-hit bonus. Lee's "from behind" AC = base AC (0xA9) + 2
   on all 67, without the DEX adjustment.
4. Memorised spells: the multiset of spell ids in 0x17..0x2B equals Lee's "Spells memorized" list for all 27
   spell-casting monster records (spell names mapped to ids with Lee 12.2.2.1). This confirms the spell-id table.
5. Field value surveys over all 184 records (in the tables above where relevant): 0x74/0x75/0x83/0xAB = 255 in every
   monster; 0x86/0x87 = 0 everywhere; undead turn index non-zero exactly for type 4; usability mask = class bits
   on 179 of 184; all monster equipment pointers zero.

Not verified (`[?]`): how the morale bits of 0x84 are read; meaning of 0xAB; icon body range at 0xBE; item
usability bits 4-7; creature-type codes beyond the ones
the spells test. Settled 2026-11-17 by code read: XP multiplier (0xB1), 0x101 (saving-throw bonus), the 7
movement differences (ADD NPC recalculation); by DOSBox: combat monsters get the movement recalculation.
None of these needs an emulator to stay parity-safe as long as the importer copies the byte.
