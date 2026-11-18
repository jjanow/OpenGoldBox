# Item records, base item table and effect records (DOS Pool of Radiance)

Status: checked on PoR DOS v1.3 (START.EXE sha1 `0640ba4f…`, GAME.OVR sha1 `31c12ed0…`, ITEMS sha1
`fc6edcdd…`, 2026-11-17). Every field carries one of three labels:
**verified** = we saw it in the v1.3 code or it holds for every data record; **Lee** / **DrEvil** = taken from
that source, consistent with the data, not re-checked in code; **`[?]`** = unknown. The table locators and the random-item array were also
checked on v1.1 and v1.2 programs (ITEMS is byte-identical in v1.1, v1.2 and v1.3).
Tool: `tools/scripts/gbitem.py` (reads everything from the player's own files; nothing from the game is embedded).

## Sources

- Stephen S. Lee, *Pool of Radiance – Exhaustive Game Information* v2.00, sections 9.2–9.4 (item lists, random
  magic items), 12.2.3 (effect format), 12.2.4 (item, base item, random item array formats), 12.6.4 (data
  segment map). CC BY-NC 4.0. Archived copy: see `dax-format.md`.
- DrEvil / TitaniumHelix, *pool-of-radiance-remake*, `decode/data/ITEM.md`, `ITEMS.md`, `SAVES.md` §4–6,
  `decode/tools/items.py`, `unexepack.py` (no licence in tree: read for reference only, no code taken).
  https://github.com/DrEvil-TitaniumHelix/pool-of-radiance-remake
- Simeon Pilgrim, coab, `Classes/Item.cs` (Curse of the Azure Bonds; same 63-byte layout family).
- Our own disassembly of the v1.3 program (objdump, 8086), offsets below. Lee's and DrEvil's layouts agree
  with each other and with what the code does, except where noted.

Addresses: "image" = offset in the EXEPACK-expanded START.EXE load image (segment 0 = image offset 0);
"DS:xxxx" = offset in the data segment, which is image paragraph 0x0C7C (image offset 0xC7C0) in v1.3
(Lee: add 0xD0A0 for the offset in an unpacked .EXE file with its 0x8E0-byte header);
"OVR" = byte offset in GAME.OVR.

## File roles

| File | Content | Addressing |
|---|---|---|
| `CHRDAT<s><n>.ITM` | a character's inventory, n × 63-byte item records, no header | same stem as the `.SAV` (every `.ITM`/`.SPC` has one); stem naming per the creature/save-file spec |
| `CHRDAT<s><n>.SPC` | that character's active effects, n × 9-byte effect records | same stem as the `.SAV`; absent if no effects |
| `MON<d>ITM.DAX` | per-monster item lists, each block n × 63 | block id = monster id; every id is also a `MON<d>CHA.DAX` block (checked, all 8 files) |
| `MON<d>SPC.DAX` | per-monster permanent effects, each block n × 9 | block id = monster id; every id is also a `MON<d>CHA.DAX` block (checked, 6 files) |
| `ITEM<d>.DAX` | placed (non-random) treasure lists, each block n × 63 | block id = treasure operand of the ECL treasure command (below) |
| `ITEMS` | base item table, 2-byte header + 128 × 16 | index = item `type` byte |

`<d>` is the virtual disk number (`dax-format.md`). All 681 item records (28 `.ITM`, 301 `MON?ITM`, 352
`ITEM?`) and 79 effect records decode with no trailing bytes.

**Treasure loader** (OVR 0x26F7; checked): the ECL treasure command takes 8 operands. Operands 1–7 are money
amounts stored as longs at DS:67F4 (7 coin/gem/jewelry kinds; owned by the ECL spec). Operand 8 = `k`:
`k < 0x80` → open `ITEM` + disk digit (DS:5376) + `.dax`, read block `k`, push each 63-byte record onto the
treasure list (DS:6810, new item becomes the head, `next` cleared); missing block → fatal error.
`k = 0xFF` → no items. Otherwise → generate `k − 0x80` random items (see "Random treasure").

## Item record (63 bytes)

Same layout on disk (`.ITM`, `MON?ITM`, `ITEM?`) and in memory (a singly linked list). All words little-endian.

| Off | Size | Field | Meaning | Status |
|---|---|---|---|---|
| 00 | 41 | `name` | Pascal string[40]: **cache** of the composed name (below), written by the naming routine. Never read back for logic. | verified |
| 29 | 1 | `byte29` | 0 in every record. Lee: list-member flag (shared with the string-list record). | `[?]` |
| 2A | 4 | `next` | far pointer to the next item; stale on disk, cleared on load | verified |
| 2E | 1 | `type` | index into the base item table (0..127) | verified |
| 2F | 1 | `word3` | name word id, slot k=1 (written last) | verified |
| 30 | 1 | `word2` | name word id, slot k=2 | verified |
| 31 | 1 | `word1` | name word id, slot k=3 (written first) | verified |
| 32 | 1 | `plus` | signed magic bonus; Lee/DrEvil: weapon to-hit/damage, armour/shield/ring AC | field verified (generator, naming); effect Lee |
| 33 | 1 | `save_bonus` | signed saving-throw bonus: when readied, added to creature 0x101 only if the base `ac_code` is exactly 128 (types 76, 92, 93); also makes the item count as magical for the detect marker | verified (START image 0xE22–0xEE5; save roll OVR 0x2BC4D, see creature spec 0x101) |
| 34 | 1 | `equipped` | 0/1 readied | verified (naming shows it) |
| 35 | 1 | `hidden` | name-word hide mask: bit (3−k) hides slot k, i.e. bit 0 hides +31, bit 1 hides +30, bit 2 hides +2F. 0 = fully identified. | verified |
| 36 | 1 | `cursed` | non-zero = cursed (Lee); counts as magical for the detect marker | marker verified; meaning Lee |
| 37 | 2 | `weight` | weight in coins (Lee); DrEvil: multiplied by `count` for encumbrance | field verified (generator writes it); per-unit DrEvil |
| 39 | 1 | `count` | stack count; 0 = single non-stacking item | verified |
| 3A | 2 | `value` | value in gold pieces (Lee) | field verified (generator writes it); unit Lee |
| 3C | 3 | `props` | type-dependent, see below | verified (code read) |

**`props` (3C–3E)** (verified, OVR offsets of v1.3). The program treats an item as a scroll when its base slot is
11..13 (OVR 0x2A255). Scrolls: each non-zero byte is a spell id (1..56, index into the spell name array DS:2912 +
41·(id−1)); bit 7 = flagged for scribing (Lee). For every other item **`3E` decides**, not `3C`:

- `3E` < 0x80 and `3D` > 0: a **usable** item. The Use command (OVR 0x222F0, needs the item readied) and the
  combat AI (OVR 0xB074) accept it; using it (OVR 0x22AC4) casts power `3D`, taken as spell table entry `3D` if
  ≤ 56, else `3D − 23` (so 80..90 → 57..67, Lee's item powers). Then, only if `3C` > 0: if `count` > 1, `count`
  is decreased; else `3C` is decreased and at 0 the item is removed from the inventory (START image 0x20A9).
  So `3C` = 0 means unlimited use, and a stack of k items with c charges gives k − 1 + c uses.
- `3E` ≥ 0x80: a **worn** item. Readying calls handler `3E` with "apply", unreadying with "remove" (OVR
  0x2250A; dispatcher OVR 0x2AEEA = far 00B0:0025). The handler table at DS:6828 (4-byte far pointers, built
  at OVR ≈0x11E00–0x12509) is shared: indices 1..127 are the effect handlers (effect id), 128..139 the item
  codes. Handlers by code:
  - 128, 129, 130, 133, 134, 136, 138, 139 (one routine, OVR 0x11B35): apply = add effect `3D` (permanent,
    `param` 12, no remove handler) and run that effect's handler; remove = delete the first effect `3D`.
    In the data: 129 ring (effect 61), 133 cloaks (effect 89), 136 two-handed sword (effect 3).
  - 131 (OVR 0x11BB2): sets Strength to 18/00 through effect 38 (hard-coded; `3D` not used), with a message;
    remove deletes the matching effect 38. Data: gauntlets.
  - 132 (OVR 0x11D00): on apply, if the wearer's alignment ≠ `3D` & 0x0F, the item is unreadied and the wearer
    takes `3D` >> 4 damage as non-weapon damage (OVR 0x2C22E). Data: long swords with `3D` = 0xF0 (lawful good,
    15) and 0x52 (lawful evil, 5).
  - 135 (OVR 0x11D8F): on apply, if Strength < 19 the item is unreadied with a message. Data: boulders.
  - 137 (OVR 0x11DD5): apply does nothing; remove deletes effect 23. Written by the spell code (OVR 0xF515:
    `3D` = 23, `3E` = 137).
- `3E` < 0x80 and `3D` = 0: no use, no handler.

Data survey (all 681 records): every charged item has `3E` = 0; every `3E` ≥ 128 record has `3C` = 0. One
record breaks Lee's grouping: the long sword with props (0, 22, 0) (Lee's Flame Tongue) has `3E` < 0x80 and
`3D` > 0, so by the code it is a usable item that casts spell 22 with no charge cost, and never confers an
effect. Whether the Use menu (OVR 0x2D669) offers it is not read `[?]`.

## Name composition (verified)

The naming routine is at image 0x0FE2 (segment 0x00BA offset 0x0442 in v1.3; 16 call sites in GAME.OVR:
inventory display, treasure generation and display, identify…). Arguments (Pascal order): creature far ptr,
item far ptr, `show_ready`, row, column, `print`. It writes the result into the item's `name` (truncated to 40).

```
s := ''
if show_ready: s := (equipped ? READY6 : NOTREADY6)     # two 6-char literals in the code segment
detect := any party member has effect 5 (walk party list DS:5D96, next at creature +104h)
if detect and (plus > 0 or save_bonus > 0 or cursed <> 0): s += '*' + ' '
if count > 0: s += IntToStr(count) + ' '
vis := 0
for k in 1..3: if item[2Eh+k] <> 0 and bit (3-k) of hidden is 0: vis |= 1 << (k-1)
done := false
for k in 3 downto 1:                       # byte 31, then 30, then 2F
    if vis bit (k-1) is 0: continue
    s += WORD[item[2Eh+k]]                 # DS:10A7 + 21*w, Pascal string[20], w = 1..255
    if count >= 2 and not done and (
           (1 << (k-1)) == vis                              # the only visible word
        or (k == 1 and vis > 4 and type <> 56h)
        or (k == 2 and (vis & 1) == 0)                       # last visible word
        or (k == 3 and type == 56h)
        or (type in (49h, 1Ch) and item[31h] <> 0B1h)):      # two ammunition types, unless word 0B1h
        s += 's' + ' '; done := true
    else:
        s += ' '
item.name := s (max 40)
```

- The plural rule only appends a letter; irregular plurals are not handled (that is the original behaviour).
- The result always ends in a space; `gbitem.py` reports `name_raw` (exact) and `name` (right-trimmed).
- The word array has 255 entries; v1.3 has 4 empty ones. Word id 0 means "no word".
- Check (2026-11-17): `.ITM` stored names are produced by this routine with `show_ready` = 1 or 0:
  27/28 records match exactly (with the matching readied/not-readied prefix); the one mismatch carries a count of 11 in the
  name and 8 in `count` (stale cache after use). `MON?ITM` records have empty names (the game composes them).
  `ITEM?.DAX` names were typed by the authoring tool: 318/352 equal the composed name after trimming, the other
  34 use different spellings (bracketed plurals, other capitalisation). The treasure loader does not rename them;
  the display paths do. An engine must always compose names and never trust the stored string.

**Identify** (verified unless marked). Shop identify (OVR 0x22F8C–0x23131; money rules `shop-format.md`), in this order: confirm prompt;
if the selected character has ≥ 200 gp, deduct 200 from them, else if the pool DS:67F4 has
≥ 200, deduct it there (money rules: `shop-format.md`), else show a "not enough money" message and stop. Only after paying: if `hidden` = 0,
show an "already identified" message (the 200 gp are not refunded in this routine; whether the UI ever offers
this case is `[?]`); else `hidden` := 0 and re-run the naming routine. Reading a scroll (OVR 0x27466–0x274AC;
reader = current character DS:5D92, scroll = DS:6B80) clears `hidden` if `HasEffect(reader, 0x10)` (any scroll;
Lee: 0x10 = Read Magic) **or** (`reader+96h` > 0 signed **and** base slot = 12). Creature +96h is the cleric
level (s8, −1/0 = none; creature spec). In plain terms: a scroll identifies itself when read by someone under
Read Magic, and a cleric scroll also when read by anyone with a cleric level (druid, paladin and ranger levels do
not count). Detect Magic (effect 5) only adds the `*` marker; it never clears `hidden`.

## Base item table (`ITEMS`, 16 bytes per type)

Not in the program: it is the data file `ITEMS`. At start-up (OVR ≈0xEBA0) the program opens it, seeks to 2,
and block-reads 0x810 bytes into DS:5582 (BSS). 0x810 = 129 entries but the file only holds 2048 bytes = 128
entries, so DS:5D82 (a 129th slot) stays unfilled (Lee: "spurious additional ITEMS entry"). Header bytes 0–1
are never used. Lookups are `DS:5582 + 16·type + j`.

| j | Field | Meaning | Status |
|---|---|---|---|
| 0 | `slot` | 0 main hand, 1 off hand, 2 body armour, 3 gauntlets, 4 helm, 5 belt/necklace, 6 robe, 7 cloak, 8 boots, 9 ring, 10 misc/ammunition, 11 magic-user scroll, 12 cleric scroll; values ≥ 13 (74, 75, 77, 78, 80–83, 91) are item kinds that cannot be readied into a body slot | 12 verified (scroll code); 11/12 consistent with all scroll types; rest Lee/DrEvil |
| 1 | `hands` | hands needed (0/1/2) | verified vs Lee 9.2 |
| 2–4 | `dmg_large` | dice count, sides, signed bonus vs. large | verified vs Lee 9.2 |
| 5 | `rof_x2` | missile rate of fire in half-attacks per round | verified vs Lee 9.2 |
| 6 | `ac_code` | ≥ 178: body armour/bracers, AC = 10 − (code − 178); 128..177: other AC bonus = code − 128; < 128: none | verified vs Lee 9.2.2 |
| 7 | `weapon_kind` | 0 slashing, 1 piercing, 128 bashing (Lee notes several types look swapped) | Lee |
| 8 | `byte8` | 0x80 on most weapons, else 0. **No reader in the program** (v1.3, settled 2026-11-17 by an exhaustive search). Every access to the table uses a displacement 0x5582–0x5591; a byte search of START.EXE (unpacked) and GAME.OVR for all of them finds no 0x558A. The 0x5582 sites are the loader (OVR 0xEBA0), ten reads of byte 0 with index `type shl 4` and nothing added (OVR 0xB05B, 0xC09E, 0xC157, 0x225C4, 0x274A1, 0x2A272, 0x2A288; START 0xDAD, 0xE5E, 0x1822), and two copies of a whole entry to the stack (OVR 0xBE62 AI weapon scoring, 0x13D33 damage vs large) whose copy of byte 8 is never read | unused by v1.3 |
| 9–11 | `dmg_small_medium` | dice count, sides, signed bonus vs. small/medium | verified vs Lee 9.2 |
| 12 | `range` | missile/throw range, exclusive (Lee's long range + 1) | verified vs Lee 9.2 |
| 13 | `usable` | class bit mask: 1 magic-user, 2 cleric, 4 thief, 8 fighter, 16 druid, 32 monk, 64 paladin, 128 ranger | bit↔class verified: per-class bit array DS:05D6 (8 bytes) read against the class name array DS:0D38 + 27·k; the test `mask and bit ≠ 0` is DrEvil |
| 14 | `flags` | bit 0 shoots arrows, 1 dex missile bonus, 2 strength bonus to hit/damage, 3 AI treats as ranged, 4 thrown, 7 shoots bolts | Lee; consistent with every bow/crossbow/thrown type in the table |
| 15 | `byte15` | always 0 | — |

There is **no weight or price** in this table: weight and value live in each item record. For randomly generated
items, per-type weights and price multipliers are immediates in the generator code (OVR 0x25F0E–0x2620B).

Notes from the check: types 48, 49 and 97–126 are all-zero; type 127 is a test-like entry (2d20, AC code 182,
range 60) that no data file uses.

**Holy water** (type 85; verified by code read): the table gives 1d1−1. The special damage is the handler of
effect 117 ("vulnerable to holy water", Lee), OVR 0x1177B (handler table index 117): when it runs for a
defender that has effect 117 and the attacker (DS:5D92) has a type-85 item in its main-hand slot (creature
+0CCh), the weapon damage (DS:6818) is **replaced by 1d6 + 1** (2..7; Lee says 1d6). Lee puts 117 in trigger
class 5 (defender took weapon damage); the class lists themselves were not read `[?]`. Against creatures without
effect 117 the table damage applies; what 1d1−1 = 0 finally does after the other modifiers is not traced `[?]`.
In the data effect 117 is on 11 monster blocks (MON2SPC/MON4SPC; undead such as set 4 id 105). The combat AI
scores holy water at 8 against targets with a non-zero turn-undead index (OVR 0xBEC4); that is scoring, not damage.

## Random treasure (verified in code unless marked)

TREASURE with `k` = 0x81..0xFE (handler OVR 0x26F7; `ecl-format.md`) makes `k − 0x80` items. Per item: the
item-class roll below (OVR 0x28A9–0x2A13), then the generator 0x25C99 builds the record in a local buffer, and the
handler copies it to a new 63-byte heap record that becomes the head of the treasure list DS:6810. After the loop
the naming routine (START 0x0FE2; current character DS:5D92, all flags 0) runs over **the whole treasure list**,
items placed earlier included (OVR 0x2AAC–0x2AF1). The ITEM-block branch (`k` < 0x80) names nothing. The money
operands are set (not added) as 16-bit values with a zero high word (0x270B). Code: `src/GoldBox.Core/Game/`
`EncounterRules.RandomItemType`, `TreasureMaker`, `EncounterHost.Treasure`.

**Item-class roll** (OVR 0x28A9, one pass per item; `1dN` = the program's dice routine 0x2BCCD, one Random(N) + 1
per die):

| 1d100 | Base type chosen |
|---|---|
| 1–60 | second 1d100: 1–47 and 50–59 → that type (45 → 59, the shield); 60–90 → 1d10: 1–4 → 36, 5–7 → 35, 8 → 34, 9 → 37, 10 → 38; 91–94 → 73; 95–97 → 93; 98–100 → 77; 48–49 → nothing assigned (below) |
| 61–85 | 61 (magic-user scroll) |
| 86–92 | 62 (cleric scroll) |
| 93–98 | 1d16: 1–7 → 71, 8–9 → 70, 10 → 84, 11–12 → 78, 13–14 → 79, 15 → 92, 16 → 67 |
| 99–100 | 69 |

(91–92 of the class roll are already taken by the cleric-scroll range, as Lee notes.)

**Second roll 48 or 49.** The type is the handler's local byte [bp−0x65], and the only writes to it are the
branches of this roll (0x28EF–0x2A0F; checked by listing every access in 0x26F7–0x2AF6). So within one TREASURE a 48
or 49 gives **the previous item's type again** (and its generator rolls again: a new plus, new scroll spells …). For
the **first** item of a TREASURE the byte is never initialised: it holds whatever an earlier routine left in that
stack slot (the handler's 0x174-byte frame is not cleared; the dispatcher calls every opcode handler at the same
depth). Chance per item: 0.6 × 0.02 = 1.2 %.

**Sampled in DOSBox-X (s18, residue probe, `dosbox-trace-method.md` "Stack residue probe")**: the handler's BP is
always SS:3EE2, so the type byte is the absolute cell SS:3E7D. The probe logged it at the handler's entry and, with
the run-loop watch, every change of it between instructions:
- **Writers seen**: LOADPIECES (leaves 1), LOADMON (0), SETUPMON (0x68), COMBAT with its loot screen (0x0F), leaving
  the loot screen (0), CALL $2C90 (0), a step's program code (0 or 0x0C in two steps that also differed in what ran
  before them; consistent with a value that depends on the square), and TREASURE itself (its last item's type: two
  random TREASUREs in a row gave 19 = the Lucern hammer the first one made, and 34 = a bastard sword across a long
  PRINTCLR; the values seen across CLRMON and PICTURE, 0x3D / 0x49, are consistent with this but were not checked
  against the loot list). **Not writers**: CLRMON, PRINTCLR (empty or long text), PRINT, PICTURE, HMENU, ONGOTO,
  SAVE, COMPARE, GOSUB/RETURN.
- **The v1.3 sites** (the seven literal random TREASUREs: ECL2 block 20 $A390 and $B0FB, ECL6 block 25 $AAA4, ECL7
  block 23 $A3CF, ECL8 block 13 $AD3A, ECL8 block 27 $A39E and $A5EC) all run CLRMON (+ LOADMON) right before
  TREASURE, after the event's text. Both Slums sites, run from their real script code, read **0** every time: $A390's
  path from the load and after earlier loot screens (5 samples, 2 boots), $B0FB's path (SETUPMON, PRINTCLR, the
  "press Return" HMENU, CLRMON, LOADMON × 2) once; there the LOADMON writes 0, and ECL6 block 25 has a LOADMON in the
  same place. The wilderness sites (ECL6–8) were not run; with no LOADMON before them their value is whatever the
  step into the square left.
- So there is no single value: it is a path-dependent residue, 0 on every v1.3 path sampled. The engine keeps it as
  a setting (`EncounterHost.FirstItemTypeResidue`, default **0**, as sampled); type 0 makes no rolls and gives a blank
  item (type 0, `hidden` 6). A non-zero residue t gives a type-t item made by the generator (12 → a plussed weapon).
  `[?]` only for the unsampled wilderness sites and for modules.

**Per-type generator** (OVR 0x25C99, arguments: base type, item pointer). Order of work and of the dice:

1. Zero the 63 bytes (and again `props`, `next`, the three word slots), `equipped` 0, `hidden` := 6 (words 2 and
   3 hidden), `cursed` 0, `type` := argument, magic number n := 0.
2. **Types 1–59, 73, 77, 93** (the "plussed" items): `plus` from **1d20** (0x25C5B: 1–14 → +1, 15–19 → +2, 20 →
   +3). Then the words (`31`, `30`, `2F`; P = 0xA1 + plus, the "+1/+2/+3" words):

   | Type | `31` | `30` | `2F` | other |
   |---|---|---|---|---|
   | 21 (javelin) | 21 | P | 0 | a **1d5** right after the plus roll; 5 → n := 0x90 |
   | 28 | 28 | P | 0 | |
   | 50, 51 | type | 0x31 | P | `hidden` := 4 (only the plus word hidden) |
   | 52 | 52 | 0x32 | P | `hidden` := 4 |
   | 53–58 | type | 0x30 | P | `hidden` := 4 |
   | 73 | 0x3D | P | 0 | |
   | 77 (bracers) | 0x4F | 0xA7 | 0xDD / 0xDE / 0xDC for plus 4 / 6 / 8 | `plus` := 2·plus + 2 |
   | 93 | 0x42 | 0xE0 | P | |
   | any other (1–59 incl. 48, 49, 59) | type | P | 0 | |

   `save_bonus` 0, `count` 0, then the **weight** (word 37) by type, tested in this order (0x25F1E–0x26126):

   | Types | Weight | Count |
   |---|---|---|
   | 1, 13, 14, 35 | 75 | |
   | 2, 20, 29, 31, 32, 33, 39, 42, 44, 46, 59 | 50 | |
   | 3, 24, 40 | 125 | |
   | 4, 15, 23, 34, 43, 45, 51 | 100 | |
   | 5, 12, 17, 19, 50 | 150 | |
   | 6 | 15 | |
   | 7 | 30 | |
   | 8, 77 | 10 | |
   | 9 | 25 | 5 |
   | 10, 26, 36 | 60 | |
   | 11, 16, 25, 27, 41, 47 | 80 | |
   | 18 | 175 | |
   | 21 | 20 | |
   | 22, 30 | 40 | |
   | 37 | 35 | |
   | 38, 53 | 250 | |
   | 52 | 200 | |
   | 54, 56 | 400 | |
   | 55 | 300 | |
   | 57 | 350 | |
   | 58 | 450 | |
   | 93 (and 47, unreachable: 47 matched the 80 list first) | 1 | |
   | any other (28, 48, 49, 73) | 40 | 10 |

   Then the **value** (word 3A) = plus × multiplier, the 16-bit product of the sign-extended plus (for bracers the
   doubled plus; 0x2612E–0x26207): 59 → 2500; 73, 28 → 150; 53, 54 → 3000; 55, 56 → 3500; 57 → 4000; 58 → 5000;
   77 → 3000; every other type → 2000.
3. **Types 61/62** (scrolls, OVR 0x26219–0x26375): **1d3** spells; first word `31` 0xD1 (type 61) or 0xD0 (type
   62), second word `30` 0xD1 + spell count, `2F` 0, `plus` 1, weight 25, `count` 0, value 0. Each spell: **1d3**
   for the level, then a uniform id in the level's range: type 61 → 1d13+8 (9–21), 1d7+28 (29–35), 1d11+44
   (45–55); type 62 → 1d8 (1–8), 1d7+21 (22–28), 1d9+35 (36–44), so spell 56 never appears. Spell i (1-based)
   goes to `props[i − 1]`; value += 300 × level (16-bit).
4. **Other types** map to a **magic number** n (1, 9, 17 … 145 = 1 + 8k; second-stage rolls in OVR
   0x26378–0x26494): 63 → 73; 67 → 145; 69 → **1d10**: 1 → 41, 2–6 → 121, 7–10 → 129; 78 → **1d3** + 1: 2 → 65,
   3 → 113, 4 → none (so n = 49 is unreachable); 79 → 57; 84 → 9; 92 → 97; 71 → **1d11**: 1–5 → 17, 6–8 → 1, 9 →
   25, 10 → 81, 11 → none; 70 → **1d10**: 1–7 → 89, 8–10 → 137. n = 33 and n = 105 are never produced, and n = 73
   only through the type-63 path, which the class roll never picks (so gauntlets are never random, as Lee says).
   Any other type (0, 60, 64–66, 68, 72, 74–76, 80–83, 85–91, 94+) makes no roll and nothing is set: the record
   keeps only its type and `hidden` 6 (a blank item).
5. If n ≠ 0 (types of step 4, or the javelin's 0x90), copy from the random item array (0x2649D–0x26561): bytes
   `2F`, `30`, `31` = low bytes of words n, n+1, n+2; `plus` := 1 and `save_bonus` := 1 (both 2 when n = 97);
   weight = word n+3; `count` := 0; value = word n+4; `props` 3C–3E = low bytes of words n+5..n+7. This overwrites
   what step 2 set for a javelin.

**DOSBox (s11, `treasure_shop` captures)**: two seeded runs of `TREASURE …, 0x88` (8 random items each, from a
patched save) match `TreasureMaker` byte for byte from 0x2E to 0x3E, item for item, and the RandSeed after the
eight items equals the program's (`TreasureShopCaptureTests`): **D** for the class roll, the generator and the draw
order (weapons, a ring, both scroll kinds, a type-93 item; neither run hit a second roll of 48/49).

So the draws per item are: the class roll (1–3 dice), then 1d20 for a plussed item (and 1d5 for a javelin), or
1d3 + 2 per spell for a scroll, or one sub-roll for types 69, 78, 71, 70. A replay must keep this order: every die
is one Random call on the shared generator (RandSeed).

- Type 21 (javelin) with 0x90: 0x90 is not of the form 1 + 8k, so the copy reads 8 words starting one word
  *before* the 19th entry: `2F` gets the last word of entry 18, the remaining fields get entry 19 shifted by one
  word. Reproduce, don't fix (parity).

**Random item array** (DS:0972, 19 entries × 8 words; code indexes `DS:0970 + 2·(n + field)`, copy at OVR
0x264A6):

| Word | Goes to item byte(s) |
|---|---|
| 0 | `2F` (low byte) |
| 1 | `30` |
| 2 | `31` |
| 3 | `weight` (37–38) |
| 4 | `value` (3A–3B) |
| 5–7 | `props` 3C, 3D, 3E (low bytes) |

The copy also sets `plus` := 1 and `save_bonus` := 1 (both := 2 for n = 97) and `count` := 0.

## Effect record (9 bytes)

Same on disk (`.SPC`, `MON?SPC.DAX` blocks) and in memory (linked list hanging off the creature).

| Off | Size | Field | Meaning | Status |
|---|---|---|---|---|
| 0 | 1 | `id` | effect id 1..127 (Lee 12.2.3 lists them all, with the trigger classes). Each id indexes the handler table DS:6828 (far pointers; dispatcher OVR 0x2AEEA; ids 5, 13, 19, 24, 27, 60, 63, 92, 127 share an empty handler, OVR 0x11DF6). Verified in code: 5 (party-wide test in the naming routine), 0x10 (scroll identify), 23 (spiritual hammer item), 38 (gauntlets), 117 (holy water). | layout verified; most meanings Lee |
| 1 | 2 | `minutes` | remaining duration in game minutes, 0 = permanent (Lee; exception for id 75) | data consistent |
| 3 | 1 | `param` | 255 = nothing special (and immune to dispel); else low nibble usually caster level, high nibble per-effect flags; Strength effects store a Strength code (Lee) | data consistent |
| 4 | 1 | `remove_calls_handler` | boolean: removing the effect first calls its handler with "remove" | verified (remove routine OVR 0x2AF10; add routine OVR 0x2BD3C writes it); 0 in every record on disk |
| 5 | 4 | `next` | far pointer, stale on disk | verified |

Data check: 79 records; ids 1–127 except one record with id 0 (`MON4SPC.DAX` block 105, the first of its 10
records; `[?]` placeholder). Monster effects are all permanent with `param` 255; the save-slot-J characters carry effect 1 with
2 minutes left and `param` 1 (consistent with Lee: a level-1 casting).

## Where the tables are found (how `gbitem.py` does it)

All found by code patterns, not fixed addresses (works on v1.1, v1.2 and v1.3):
- START.EXE: if the MZ entry is an EXEPACK stub (`RB` signature), expand it (documented Microsoft scheme; own
  implementation). DS = immediate of `mov dx,imm / mov ds,dx` at the start of the routine the entry far-calls
  (Turbo Pascal System init).
- Name words: `mov dx,21 / mul dx / mov di,ax / add di,imm` → word w at DS:imm + 21·w (v1.3: imm = 0x10A7).
- Spell names: same with 41 (v1.3: 0x28E9, so id 1 at DS:2912); 56 consecutive names.
- Random item array: in GAME.OVR, `mov di,ax / shl di,1 / mov dl,[di+imm]` → imm (v1.3: 0x0970).
- Base table: the `ITEMS` file next to the program.

## JSON (`gbitem.py … --json`)

- `items`: `{file, program{…locator results…}, blocks:[{block, bytes, trailing_bytes, records:[item]}]}`; `block`
  is the DAX id or `null` for `.ITM`. `item` = `{index, type, words{2f,30,31}, word_text, plus, save_bonus,
  equipped, hidden_mask, cursed, weight, count, value, props[3], stored_name, name, name_raw, name_identified,
  detect_magic_marker, base{…}, scroll_spells[{spell, scribe_flag, name}] | props_hint, byte29, next_ptr}`.
  `props_hint` follows "props" above (`3E` decides): `{kind: "usable", charges, unlimited, power,
  power_spell_id, power_spell_name?}` (`power_spell_id` = `3D` if ≤ 56, else `3D` − 23), `{kind: "worn", handler,
  action, effect_id? | alignment?, damage?}` (`action`: `add_effect`, `strength_18_00`, `alignment_check`,
  `needs_strength_19`, `remove_effect_23`, or `unknown` for codes not in the table) or `{kind: "none"}`.
- `effects`: same block wrapper, records `{index, id, minutes, param, param_lo, param_hi, remove_calls_handler,
  next_ptr}`.
- `basetable`: `{program, types:[{type, raw, slot, slot_name, hands, damage_large[n,s,b], rate_of_fire_x2,
  ac_code, ac{kind,bonus}|null, weapon_kind, byte8, damage_small_medium[n,s,b], range, usable_mask, usable_by[],
  weapon_flags, weapon_flag_names[], byte15}]}`.
- `random`: `{program, entries:[{magic, ds_offset, word_2f, word_30, word_31, weight, value, props[3], name}]}`.
- `words`: the word and spell arrays as read from the player's program (local use only; not for publishing).

## Verification (2026-11-17)

- Decoded every item record in every v1.3 file (12 `.ITM`, 8 `MON?ITM.DAX`, 8 `ITEM?.DAX`: 681 records):
  all types < 128, every non-zero word id resolves, no empty names, no trailing bytes.
- Name composition checked against the game-written `.ITM` names (27/28 exact, 1 stale count). That data only
  exercises single- and two-word names, the count prefix, the single-word plural and the readied prefix; the
  ammunition, type-0x56 and `k = 1` plural branches are straight ports of the disassembly with no test case.
- Base table vs Lee 9.2: hands, both damage columns, range and rate of fire match for types 1, 12, 18, 36, 38,
  39, 41–47, 9, 21 (Lee's thrown/long range = our range − 1, rate of fire = our value / 2).
- Charges vs Lee 9.3/9.5: wand charges 67 (monster 94), 20, 50, 35 and 23 match the decoded `props[0]`.
- Random generator: Lee 9.4 probabilities match the disassembly (class roll, sword roll, plus roll, bracers
  AC, ring/wand/potion/misc sub-rolls, the dead branches).
- Effects: 79 records in 10 `.SPC` + 6 `MON?SPC.DAX`, no trailing bytes.
- Locators re-run on v1.1 and v1.2 START.EXE/GAME.OVR: all tables found (at different DS offsets); the 19
  random-array entries are identical in v1.1, v1.2 and v1.3 (JSON diff without offsets).

## Open `[?]`

- Whether the Use menu offers the Flame Tongue (props 0, 22, 0) as a usable item.
- Holy water: trigger-class lists not read; final damage of 1d1−1 against ordinary targets.
- `byte29`; `weapon_kind` oddities.
- Effect id 0 record in `MON4SPC.DAX` block 105.
- The type of a random TREASURE's first item when its second roll is 48/49 at the unsampled wilderness sites (stack
  residue, path-dependent; 0 at both Slums sites in DOSBox-X, s18; see "Random treasure").

Settled 2026-11-17 by code read: base `byte8` is never read; consumable vs worn (`3E`) and the handler codes; `save_bonus`; scroll
self-identification (+96h = cleric level); holy water 1d6 + 1 against effect-117 creatures; the random item maker 0x25C99 in full (weights, value
multipliers, draw order, naming pass).
