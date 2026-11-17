# GEO 3D map format (DOS Gold Box, Pool of Radiance)

Status: **layout verified** against every GEO block of Pool of Radiance DOS v1.1, v1.2 and v1.3 (29 blocks, byte-identical
in all three versions) and against 34 of S. S. Lee's hand-drawn area maps (2026-11-17, see "Verification").
Edge wrap, the door menus, door state after a save and the engine's use of the event byte were checked in
GAME.OVR and in DOSBox-X (2026-11-17; method `docs/re/dosbox-trace-method.md`). Which ECL block loads which map
rests partly on a heuristic script scan (see `ecl-format.md` for the exact list); open points are marked `[?]`.
Container: `docs/re/dax-format.md`. Decoder: `tools/scripts/geo.py`.

## Sources

- Stephen S. Lee, *Pool of Radiance – Exhaustive Game Information* v2.00 (2026-04-02), CC BY-NC 4.0: 12.1.3 file manifest
  (GEO/WALLDEF sha1 = ours), 12.2.5.1 "3D Map Format", 12.2.5.2 "Wilderness Map Special Location Format", 12.3.3 ECL
  opcodes 0x21 LOAD FILES / 0x37 LOAD PIECES, 12.4.2 VM addresses $C04B–$C04F, chapter 6 area maps (ASCII, with
  coordinates). https://gamefaqs.gamespot.com/pc/564785-pool-of-radiance/faqs/73869
- Simeon Pilgrim, coab (CotAB C# port): `Classes/GeoBlock.cs` (planes), `engine/ovr031.cs` (map accessors, wall-set
  loader, 3D view), `engine/ovr003.cs` (LOAD FILES / LOAD PIECES). https://github.com/simeonpilgrim/coab — read only.
- Gold Box Explorer, `src/Common/Plugins/GeoDax` (MIT). https://github.com/bsimser/Gold-Box-Explorer — read only.
- DrEvil/TitaniumHelix PoR remake, `decode/data/GEO.md`, `WALLDEF.md` (no licence in tree) — read only.
- talonius/pool-of-radiance, Turbo Pascal reconstruction of PoR (no licence; reference only): `view3d.pas`,
  `explore.pas`, `eclvm.pas`, `mapview.pas`, `wilds.pas`.

Lee describes the map as "a 16*16 array of 4-byte records". The bytes are actually stored as **four separate 256-byte
planes** (coab, talonius, DrEvil all read it that way). Against six of Lee's own area maps, the planar reading matches
98.5–100% of edges and picks the right block every time. The interleaved reading is at chance (49–52%) and picks the
right block for none of them. Lee's field list (walls N/E, walls S/W, event, barriers) is right; the interleaving is
not.

## Layout

All PoR GEO blocks have raw size **1026 (0x402)**; the engine refuses any other size. Integers little-endian.

```
offset   size  content
0        2     u16 = 0x0400 in all 29 blocks (size of the plane data); skipped by the loader
2        256   plane 0: walls N/E   byte = (N wall type << 4) | E wall type
258      256   plane 1: walls S/W   byte = (S wall type << 4) | W wall type
514      256   plane 2: event byte  (see "Event byte")
770      256   plane 3: barriers    bits 1-0 = N, 3-2 = E, 5-4 = S, 7-6 = W
```

Each plane is row-major: byte `y*16 + x` belongs to square (x, y), 0 ≤ x, y ≤ 15.

### Coordinates and orientation

- x grows **east**, y grows **south**; north is y−1. Square (0,0) is the north-west corner.
- Facing uses the Gold Box 8-way code: 0 N, 2 E, 4 S, 6 W (odd values are diagonals, not used on 3D maps). Lee's VM
  variable $C04D stores the cardinal facing as 0–3.
- Every square stores **its own four faces**. The edge between (x,y) and (x+1,y) is the E face of the first square
  and the W face of the second; they are separate data. Over all 29 maps, 13,920 interior edges were checked:
  - both faces in the same class (no wall / impassable / barrier 1 / 2 / 3): 13,262; impassable wall on one face and
    no wall on the other (wall seen from one side only): 540; impassable on one face, barrier 1 on the other: 44;
    other mixes: 74 (barrier 1 vs 2: 42; no wall vs barrier 1: 18; barrier 1 vs 3: 12; impassable vs 2: 2).
  - where both faces have a wall, the wall types differ on 1,605 of 5,408 edges (each face carries its own texture).
- Movement checks only the face of the square the party stands on (talonius `MoveForward`; coab `WallDoorFlagsGet`),
  so a one-sided wall is one-way. Opening a door changes both faces.
- **Edges: the map wraps** (checked 2026-11-17, program read and DOSBox-X).
  - The three map accessors (barrier GAME.OVR 0x2EC47, wall 0x2ED72, event 0x2EE71) wrap a coordinate outside
    0..15 to the other side, except that while the current ECL block (DS:84DC) is 0 or 10 they return 0 for a
    square off the map. That rule only matters for lookups beyond the edge (e.g. the 3D view); the party's own
    square is always on the map.
  - On a forward step (Up key, 0x16D58) the engine first clears VM $6DD5, and when the face is not impassable and
    the step would leave the grid it sets **$6DD5 = 1** ("left the map") without moving. Then vector 0 (move) runs;
    a script that handles the exit sets $6DC9 = 255, which cancels the step (0x17443) and loads another area. If the
    script does nothing, the step routine (0x16E06) moves the party and wraps x/y to the other side.
  - DOSBox-X test: a copy of save A with the Slums script's vector 0 pointed at an EXIT (so the exit is not handled),
    party at 15,4 facing E, one step: party at **0,4** on the same map, $6DD5 = 1, previous x $49F0 = 15. With the
    unmodified script the same step leaves the Slums (the script checks $6DD5).
  - The wrap is also real data: the Valjevo Castle hedge maze is drawn across the inner corners of four 16×16 maps,
    and Lee's maps of the wilderness lairs use wrapped coordinates.

### Walls (planes 0 and 1)

A 4-bit wall type per face:

| value | meaning |
|---|---|
| 0 | no wall (the engine's barrier lookup returns 1 = passable) |
| 1–15 | wall graphic: wall-set **slot** `(t−1) div 5 + 1` (1..3), wall **`(t−1) mod 5`** (0..4) inside that slot's WALLDEF block |

The wall type picks the picture only. Whether the face can be crossed is the barrier (plane 3). A drawn wall with
barrier 1 is an archway, an open door or scenery such as hedges, trees and fences. The GEO data can't tell these
apart; only the wall graphic differs.

Counts over all 29 maps (4 × 256 × 29 = 29,696 faces): type 0 on 16,898 faces; types 1–15 on the rest; every type
1–15 occurs.

### Barriers (plane 3)

Two bits per face, read only when that face's wall type is non-zero:

| value | meaning | faces with a wall | sources |
|---|---|---|---|
| 0 | impassable | 9,780 | Lee "Impassable"; engine refuses the step |
| 1 | passable | 2,876 | Lee "Passable" |
| 2 | locked door: menu Bash / Pick / Knock / Exit (below) | 128 | Lee "easy-to-open barred door"; GAME.OVR 0x17480; DOSBox-X |
| 3 | harder door: menu Bash / Pick / Knock / Exit, but Pick never opens it and Bash needs Strength 18/91+ (below) | 14 | Lee "hard-to-open barred door"; GAME.OVR 0x175BF |

- 88 faces without a wall store barrier 1 and 2 store barrier 2. The engine ignores these values, but `dump` reports them as
  stored.
- At run time the engine edits plane 3 in memory. Opening a door sets the face to 1 and the facing face of the
  next square too (0x167D4, called twice). When a locked door's menu would offer only Exit, that face is set to 0
  (0x166CB): it becomes a plain wall.
- **These edits are not saved.** The save game has no map data (`save-format.md`), and loading a save re-reads the
  GEO block from the DAX (loader 0x2FA40). DOSBox-X check: the Slums door at (10,2) N was bashed open (plane 3 byte
  0x16 → 0x15), the game saved to slot B and loaded again: the byte was 0x16 again (locked). Re-entering an area
  runs LOADFILES, which reloads the map the same way, so doors should re-lock then too (code read, not tested).

**Door menu** (walking into a barrier 2 or 3 face; GAME.OVR 0x17443–0x1771B, door routines 0x1695F–0x16D57). The
menu is built from three DS flags, DS:6DDE (Bash), DS:6DDF (Pick), DS:6DE0 (Knock), plus Exit, which is always there.
Where the flags are set back to 1 is not traced `[?]` (they persist at least while the party stays at the door).

- **Shown:** Bash if DS:6DDE. Pick if DS:6DDF and someone in the party has a thief level. Knock: for barrier 2 if
  DS:6DE0 and someone has spell 31 (Knock) memorised; for barrier 3 if DS:6DE0 and someone has a magic-user level.
  If only Exit is left, the face becomes barrier 0 (above).
- **Bash** (both types): party members are tried in order until one succeeds; a success opens the door and the
  party steps through. Barrier 2: Strength 3–7 needs 1 on 1d6, 8–15 ≤ 2, 16–17 ≤ 3, 18/01–18/50 (and plain 18) ≤ 3,
  18/51–18/99 ≤ 4, 18/00 ≤ 5; Strength 19 and up is not in the table and never succeeds (a quirk). Barrier 3:
  18/91–18/99 needs 1 on 1d6, 18/00 ≤ 2, 19–20 ≤ 3, 21–22 ≤ 4, 23 ≤ 5, 24 ≤ 7 on 1d8, 25 always; any member tried
  with less sets DS:6DDE = 0, so Bash leaves the menu for later tries.
- **Pick**, barrier 2: members in order, success if 1d100 ≤ open-locks skill (creature 0x78) and status is okay
  (0x10C = 0). After one try, successful or not, DS:6DDF = 0 and Pick leaves the menu. DOSBox-X: with save A's party
  the menu showed Bash, Pick, Exit; Pick failed, and the next try showed Bash, Exit only; Bash then opened the door.
  Barrier 3: choosing Pick only sets DS:6DDF = 0; it never opens the door.
- **Knock** (both types): finds the first member with spell 31 memorised, clears that spell slot and opens the door;
  without such a member nothing happens.
- Dice: the game's dice routine (far call to GAME.OVR unit stub B0:48) with (count, sides).

### Event byte (plane 2)

One byte per square, copied into the ECL variable Lee lists as $C04F ("event number of current square") after every step.
The engine itself uses only:

- **bit 7**: when set (byte > 0x7F), the 3D view takes the sky colour from VM $49FE instead of $49FD (GAME.OVR
  0x2DD2A). DOSBox-X: in the Slums the sky turned from cyan to white when the party stepped onto a 0x80 square.
  2,216 squares hold exactly 0x80.

All other bits are a convention of each area's script. Checked 2026-11-17: the engine stores the byte in DS:6AB1
after each step (0x3A52, 0x3F0E, 0x8F20, 0x16E93, 0x2DD27) and hands it to the scripts as $C04F (VM reader 0x840F);
the only test the engine makes on it is the bit-7 sky test; plane 2 is read only by the event accessor 0x2EE71. Lee's location numbers match the **low 6 bits** on 407 of the 448
numbered squares we compared (see "Verification"). Valjevo Castle uses its own numbering, which accounts for most of the
misses. Lee notes that the top 1–3 bits "are often flags". Prior art reports that scripts test 0x20 and 0x40 around
random encounters `[?]`. An importer must hand the raw byte to the script.

## Wall sets: how a map gets its graphics

**A GEO block names no wall set.** The area's ECL script loads the graphics with opcode **0x37 LOAD PIECES a, b, c**
(Lee 12.3.3; talonius `EclLoadFiles`; coab `CMD_LoadFiles`):

- `a`, `b`, `c` are WALLDEF block ids for slots 1, 2, 3, read from `WALLDEF<n>.DAX`, where `<n>` is the current disk
  number (the digit of the area's ECL/GEO file).
- `a = 127` loads WALLDEF block **0** into slot 1. Only `WALLDEF3/0`, New Phlan's three-block set, uses this.
- `255` leaves a slot empty (its cache entry is cleared).
- When two area flags are both set, only `a` → slot 1 and `c` → slot 3 are loaded. This is how a two-block WALLDEF in
  slot 1 can fill slots 1–2 (talonius `mapGraphics1/2`; coab `field_1CE/1D0`) `[?]` which areas set the flags.
- Scripts can also load a set by writing a value > 0x80 to VM words $322/$324/$326, which loads WALLDEF id (value & 0x7F)
  into slot 1/2/3 (talonius `eclmenu.pas`) `[?]`.

The map itself is loaded by **0x21 LOAD FILES m, _, _** (GEO block id `m`; 255/127 = none) when the area is a 3D area.
One script may load several maps (see the table).

### WALLDEF / 8X8D, as far as a map needs them

- A WALLDEF block is 780 bytes, or 1560 or 2340 for a multi-block record. 780 bytes = 5 walls × 156 bytes of 8×8 symbol
  numbers. A record of k blocks fills slots s..s+k−1 and must fit within slot 3.
- The graphics come from `8X8D<n>.DAX`. A one-block WALLDEF id `i` uses 8X8D block `i`. A k-block record uses 8X8D blocks
  `i*10+1 … i*10+k`, with id 0 counted as 10 (so 101–103). On load, symbol numbers ≥ 45 are shifted to the slot's symbol
  range (the 3D-view spec will cover this).
- The 156-byte layout of one wall (10 pieces for the near/middle/far rows) belongs to the 3D renderer spec. DrEvil
  `WALLDEF.md` documents it; we have not verified it `[?]`.

## Maps and areas (PoR v1.3)

The digit in `GEO<n>.DAX` is the disk number. Block ids are unique across all eight files: 0–32, with 8, 11, 12 and 19
unused. In the table:

- "Loaded by" and "LOAD PIECES" come from `geo.py refs`. It is a byte-pattern scan for opcodes 0x21/0x37 with three
  immediate operands, not a disassembly: it can miss computed operands and could false-match. `[?]` marks blocks where it
  found nothing.
- "FAQ check" refers to the "Verification" section.

| GEO file | id | Area (Lee's FAQ section) | Loaded by ECL block | LOAD PIECES (slot 1, 2, 3) | FAQ check |
|---|---|---|---|---|---|
| GEO3 | 0 | New Phlan, civilised area | 0 (8 and 11 re-load it too) | 127 → WALLDEF3/0 (3 blocks) | yes |
| GEO6 | 1 | Buccaneer Base | 1 | 2, 6, 6 | yes |
| GEO4 | 2 | Cadorna Textile House | 2 | 1, 3, 20 | yes |
| GEO5 | 3 | Valjevo Castle, NW quadrant + NW part of the hedge maze | 3 by the same-id convention `[?]`; the scan finds no immediate LOAD FILES in block 3; block 7 (inner tower) loads it, probably on exit | 24, 25, 1 (block 3) | yes |
| GEO5 | 4 | Valjevo Castle, NE quadrant + hedge maze NE | 4 | 24, 25, 1 | yes |
| GEO5 | 5 | Valjevo Castle, SE quadrant + hedge maze SE | 5 by the same-id convention `[?]`; the scan finds nothing in block 5; Lee labels this quadrant "ECL Script 3" and the hedge maze "ECL Script 5" (unresolved) | `[?]` | yes |
| GEO5 | 6 | Valjevo Castle, SW quadrant + hedge maze SW | 6 | 24, 25, 1 | yes |
| GEO5 | 7 | Valjevo Castle inner tower (two small levels) | 7 | `[?]` | no (FAQ map has no coordinates) |
| GEO2 | 9 | Stojanow Gate | 9 | 1, 9, 23 | yes |
| GEO4 | 10 | Valhingen Graveyard | 10 | 21, 22, 20 (21 = 2 blocks) | yes |
| GEO8 | 13 | Kobold Caves | 13 | 17, 18, 255 | yes |
| GEO3 | 14 | Kovel Mansion | 14 | 1, 3, 5 | yes |
| GEO2 | 15 | Mendor's Library | 15 | 1, 19, 23 | yes |
| GEO8 | 16 | Lizardman Keep | 16 | 9, 20, 23 | yes |
| GEO7 | 17 | Nomad Camp | 17 | 7, 8, 23 | yes |
| GEO1 | 18 | Podal Plaza | 18 | 1, 3, 5 | yes |
| GEO2 | 20 | Slums | 20 | 2, 4, 1 | yes |
| GEO4 | 21 | Sokal Keep | 21 | 1, 5, 9 | yes |
| GEO7 | 22 | Yarash's Pyramid, level 1 | 22 | 1, 3, 255 | yes |
| GEO7 | 23 | Yarash's Pyramid, levels 2–3 | 23 | 1, 3, 255 | yes |
| GEO1 | 24 | Temple of Bane | 24 | 1, 3, 5 | yes |
| GEO6 | 25 | Western wilderness lairs | 25 | 17, 17, 17 | yes (4 lairs) |
| GEO7 | 26 | Central wilderness lairs | 26 | 7, 8, 23 | events only |
| GEO8 | 27 | Eastern wilderness lairs | 27 | 17, 17, 17 | yes (4 lairs) |
| GEO6 | 28 | Zhentil Keep Outpost | 28 | 1, 2, 9 | yes |
| GEO8 | 29 | Kuto's Well | 29 | 3, 20, 1 | yes |
| GEO8 | 30 | Lizardman Keep catacombs | 16 | 9, 17, 23 | yes |
| GEO1 | 31 | Wealthy Area | 24 | 1, 3, 5 | yes |
| GEO8 | 32 | Kuto's Well catacombs | 29 | 18, 17, 1 | yes |

- Every WALLDEF id in the table exists in the same-numbered WALLDEF file, and every 8X8D block the pairing rule asks for
  exists. ECL 10's middle operand (22) names no block; this fits the two-flag rule above (21 fills slots 1–2, 20 goes to
  slot 3) `[?]`.
- ECL blocks 8 and 11 (Lee: the council and the training hall in New Phlan) and 19 (the overland wilderness script) have
  no map of their own.
- In the ECL/GEO ids, 0–32 identify an area, and most areas use the same number for script and map (DrEvil's
  convention, confirmed by the scan wherever it finds a LOAD FILES). The exceptions are GEO 30–32, which are second maps
  of areas 16, 29 and 24. For Valjevo Castle (GEO 3–7), the script-to-map assignment is open `[?]`.
- While ECL block 0 (New Phlan) or 10 (Valhingen Graveyard) runs, the map accessors return 0 for squares off the map
  instead of wrapping (GAME.OVR 0x2EC5D; see "Edges"). Steps still wrap if a script leaves an edge open and unhandled.
- Map names 24 and 31 (Lee's script list has them the other way round) were settled from the area script and the
  program: see `ecl-format.md`, "Scripts and areas".

## Wilderness (overland)

The overland map is **not** a GEO block and not in any DAX file. talonius and DrEvil place a 44 × 36 table of 1-based
tile numbers in START.EXE's data segment, drawn with the BACPAC/SQRPACI tiles. Its format belongs to the overland spec
`[?]`.

**Special locations** (Lee 12.2.5.2): sites whose tile changes with plot state. The record is 6 bytes:

```
+0  u16  x   overland column
+2  u16  y   overland row
+4  u16  tile number (1-based) to draw while the site is shown
```

- PoR has **9** records. Which plot flag shows each record is coded in the engine per record index (talonius
  `BuildWildernessMap`), not in the table. A reimplementation needs those conditions from the engine spec.
- The table sits verbatim in the START.EXE file. v1.3's START.EXE is EXEPACKed, but this table survives the packing
  unchanged. File offsets: v1.1 (47,936 bytes) **33786**, v1.2 (75,536 bytes) **61380**, v1.3 (65,231 bytes) **52740**.
  The 54 bytes are identical in all three. `geo.py special START.EXE --offset N` decodes them. The table contents are not
  copied here.

## Version differences

`GEO1–8.DAX` and `WALLDEF1–8.DAX` are byte-identical in v1.1, v1.2 and v1.3. Their sha1 hashes equal Lee's manifest. The
special-location table is identical in all three. Only its file offset moves.

## Tool: `tools/scripts/geo.py`

```
python3 -I tools/scripts/geo.py list    GEO?.DAX ...
python3 -I tools/scripts/geo.py dump    GEO?.DAX ... [--id N] [--json]
python3 -I tools/scripts/geo.py ascii   GEO?.DAX ... [--id N] [--cell hex|low6|none]
python3 -I tools/scripts/geo.py refs    ECL?.DAX ...            (heuristic scan, see above)
python3 -I tools/scripts/geo.py special START.EXE --offset N [--count 9]
```

`dump --json` prints a list with one object per block, values exactly as stored:

```
{"file": "GEO1.DAX", "set": 1, "id": 18, "header": 1024, "width": 16, "height": 16,
 "squares": [{"x": 0, "y": 0, "walls": [N, E, S, W], "barrier": [N, E, S, W], "event": 0}, ...]}
```

- `squares` is row-major (index y*16+x).
- `walls` holds 0–15.
- `barrier` holds 0–3 as stored. The engine uses it only where the wall is non-zero.
- `event` holds 0–255.
- `set` is the disk digit.

The module also exposes `GeoMap.passage(x, y)` (the effective barrier, 1 where there is no wall) and `wall_slot(t)`.

`ascii` uses Lee's FAQ layout: 3 columns × 2 lines per square, with x labels on top and y labels on both sides. Each
edge is drawn from both faces. On the outer frame only the inside face is drawn, although the map wraps there:

- `' '` no wall on either face
- `#` impassable from both faces
- `+` passable (door, archway, scenery)
- `=` locked door on a face (barrier 2)
- `%` barrier 3
- `!` passable from one face only

Squares show the event byte in hex (`--cell low6` shows the low 6 bits in decimal, for comparison with Lee's numbers).

## Verification (2026-11-17)

Inputs: PoR v1.3 files (WizardWorks image, identical to the talonius byte-exact target), plus v1.1 and v1.2 files
unpacked from `reverse-engineering/dos/original-images/por/`. Scratch scripts (not shipped) import `geo.py`'s decoder, so
the numbers below test the shipped code.

1. **All blocks decode.** 29/29 GEO blocks in v1.3 are 1026 bytes, with header 0x0400. The same holds for v1.1 and v1.2,
   and all 29 blocks are byte-identical across the three versions.
2. **Plane order and bit order against Lee's maps.** We parsed 34 ASCII maps from FAQ chapter 6, using their own
   coordinate labels (including wrapped labels). Every map was scored against all 29 blocks. On drawn edges, the best
   match was the expected block for every map except the Lizardman Keep catacombs. Lee draws that map as solid rock with
   tunnels, so it was assigned to GEO 30 by its blank edges (234/244 open) and by the ECL scan. Totals for the matched
   blocks:
   - Lee draws a wall (`#` or `+`) and GEO has a wall on that face: 10,219 / 11,693. Nearly all misses are solid rock or
     outside areas that Lee fills with `#`: the Lizardman Keep and its catacombs, Kuto's Well catacombs, Kobold Caves,
     Buccaneer Base, Pyramid levels 2–3, Sokal Keep, and the castle walls around the hedge maze.
   - For those faces, Lee's `+` corresponds to barrier ≠ 0 and `#` to barrier 0: 10,149 / 10,219.
   - Lee leaves the edge blank and GEO has no wall on that face: 14,729 / 16,707. The misses are passable scenery Lee
     doesn't draw (graveyard, nomad camp, castle grounds) and the unexplored parts of the partial Valjevo maps.
   - Per map, drawn-edge agreement is 100% on 13 maps (Slums, Kuto's Well, Podal Plaza, Kovel Mansion, Zhentil Outpost,
     Valhingen Graveyard, Nomad Camp, the four Valjevo quadrants, two western lairs) and 97–99.6% on 7 more.
3. **Hedge maze.** The FAQ hedge-maze map is drawn as one 24 × 24 picture with wrapped labels. It matches the inner
   corners of GEO 3 (NW), 4 (NE), 6 (SW) and 5 (SE). Every hedge edge (`"`, 781 of 782) is a wall with **barrier 0**. So
   crossing a hedge, which Lee says costs damage, must be scripted `[?]`.
4. **Event byte.** On numbered squares, Lee's location number equals `event & 0x3F` on 407 of 448 squares. GEO 26, whose
   map Lee doesn't draw, matched all 5 of the coordinates he lists.
5. **ASCII render.** We ran `geo.py ascii --cell none` and parsed its output with the same parser as Lee's maps, then
   diffed the edge glyphs. `=` and `%` count as Lee's `+`, and Lee's other symbols (water, docks, rubble) were skipped.
   - GEO 0, New Phlan: 845/871 agree. 22 of the misses are `!` (one-sided walls) at the docks, where Lee draws `#`.
   - GEO 20, Slums: 1008/1020 agree. The misses are 8 blank→`#` and 4 blank→`+`.
   - GEO 29, Kuto's Well: 958/966 agree.
   GEO 7 (inner tower) was rendered but not compared, because Lee's drawing has no grid coordinates.
6. **Wall sets.** Every WALLDEF block is a multiple of 780 bytes. The largest symbol number is 113–115 in one-block
   records, 185 in two-block records and 255 in the three-block record. This fits slot ranges of 70 symbols each. Every
   WALLDEF id has the matching 8X8D block(s): `i`, or `i*10+1…` for multi-block records. All LOAD PIECES ids found by the
   scan exist.
7. **Special locations.** We found the 9 records at the offsets listed above. They decode to plausible overland
   coordinates (x < 44, y < 36) and agree with talonius's constant table.

**Not checked yet:**

- One-way walls in DOSBox (the step code reads only the party's own face, GAME.OVR 0x17469, which supports them).
- Where DS:6DDE/6DDF/6DE0 (door menu flags) are reset.
- Whether ECL 3 and 5 load GEO 3 and 5 through computed operands.
- The WALLDEF piece layout.
- The overland tile table.
