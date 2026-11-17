# Survey: Unlimited Adventures (FRUA) v1.2, DOS — formats and scripting

Status: **survey** (2026-11-17). Purpose: learn what the last Gold Box engine needs before we write
`docs/module-format.md` (PLAN section 2) and the FRUA importer (phase 7). Formats and the event model only; no
rules work. **Data layouts** (sizes, chunks, headers, records, id tables, the dispatch table, routine entry points
and strings) are checked against the v1.2 files and `CKIT.EXE`. **Engine behaviour** (condition meanings, run loop,
triggers, packing methods, text folding) is read from the talonius reconstruction at the cited EXE offsets and was
not run under DOSBox-X; it is marked "(reconstruction)" where it appears. Anything else unconfirmed is `[?]`.
Offsets inside data files are **0-based** (the UA Shell HackDocs number bytes from 1).

## Sources

- **Install:** `reverse-engineering/dos/original-images/frua/v1.2_3.5in-720K+1.44M/` (PGPP, clean). The three disks
  hold PKZIP self-extractors (`FRUADST1..3.EXE`, opened with 7z); together they unpack to the install tree below.
  v1.0 (`v1.0_1993_3.5in-720K+1.44M/`) was unpacked too for a diff.
- **Program:** `CKIT.EXE` v1.2, 587,843 bytes, sha256
  `eb64aa5e44b6afffcc634a3f39dea639386412e6bb50c1b1ddf06a1583237550` (the same hash as the talonius target). Offsets below are file offsets in this EXE.
- **Designs checked:** the shipped `HEIRS.DSN` (26 areas) and `TUTORIAL.DSN`; two community designs from the UA
  file archive: `3fightsandabeer.zip` and `abyss.zip`
  (http://frua.rosedragon.org/pc/modules/0-9/3fightsandabeer.zip, http://frua.rosedragon.org/pc/modules/a/abyss.zip).
  41 GEO files in all.
- **UA Shell HackDocs** (simoN, 2003; `scratch-priorart/frua/dl/hackdocs/`): `UAFILES`, `GEODATA`, `GEOGRIDS`,
  `GEOEVENT`, `GEOENTRY`, `SCRIPT`, `TLBFORM`, `STRGFORM`, `GAMEDAT`, `ITEM*`, `VAULT`, `CCHFORM`, `MONSTDAT`,
  `UAIMPORT`, `UAPALETT`, `UASOUND`. Community notes, no licence; restated here, checked against files and code.
- **talonius/forgotten-realms-unlimited-adventures** (Turbo C reconstruction of `CKIT.EXE`, no licence grant;
  reference only, nothing copied) and **talonius/dark-queen-of-krynn** (same, for DQK; read through `gh api`).
  Used to find routines; every routine cited below was located in the shipped EXE by offset.
- `UAHELP.TXT` (the shipped designer help) for editor limits.

Throwaway readers (IFF chunks, HLIB libraries, event tallies, 6-bit text) lived in the session scratch folder;
nothing went into `tools/`.

## Summary

- **No ECL.** FRUA runs **fixed 20-byte event records** (38 types), each with a common 4-byte head (type, flags +
  condition kind, condition value, chain target). One `switch` over the type byte in `CKIT.EXE` (0x30D29, jump
  table 0x30D3A) dispatches them. There is no bytecode interpreter in the program and no bytecode in the data.
- **Lineage.** Dark Queen of Krynn, the previous MicroMagic engine, still has an ECL VM (66 opcodes, talonius
  `eclrun.c`/`eclcmds.c`). FRUA removed the VM and kept many of DQK's command handlers as event handlers. Each FRUA
  event is a fixed "macro" of what an ECL script would spell out with several opcodes.
- **Override model.** For data the engine first looks for a loose file `<NAME><nnn>.DAT` in the design folder, else
  item *nnn* of `<NAME>.GLB` in the install. Art works the same way: a loose `<4 chars><bank><nnn>.TLB` in the design
  folder, else the master `.TLB` library. A design is the set of overrides on top of the engine's defaults.
- **Design = folder `NAME.DSN`** with `GAME001.DAT` (388 bytes), up to 40 `GEOnnn.DAT` areas (12,962 bytes each,
  IFF-like chunks: header, 576-cell map, 100 events, text pool), `MONSTnnn.DAT` (450 bytes), `STRGnnn.DAT` label
  tables, art `.TLB` files and `SAVE\` (characters `.CCH`, saves `SAVGAMx.CSV`, vaults).
- **State is tiny:** 64 one-byte variables (8 keys, 12 special items, 44 quests) and one "done" bit per event slot
  per area. Everything else a PoR script keeps in VM memory does not exist.

## 1. Install inventory (v1.2)

`START.BAT` runs `CKIT.EXE` with sound arguments. The installer copies the three archives' contents into one tree:

| Path | Files | Content | Container |
|---|---|---|---|
| root | `CKIT.EXE` | Game **and** editor in one program | MZ + RTLink/Plus overlays (section 8) |
| root | `UAHELP.TXT`, `START.BAT`, `README.BAT` | Designer help, launcher | text |
| `ART\` | `DRAW*.LBM/.PCX`, `TOWN.*` | Templates for drawing art to import (pictures, big pictures, combat icons, sprites) | PCX / IFF ILBM |
| `DISK1\` | `ALWAYS.TLB` | 26 small UI glyphs (pointer, map dots, arrows) | TLB |
| | `TITLE.TLB` | Title screens (6 sub-libraries) | master TLB |
| | `GAME.FON` | Font (1,152 bytes) | raw `[?]` |
| | `ITEM.DAT` | 254 item templates × 18 bytes (4,572) | flat table |
| | `ITEMS.DAT` | 128 base item types × 16 bytes (2,048) | flat table |
| | `SOUNDS.GLB` | 13 digital sounds | HLIB, signature `DIG4` |
| | `SFXDQ.VOC` | Sound effects (13 marked blocks) | Creative VOC |
| | `*DQ1..3.XMI`, `*.ADV`, `INSTR.AD` | Music (AdLib, Roland, PC speaker, Tandy) and Miles drivers | XMIDI |
| `DISK2\` | `8X8DB.TLB`, `8X8DC.TLB` | Wall sets (9 + 7 sets × 47 tiles) | master TLB |
| | `BACK.TLB` | 19 backdrops (sky/ground, 88×88) | master TLB |
| | `PICA..PICF.TLB` | 155 encounter pictures, ids 2–231 | master TLB |
| | `BIGPIC.TLB`, `BIGPIX.TLB` | Big pictures and overland maps, ids 240–252 (304×120) | master TLB |
| | `SPRIT.TLB` | 38 sprite sets (approach frames) | master TLB |
| | `CPIC.TLB` | Monster combat icons, 254 frames (id n and n+128 = the two poses) | TLB with id table |
| | `DUNGCOM.TLB`, `WILDCOM.TLB` | Combat terrain (5 dungeon, 4 wilderness sets of 24×24 tiles) | master TLB |
| | `COMSPR.TLB` | Combat missiles/effects | TLB |
| | `TOPVIEW.TLB` | Area-map tiles (8×8) | TLB |
| | `MONST.GLB` | 127 default monster records | HLIB `DATA`, PackBits items |
| `DISK3\` | `CBODY.TLB` | Player combat icons | TLB |
| | `FRAME.TLB`, `GEN.TLB`, `MENU.TLB` | Screen frames, menu bar art | TLB |
| | `GEO.GLB` | Blank area templates (see 2.1) | HLIB `DATA` |
| | `STRG.GLB` | Default label tables 1–5 | HLIB `DATA` |
| | `GAME.GLB` | Default `GAME001.DAT` | HLIB `DATA` |
| | `SCRIPT.GLB` | 58 editor form layouts (one per event type, plus area, game, monster, NPC forms) | HLIB `DATA` |
| `HEIRS.DSN\`, `TUTORIAL.DSN\` | | Shipped designs (section 2) | |

- Everything the **engine** supplies is under `DISK1..3`. A design ships only its own overrides.
- **Compression.** The HLIB reader supports five packing methods for a whole library (PackBits, two Huffman
  variants, LZW, LZ; reconstruction, `library.c`, EXE 0x016BE3–0x017BCA). Every library in the install and in both
  community designs has method 0 (stored). `MONST.GLB` items are PackBits-packed one by one (section 6). TLB images
  use their own RLE drawing methods (section 5).
- **v1.0 → v1.2:** same file set and sizes. Changed: `CKIT.EXE` (587,318 → 587,843), `MONST.GLB`, `SCRIPT.GLB`,
  `STRG.GLB`, seven Heirs areas, the art templates. The GEO version word is the same (below). No format change was
  seen.

### HLIB container (`.GLB` and `.TLB`)

16-byte header: tag `HLIB`, u32 total size, u16 item count, u8 packing method, u8 "has id table" flag, 4-char
signature (`DATA`, `TILE`, `DIG4`, or `HLIB` for a master library of sub-libraries). Then count+1 u32 offsets from
the library start (the last = end). When the flag is set, item 0 is an id table: u16 n, then n pairs (id, item
index). Lookup by id goes through that table (talonius `library.c` `lib_find`). All little-endian. Checked on every
library in the install. 9 of 225 community TLBs have a size field that differs from the file length `[?]`.

## 2. Design (`.DSN` folder) layout

`START.DAT` in the install root names the current design folder. A new design gets `NAME.DSN\` and `NAME.DSN\SAVE\`.

| File | Size | Holds | Default when absent |
|---|---|---|---|
| `GAME001.DAT` | 388 | Design title (32), start XP, platinum, gems, jewellery (u32 each), start area, start entry point, start equipment class, 8 key names and 12 special-item names (16 bytes each), a password (15) | `GAME.GLB` id 1 |
| `GEOnnn.DAT` | 12,962 | One area, nnn = 001–040: 001–004 overland, 005–040 dungeon (section 3) | `GEO.GLB`: id 1–4 and 41 → blank overland, 5–40 and 42 → blank 16×16 dungeon, 43 → a third template `[?]` |
| `MONSTnnn.DAT` | 450 | One edited monster or NPC (section 6) | `MONST.GLB` item nnn |
| `STRG001.DAT` | 3,668 | Label table 1: 261 art/monster slots (names, kind bits) | `STRG.GLB` id 1 |
| `STRG002.DAT` | 1,120 | Label table 2: dungeon/wilderness combat art, backdrops, walls, event names; written only when one is renamed | `STRG.GLB` id 2 |
| `STRG003.DAT` | 574 | Label table 3: the 40 area names (editor only) | `STRG.GLB` id 3 |
| `<art>.TLB` | varies | Imported art, one slot per file (section 5) | master libraries in `DISK2/3` |
| `SAVE\*.CCH` | 398 + 18·items + 10·effects | Characters (a design may ship premade ones) | – |
| `SAVE\SAVGAMx.CSV` | 10,285 | Save game, x = A–J | – |
| `SAVE\VAULTx.DAT` | 916 | The vault of save x | – |

- **Loader (verified in the EXE):** `data_load` at 0x2C126 builds `<NAME><nnn>.dat` (format string at 0x8AAEB),
  tries it in the design folder, and on failure reads id nnn from `<NAME>.glb` (0x8AAF6). Names used: `GEO`, `MONST`,
  `STRG`, `GAME` (talonius `glbdata.c`, `view3d.c`, `monedit.c`, `labels.c`). `ITEM.DAT`/`ITEMS.DAT` are read
  from `DISK1` only, never from the design: **a design cannot change items** without a hack.
- **Label tables (`STRGnnn`):** a 14-byte head record whose first u16 is the record count, then 14-byte records:
  slot number, kind bits, 12 bytes of 6-bit packed name (15 characters). Kind bits in table 1: 1 picture, 2 sprite,
  4 monster combat icon, 8 NPC combat icon, 16 big picture, 32 overland map, 0x40 monster edited (never set in
  `STRG.GLB`; in Heirs it marks the four slots that have a `MONST` file, plus slot 84, which has none `[?]`); 0x80
  "art imported" per the HackDocs, not seen `[?]`. A slot with a combat icon is a monster: slot 42 means
  `MONST042.DAT`, `CPIC1042.TLB`, `SPRI0042.TLB`. Heirs' table holds the same records as `STRG.GLB` apart from those
  flags, renamed labels and a different order after record 124.
- **Community designs agree:** all GEO files 12,962 bytes with the same chunk layout and version word, `MONST` 450,
  `STRG001` 3,668, `STRG003` 574, `GAME001` 388, `.CCH` sizes fit 398 + 18i + 10e. File-name case varies
  (`8x8d1009.tlb`, `Back1032.TLB`); one zip has the `.DSN` contents at its root, the other nests a folder. `abyss`
  also asks the player to replace the engine's `DISK1\SFXDQ.VOC` (a "hack", outside the design).

### 2.1 How a design refers to engine data

Only by **number**: area 1–40, monster/art slot 1–255, picture id, wall set id, backdrop id, combat art id, item
template 1–254, sound 1–13. There are no file names inside the records. Whether a number resolves to the engine's
default or to the design's copy is decided by the loose-file-first rule above.

## 3. Area file (`GEOnnn.DAT`)

Tagged chunks with **big-endian** sizes: `FORM` (size), `AMOD` (size), then `HDR ` (290), `MAP ` (3,456), `ENCR`
(2,000), `STRG` (7,168), each padded to even length. This is IFF-like, not standard IFF (the `AMOD` tag carries its
own size). Writer: talonius `areafile.c` `save_3d_map`; the file is always 0x32A2 bytes. Chunk data offsets: HDR 24,
MAP 322, ENCR 3,786, STRG 5,794. Byte order: only the HDR version word and the 8 zone-text words are big-endian (the
writer swaps them); every other multi-byte field, in the header and in event records (text slots, amounts,
donation value), is little-endian.

### HDR (file offsets)

| Off | Size | Field |
|---|---|---|
| 24 | 2 | Version, big-endian; 0x006A in all 41 files (v1.0 and v1.2) |
| 26, 27 | 1+1 | Rows (height), columns (width). Dungeon: each 11–50, rows × cols ≤ 576. Overland: always 15 × 38 |
| 28–30 | 3 | Wall-set ids for wall slots 1–3 (255 in overland) |
| 31 | 1 | Area view (overhead map) allowed |
| 32–35 | 4 | Dungeon: backdrop ids 1–4. Overland: byte 32 = big-picture id of the map (240+), rest 255 |
| 36, 37 | 2 | Dungeon / wilderness combat art id |
| 38–69 | 8 × 4 | Entry points: x, y, facing (0/2/4/6), pad |
| 70–101 | 8 × 4 | Rest encounters, one per zone: minutes (u16), event slot, chance % (+128 = no resting in the zone) |
| 102–133 | 8 × 4 | Step events: every n steps (u16), event slot, zone-exclusion bits (bit z = not in zone z+1) |
| 134–141 | 8 | Unused text `[?]` |
| 142–157 | 16 | Area name (editor only) |
| 158–285 | 8 × 16 | Zone names (editor only) |
| 286, 287 | 2 | Monster for the summoning spell; undead-turning modifier |
| 288–295 | 8 | Overland: wilderness combat art per zone |
| 296–311 | 8 × 2 | Overland: text slot per zone (big-endian words) |
| 312, 313 | 2 | Camp picture, treasure picture |

### MAP: 576 cells × 6 bytes, row-major over the area's width

| Byte | Content |
|---|---|
| 0–3 | N, E, S, W face. Low nibble = wall graphic 1–15 (0 = none); high nibble = door/passage kind: 0 open, 1 secret, 2 locked, 3 locked secret, 4 wizard-locked, 5 wizard-locked secret, 6–13 needs key 1–8, 14 blocked, 15 false door |
| 4 | Event slot 1–100 run on entering the square (0 = none) |
| 5 | Bits 0–1 backdrop 1–4, bits 2–4 zone 1–8; bits 5–7 unused in all 41 files |

Cells past rows × cols are zero in every file checked. In overland areas the wall bytes hold only 0 or 0xF0 (an
impassable edge `[?]`), and the zone gives the terrain (combat art, zone text).

### ENCR: 100 event records × 20 bytes (section 4)

### STRG: the area's text pool

A 6-byte head (capacity 6,762, a word 0xFFFF, bytes used), 400 length bytes (one per text slot, length in packed
bytes, 0 = free), then the packed text in slot order. Events name text by slot (u16). Section 7 has the encoding.

## 4. Scripting: event records, not ECL

### 4.1 Where the dispatcher is

- `events_run` starts at 0x30C9D. It reads the record, tests its condition, marks it done, then does
  `type − 1; if > 37 → default; jump table[type−1]` (0x30D28–0x30D35, table of 38 words at 0x30D3A). Types 28, 30
  and 31 jump to the default exit: they do nothing. Types 10 and 21 share one handler. Checked in the EXE.
- No ECL interpreter: the FRUA reconstruction has no `ecl*` module or symbol, and the design files hold no bytecode.
  DQK's `eclrun.c` is an ECL interpreter (66 opcodes) run from entry points around each move. FRUA's `evcmds.c` (EXE
  0x041090–0x04480E) is described by its author as derived from DQK's ECL command module (menus, damage, monster
  loading, NPC join, who, treasure …). So the engine's lineage is ECL VM → typed event records.

### 4.2 Record head (bytes 0–3, all types)

| Byte | Content |
|---|---|
| 0 | Event type 1–38 (0 = empty slot) |
| 1 | bit 0: run once (skip if its done bit is set). bit 1: chain only if the event ran. bit 2: chain only if it did not. bits 3–7: condition kind |
| 2 | Condition value |
| 3 | Chain: event slot to run next (0 = none) |

Condition kinds (byte 1 >> 3), from `event_test` (0x30781; reconstruction, `mapevent.c`). The kind values match the
HackDocs and the editor forms; their tests are not checked under emulation:

| Kind | Runs when | Value |
|---|---|---|
| 0 | always | – |
| 1 / 2 | variable v ≠ 0 / = 0 ("party has / lacks key, item or quest") | v 0–63 |
| 3 / 4 | daytime (state byte 8 in 6–19, the hour `[?]`) / night | – |
| 5 | random 0–99 ≤ v | v |
| 6 / 7 | party is / is not searching | – |
| 8 | facing in the mask (always true outside the 3D view) | bits N, E, S, W |
| 9 / 10 / 11 | quest v complete (255) / failed (128) / in progress (other non-zero) | v |
| 12 / 13 | someone is / nobody is detecting traps (effect 0x13) | – |
| 14 / 15 | someone can / nobody can see invisible (effect 0x18) | – |
| 16 | a member of class v (multi-class counts) | class |
| 17 | a member of race v | race |

Two types add their own gate: quest stage (35) runs only when the quest variable equals its byte 9; special item
(38) only when the variable's state matches its byte-7 bit 2.

### 4.3 Run loop and triggers (reconstruction)

- **Triggers:** the square's event slot on entering it (`move.c`); the 8 step events (counter per slot, zone mask);
  the 8 rest encounters during camp (`rest.c`); event fields that name another slot (branches below); a save made
  inside a town or service event resumes that event on load (state bytes 0x85 and 0x31).
- **Chaining:** after a handler, the loop runs byte 3's slot unless the handler set its own next slot, the chain flag
  forbids it, or the party moved. Branching types pick the next slot from their own fields (menu answer, success /
  failure, fight / talk / escape, tavern fight, temple donation, yes / no).
- **Return stack:** a yes/no answer or menu button can push the current slot ("return to this question"); when a
  chain ends without movement the loop pops and reruns it. "Back up one step" restores the previous square.
- **Done bits:** one bit per (area, slot): bit index area × 100 + slot − 1, from state + 0x20D (save-file offset
  0x20C). The area number is 1-based (a transfer stores the destination area 1–40 directly), so bits 0–99 are
  unused and area 40's bits land at state 0x401–0x40D, past the 1,024 bytes a save writes (state + 1 … + 0x400).
  Whether area 40's run-once events reset on load is not checked `[?]`.
- **Variables:** 64 bytes at state + 0x45 (save-file offset 0x44): 0–7 keys, 8–19 special items, 20–63 quests (0 none,
  255 complete, 128 failed, other = stage). These are the only design-visible variables.

### 4.4 Event types

Short names are ours. Fields in bytes 4–19; "pic" = picture id at byte 6 (≥ 240 big picture) with art kind in byte
7 bit 7 (picture / big picture / none vs sprite). Text fields are u16 slots in the area's pool. Detail per byte:
`GEOEVENT.TXT` and the editor forms in `SCRIPT.GLB` (entry n = type n). Counts are records in the 26 Heirs areas
(1,040 events, 309 chained).

| # | Ours | Carries | Heirs | Nearest PoR ECL |
|---|---|---|---|---|
| 1 | COMBAT | text, pic, morale, 6 groups (count + monster slot), surprise, indoors/outdoors, distance, attack side, flags (no treasure, party can't die, no magic, auto-approach), turn-undead difficulty, friendly groups | 117 | CLRMON, LOADMON ×n, SETUPMON, COMBAT |
| 2 | TEXT | up to 5 text slots with "press return" and highlight bits, pic, distance, sound, back-up step | 443 | PICTURE, PRINT/PRINTCLR |
| 3 | TREASURE | platinum, gems, jewellery, 8 item templates, identified flag | 21 | TREASURE |
| 4 | DAMAGE | text, pic, target (all / active / random / % each), save type and bonus or THAC0 hit, attacks, dice | 13 | DAMAGE |
| 5 | STAIRS | ask text, yes/no transfer, destination x/y or entry point, facing, run destination event | 36 | SAVE into the position cells |
| 6 | TRAIN | allowed classes, cost factor | 11 | PROGRAM 0 |
| 7 | TAVERN | 4 tale texts, drink names, fight chain, drunk chain | 4 | (scripted in PoR) |
| 8 | SHOP | cost factor, 4 item-class sets | 15 | COMBAT with shop flag |
| 9 | TEMPLE | top spell level, cost factor, donation value and chain | 12 | COMBAT with temple flag |
| 10 | BUTTONS | prompt, up to 5 buttons each with chain, "return here" and back-up bits | 6 | HMENU + ONGOTO |
| 11 | TRANSFER | ask text, enter-cell vs in-cell, yes/no, destination area, x/y or entry, facing, run destination event | 71 | NEWECL / LOADFILES |
| 12 | TOUR | start, facing, up to 24 scripted moves (2 bits each), two texts at chosen steps | 3 | (scripted) |
| 13 | ADDNPC | NPC slot, % of max hit points, pic | 5 | ADDNPC |
| 14 | NPCSAYS | like TEXT, only if the NPC is in the party | 3 | IF + PRINT |
| 15 | ENCOUNTER | text, pic, monster speed, 5 buttons (approach, retreat, fight, wait, talk) each with an outcome, range-0 outcome; combat / talk / escape chains | 1 | ENCMENU |
| 16 | UTILITY | store / add / subtract a value into a variable; all-of / any-of test over 6 variables into a result variable; end the game | 11 | SAVE / ADD / SUB / AND / OR |
| 17 | SOUNDS | up to 10 sound ids | 3 | – |
| 18 | WHOTRIES | ability or thief-skill check (vs die or minimum), tries; success / failure: nothing, chain, teleport or back up | 2 | WHO + COMPARE + IF |
| 19 | WHOPAYS | gems / jewellery / platinum amount; outcomes as 18 | 0 | (scripted) |
| 20 | PASSWORD | password text slot, tries; outcomes as 18 | 0 | INPUTSTR + COMPARE |
| 21 | LISTMENU | as 10 with a vertical list | 0 | VMENU + ONGOTO |
| 22 | TOWN | which services exist (temple, training, shop, inn, tavern, vault), classes, costs, items, a tale | 3 | (scripted hub) |
| 23 | CHAIN | next slot in byte 8 | 7 | GOTO |
| 24 | VAULT | text, pic | 6 | – |
| 25 | COMBATLOOT | as 3, given after the next fight | 32 | TREASURE before COMBAT |
| 26 | XP | amount, whole party / active member, sound | 7 | (SAVE into XP) |
| 27 | PASSTIME | days, hours, minutes | 1 | CLOCK |
| 29 | CAMP | text, pic | 0 | PROGRAM 9 |
| 32 | REMOVENPC | NPC slot | 6 | LOADCHAR with 0x80 |
| 33 | PICKCOMBAT | as 1, one group picked at random `[?]` | 58 | RANDOM + ONGOSUB + LOADMON |
| 34 | TELEPORT | as 11 (5, 11 and 34 share one handler) | 80 | SAVE into the position cells |
| 35 | QUESTSTAGE | quest variable, stage gate, new value, chains | 21 | COMPARE + SAVE |
| 36 | YESNO | question; yes / no chains, "return here" / back-up bits | 4 | HMENU + IF |
| 37 | TALES | tavern tales | 5 | – |
| 38 | SPECIALITEM | give / take a key or special item (variable 0–19) | 33 | SAVE to a flag |

Types 28, 30, 31: no handler (the hackdocs suggest DQK leftovers such as a cast-spell or death event `[?]`).

### 4.5 Compared with PoR's ECL

- PoR (`ecl-format.md`): one bytecode script per area, five entry vectors (move, search, pre-camp, camp interrupted,
  init), 62 opcodes, arithmetic and compare on any VM address, GOSUB/ONGOTO, per-script state in a 7,680-byte
  area plus a large flag space. The engine knows nothing about events.
- FRUA: no per-area code. Entry points are fixed by the engine (square, step, rest), control flow is a single
  next-slot pointer plus per-type branches and a small return stack, conditions are one of 18 tests, and state is
  64 bytes + done bits. Doors, locks, keys, secret doors and searching are **map data** (wall high nibble), not
  script. Shops, temples, training, taverns, vaults and towns are engine services with parameters.
- Anything FRUA can express maps onto a small ECL program, but not the other way round: PoR scripts compute
  (ADD/MUL on arbitrary cells), read and write character fields, use GOSUB, tables and the event byte's bit
  conventions.

## 5. Maps compared with PoR

| | PoR | FRUA |
|---|---|---|
| Grid | 16 × 16 fixed, 1,026-byte block | rows × cols ≤ 576 (11–50 each); overland 38 × 15 |
| Walls | 4-bit graphic per face (planes 0/1) | same 4-bit graphic, 3 wall slots × 5 (as PoR's `(t−1) div 5`) `[?]` |
| Passability | separate barrier plane (2 bits per face) | high nibble of the wall byte: open, secret, locked, wizard-locked, key 1–8, blocked, false door |
| Event | 1 byte per square, meaning set by the script | event slot 1–100 run on entry |
| Other per-square | – | backdrop 1–4, zone 1–8 |
| Area data | in the ECL script | header: entry points, wall/backdrop/combat art, rest and step events, zones |
| Overland | separate wilderness format | a big picture with an 8-pixel grid over it (304 × 120 = 38 × 15 cells), zones as terrain |

## 6. Records

| Record | FRUA | PoR | Notes |
|---|---|---|---|
| Character | 398 (0x18E) + 18 per item + 10 per effect, one `.CCH` file | 285 + `.ITM` 63/item + `.SPC` 9/effect | FRUA record is the in-memory struct with stale far pointers at the front (next, effects, items, 13 equipped slots, fight) |
| Monster / NPC | 450 = 398 character record + a 52-byte tail: 16 item template ids, 16 charge ids, 10 effect ids, 10 bytes `[?]` | 285 in `MONnCHA.DAX` | `MONST.GLB` items are a big-endian u16 unpacked length (450 in the data) + PackBits; loose files are raw (reconstruction, `saveload.c` `mon_load`) |
| Item instance | 18: base type, 3 name-word ids, weight, value, bonus, save bonus, readied, hidden-name bits, cursed, bundle size, charges, effect/spell codes | 63 | name words from a vocabulary, as in PoR |
| Item template | `ITEM.DAT` 254 × 18 | – | events and monsters give templates by number |
| Base item type | `ITEMS.DAT` 128 × 16 | `ITEMS` 16 bytes per type | same record size as PoR's table; field match not checked `[?]` |
| Save game | `SAVGAMx.CSV` 10,285: 1,024-byte state block (from state + 1), position (5), view modes, member count at 0x40D, the members (character + items + effects), padded to 0x282D | `SAVGAMx.DAT` 13,137 + 3 files per member | Heirs' save holds 6 members, no variables or done bits set |
| Vault | `VAULTx.DAT` 916: platinum, gems, jewellery (u32), item count, 50 × 18 items | – | |

- **Character transfer between designs:** copy the `.CCH` into the other design's `SAVE\`. Items are stored as full
  18-byte records, so a character keeps items even when the other design uses different templates. DQK's import
  of Death Knights characters was removed from FRUA (talonius `saveload.c`). FRUA has no import from the older
  games.
- Save writer at 0x32B07; `SavGam`, `Vault%c.DAT` and `cch` strings at 0x8AE7E, 0x8AEB5, 0x8AE05.

## 7. Text

- Area text: per-area pool (STRG chunk), 400 slots, 6,762 bytes of packed text. 6 bits per character, 4 characters
  in 3 bytes, code 0 ends a string. Codes 32–63 are themselves; 1–31 stand for 65–95; lower case is folded to upper
  case (reconstruction, `packtext.c`, resident 0x00F305). So all event text is **upper case only**, which matches
  `UAHELP.TXT`. Decoding a Heirs area gives letters, digits, punctuation and `^`.
- `^` is a substitution token: the first two are replaced by the active character's name (reconstruction,
  `textbank.c`).
- `@` packs to 0 and so ends the string early (HackDocs `GEODATA.TXT`).
- Labels (`STRGnnn`): same 6-bit packing, 15 characters. `GAME001.DAT` names (title, keys, items): plain zero-padded
  ASCII, mixed case.
- Engine text (menus, messages) is in `CKIT.EXE` and `SCRIPT.GLB`; designs cannot change it.
- Limits from the HackDocs, not re-derived: 228 characters per string, about 8,960 per area `[?]`.

## 8. The program

- `CKIT.EXE`: one MZ program for the game, the design editor and the art gallery. Resident image 120,833 bytes;
  the rest is 27 RTLink/Plus overlay sections (strings "RTLink(R)/Plus" in the run-time). Built with Turbo C 2.01,
  large model (talonius README; MicroMagic, as DQK).
- No separate compiler or build step: the editor writes the same files the game reads. "Test design" plays the
  design in place.
- The DQK engine is the base: many modules are DQK's (`glbdata.c`, `saveload.c`, `library.c`), others are new
  (`mapevent.c` has no DQK match).
- Community "hacks" change the engine, not the design: replaced `DISK1..3` files (sound, items, spells, art) or
  byte patches to `CKIT.EXE` applied by UA Shell from a `DIFF.TBL` per design (`MONSTSUM.TXT`, `UATFILES.TXT`).

## 9. Implications for the module format

### Take from FRUA

1. **Overrides keyed by (kind, number) over a base pack.** FRUA's loose-file-first rule maps directly onto
   "module depends on module" in PLAN 2: a `frua-base` module built once from the player's install (master TLBs,
   `MONST.GLB`, `ITEM.DAT`, `ITEMS.DAT`, GLB defaults, labels), and one module per design that only overrides. The
   same scheme fits original games (a game module plus community add-ons).
2. **Typed events with a common head as the module's own script form.** Type, once flag, one condition from a
   closed list, a chain pointer, and per-type branch fields cover what FRUA designs do. It is easy to edit, easy to
   check, and needs no compiler. Our module script (PLAN 8) can start from this model and add what it lacks.
3. **Engine services as parameterised events:** shop, temple, training, tavern, inn, vault, town, camp,
   encounter menu, combat setup. PoR's ECL drives the same services through flags; both importers can target one
   set of service events.
4. **Door and lock semantics in the map** (secret, locked, wizard-locked, key n, blocked, false door) instead of in
   scripts.
5. **Editor schema from data.** `SCRIPT.GLB` describes every event field (bit position, range, choices). Our JSON
   schemas should carry the same information so one generic editor form works for every event type.

### Must hold to import FRUA designs losslessly

- Area numbers 1–40 (overland 1–4), event slots 1–100, text slots 1–400 and their numbering. The done bits
  (area × 100 + slot − 1) and saves depend on them, including the area-40 quirk in 4.3.
- The **raw 20-byte record** next to the decoded fields. Some bits are not understood (temple +16, teleport +16)
  and must survive a round trip.
- Area header fields: variable grid size, entry points, wall/backdrop/combat-art ids, 8 zones with backdrop,
  combat art, rest encounter and text, 8 step events, summoned monster, undead modifier, camp and treasure
  pictures, area-view flag.
- Per-cell: 4 faces (graphic + door kind), event slot, backdrop, zone.
- The 64 variables with their FRUA meaning (keys, items, quests with stage values 0/128/255/other) and their names
  from `GAME001.DAT`; start area, entry point, money, XP and equipment class.
- Monster/NPC records (450), item templates (254 × 18) and base types (128 × 16), characters (`.CCH`), vault,
  premade party in `SAVE\`.
- Art by slot id, with palette ranges and colour cycling (TLB colour tables), and the label tables (designer names
  of slots).
- Text as written: upper case, `^` mapped to a name placeholder token, and the empty / `@`-truncated strings kept
  as they are.
- **Hacks:** a design may need replaced engine files or EXE patches. The importer should hash the install against
  the clean v1.2 set, import replaced data files as engine-data overrides in the design module, and report EXE
  patches it cannot express `[?]`.
- Importer practicalities: find the design root by `GAME001.DAT` (zip layouts differ), match file names
  case-insensitively, take premade `.CCH` files as part of the module.

### What FRUA lacks that original-game modules need

- **General scripting.** PoR and the other originals need raw ECL (per-game opcode tables, five entry vectors, VM
  memory) — FRUA cannot express their scripts. The module format must allow an area to carry either typed events or
  an ECL script (PLAN 1.3 point 2), and later the readable module script.
- **More state:** arbitrary VM addresses, script-local tables, character-field access, many flags. FRUA has 64
  bytes.
- **PoR map details:** separate barrier plane per face, one-way edges from mismatched faces, the event byte's
  bit conventions (sky bit 7), fixed 16 × 16 maps and wilderness formats.
- **Per-game rules:** FRUA has one fixed rule set (FRUA's own spells, classes, items). Imported FRUA designs name a
  `frua` rule profile; original games their own.
- **Mixed-case text, string variables, per-language text:** FRUA text is upper case and per area.

## 10. Open `[?]`

- Label kind bit 0x80; Heirs slot 84 flagged as edited with no `MONST084.DAT`.
- `GEO.GLB` template 3 (id 43) and area id 41/42 use.
- Overland wall value 0xF0 meaning; whether dungeon wall graphics follow PoR's `(t−1) div 5` slot split.
- TLB wall-override bank digit 1–3 (`8x8d1nnn` … `8x8d3nnn` in one design): presumably the area's wall slot.
- PICKCOMBAT (33) group choice; condition kinds checked only through the reconstruction, not under DOSBox.
- Size fields of 9 community TLBs that differ from the file length.
- `GAME.FON` layout; `SOUNDS.GLB` (`DIG4`) item format.
- Text limits from the HackDocs (228 characters per string).
- Area 40's done bits outside the saved block (4.3).
- Nothing here was run under DOSBox-X.
