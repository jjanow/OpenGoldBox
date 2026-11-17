# Save games (Pool of Radiance, DOS)

Status: **layout verified** (2026-11-17) on the two save games shipped with PoR DOS v1.3 (`SAVGAMA.DAT` +
`CHRDATA1-6.*`, `SAVGAMJ.DAT` + `CHRDATJ1-6.*`) and the v1.1 set (`SAVE\SAVGAMA.DAT` + `SAVE\CHRDATA1-6.*` from `SAVE.ARC`
of the v1.1 360K disks, byte-identical to v1.3's A set). The meaning of individual game-state variables is only partly known; unknowns are
`[?]`.

Tool: `tools/scripts/gbchar.py savegame SAVGAMx.DAT [--json] [--raw]`, `gbchar.py char CHRDATxn.SAV`.
Character record: `docs/re/character-format.md`. Item and effect records: `docs/re/item-format.md`.

## Sources

- Stephen S. Lee, *Pool of Radiance – Exhaustive Game Information* v2.00 (CC BY-NC 4.0), sections 12.5 "Save File
  Format", 12.4.1 "Script Flag List", 12.4.2 "Other Script Addresses".
- talonius/pool-of-radiance, `src/saveload.pas` (`SaveGame`, `LoadGame`, `SaveChar`, `LoadChar`), `src/types.pas`
  (`TGbvmZone0State`, `TPartyPosition`), `src/includes/c_area.pas`, `c_ifc.pas`. No license: read only.
- DrEvil-TitaniumHelix/pool-of-radiance-remake, `decode/data/SAVES.md` §1-2 (overlay 17 routines). No license in
  tree: read only.
- `scratch-priorart/survey/key-facts.txt` (cross-checked sizes, incl. the Amiga ones from the wish docs).

## Files of one save game

A save game is one `SAVGAMx.DAT` plus three files per party member. `x` is the **save slot letter A..J** (the game's
save/load menu offers ten slots). The retail game ships slot A (and v1.3 also slot J, dated 1992) holding a
ready-made level-1 party of six, so a new player can start without creating characters.

| File | Size | Content |
|---|---|---|
| `SAVGAMx.DAT` | 13,137 | Game state (below) and the list of party files |
| `CHRDATxn.SAV` | 285 | Creature record of party member n (n = 1..8, party order), written raw |
| `CHRDATxn.ITM` | k × 63 | The member's items, in inventory order, headerless. **Absent** when the member has no items |
| `CHRDATxn.SPC` | k × 9 | The member's active effects, in list order, headerless. **Absent** when there are none |

- The game writes the 285 record bytes as they are in memory, then walks the item list and the effect list and writes
  each record. Pointers inside the records (character 0x7F, 0xC8, 0xCC..0xFF, 0x104, 0x108; the link fields inside
  item and effect records) are stale on disk. On load the game reads the record, clears those pointers, reads
  `.ITM`/`.SPC` records until end of file and relinks them, then recalculates the character (talonius `LoadChar`,
  DrEvil `ReadCharacter`).
- Shipped examples of absent files: `CHRDATA4.SPC`, `CHRDATA5.SPC` (no effects). `CHRDATA1.ITM` = 2 items (126 bytes),
  `CHRDATA1.SPC` = 4 effects (36 bytes).
- Location: the save directory is a path read from the game's configuration text file at start-up (talonius
  `gameboot.pas`; DrEvil `DS:537A`); the game creates it if missing. v1.1 ships its saves in a `SAVE\`
  subdirectory; the v1.3 archive has them next to the game files. `[?]` the default path each installer writes.
- **Roster files** (not part of a save game): characters stored outside the party ("add character" / training
  hall) are `<name>.CHA` + `<name>.ITM` + `<name>.SPC` (same formats; name made 8.3-safe) and are listed in
  `CHARLIST.TXT`, one name per line. None are shipped. When a save game is saved or loaded, the game deletes the
  roster files of every character now in the party (talonius `SaveGame`/`LoadGame`).

## SAVGAMx.DAT layout (13,137 bytes)

The game writes, in this order (Lee 12.5, talonius `SaveGame`, DrEvil `ov17_1D58`; all three agree):

| File offset | Size | Content |
|---|---|---|
| 0 | 1 | Current data set ("disk") number 1..8: the digit of the `ECLn`/`GEOn`/`MONn…` files in use. Also stored at GBVM $6E12 |
| 1 | 2048 | GBVM area $4900..$4CFF, one **u16 per address** (1024 words) |
| 2049 | 2048 | GBVM area $6B00..$6EFF, one u16 per address |
| 4097 | 1024 | GBVM area $9700..$98FF, one u16 per address (512 words) |
| 5121 | 7680 | GBVM area $9900..$B6FF, one **byte** per address: the ECL script currently loaded |
| 12801 | 5 | Party position (table below) |
| 12806 | 1 | Location type (enum below) |
| 12807 | 1 | Interface state when saved (enum below) |
| 12808 | 1 | Party size k (1..8). Also stored at GBVM $6E3E |
| 12809 | 328 | 8 × Pascal string[40] (41 bytes each: length + 40 chars): file stem `CHRDATxn` of party member n |

1 + 2048 + 2048 + 1024 + 7680 + 5 + 1 + 1 + 1 + 328 = 13,137 = size of both shipped files.

- "GBVM" = the address space the ECL scripts read and write (Lee's term: Gold Box Virtual Machine). In the three
  word areas every address is a 16-bit cell, so **file offset = area start + 2 × (address − area base)**. Example:
  flag $4A00 is at 1 + 2 × 0x100 = 0x201 (Lee 12.4.1 gives the same 0x201). The scripts mostly treat the cells as
  bytes; in both shipped saves no cell has a non-zero high byte.
- Only the first k name strings are meaningful. The bytes after each string's length, and the strings for members
  k+1..8, are leftover memory (the shipped files contain fragments of other text there). A reader must use the
  length byte and k, never scan for text.

### Party position (offset 12801)

| +0 | `x` | Map column 0..15 (signed byte in the game) |
|---|---|---|
| +1 | `y` | Map row 0..15 |
| +2 | `facing` | 8-way direction code: 0 N, 2 E, 4 S, 6 W (odd values would be diagonals `[?]`: none in the shipped saves). ECL reads GBVM $C04D as facing / 2 = 0..3 (talonius `ReadGbvm`; Lee lists $C04D as 0..3) |
| +3 | `wall` | Wall type in front of the party on its square (GBVM $C04E) |
| +4 | `event` | Event byte of the party's square (GBVM $C04F; upper bits are flags) |

Lee lists these five bytes as GBVM $C04B..$C04F.

### Enumerations

| Field | Codes |
|---|---|
| location type (12806) | 1 3D map (towns and dungeons), 2 western wilderness, 3 central wilderness, 4 eastern wilderness (talonius `C_Area`). On load the game reloads the 3D map when the value is < 2 (talonius, DrEvil) |
| interface state (12807) | 0 main menu / training hall, 1 shop or temple, 2 camp, 3 moving outdoors, 4 moving indoors, 5 combat, 6 looting, 7 game won (talonius `C_Ifc`, `ending.pas`). Both shipped saves: 2 (saved from camp) |

### Known GBVM cells

Cells read by the tool. All in the $4900 or $6B00 areas; addresses from Lee 12.4.2 unless noted.

| Address | JSON key | Meaning |
|---|---|---|
| $49C3, $49C4 | `location.outdoor_x/y` | X, Y coordinate (Lee: outdoors) `[?]`: 0 in both shipped saves although the party is in a 3D map |
| $49C5 | `location.map_id` | Current map / ECL script id (the block id in `GEOn.DAX` and `ECLn.DAX`) |
| $49C6..$49CC | `time.*` | Clock: minute segment (unused), minutes ones digit, minutes tens digit, hour, day, month, years elapsed |
| $49E6 | `location.view_3d` | Lee: "outdoors flag"; talonius: non-zero when a 3D view is loaded (towns too). 1 in both shipped saves (party in a town map). `[?]` name |
| $49F0, $49F1, $49F2 | `location.previous.*` | Previous X, Y and previous ECL script id |
| $49FC | `settings.game_speed` | Game speed (written from the options before saving) |
| $49FD, $49FE | – | Sky and ceiling colour codes |
| $49FF | `settings.portraits`, `monster_pics` | bit 0 portraits on, bit 1 monster pictures on |
| $4A00..$4AFF | `flags` (256 values) | Script flags: most quest and world state. $4A00..$4A1F are reset whenever a new script loads; $4A20..$4AFF persist. Per-flag meanings: Lee 12.4.1 (not copied here) |
| $4AFA..$4AFC | `wall_sets[].walldef_id` | Wall-definition set loaded in wall slots 1..3 (s16; negative = none) |
| $4AFD..$4AFF | `wall_sets[].slot` | Symbol slot of each loaded wall set |
| $6E12 | `disk_gbvm` | Disk number (copy of byte 0) |
| $6E3E | `party_size_gbvm` | Party size (copy of byte 12808) |

The wall-set cells: Lee lists the save routine copying into $4AF9..$4AFE; talonius (`GBVM_WALLSLOT_ID + i`,
i = 1..3, base $4AF9) and DrEvil (block offsets 0x3F4..0x3FE) give $4AFA..$4AFF. The shipped data supports
$4AFA..$4AFF: ids 2, 4, 1 at $4AFA..$4AFC and slots 1, 2, 3 at $4AFD..$4AFF, with $4AF9 = 0.

The rest of the $6B00 area (combat setup values such as morale threshold $6DC6 and surprise $6DCB, shop values …)
and the whole $9700 area are ECL working storage `[?]`. The $9700 area holds text buffers one character per cell in
the shipped saves. The tool exposes them only with `--raw`.

### ECL area

The 7680-byte area at offset 5121 is the ECL script of the current map. The game loads script block `map_id` from
`ECL<disk>.DAX`, drops the block's first 2 bytes and copies the rest to $9900, after zeroing the area (talonius
`LoadEclScript`; prefix also noted in `docs/re/dax-format.md`). Lee: "The save game file includes the entire current
ECL script." So a save is tied to the exact script version it was made with. An importer should rebuild this from the
game's own ECL file and only check it against the save; on load the game runs the stored copy (check 9).

## How a save game ties the files together

Loading `SAVGAMx.DAT` (talonius `LoadGame`, DrEvil `ov17_1602`):

1. Read the fixed layout above. Restore the options from $49FC/$49FF and the disk number.
2. For i = 1..k: read `CHRDATxi.SAV` (+ `.ITM`, `.SPC` if present) by the stem stored in name slot i and append the
   character to the party. The stem is not rebuilt from the slot letter; a renamed set would still load. Missing
   `.SAV` files are skipped.
3. Load the combat icons of each member; reload the current 3D map (`GEOn` block `map_id`) when the location type
   is < 2, and the three wall sets from $4AFA..$4AFF; outdoors, load the wilderness icons instead.

Saving does the reverse: it writes the options and wall slots into the GBVM cells, writes `SAVGAMx.DAT` with name
slot n = `CHRDATxn` for each member in party order, then each `CHRDATxn.SAV/.ITM/.SPC`. Old `.ITM`/`.SPC` files are
deleted first, so an absent file really means "none".

## Amiga `[?]`

The Amiga port uses the same file names with different sizes: `CHRDATAn.sav` 288, `.itm` 65 per item, `.spc` 10
per effect, `savgamA.dat` 13,141 (wish docs, via `key-facts.txt`). Big-endian and padded records are likely, but
not checked by us. Later.

## JSON (`gbchar.py savegame SAVGAMx.DAT --json`)

```json
{
 "format": "gbchar/1", "kind": "savegame", "file": "SAVGAMJ.DAT", "letter": "J",
 "disk": 2, "disk_gbvm": 2,
 "location": {"type": {"id": 1, "name": "3d_map"}, "map_id": 20, "x": 14, "y": 5, "facing": 4, "wall": 0,
              "event": 0, "view_3d": 1, "outdoor_x": 0, "outdoor_y": 0,
              "previous": {"x": 14, "y": 4, "script": 20}},
 "interface_state": {"id": 2, "name": "camp"},
 "time": {"minute_segment": 0, "minute_ones": 6, "minute_tens": 5, "hour": 10, "day": 0, "month": 0, "year": 0},
 "settings": {"game_speed": 4, "portraits": 1, "monster_pics": 1},
 "wall_sets": [{"walldef_id": 2, "slot": 1}, {"walldef_id": 4, "slot": 2}, {"walldef_id": 1, "slot": 3}],
 "flags": [255, 0, 0, 0, "… 256 values, $4A00 first …"],
 "ecl": {"size": 7680, "sha256": "…"},
 "party_count": 6, "party_size_gbvm": 6,
 "party": [{"index": 1, "file_stem": "CHRDATJ1", "sav": "CHRDATJ1.SAV", "items_file": "CHRDATJ1.ITM",
            "effects_file": "CHRDATJ1.SPC",
            "summary": {"name": "…", "race": "dwarf", "class": "fighter", "hp": 11, "hp_max": 11, "status": "okay"}},
           "…"]
}
```

- `party[].sav`/`items_file`/`effects_file` are file names found next to the save (case-insensitive), `null` when
  missing. Full character data: run `gbchar.py char` on `sav`. `--no-party` skips opening them.
- `--raw` adds `raw_areas` with the four GBVM areas as hex (`area_4900`, `area_6B00`, `area_9700`, `ecl_9900`).
- Same stability rules as `docs/re/character-format.md`.

## Verification (2026-11-17)

1. Both shipped `SAVGAM*.DAT` are exactly 13,137 bytes and parse with the layout above, leaving no bytes over.
2. Byte 0 (2) = GBVM $6E12 (2), and party size byte (6) = GBVM $6E3E (6) in both saves.
3. ECL area = `ECL2.DAX` block 20 minus its 2-byte prefix, 7677 of 7677 bytes equal, in both saves (disk 2,
   map 20 = $49C5); the last 3 bytes of the area are 0. This confirms the area offsets, the disk byte, the map-id
   cell and the 2-byte script prefix together.
4. Map 20 exists in both `GEO2.DAX` and `ECL2.DAX`; wall sets 2, 4, 1 are block ids of `WALLDEF2.DAX`
   (which has 1, 2, 4, 9, 19, 23). Not cross-checked against the map's own wall references `[?]`.
5. Every party name slot 1..6 points to an existing `.SAV` (285 bytes), whose item count (0xC7) equals the number
   of 63-byte records in its `.ITM`; all `.ITM`/`.SPC` sizes are multiples of 63 / 9.
6. Save A vs save J: 289 bytes differ in `SAVGAM` (position, clock, a few flags, $9700 text buffers, combat
   leftovers in $6B00, name-slot garbage); the characters differ in XP, money, current HP and effects, as expected
   after some play.
7. v1.1 `SAVE\SAVGAMA.DAT` and `SAVE\CHRDATA*.*` (unpacked from `SAVE.ARC`, v1.1 360K set in
   `reverse-engineering/dos/original-images/por/`) are byte-identical to v1.3's.

8. DOSBox-X (2026-11-17, RAM read from emulator save states with `tools/scripts/dosbox_state.py`; method
   `docs/re/dosbox-trace-method.md`): right after loading save A from the main menu, the four VM areas in memory
   equal the file's areas cell for cell, except $6DD2/$6DD3 (24 in the file, 0 in memory: the script init clears
   them, `ecl-format.md`). A save made in the game (slot B, after a fight) and loaded again matched with no
   difference at all. The party position bytes equal DS:6AAD–6AB1.
9. The game runs the script stored in the save, not a fresh copy: a copy of save A whose stored vector 0 was changed
   loaded with the changed vector in effect (DOSBox-X). So the ECL area matters on load; an importer that rebuilds
   it from the ECL file must produce the same bytes.
10. No map state is saved: doors opened before a save are locked again after loading it (DOSBox-X), and every
    LOADFILES of the map (entering the area) re-locks them too (program read; `geo-format.md`).

Run: `python3 -I tools/scripts/gbchar.py check <gamedir>` repeats checks 1-3 and 5.
