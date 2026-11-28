# Module format (draft 0, s15)

The contract between the importer, the engine and (later) the editor. A **module** is everything the engine needs to
play one game or one community adventure: maps, scripts, art, records, text and the rule tables. The engine never reads
the original containers (DAX, HLIB, FRUA design folders). Only `GoldBox.Importer` reads them.

Inputs:
- PoR: the `docs/re/*-format.md` specs.
- The three late-engine surveys, section "implications" of each:
  - `docs/re/survey-dqk.md` §9 (Dark Queen of Krynn);
  - `docs/re/survey-pod.md` §8 (Pools of Darkness);
  - `docs/re/survey-frua.md` §9 (Unlimited Adventures).
- `docs/re/survey-sotsb.md` (Silver Blades, s18): dates each ECL change between PoR and PoD (its §8).

The format must hold all four without loss. "Without loss" means the importer can write the original bytes of every
raw resource back out, so they can be compared against the source and against saves.

Status: draft, implemented for PoR DOS (s15–s16): `src/GoldBox.Data/Module/` reads modules, `goldbox-import por`
writes everything the engine and the Client read (scripts, areas, maps, records, item types, the program's tables and
texts, the DOS art, the shipped saves as engine saves (§15, s18); with `--amiga`, the Amiga art overlay, s17; the overland, s18), and the Client plays PoR from a module with no game folder (`--module DIR`;
`--game DIR` imports into a cache first). `[?]` marks choices still open. See "Implementation order" for what is done.

## 1. Principles

1. **Original ids are kept.** Scripts, maps, monsters, items, pictures and wall sets keep the number the original
   used. Saves and ECL then stay valid without translation tables.
2. **Raw next to decoded.** A record whose layout is not fully known is stored as its raw bytes plus the fields we
   have decoded. The engine uses the decoded fields. The raw bytes keep round trips exact and let a later decode
   fill in more fields without re-importing. (Format 0.x: for PoR records raw is authoritative and the engine uses
   it; see section 11.)
3. **The engine is generic; per-game behaviour is named.** A module names a **rules profile** and a **VM profile**.
   Both are code and tables in the engine (public). Numbers read from the player's program (combat tables, spell data,
   program strings) are game data: the importer writes them into the module's `rules/` and `text/` folders, never into
   the engine source.
4. **Modules layer.** A module may depend on others. A resource is found by its key in the module itself first, then
   in its dependencies, in the order they are listed. This is FRUA's "design file first, then the engine library"
   rule, and it also covers sequels and community add-ons on top of an imported game.
5. **No platform formats inside.** Art becomes indexed PNG plus JSON metadata. Text becomes UTF-8 JSON. Scripts are
   the exception: ECL is kept as bytecode, because the VM runs it as-is.
6. **Versioned.** Every module states `formatVersion`. The engine refuses a newer major version and migrates older
   ones.

## 2. Resource keys

Every resource is addressed by **(kind, bank, id)**.

- `kind`: `script`, `map`, `area`, `wallset`, `picture`, `sprite`, `icon`, `backdrop`, `combatGround`, `tile`, `monster`,
  `itemList`, `treasureList`, `effectList`, `itemTemplate`, `font`, `sound`, `music`, `events`, `textPool`.
  `treasureList` is PoR's placed treasure (`ITEM<d>`): its ids collide with the monster item lists (section 16).
- `bank`: a namespace for ids the original reuses. PoR picks `WALLDEF<d>`, `8X8D<d>`, `MON<d>CHA/ITM/SPC` and
  `ITEM<d>` by the disk digit, and the same id means different data on different disks. For those kinds the bank is the
  disk number. Scripts and maps have ids that are unique over all disks, so they use bank 0. Bank 0 is the default
  everywhere. DQK, PoD and FRUA have no disk files, so they use bank 0 throughout. Curse and SotSB keep disk files
  (SotSB: `ECL`, `GEO`, `WALLDEF`, `8X8D`, `ITEM` on 3 disks; `MON1` only) with script and map ids unique over the
  disks, so PoR's bank rules apply to them. One difference: SotSB's script loader retries a missing block instead of
  failing (survey-sotsb §3.1).
- `id`: the original number.

The VM profile says, per kind, whether lookups go through the bank. For PoR, the current bank is VM $6E12 (DS:5376,
the disk byte). In paths a key is written `<kind>/<bank>/<id>`, e.g. `wallsets/3/0.json`, `monsters/2/41.json`.

The PoR script's disk is still needed: $6E12 is VM state that scripts read and write. The importer records each
script's and each map's disk as `meta.disk` in its JSON. The key stays bank 0, but the `ecl-por-1.3` profile checks
`meta.disk` against the current disk: the program opens `ECL<d>`/`GEO<d>` of the current disk and fails when the block
is not there, and the module must fail the same way (a lookup that ignored the disk would succeed where the original
stops). A resource without `meta.disk` (a hand-made module) is found on any disk. (Changed in s15: the draft said the
engine does not use the disk to find a script.)

## 3. Layout

```
module.json                    manifest (section 4)
areas/<id>.json                what an area is: view kind, script or events, maps, entry points (section 6)
maps/<bank>/<id>.json          one grid: size, edge rule, named layers (section 7)
scripts/<bank>/<id>.ecl        ECL bytecode exactly as the VM loads it (section 8)
scripts/<bank>/<id>.json       sidecar: original prefix bytes, disk, source block
events/<area>.json             typed events, FRUA style (section 9)
art/<variant>/<container>/<bank>/<id>.json (+ .png)  indexed images or raw data blocks (section 10)
records/monsters/<bank>/<id>.json      creature records (section 11)
records/itemLists/<bank>/<id>.json     monster inventories
records/treasureLists/<bank>/<id>.json placed treasure (PoR ITEM<d>)
records/effectLists/<bank>/<id>.json
records/itemTemplates.json, records/itemTypes.json
rules/program.json             the program's data segment and table offsets (section 12)
rules/overland.json            the overland tile map and special locations (sections 6, 12)
text/<lang>.json               program and module text by key (section 13)
audio/dos/pcsound.json         the program's PC-speaker / Tandy sound driver data (section 10a; optional)
audio/amiga/sound.json         the Amiga version's music and effect players' data (section 10a; with the Amiga overlay)
transfer.json                  what characters bring in and take out (section 14)
saves/                         shipped saves / premade characters (optional; section 15; PoR: engine saves a.json, j.json)
```

A module is a directory, or the same tree in a zip. File names are lower case, and ids are decimal with no padding.

## 4. Manifest (`module.json`)

```json
{
  "formatVersion": "0.1",
  "id": "por",
  "title": "Pool of Radiance",
  "version": "1.3.0",
  "kind": "game",
  "rules": "por-1.3",
  "vm": "ecl-por-1.3",
  "start": { "area": 0, "map": 0, "x": 0, "y": 0, "facing": 0 },
  "dependsOn": [],
  "art": { "variants": ["dos", "amiga"], "defaults": { "picture": "amiga", "portrait": "amiga", "wall": "amiga", … } },
  "source": {
    "importer": "GoldBox.Importer 0.1",
    "game": "por", "platform": "dos", "release": "1.3",
    "inputs": [ { "file": "GAME.OVR", "sha256": "…" } ]
  },
  "credits": "", "license": ""
}
```

- `kind`: `game` (imported original), `base` (shared library, e.g. `frua-base` built from the FRUA install), `design`
  (a FRUA or community adventure), `addon`.
- `rules` and `vm` name profiles that ship with the engine. `vm` is absent for event-only modules (FRUA designs).
  **No `vm` means no ECL VM** (settled s18). The VM a module runs under is the first `vm` in lookup order: the
  module's own, else its dependencies' (so an add-on with no `vm` runs under the game it extends). When no module in
  the chain names one, the module has no VM: it may hold events, art and records, but no scripts are run. Opening such
  a module as PoR content (`PorModule`) fails with `ModuleHasNoVmException`, since PoR's areas are ECL scripts. An
  unknown name fails too (`UnknownEclProfileException`). There is no silent default: before s18 a missing `vm` ran
  `ecl-por-1.3`.
- `dependsOn`: `[{ "id": "frua-base", "version": ">=1.2" }]`. Resolution order is the list order, after the module
  itself.
- `source` records the files the import read, by hash. This is enough to say "re-import needed" when the importer
  changes, and it ties bug reports to a release.
- `start`: for imported games, the new-game position. **PoR (s19, `docs/re/start-menu.md` §1):** the program's
  start-up and party menu fix it (ECL3 block 0 at 15,1 facing west, VM zeroed, then that script's init and entry), so
  the engine's PoR start (`PorGame.NewGame`) does not read `start`; the importer's value stays informational. Other
  games' start-up sequences are not read yet `[?]`.

## 5. Profiles (in the engine, not in the module)

### 5.1 VM profile

One per ECL dialect. All of these differ between PoR, PoD and DQK (surveys). Silver Blades (SotSB,
`docs/re/survey-sotsb.md`) sits between PoR and PoD; its column shows which changes are older than PoD:

| Field | PoR v1.3 | SotSB v1.30 | PoD | DQK |
|---|---|---|---|---|
| Script load address, prefix bytes dropped, size cap | $9900, 2, 7,680 | $8000, 2, 7,680 | $8000, 2, 7,680 | $8000, 0, 12,003 |
| Memory map: regions, cell width, saved or not | 4 regions, 16-bit cells + byte script area | PoR's 4 regions moved: $4B00, $7C00, $7A00, script $8000 | 1 KB byte globals, 510-byte character window at $0401 | game block $0001–$0400, character window $0401–$0800 |
| Operand width rule | from the address region | from the address region | from the operand type | from the operand type |
| Special cells (live engine state: x, y, facing, …) | `ecl-format.md` "Special addresses" | PoR's addresses; $C04D raw outdoors, $4BEA facing, $4BE6 mode switch (survey §3.3) | survey §3.3 | survey §3 |
| Unmapped read | stack residue | stack residue | 0 | 0 |
| Opcode table: handler per number, operand counts, variadic count position | 0x00–0x3D | 0x00–0x41 without 1F; 3C a stub | 0x00–0x41 (66) | 0x00–0x41 |
| IF-skip table | PoR's own, with its six mis-skips | separate; mis-skips 15, 25, 26, 2B, 34, 36 | separate; 15, 25, 26, 27, 2B, 31, 34, 36 | correct skips |
| RETURN on an empty stack | falls through | exits | exits | exits |
| CALL and PROGRAM id maps | PoR | raw ids like PoR's, other values; PROGRAM 0, 9 | PoD (small ids) | DQK |
| Character view (script field → record field, write whitelist) | $6B00 redirections | redirections at $7C00 into the 439-byte record | raw record window | 58 translated fields |
| Script bytes saved with the game | yes | no (reload by id, $4BF2) | no (reload by id) | no |
| Banked kinds | wallset, monster, itemList, treasureList, effectList (by disk); script and map ids checked against the disk (section 2) | wallset, tile, treasureList by disk (3 disks); script and map ids checked against the disk, as PoR; monsters fixed bank 1 (`MON1`; item and effect lists presumably too `[?]`) | none | none |

Dating (SotSB survey §8, with a quick Curse check): the script base $8000, the moved regions, CLOCK's second operand,
opcodes 3E–40 and RETURN-exits come from **Curse**; ADD EP (41), the 0x3C stub and reload-by-id saves from **SotSB**;
the byte globals with type-driven width, unmapped = 0, the record window, small CALL ids, the journal opcode and the
encounter opcodes turned into UI primitives (0C, 1D, 21, 22, 23, 27, 29, 2C, 31, 37) from **PoD**; correct IF-skips
from **DQK**. A SotSB profile is therefore PoR's VM with other bases and tables. Its layout, regions, disk cell,
tables, width rule and unmapped read fit `EclProfile` as it is; it needs an exit member in `EclReturnOnEmptyStack`, a
character view other than `Por6B00Redirection` (which hard-codes PoR's base and record offsets; SotSB needs the
redirection list as data or a second view kind) and its own `EclHandlerSet` (survey §9).

The profile is C# (handlers) plus data in `src/GoldBox.Core/Ecl/Profiles/`. It is ours, written from the specs, and it
holds no game data. The module format only names the profile.

Done (s17): `EclProfile` holds the table's rows as fields, and `EclProfiles` resolves the manifest's `vm` to one.
`ecl-por-1.3` (`EclProfiles.Por13`) was the only profile (SotSB: below). Its values are the constants the VM was written with, and a
test checks that they match. The PoD and DQK columns above stay documentation until those VMs are built. Row by row:

- Load address, prefix, size cap: `ScriptBase`, `ScriptPrefix`, `ScriptAreaSize` (plus `VectorCount`, `InitVector`).
- Memory map: `Regions` (start, end, cell width, saved). The reader and writer that use them are code in `EclMemory`.
- Operand width: `OperandWidth` (`FromAddressRegion`).
- Unmapped read: `UnmappedRead` (`StackResidue`). The value used stays in `EclVmPolicy.UndefinedValue`.
- Special cells: code in `EclMemory` (reader and writer); `DiskCell` names $6E12.
- Opcode table: `Opcodes` (`EclOpcodeTable`: rows with operand counts and the variadic count position). The handlers
  are code: `Handlers` names the set (`Por13` = `EclVm.Execute`).
- IF-skip table: `Opcodes.SkipCount`; `Opcodes.MisSkips` lists PoR's six (0C, 15, 25, 26, 2B, 36).
- RETURN on an empty stack: `ReturnOnEmptyStack` (`FallsThrough`).
- CALL and PROGRAM ids: `CallIds`, `ProgramIds` (id → what it does) and `ProgramIdsThatExit` (9). The effects are
  code in the hosts (`EncounterHost`, the Client's player).
- Character view: `CharacterView` (`Por6B00Redirection`). The redirection list is code in `EclMemory`.
- Script bytes saved: `ScriptBytesSaved`.
- Banked kinds: `BankedKinds` and `DiskCheckedKinds` (kind names from section 2). `PorModule` reads both (s18): a
  monster or treasure-list lookup uses the disk as its bank only when the profile banks the kind, and a script or map
  is checked against `meta.disk` only when the profile checks the kind. A monster's item and effect lists are found
  through the keys in its document, which carry their bank. `wallset` is banked by the container's disk digit in the
  art path (`art/dos/walldef/<d>/…`, section 10), not through a resource key.

The VM reads the layout, the regions, the opcode and IF-skip tables, the PROGRAM exit ids, the fixed cells
(`EclProfile.Cells`: old script id, view flag, map id, wall flags, party size, SURPRISE result, the script init's
writes and clears, the character-view base) and the record size from the profile. The other fields name code: the
handler set picks the branches in `EclVm` / `EclMemory` where a dialect's handler differs. The profile hangs off
`EclMemory` (`new EclMemory(policy, profile)`; `EclVm.Profile` is the memory's). Both default to `ecl-por-1.3`, so
callers that name none run PoR. `IPorContent.VmProfile` gives the content's profile: a DOS folder is `ecl-por-1.3`, and a
module is its `vm` (section 4: its own or a dependency's; none fails with `ModuleHasNoVmException`). An unknown name
fails when the module is opened (`UnknownEclProfileException`).

Done (s24): `ecl-sotsb-1.3` (`EclProfiles.Sotsb13`), the SotSB column as data. It adds
`EclReturnOnEmptyStack.Exits`, `EclCharacterView.Sotsb7C00Redirection` and `EclHandlerSet.Sotsb` and
`ProgramExitGuardCell` (SotSB's PROGRAM 9 exits only while $4C38 = 0; null for PoR; `EclVm` reads it). Since s28 the
headless VM runs it (`survey-sotsb.md` §10: the handlers, the $7C00 view, the special-cell changes); `PorModule` still
refuses it with `EclProfileNotRunnableException` (the Client reports it like an unknown profile), as the game host
around the VM (encounters, combat, rules) is PoR's. Its opcode table is `EclOpcodes.Sotsb`: PoR's rows with CLOCK's second
operand, 3E DUMP, 3F FINDSPECIAL, 40 DESTROYITEMS, 41 ADDEP, and no row for the 0x3C stub; the IF-skip counts are the
program's chain (mis-skips 15, 25, 26, 2B, 34, 36). `EclDecoder` and `EclAnalysis` take a profile (default PoR), and
`goldbox-tools ecl-check GAMEDIR --vm ecl-sotsb-1.3` and `tools/scripts/ecl.py … --vm sotsb` read a SotSB folder. The
new-game start (script 3 or 17) stays in `survey-sotsb.md`. Banked kinds: `wallset`, `tile`, `treasureList`; monsters
are always bank 1 (`MON1`) and the item and effect lists presumably too `[?]`, so they are not in the set.

`EclVmPolicy` stays separate. It holds the `[?]` switches, which a test or a DOSBox check may change for one VM.
A profile is shared and fixed.

### 5.2 Rules profile

Names the rule code (combat, spells, levels, treasure, shop, temple, camp) and the shape of the tables it expects in
`rules/`. Profiles: `por-1.3` first, then one per game. `frua` covers FRUA designs. A community `goldbox-modern`
could reuse a profile's code with edited tables.

Done (s18) for PoR: `PorRulesProfiles` (`src/GoldBox.Data/Module/Por/PorRulesTables.cs`) resolves the manifest's
`rules` (unknown name: `UnknownRulesProfileException`, checked when the program tables are first read; no `rules`
means `por-1.3` `[?]`, as for `vm`). `por-1.3` lists the decoded tables it needs (§12). The rule code stays Core's.

## 6. Areas (`areas/<id>.json`)

An area is what the party is "in": one script, or one event set, plus the maps it may show. The id is the script id
for ECL games (PoR: 0–29) and the area number for FRUA (1–40).

```json
{
  "id": 24,
  "views": [ "3d" ],
  "script": { "kind": "script", "bank": 0, "id": 24 },
  "maps": [ 14, 24, 26, 31 ],
  "noWrapOffMap": false,
  "notes": "maps includes the values of computed LOADFILES operands, found from the code before them"
}
```

- `views`: the views the area's script switches between: `3d` (grid maps), `overland-tiles` (PoR's wilderness: a tile
  table plus special locations), `overland-picture` (DQK: a big picture plus script logic, no grid); empty for a
  script with no view of its own (PoR 19, the silver dragon lair). A list, not one value (changed in s15): PoR's
  wilderness scripts 25–27 are both 3D lairs and the overland (`$49E6`), and 8 and 11 load map 0 (the draft listed
  them as map-less).
- `name` is optional. Area names are not in the PoR files, so the importer writes none.
- `script` xor `events` (section 9). A FRUA area has `events` and exactly one map. `maps` lists the maps the script is
  known to load. It is for tools and checks only; the script decides at run time. The PoR importer takes the immediate
  map operands of LOADFILES on code reachable from the vectors that name a GEO block on the script's own disk. For a
  computed operand (a cell; blocks 3, 5, 24) it adds the values the cell can hold there when the code just before the
  LOADFILES fixes them (s18, `EclValueSets`: walking back over straight-line code with no entry point, jump target or
  GOSUB in between, through SAVE, ADD, SUB, AND, RANDOM and GETTABLE from a script table indexed by the facing), and
  accepts a GEO block on the script's disk or on a disk an immediate SAVE to $6E12 sets on the way (block 24 leaves
  for areas 14 and 26 that way). Result for v1.3: block 3 loads 3 and 5 (a facing table), block 5 loads 3–6
  (3 + Random(4)), block 24 loads 14, 24, 26 and 31 (a facing table). A computed operand that is not fixed this way is
  named in `notes` with `[?]`: block 5's LOADFILES at $9AD8 reads $6E82, which the code before it sets only behind
  IFs on the current map (3–6), so another map would read a stale value.
- `noWrapOffMap`: PoR returns 0 off the map while scripts 0 or 10 run (`geo-format.md` "Edges"). Fields like this
  are per-area flags taken from program rules, written by the importer. The engine reads the flag from the area
  (`IPorContent.NoWrapOffMap`, s16); the folder source answers with the program's rule (scripts 0 and 10).
- FRUA area header fields go in `areas/<id>.json` under `frua`: entry points, wall/backdrop/combat-art ids, 8 zones
  (backdrop, combat art, rest encounter, text), step events, summoned monster, undead modifier, camp and treasure
  pictures, area-view flag. The raw 290-byte header is kept beside them (`raw`, base64).
- Overland (`overland-tiles`): the module's one overland table is `rules/overland.json` (changed in s18: the draft
  put `tiles` and `specialLocations` in each area, but PoR's three wilderness scripts share one table, so it is
  written once). An area that shows it says where its x = 0 sits: `"overland": { "column": 13 }`.

  ```json
  { "width": 44, "height": 36, "tiles": [ … width × height, row-major … ],
    "specialLocations": [ { "x": 0, "y": 0, "tile": 1 }, … ],
    "recovery": { "listA": [ … ], "listB": [ … ], "from": [ 16 colours ], "toA": [ 16 ], "toB": [ 16 ],
                  "first": { "x": 0, "y": 0 }, "last": { "x": 0, "y": 0 }, "pan": [ { "x": 0, "y": 0 }, … ],
                  "source": { "container": "START.EXE", "listA": …, "listB": …, "from": …, "toA": …, "toB": …,
                              "panX": …, "panY": … } },
    "source": { "container": "START.EXE", "tiles": 13896, "specialLocations": 3126 } }
  ```

  `recovery` (optional, s60, importer 0.7.40): PoR's land recovery after Yarash (GAME.OVR 0x20D50, `geo-format.md` "The
  land recovers"): the tile lists A and B (sheet item numbers, bit 7 the high sheet), the colour maps `from` → `toA` /
  `toB` (16 EGA colours each), the view's top-left overland square before the frames (`first`) and after them (`last`),
  and one square per animated frame (`pan`; its length is the frame count). The engine runs the routine
  (`OverlandRecovery`, Core) on these tables; without them the recovery is skipped with a diagnostic (no recolour, no
  RandSeed draws), so a module imported before 0.7.40 should be imported again.

  Values are stored as the program holds them: `tiles` are 1-based tile numbers (the view draws value − 1), the
  special locations are in stored order (x and y over the whole table), and the rules key them by 1-based index.
  `source` gives the data-segment offsets read. The rule saying when each special location shows is rules-profile
  code (`OverlandMap.DrawnTile`, traced from GAME.OVR 0x2DE39; the VM cells' meanings `[?]`, `geo-format.md`).
  **As built for PoR (s18):** the importer finds the tables through GAME.OVR's code (`PorOverland`: DS:3648, 44 × 36;
  DS:0C36, 9 × 6 bytes; the column table DS:3C76) and writes areas 25 / 26 / 27 with columns 0 / 13 / 26 (the
  program's view modes 2–4). The engine reads the map through `IPorContent.Overland()` and the column through
  `IPorContent.OverlandColumn(script)`; the combat arena's wilderness terrain comes from the overland's tiles, so an
  edited overland changes both. The Client draws the overland view and travels on it (s19, `geo-format.md`
  "Overland view, move and entry"); the art is the module's `sqrpaci`, `bacpac` and `icon` (DOS or the Amiga overlay).

## 7. Maps (`maps/<bank>/<id>.json`)

```json
{
  "width": 16, "height": 16,
  "edges": "wrap",
  "layers": {
    "wallN": [ … w×h … ], "wallE": [ … ], "wallS": [ … ], "wallW": [ … ],
    "barrierN": [ … ], "barrierE": [ … ], "barrierS": [ … ], "barrierW": [ … ],
    "event": [ … ]
  },
  "raw": { "header": "AAQ=" },
  "meta": { "disk": 1 },
  "source": { "container": "GEO1.DAX", "block": 24 }
}
```

- Any size: DQK is up to 39 × 39, and FRUA areas are 11–50 per side with at most 576 cells. Each layer is a row-major
  array of `width × height` integers, x east and y south (`geo-format.md`).
- `edges`: `wrap` (PoR), `solid` (DQK `[?]`), `none`.
- **Layers are named, and the rules profile interprets them.** Every square stores its own four faces, because PoR's
  one-way walls come from faces that disagree. PoR uses wall type and barrier per face, plus the event byte. FRUA uses
  `wallN..W` (graphic) and `doorN..W` (door kind: secret, locked, wizard-locked, key n, blocked, false), plus
  `eventSlot`, `backdrop` and `zone`. A layer the profile does not know is kept and ignored.
- `raw.header`: bytes before the planes that are not decoded. PoR has a 2-byte size word; DQK has 6 bytes `[?]`.
- PoR layers hold the **stored** values (wall nibbles, the 2-bit barrier of every face, the event byte), not what
  the engine's accessors compute: a face with no wall reads as passable whatever its barrier bits say, and those bits
  are kept so the block rebuilds.
- `meta.disk` and `source`: section 2 (disk check) and the original block, as for scripts.
- Wall graphics are not named by the map. In PoR, the script loads wall sets into slots (LOADPIECES, $6E22–$6E26).
  In FRUA, the area header names them. The map only holds slot-relative wall types.

## 8. Scripts (`scripts/<bank>/<id>.ecl`)

- The bytes the VM loads at the profile's load address, with the container prefix removed (PoR: the 2-byte 0x1388;
  DQK: nothing; Curse, SotSB and PoD drop the same 2-byte prefix). The sidecar `.json` keeps the dropped prefix, the source container and block, and PoR's
  disk number: `{ "prefix": "iBM=", "meta": { "disk": 2 }, "source": { "container": "ECL2.DAX", "block": 20 } }`.
  These are enough to rebuild the original block byte for byte. The PoR engine source hands the VM the rebuilt
  block (prefix + bytecode), as the DAX reader does.
- The text inside a script (6-bit packed strings) stays in the bytecode. Translation works by override: `text/<lang>.json`
  may hold `script:<bank>.<id>@<address>` keys that the host prints instead of the decoded string `[?]`. This is not
  needed for parity.
- Self-modifying scripts are why PoR saves the script. A save records which module script it started from (section 15).
- A later readable module script (PLAN §8) compiles to one of two targets: ECL for a VM profile, or typed events.
  It is not part of format 0.x.

## 9. Typed events (`events/<area>.json`)

For FRUA designs, and later for hand-made modules that don't want bytecode.

```json
{
  "slots": [
    { "slot": 1, "type": "text", "once": true, "chain": 0, "condition": { "kind": "always" },
      "fields": { "text": 12, "picture": 0 }, "raw": "base64 of the 20 bytes" }
  ],
  "text": { "12": "WELCOME TO …" }
}
```

- The head every event shares: type, run-once and chain bits, one condition from a closed list, and the next slot
  (survey-frua §4.2). `fields` hold the decoded per-type values. `raw` holds the record exactly as stored.
- FRUA's numbering is part of the data: slots 1–100, text slots 1–400, done bits area × 100 + slot − 1. Imports keep
  it. `text` is the area's text pool, keyed by slot.
- **Engine services are event types**: shop, temple, training, tavern, inn, vault, combat setup, encounter menu.
  ECL hosts call the same services, so one Core implementation serves both. The event-type list and its JSON
  schemas are defined in phase 7, from `SCRIPT.GLB`'s field descriptions (bit position, range, choices). Those
  descriptions also drive the editor's forms.

## 10. Art (`art/<variant>/<kind>/<bank>/<id>.png` + `.json`)

- **As built for PoR DOS (s16):** the folder is the DAX container's name without its disk digit, lower case, and the
  bank is the digit (`CPIC3.DAX` block 12 → `art/dos/cpic/3/12`; `DUNGCOM.DAX` → `art/dos/dungcom/0/…`). The
  `.json` sidecar (`ArtDocument`) holds per frame width, height, item count, x, y, the CGA map and, in an animation,
  the delay. `<id>.png` holds a frame's items stacked top to bottom (8-bit indexed, the 16 standard EGA colours as
  its palette); an animation has `<id>/<frame>.png`. A block that is not a picture keeps its bytes in `raw`
  (`WALLDEF<d>` wall-set records and the 8X8D1 font block 201). A block is written as a picture only when it encodes
  back to the same bytes. Engine side: `IPorContent.Picture(file, id)` / `ArtData(file, id)`. Kind names
  (`picture`, `sprite`, …) below are the long-term plan; the container names stand in for them in 0.x.
- **Indexed PNG, never flattened to RGB.** The palette stays the palette, so colour cycling and palette swaps keep
  working. 16-colour EGA art is a 16-entry PNG. A 256-colour picture carries the colours it owns, and the JSON says
  where they sit in the 256-entry screen palette.
- One file per frame: `<id>.png` for a single image, `<id>/<frame>.png` for frames and animations.
- Sidecar JSON:

```json
{ "transparent": 255, "hotspot": [0, 0], "paletteRange": [32, 63], "cycles": [ { "from": 40, "to": 47, "rate": 4 } ],
  "egaFallback": [ … ], "frames": 3, "delta": "xor", "source": { "container": "PIC1.DAX", "block": 5 } }
```

- **Variants**: `dos` is always complete. `amiga` holds only the assets the Amiga version has (section 4.3 of the
  PLAN). The engine looks in the variant the player chose, then in `dos`. `module.json` `art.defaults` gives the
  "Best" mix per category. A wrong or missing Amiga asset therefore never breaks a module.
- **As built for the PoR Amiga overlay (s17):** `goldbox-import por GAMEDIR OUTDIR --amiga AMIGADIR` reads the
  unpacked Amiga v1.0 disks (`program`, the `.dax` files of disk B) and writes `art/amiga/<container>/0/<id>`. The
  Amiga files have no disk digit, so bank 0 serves every DOS bank of that container. Each PNG's palette is the
  16-colour table the program sets for that file, read from the player's `program` (hunk #23); the sidecar's
  `paletteTable` names it. Files that store two tiles per item (`8x8d`, `sqrpaci`, `bacpac`, `dungcom`, `randcom`,
  `wildcom`) are split into DOS's item shape (`packedTiles: 2`). Every other shape stays as the Amiga stores it:
  HEAD is 56 px wide with no position (it belongs at x = 16 `[?]`), SPRIT frames are tighter, and PIC animations
  can have a different number of frames. Left out, so DOS draws them: the two placeholder blocks (SPRIT 1 and 9)
  and SPRIT 120, whose far frames are 8×1 stubs where DOS has art (one asset is drawn with one palette, so frames
  are not mixed across variants). Scripts, maps, records and WALLDEF come from DOS only. Engine side: `IPorContent.Art(file, id, choice)`
  (`PorArtChoice` Dos / Best / Amiga) returns the frames and their palette, falling back to DOS. `Picture` and
  `ArtData` always return the DOS art, which the parity screens draw. The manifest lists `"variants": ["dos",
  "amiga"]` and sets Amiga as the default for every category (PLAN §4.3, decided s5).
- **Title painting (s18):** when the Amiga folder also holds disk A's `intro`, the importer decrunches it and writes
  the title painting as `art/amiga/intro/0/1` (320×200, the 16 colours the intro fades in, category `title`, sidecar
  without `paletteTable`; docs/re/por-art-formats.md §5). Without `intro` the overlay simply has no title.
  Importer 0.7.8 (s24) adds the intro's eight credits panels as `art/amiga/intro/0/2`–`/9`, in the order the intro
  shows them, each a whole 320×200 screen as it stands once faded in (the panel's sheet rows at their screen row,
  index 0 elsewhere) with the 16 colours its fade reads; the rows and places are read from the intro's code
  (`AmigaIntro.Panels`). The Client plays the Amiga intro from them (docs/re/start-menu.md §1a).
- **Client (s18):** the art setting Original DOS / Best / Amiga (`--art dos|best|amiga`, settings key `"Art"`, the
  controls screen's ART row; default Best) goes through `IPorContent.Art`. Each asset's palette gets its own run of 16
  in a 256-entry screen palette (`ScreenPalette`; EGA stays 0–15), so DOS and Amiga art share a frame; the indices are
  moved into the run when the asset is loaded. Drawn through it: the 3D view's 8X8D tiles (Amiga 0 = DOS mask 13 on
  every tile; Amiga 202/203 have one blank tile more, DOS's count is used; s19: the backdrop then takes the Amiga
  program's pens of that palette), the combat grounds DUNGCOM/WILDCOM (cut to DOS's count) and RANDCOM (s19, from
  tile 0x22), the CPIC monster icons and COMSPR, the encounter pictures (PIC, HEAD + BODY with the Amiga head at
  x = 16 `[?]`, SPRIT over the view; s19: PIC animations and the camp's PIC 29) and the title painting at start-up
  (Best/Amiga only; s22: as the introduction's title page in place of DOS TITLE block 2). Original DOS draws what it drew
  before, pixel for pixel; the encounter pictures are new on every setting (before, a text stand-in).
- **Rune font (importer 0.7.22, s38):** when the Amiga folder also holds disk A's `fonts` folder, the importer reads
  `protect.font` (the font the program opens for PROTECT's glyphs; name, height, pen and colours read from `program`
  after checking the code that uses them, `PorAmigaRunes`; docs/re/ecl-format.md "PROTECT") and writes
  `art/amiga/protect/0/8` (category `font`, source `protect.font` block 8): one frame of 256 items of 8 × 8, item c
  being character c as the program's Text call draws it in a text cell, index 0 where no bit is set (not drawn: JAM1)
  and pen − 16 where one is; the PNG's palette is the interface colours 16–31 from `program`, no `paletteTable`.
  Without the folder the overlay has no rune font and the Client draws the DOS runes. Engine side:
  `IPorContent.Art("PROTECT", 8, choice)`; Client `RuneGlyphs` under Best / Amiga.
- **Text font (importer 0.7.24, s39):** with disk A's `fonts` folder the importer also reads `poolrad.font` (the font
  start-up sets on the window for all other text; name and height from `program`, `PorAmigaText`, after checking the
  code that draws with it) and writes `art/amiga/poolrad/0/8` (category `font`, source `poolrad.font` block 8): 256
  items of 8 × 8 as for the rune font, 1 where a bit is set; the PNG's palette is the interface colours 16–31. With it
  `art/amiga/text-pens.json` (`AmigaTextPensDocument`: `pens`, for each DOS text colour 0–15 the pen the program draws
  it in; the program draws each line again in pen + 1 one pixel to the right; `highlight`, importer 0.7.27: the lit
  entry's bar pen and its two text pens; `row24`, importer 0.7.29: row 24's colours as register, 12-bit colour pairs;
  `source`). From importer 0.7.27 the Amiga combat art (CPIC, CHEAD, CBODY,
  COMSPR and the ground sets) carries palette table 0, the one the combat screen loads. Engine side:
  `IPorContent.AmigaTextPens()`; Client `AmigaText` (`TextFont.Face`) under Amiga only (docs/re/por-art-formats.md §4
  "Text font").
- **Window frame (importer 0.7.26, s39):** `art/amiga/frame/0/1`–`/5` (category `frame`, source `program`, palette
  table 3): the top strip, bottom strip, left side, right side and corner the Amiga program's frame routine copies
  around a window, read from the player's `program` after checking that routine (`PorAmigaFrame`). Not in the Best
  mix. Client: `AmigaFrame` through `Chrome.Box` under Amiga only.
- **Party-icon colours (importer 0.7.5):** `art/amiga/icon-colours.json` (`AmigaIconColoursDocument`: `pairs`, the six
  index-pair bytes the Amiga program's party-icon recolouring reads, hunk 31 +0x210F, taken from the player's
  `program` after checking the three routines that read them; `source`). Written only when the pairs hold the pens of
  the DOS colour pairs. Engine side: `IPorContent.AmigaIconPairs()`; the Client builds party icons the Amiga way under
  Best / Amiga (docs/re/por-art-formats.md §4 "Party icons").
- **Land's-recovery tables (importer 0.7.42, s60):** `art/amiga/overland-recovery.json`
  (`AmigaOverlandRecoveryDocument`: `listA`, `listB` (tile numbers, bit 7 the high sheet), `map` (16 Amiga tile pens),
  `first`, `last`, `pan` ([x, y] per frame), `generatorHigh`, `generatorLow` (the recolour generator's nibble tables, 16
  each), `source`), read from the player's `program` after checking the routine hunk 13 +0x2872, its recolour hunk 27
  +0x7160 and generator +0x72C8 (`PorAmigaOverlandRecovery`). Engine side: `IPorContent.AmigaOverlandRecovery()`; the
  Client recolours Amiga tile sheets with it in step with the DOS pass (docs/re/geo-format.md "The land recovers").
- `kind` names follow what the engine draws: `picture`, `portraitHead`, `portraitBody`, `sprite`, `icon`, `wallset`
  (symbol sheets, plus the WALLDEF layout in JSON), `backdrop`, `combatGround`, `tile`, `frame`, `font`, `title`.
- The palette values themselves come from the player's files at import. The engine source holds none (see PLAN §9,
  the held-back Amiga palette scripts).

## 10a. Sound (`audio/`)

- **As built for PoR DOS (s19):** PoR DOS has no sound files: its effects are tunes of a timer-driven PC-speaker /
  Tandy sequencer inside START.EXE (docs/re/sound.md). The importer finds the driver by its code
  (`PorSoundDriver`) and writes `audio/dos/pcsound.json` (`PcSoundDocument`): `driver` (the dialect the engine runs,
  `por-1.3`), `origin` and `data` (base64: the driver's data bytes as they sit in its code segment from `origin`, so
  the stream, envelope and LFO pointers inside stay valid), the segment offsets of the two song tables, the two note
  tables and the two voice-block arrays, the voice size, the tune count, the timer divisor and the BIOS-chain count,
  and `cueTable`: the data-segment offset (in `rules/program.json`) of the program's 16 cue words, which the engine
  reads from there (not copied). The engine (`GoldBox.Core.Sound`) runs the driver and synthesises the speaker or
  the SN76489 from its port writes. Engine side: `IPorContent.Sound()`.
- **Variants** follow the art rule: `dos` is always there when the program had a driver; `amiga` is written by the
  Amiga overlay import (importer 0.7.9, s25): `audio/amiga/sound.json` (`AmigaSoundDocument`): `driver`
  (`amiga-por-1.0`, the players of docs/re/sound.md §5), `music` (base64: the music player's data hunk 28 whole,
  unrelocated: tables, songs, patterns, samples) and `musicRelocs` (offsets of the longs in it that point into it),
  `effects` (hunk 33 from `effectsOrigin` 0x574: the effect player's tables), `effectRelocs` (hunk 33 offsets of
  its sample pointers) and `effectSamples` (hunk 29), and `cues` (the 13 cue words at hunk 31 +0x18A8). Found
  through the players' code (`PorAmigaSound`: the relocated table operands must match). Engine side:
  `IPorContent.AmigaSound()`; the engine runs the music and effect players through a shared `PaulaSynth`. The
  Client plays Amiga music under Best / Amiga presentation or Amiga sound. Selecting Amiga sound enables the
  imported sampled effects. The other sound choices use the DOS effect driver.
- Kind names for later modules: `sound` (an effect by id) and `music` (a song by id), each with its own format
  (PCM WAV for sampled sound is the plan `[?]`).

## 11. Records

- **Raw is authoritative in format 0.x.** Byte arrays are written as base64. Each record carries `raw` (its bytes)
  and `fields` (the decoded view, nested objects named as in the `docs/re` specs and the `gbchar.py`/`gbitem.py`
  JSON). The engine builds its records from `raw`: Core works on the PoR byte layouts (the VM reads character bytes
  through the $6B00 window). `fields` is a read-only view for tools and people; writing a record's fields back over
  its raw bytes gives the raw bytes (tested). When an editor needs to change records, fields will win over raw where
  both exist `[?]`. (Changed in s15: principle 2 says the engine uses the decoded fields; for PoR records it does not
  yet.)
- **Monsters** (`records/monsters/<bank>/<id>.json`): `fields` (decoded, named as in `character-format.md`), `raw`
  (the record bytes), `items` (an `itemList` key), `effects` (an `effectList` key). The record shape is per rules
  profile: PoR 285 bytes, PoD 510, DQK a character record plus a variable tail, FRUA 450. The profile's codec names
  the fields.
- **Item lists**, **treasure lists** and **effect lists**: `{ "recordSize": 63, "records": [ { "fields": {…},
  "raw": "…" } ], "trailing": null, "source": {…} }`, item records (63 bytes in PoR) or effect records (9 bytes).
  `trailing` holds bytes after the last whole record (none in PoR). PoR's ECL TREASURE operand `k < 0x80` reads
  `treasureLists/<disk>/<k>`; a monster's inventory is `itemLists/<disk>/<id>`.
- **Item templates and types**: PoR's `ITEMS` (128 × 16) goes to `itemTypes.json`: `{ "entrySize": 16, "header":
  "…" (the 2 bytes the program skips), "types": [ { "type": 0, "fields": {…}, "raw": "…" } ] }`. FRUA's `ITEM.DAT`/`ITEMS.DAT`
  go to `itemTemplates.json` and `itemTypes.json`. PoD's 17-byte templates go to `itemTemplates.json`.
- **Characters** are not module data. Premade parties are save data (section 15).

## 12. Rules data (`rules/*.json`)

Tables the rules profile needs, read by the importer from the player's program.

**As built for PoR (s16):** `rules/program.json` (`ProgramRulesDocument`) holds the program's **data segment raw**
(DS:0000 to the end of START.EXE's load image) and the DS offsets of the tables, located through the code at import
(`PorProgram` / `PorSpellData`): spells, experience, the three effect name lists, AI steps, random items, turn undead,
item words, temple names, the camp line, the level-stats tables and the ADDNPC portrait tables; plus handler 48's two
names (from GAME.OVR's code), and the 3D view's tables (piece layout, sky colours, command line). The data segment is
kept whole because the program reads some tables past their ends (level stats, portrait bytes for any gender). The
engine reads the tables through `PorProgramData`, built either from the program files or from this file, with the
same reader (`PorDsTables`), so the two give identical tables (round trip `PROGRAM`). The combat map's tables are at
fixed v1.3 DS offsets, checked. Code segments are not copied.

**Decoded tables (s18).** Next to `program.json` the importer writes one named-field file per table group
(`src/GoldBox.Data/Module/RulesDocuments.cs`, codec `PorRulesTables`). Each names its `profile` and, per table, the
`dsOffset` the program indexes from (cells at that base plus the program's own index arithmetic). Numbers are plain
ints (unsigned or signed bytes as the program reads them, words, signed dwords); a value out of range is refused.

| File | Table (v1.3 DS) | Decoded cells |
|---|---|---|
| `rules/spells.json` | spell table 31FA | ids 1..67, the 16 bytes as Core's `SpellInfo` names them (spells-effects.md "Spells") |
| `rules/experience.json` | experience 409B (class × 0x38 + level × 4) | levels 2..9 per class 0..7 (−1: a level the class does not reach `[?]`) |
| `rules/levelStats.json` | THAC0 3C7C, cleric slots 40BC, magic-user slots 41D4, saves 426E, thief level 3CF3, race 3D3B, Dexterity 3D4E | THAC0 classes 0..7 × levels 0..10; slots levels 2..6 × spell levels 1..3; saves classes 0..7 × levels 1..9 × the five saves; thief skills levels 1..9; race adjustments races 1..7; Dexterity adjustments 9..18 (five skills) |
| `rules/turnUndead.json` | 0447 (10 × row + column) | rows 1..10 × columns 1..10, signed |
| `rules/aiSteps.json` | 02AE (5 × row + attempt) | rows 1..6 × attempts 1..5 |
| `rules/randomItems.json` | 0970 (2 × (n + field)) | the 19 entries n = 1 + 8k, eight words each (item-format.md) |
| `rules/temple.json` | 00F1 (41 × k) | the ten heal names, string[40] (non-zero slack kept as `padding`) |
| `rules/combatMap.json` | 2744, 2844, 02D2–0341 | terrain 0..63; icon cells sizes 1..7; ring direction / start, quad direction, start quads, row bands (the wilderness map at 3648 is in `rules/overland.json`) |

A file owns only the cells it names. The tables interleave in the data segment: a class's experience cells for levels
10..13 are the cleric (class 0) and magic-user (class 5) spell slots; the cells of levels 0 and 1 are other data `[?]`;
saves and THAC0 rows past a class's last level are the next class's; thief race 0's row is thief level 9; terrain 64
and 65 are icon size 0's cells; random-item word 0 and AI attempt 6 of row 6 are other data. Those cells stay only in
`program.json`, and a program read that lands on them reads what the program reads. No two files own the same byte.

The engine reads `program.json` first, then writes every decoded file's cells over its data segment and takes the
files' offsets (`PorRulesTables.Load`, from `PorModule.Program()`), so Core's readers stay as they are and a changed
field changes the table (tested). Without decoded files the raw path is used as before. With any of them present, the
rules profile requires all eight, each naming that profile, else the module fails on its first program read
(`RulesTablesException`). The decoded files rebuild the program's bytes cell for cell (`RULES` in `PorRoundTrip`),
and the tables read from a module equal those read from the program files (`RulesTablesTests`).

`program.json` stays: the program reads past table ends and between them (above), and the strings (item words, camp
line, effect names), the NPC portrait bytes and the view tables are not decoded. Not tables, so not in `rules/`:
temple prices (immediates in each temple routine, Core `TempleRules.Price`), the race level limits (code, START
0x3AF7, `LevelStats.RaceLimitStops`). The Strength to-hit and damage bonuses (START 0x1E22 / 0x1EC5) and the Dexterity
AC and missile adjustments (0x1C86 / 0x1D08) are given by strength or Dexterity ranges and arithmetic in
character-format.md (Core `StatRecalc`); no DS table is known for them `[?]`.

The long-term list:

- combat tables (THAC0, saves, to-hit, damage), level and XP tables, the spell table, race and class limits;
- the random-treasure tables, shop and temple price tables;
- the overland tile table and special locations: **done (s18)**, `rules/overland.json` (section 6), referenced from
  `areas/`. Its bytes stay inside `rules/program.json`'s data segment too; the engine reads the overland document.

Each file names the profile and the program offset it came from. A file is decoded where the spec is settled and raw
where it is not. The rules profile checks that every table it needs is there when the module loads.

## 13. Text (`text/<lang>.json`)

```json
{ "program.menu.camp": "CAMP", "program.0x1A2B": "…", "frua.label.1.42": "…" }
```

- **As built for PoR (s16):** `strings` keyed by the Client's message names (`Msg.*`, `CombatMsg.*`,
  `EffectLabel.n`): the offsets are in `PorTextCatalog` (GAME.OVR code-segment strings and three START.EXE image
  strings, v1.3 only), plus `spellNames` and `pagePrompt`. Strings in the data segment (menus, item words, temple
  names, spell and level names) are read from `rules/program.json` and not repeated here.
- Program strings, read from the EXE by offset per release (the offsets differ between v1.0 and v1.1 in DQK), keyed
  by a stable name where the engine uses them and by offset otherwise.
- Area text pools of FRUA designs live with the events (section 9). Script text lives in the scripts (section 8).
- The printed journal is not in the files, so it is never imported. A module may add `journal.<n>` entries itself
  (community modules, or a player's own transcription for the in-game journal QoL option).
- Case is kept as written: FRUA and DQK script text is upper-case only.

## 14. Transfer (`transfer.json`)

What a character may bring in and take out: level caps, item stripping, gold reset, and the source games or modules
accepted (`from: ["por", "frua:*"]`). The original chains are built in (PLAN §5). The character manager chains
conversions (PoR → Curse → Silver Blades → PoD), so `transfer.json` names only its own direct sources. Format details
belong to phase 4 `[?]`.

## 15. Saves (engine side; recorded here because the module fixes them)

- An engine save names the module (id, version, a content hash) and holds: VM memory (the regions the VM profile marks
  as saved), position, party, clock, and either the current script bytes (VM profiles that save them, PoR and
  Curse) or the script key (SotSB, PoD, DQK, FRUA; SotSB keeps the id in VM $4BF2).
- **Import of original saves**: the importer converts `SAVGAMx.DAT` + `CHRDAT*` into an engine save. PoR's stored
  script is checked against `scripts/0/<id>.ecl`. If they differ (a self-modified script), the stored copy wins, as in
  the game.
- Shipped saves (PoR's ready-made party in slot A/J, FRUA premade `.CCH`, DQK's quick-start party) go to `saves/`.

**As built for PoR (s18):** one JSON file per saved game, `EngineSaveDocument` (`src/GoldBox.Data/Module/SaveDocuments.cs`,
schema `save.schema.json`); the converter is `PorEngineSave`. Slot X is `x.json` (`saves/a.json`, `saves/j.json` in the
module; `a.json` … `j.json` in the Client's save folder).

```json
{
  "formatVersion": "0.1",
  "module": { "id": "por", "version": null, "contentHash": "e46a…" },
  "vm": "ecl-por-1.3",
  "memory": [ { "name": "4900", "start": 18688, "cellBits": 16, "cells": "<base64>" }, "… 6B00, 9700, script" ],
  "position": { "x": 14, "y": 5, "facing": 4, "wallAhead": 0, "eventByte": 0 },
  "script": { "key": { "kind": "script", "bank": 0, "id": 20 }, "disk": 2, "matchesModule": true },
  "party": [ { "stem": "CHRDATJ1", "fields": {…}, "raw": "<285 bytes>", "items": [ { "fields": {…}, "raw": "…" } ],
               "effects": [ … ] }, "…" ],
  "clock": { "year": 0, "month": 0, "day": 0, "hour": 10, "minute": 56 },
  "por": { "slot": "J", "disk": 2, "locationType": 1, "interfaceState": 2, "partySize": 6, "nameSlots": "<328 bytes>" },
  "source": { "container": "SAVGAMJ.DAT" }
}
```

- `memory`: the regions the profile marks `Saved`, in the profile's order, with name, first address and cell width
  checked on load. 16-bit cells are little-endian words. For PoR the `script` region is the 7,680-byte script area: the
  stored script bytes. They win on load. `script.matchesModule` records whether they equalled `scripts/0/<id>.ecl`
  (prefix dropped, zero-filled) when the save was written; it is informational. Both shipped saves match.
- `party`: in party order, raw record bytes plus a `fields` view (§11 shapes). `items` / `effects` are null when the
  original had no `.ITM` / `.SPC` file (absent = none, as the program writes). `stem` is the file stem the original
  named, kept so a renamed set round-trips; `por.slot` is the `SAVGAMx.DAT` letter, or the first stem's when the file
  name is not known. `itemsTrailing` / `effectsTrailing` keep bytes after the last whole record of a file. There are
  none in the shipped saves, and the load ignores them as the program does (it reads whole records until the end). A
  null `party` means the original's party files could not be read; the
  game then runs without a party, as before.
- `por`: the rest of `SAVGAMx.DAT`. The disk byte is kept apart from cell $6E12. The name slots are kept raw because
  they hold leftover memory after each stem and in the unused slots.
- `clock` is a read-only view of $49C6–$49CC. The memory region is what a load uses (raw is authoritative, §11).
- `module.contentHash` (`ModuleContentHash`): SHA-256 over `scripts/`, `maps/`, `areas/`, `events/`, `records/` and
  `rules/` of each module in the chain. Each file goes in as its lower-case relative path and its bytes, sorted. Art,
  text, saves and the manifest are left out, so the Amiga overlay or a re-import with the same game data keeps the hash (a second import of v1.3 gives the same hash and the same engine saves, tested).
- **Load checks** (`PorEngineSave.Check`): the format's major version, the VM profile (must be the content's), the
  saved regions, the module id (must be the content's) and the module version (must be the content's when both name one;
  decided s21). The load refuses a mismatch; the party menu shows "SAVE X COULD NOT BE LOADED." (Client only). A
  different content hash with the same version (a re-import by a newer importer) is loaded with a `[?]` note in the VM
  diagnostics, since the save carries its own script and maps and records come from the module.
- **Round trip**: `PorEngineSave.ToOriginalFiles` writes `SAVGAMx.DAT` and the `CHRDATxn.SAV/.ITM/.SPC` files back.
  Every one of the 36 shipped save files is rebuilt byte for byte (`PorRoundTrip` kind `ENGINE SAVE`, which also
  checks that no file the original names is left out). A slot letter other than the save's own renames the set: stems
  `CHRDAT<letter>n`, zeros after them, as the program's save writes them.
- **Loading**: `PorGame.FromSave` takes an engine save (`.json`) or an original `SAVGAMx.DAT`. An original is
  converted first, with its party files looked up next to it, then in the content's save folder (a game folder's).
  Both forms then load through one path: engine save → `PorSaveGame` → the program's Load. A game from `saves/a.json`
  equals the game from the folder's original files in VM state, party records, program variables, entry run and over
  a walk (A and J, `PorModuleTests`). `PorArea.FromSave` reads an original file directly. The capture replays take a
  shipped save from the content through `PorShippedSave`: a game folder's original files or a module's `saves/x.json`.
- **Client**: the camp's Save writes `x.json` into the player's save folder: the bytes the program's save builds,
  converted with the party in memory. `--save X` looks for, in order: the player's `x.json`, the player's
  `SAVGAMX.DAT` (from earlier Clients), then the content's `x.json` and `SAVGAMX.DAT` (a module has only the
  first). `--save FILE` takes either form. The Load menu's highlight counts both forms as slots.
- **Tools**: `goldbox-import save-export SAVE.json OUTDIR [SLOT]` writes the original files for the DOS game.
  `goldbox-import save-import SAVGAMx.DAT MODULEDIR OUT.json` converts an original save for a module.
- The importer writes `saves/a.json` and `saves/j.json` after the manifest. Since importer 0.7.8 it no longer
  copies the original files into `saves/`: they are manifest inputs only, and the engine saves rebuild them exactly
  (`PorRoundTrip` kind `ENGINE SAVE`). DOSBox-X checks and `gbchar.py` take the originals from the game folder.

## 16. What the PoR importer writes (mapping)

| Original | Module |
|---|---|
| `ECL<d>.DAX` block n | `scripts/0/<n>.ecl` (+ `.json`: prefix 0x1388, disk d) and `areas/<n>.json` |
| `GEO<d>.DAX` block n | `maps/0/<n>.json` (16 × 16, `wrap`, 9 layers, header word) |
| `WALLDEF<d>.DAX` block n | `art/dos/walldef/<d>/<n>.json` (raw record) |
| `8X8D<d>.DAX` block n | `art/dos/8x8d/<d>/<n>.png` + `.json` (block 201 of disk 1, the font: raw) |
| `MON<d>CHA/ITM/SPC.DAX` block n | `records/monsters/<d>/<n>.json`, `records/itemLists/…`, `records/effectLists/…` (monster) |
| `ITEM<d>.DAX` block k | `records/treasureLists/<d>/<k>.json` (placed treasure; the ids collide with `MON<d>ITM`, below) |
| `ITEMS` | `records/itemTypes.json` |
| every other DAX (`PIC`, `CPIC`, `HEAD`, `BODY`, `CHEAD`, `CBODY`, `SPRIT`, `COMSPR`, `DUNGCOM`, `WILDCOM`, `RANDCOM`, `BACPAC`, `SQRPACI`, `TITLE`, `ICON`, `FINAL5`) | `art/dos/<container>/<bank>/<id>.json` + `.png` |
| Amiga `*.dax` (art files) | `art/amiga/<container>/0/<id>.json` + `.png`, palette from Amiga `program` (`--amiga`, §10) |
| START.EXE / GAME.OVR tables | `rules/program.json`, the decoded `rules/*.json` (§12), `rules/overland.json` (+ `overland.column` in areas 25–27), `text/en.json` |
| `SAVGAM?.DAT` + `CHRDAT*` | `saves/<slot>.json` (engine saves, §15; the original files are not copied since 0.7.8) |

`ITEM<d>` and `MON<d>ITM` both use block ids per disk. **They collide** (checked by the importer on v1.3, s15): 11 ids
on 5 disks are both, with different bytes every time: disk 1: 6, 9, 22; disk 2: 1, 2, 3, 5; disk 3: 51, 53; disk 4:
29; disk 8: 32. So placed treasure has its own kind, `treasureList`.

PoR v1.3 import (s16): 29 scripts and areas, 29 maps, 172 monsters, 105 monster item lists, 42 effect lists, 46
treasure lists, 128 item types, 822 art blocks (783 pictures, 39 raw: the WALLDEF records and the font), 266 program
texts, 36 save files; every block (and the `ITEMS` file, the program data, the saves) rebuilds byte for byte, from the
files and again through the engine's readers (2,489 comparisons). The module is about 12 MB. Since s18 the overland too
(`rules/overland.json`: tiles and special locations as bytes, through the engine, and the 29 areas' columns): 2,523;
since s60 the land recovery's tables (seven byte tables and the squares, and through the engine): 2,532.
With `--amiga` (Amiga v1.0, s17): 508 Amiga pictures over 16 files (every picture block except the three left out), each rebuilding to its decoded Amiga block (`PorAmigaArt.Check`); 5 MB more.

## 17. Implementation order

1. `GoldBox.Data/Module`: the manifest, resource keys, a resolver over the module plus its dependencies, readers for
   maps, scripts and records. Interfaces replace `PorGameDirectory` where Core reaches data.
   **Done (s15–s16):** `IPorContent` (Data; implemented by `PorGameDirectory` and `Module.Por.PorModule`) serves
   scripts, maps, monsters, item/effect/treasure lists, item types, the program's tables and texts
   (`PorProgramData`), art (`Picture`, `ArtData`: wall sets, symbols, font, combat art), saves and the no-wrap rule.
   `PorGame`, `PorArea`, `PorViewArt`, `PorFight`, `PorEncounterData`, the capture replays and the Client's screens
   take only an `IPorContent`.
2. `GoldBox.Importer` (CLI `goldbox-import por GAMEDIR OUTDIR [--amiga AMIGADIR]`): scripts, maps, records, item
   types first. Then art (DOS, then Amiga), rules tables, text, saves. **Done (s16; the Amiga overlay s17; the
   overland `rules/overland.json` s18, importer 0.6 after the s18 merges; `audio/dos/pcsound.json` and new `PartyMsg`, `CombatMsg`, `Hall`, `End`, river texts s19, importer 0.7; the icon editor's size and hair / face words `PartyMsg.Icon*` s20, importer 0.7.1; HMENU's one-word swap `Msg.HmenuButton` / `Msg.HmenuEnter` s21, importer 0.7.2; the encounter menus, messages and parley attitudes `Msg.Encounter*`, `Msg.BothSidesWait`, `Msg.MonstersFlee`, `Msg.ParleyAttitudes` s21, importer 0.7.3; the introduction's credits `Intro.0`–`Intro.28` s22, importer 0.7.4; the Amiga party-icon colour pairs `art/amiga/icon-colours.json` s22, importer 0.7.5; the Amiga intro's credits panels `art/amiga/intro/0/2`–`/9` s24, importer 0.7.8; the Amiga players' data `audio/amiga/sound.json` s25, importer 0.7.9; the spell effect messages `EffectMsg.*` s25, importer 0.7.12; the area map refusal `Msg.NoAreaMapHere` s25, importer 0.7.13; the monster specials' messages `CombatMsg.Handler*` s27, importer 0.7.14; the status-change messages `CombatMsg.Status*`, `CombatMsg.Poisoned`, `Rots`, `Drained*` and the fight-start line `CombatMsg.BattleBegins` s29, importer 0.7.18; revive, escape and seven handler texts `CombatMsg.StatusRevived*`, `StatusEscapes`, `Handler*` s30, importer 0.7.19; the guard word `CombatMsg.Guarding` s30, importer 0.7.20; the Amiga rune font `art/amiga/protect/0/8` s38, importer 0.7.22; the scroll list title `Msg.OnScroll` s38, importer 0.7.23; the Amiga text font `art/amiga/poolrad/0/8` and `art/amiga/text-pens.json` s39, importer 0.7.24; the menu and key-prompt pens and the Amiga window frame `art/amiga/frame/0/1`–`/5` s39, importer 0.7.26).** The importer and the round
   trip live in `GoldBox.Data/Module/Por/` (the Client imports with them); the CLI stays in `GoldBox.Importer`.
3. **Round-trip test:** for every raw resource, module → original block must be byte-identical to the DAX block. All
   existing real-data tests also pass with the Client and Core reading the module instead of the game folder.
   **Done (s15–s16)** for every imported kind (`PorRoundTrip`, also run by the CLI); the same scripts, saves and golden
   DOSBox captures run identically from the module alone (`PorModuleTests`: art, program and saves from the module
   too), and the Client's texts, tables and combat art match (`ModuleResourcesTests`).
4. Client `--module DIR` next to `--game DIR`. `--game` imports to a cache folder first, then loads the module.
   **Done (s16):** `--module DIR` needs no game folder. `--game DIR` imports into
   `$XDG_CACHE_HOME/goldbox/modules/por-<key>` (`PorModuleCache`; the key hashes the importer version and the
   folder's files) and plays from there, so the Client always runs from a module. Old cache folders are not removed.
   **s18:** `--amiga DIR` (remembered; or `GOLDBOX_POR_AMIGA_DIR`) imports the Amiga overlay with the folder; the cache
   key then also hashes the Amiga files the overlay reads (`PorAmigaArt.InputFiles`: `program`, the overlay `.dax`
   files, `intro` when present), so a cache without the overlay is not reused.
   s18: `text/en.json` gains the View page's labels (`Msg.NpcTag`, `WeaponLabel`, `ArmorLabel`, `AcLabel`,
   `HpLabel`, `Thac0Label`, `DamageLabel`) and the fight's cast message words (`CombatMsg.CastsASpell`, `SpellLabel`); importer 0.6 (s18 merges), so caches made before are rebuilt. The page's DS name
   tables (gender, race, alignment, class, status, coins) are read from `rules/program.json`'s data segment.
5. JSON schemas under `src/GoldBox.Data/Module/Schemas/`, tested against the importer's output. **Done (s16):**
   generated from the document types (`ModuleSchemas`, `goldbox-import schemas DIR`); a test checks the files match
   the types and that every imported JSON file reads strictly (no unknown members) into its type. [?] Instances are
   not validated against the schema files themselves (no validator in the BCL).
6. The VM profile (§5.1). **Done (s17)** for PoR: `EclProfile` / `EclProfiles` in `src/GoldBox.Core/Ecl/Profiles/`,
   resolved from the manifest's `vm`. The VM takes its layout, opcode and IF-skip tables from it. A pure refactor:
   every test and every `ecl-compare` capture gives the same result as before.
   SotSB surveyed (s18, `docs/re/survey-sotsb.md`). **Decode profile done (s24):** `ecl-sotsb-1.3`
   (`EclProfiles.Sotsb13`) decodes every SotSB block (`ecl-check --vm ecl-sotsb-1.3`). **Runs headless (s28):** its
   handlers, character view and moved cells (`survey-sotsb.md` §10); the game host around the VM is still PoR's (§5.1).
7. Engine saves (§15). **Done (s18)** for PoR: `EngineSaveDocument` + `PorEngineSave`. The importer converts the
   shipped saves (`saves/a.json`, `saves/j.json`), and they write the 36 original files back byte for
   byte. `PorGame.FromSave` loads either form through one path. The Client's camp Save writes engine saves, and
   `--save` prefers them. Schema `save.schema.json`; the importer's and the Client's saves read strictly.
8. Decoded rules tables (§12) and the rules profile (§5.2). **Done (s18)** for PoR: eight `rules/*.json` files with
   named fields, laid over `program.json`'s data segment by the engine; each rebuilds its bytes (`RULES` in the round
   trip, 8 files), the module's tables equal the program's, a changed field changes the table, and the schemas and
   strict reads cover them.

## 18. Open `[?]`

- JSON arrays vs a binary sidecar for large layers and raw blobs. JSON is used until size or load time says otherwise.
- Settled for PoR (s19): the start-up sequence (party menu, new game vs load; `docs/re/start-menu.md`). Characters made
  at the party menu are roster files (the original's `charlist.txt` plus one save party entry per character), not
  module data. Open: whether `start` should name the PoR new-game script and square (it is informational now), and
  the other games' start-up.
- DQK's 6 map-header bytes and edge rule. Settled: PoD drops the same 2-byte prefix as PoR (`survey-pod.md` §3.1)
  and exits on RETURN with an empty stack (`survey-pod.md` §3.4; Curse and SotSB already do, `survey-sotsb.md`).
- VM profiles beyond PoR: a SotSB profile needs the region bases and the character redirection list as profile data
  (§5.1); the no-wrap script set is per game (PoR {0, 10}, SotSB {0, 80}; `noWrapOffMap` in `areas/` carries it).
- SotSB's new-game start: script 3 (disk 1) or 17 (disk 2), chosen by a program flag `[?]`; a load restarts the
  script whose id is in VM $4BF2. Same open question as PoR's start-up sequence.
- Settled (s15): PoR's `ITEM<d>` and `MON<d>ITM` ids collide (section 16); `treasureList` added.
- The zip form of a module (section 3): only folders are read so far.
- The importer does not detect the PoR release; it writes `por-1.3` / `ecl-por-1.3` (the engine supports v1.3 only).
- Settled (s18): no `vm` means no ECL VM (section 4); the first `vm` in the dependency chain applies; PoR content
  without one is refused.
- `source.inputs` hashes the files read (START.EXE and GAME.OVR included since s16); a test copy with a patched
  `GAME.OVR` hashes differently from a clean install.
- Rules tables (s18): `program.json` is still needed (reads past table ends; strings, portraits and view tables not
  decoded). The decoded row ranges (levels, races, Dexterity) are the ones PoR's classes reach, inferred from where the
  next table starts and the AD&D tables, not from a code bound `[?]`; −1 in the experience table and the cells of
  experience levels 0 and 1 `[?]`. When an editor changes a decoded table, other games' profiles need their own files.
- The Amiga overlay (s17; drawn by the Client s18): HEAD's x offset (16), and the palettes of HEAD/BODY/ICON
  (table 2) and the combat icons and grounds (table 3) are inferred, not traced (docs/re/por-art-formats.md §4).
  Client side (s19, docs/re/por-art-formats.md §4 "Amiga program reads"): under Amiga walls the backdrop takes the
  Amiga program's pens; RANDCOM is the Amiga set as the Amiga draws it; the party icons are built the Amiga way
  (`art/amiga/icon-colours.json`). `[?]`: the EGA → pen move of the icon colours is from the art (EGA 5 and 13, absent from the
  art, take the nearest table-3 pen by RGB, 6 and 7: user decision s28); the sprite frame per
  distance and the use of the block x, y are from the art; the Amiga PIC animations are composed as the Amiga program
  composes them (XOR with the first frame) but timed with the DOS rule (the Amiga times them by poll passes). The title painting is optional (disk A's `intro`).
- Settled (s18): the PoR overland tile table and special locations are in `rules/overland.json`, placed by areas
  25–27 (section 6). Open: what the special-location VM cells mean, and the tile file the view draws from.
- Saves (section 15): a save from another module version is refused (s21), other content with the same version loads
  with a note. The shipped original save files are no longer copied into `saves/` (importer 0.7.8): the engine saves
  replace them.
- Areas: computed LOADFILES operands are listed when the code before them fixes them (s18; PoR 3, 5, 24); block 5's
  $9AD8 is the one left `[?]` (section 6).
- Fields over raw: when an editor changes records, which wins (section 11).
- Script text translation keys (section 8).
- How a 256-colour game's screen palette is assembled from per-asset ranges (DQK, PoD art specs pending).
- FRUA hacks (replaced engine files, EXE patches): imported as data overrides where possible, reported otherwise.
