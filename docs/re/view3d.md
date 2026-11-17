# 3D view, area map and movement (DOS Gold Box, Pool of Radiance)

Status: **verified** 2026-11-17 (s10). Our renderer matches DOSBox-X screenshots of PoR v1.3 pixel for pixel: 9 views
and 5 area maps of the Slums (88 × 88 pixels each; list under "Verification"). The program tables are read from the
player's START.EXE (v1.1, v1.2, v1.3). Open points are marked `[?]`.

Code:
- `GoldBox.Core.Explore`: map accessors, movement, view composition, rasterising.
- `GoldBox.Data.Por`: `PorStartExe`, `PorViewArt`, `PorGeo`, `PorArea`.
- Check: `goldbox-tools view-compare`.

Map format: `geo-format.md`. Pictures: `por-art-formats.md`.

## Sources

- DrEvil/TitaniumHelix PoR remake: `decode/data/WALLDEF.md` and `decode/pseudo/VIEW.pas.md` (routines DrawWallPiece,
  Draw3DView, DrawAreaMap, LoadWallSet, Put8x8Symbol, RefreshView, ExploreCommand, MoveParty). No licence; read only.
- talonius/pool-of-radiance, `view3d.pas` (Turbo Pascal reconstruction; no licence; read only). It agrees with DrEvil
  on every routine used here. It spells out the right-hand far-row passes, which DrEvil only summarises.
- Our own checks: the tables found in START.EXE (below), and the DOSBox-X screenshots.

## Screen

- The view is an 11 × 11 grid of 8 × 8 cells (88 × 88 pixels). On the 320 × 200 EGA screen its top-left pixel is at
  (24, 24) (measured in DOSBox-X).
- The 3D view and the area map share this window. The A command switches between them, unless the area's script set
  $49FB ≠ 0; then the program refuses with a short message `[?]`.

## Program tables (START.EXE)

`PorStartExe` finds the wall-piece table by its structure, not by stored values or offsets:

- 10 start bytes, 10 widths, 10 heights, then 10 column words and 10 row words.
- The starts begin at 0 and the pieces tile 156 bytes exactly.
- Every column and row lies in 0..10.

The table is found exactly once in each version: DS:0C8C, at file offset 33872 (v1.1), 61466 (v1.2) and 52826 (v1.3).
v1.3's START.EXE is EXEPACKed, but this part of its data segment is stored literally.

| DS | Size | Content |
|---|---|---|
| 0C26 | 16 bytes | Sky colour list. The script's sky index ($49FD or $49FE) picks the EGA colour from it. |
| 0C8C | 10 bytes | Piece start in the 156-byte wall record. |
| 0C96 | 10 bytes | Piece width in cells. |
| 0CA0 | 10 bytes | Piece height in cells. |
| 0CAA | 10 words | Piece column in the view, before the per-square offset. |
| 0CBE | 10 words | Piece row in the view. |
| 2722 | 5 words | First symbol of blocks 0..4. |
| 272C | byte | Rebasing threshold. |

The 3D command line (DS:04B6 in v1.1 and v1.2) is moved by the packing in v1.3, so `PorStartExe.FindCommandLine`
finds it by structure as well: a Pascal string of capitalised words, followed within 64 bytes by the overland command
line, which is the same string without its first word (DS:04DF). The Client draws it as the original does, with the
first item highlighted.

DS:2722 and DS:272C are readable only in v1.1 and v1.2; v1.3 packs that part. They follow from the tile counts
(below), and `SymbolSet.FromCounts` derives them that way.

## Symbols

All 8 × 8 tiles share one numbering (Put8x8Symbol). Number 0 is "nothing". The blocks are:

| Block | Symbols | Tiles from |
|---|---|---|
| 0 | 1..45 | 8X8D1 block 203 (45 tiles): common tiles |
| 1, 2, 3 | 46..115, 116..185, 186..255 | the 8X8D block of the wall set in slot 1, 2, 3 (70 tiles each) |
| 4 | 256..286 | 8X8D1 block 202 (31 tiles): the area map |

- The bases are 1 + 45 for block 1, plus 70 per slot after that.
- Blocks 201–203 exist only in 8X8D1.DAX, so they are always read from disk 1.
- 8X8D blocks are ordinary DOS picture blocks of 8 × 8 items.
- Wall pieces are drawn **masked**: colour 13 (light magenta) is see-through. The area map is drawn unmasked.

## Wall sets (LoadWallSet)

- A WALLDEF record is k × 780 bytes: k blocks of five walls × 156 symbol numbers.
- Block f of the record goes into wall slot `slot + f − 1`. Then every byte ≥ 45 (the threshold value 45 included)
  gets `base(slot) − base(1)` added. Every block of a multi-block record uses the delta of the **first** slot.
  - All 38 WALLDEF records load.
  - Each of their non-zero symbols has a tile (test `Every_wall_set_loads_with_tiles_for_all_its_symbols`).
- The tiles come from the 8X8D file of the current disk:
  - a one-block record with id i uses block i;
  - a k-block record uses blocks i·10 + 1 … i·10 + k, with id 0 counted as 10;
  - see `geo-format.md` for the 127 → id 0 rule.
- The program stops with a fatal error when a WALLDEF block is missing, so in play no script asks for one.
  - Entered with fresh VM memory (the Client's `--area`), the Valjevo Castle scripts ask for slot 2 after a two-block
    set: the two-flag rule ($49E7/$49E8, set by earlier play) is off.
  - `PorViewArt` then leaves the slot as it is and logs a diagnostic `[?]`.
  - `PorAreaEntryTests` enters every script 0..32 this way. Script 19 (the overland) loads no map and is refused.
- LOADPIECES with 255 empties a slot. `[?]` Whether the program clears the bytes is not read; we zero them.
- Wall type t (1..15) is the 156 bytes at (t − 1) × 156 of the three slots.

## The 156-byte wall record

Ten **pieces**. Each is a row-major block of symbol numbers (width × height cells, from the tables above).

| Piece | Used for |
|---|---|
| 0 | front wall, far row (two squares ahead) |
| 1, 2 | left / right side walls, far row |
| 3 | front wall, middle row (one ahead) |
| 4, 5 | left / right side walls, middle row |
| 6 | front wall, near row (the party's square) |
| 7, 8 | left / right side walls of the party's square |
| 9 | far corner, where a run of far front walls ends |

DrawWallPiece draws a piece cell by cell from its top-left (column, row):

- Cells outside 0..10 are clipped, and symbol 0 is skipped.
- The byte index advances for every cell, whether it is drawn or not.

## Draw3DView

Painter's order, far to near. Let `c` be the square two ahead.

1. Backdrop (below).
2. **Far row** (`c`).
   - **Front walls, leftward.** Walk 4 squares from `c` to the left, column offset 0, −2, −4, −6:
     - if the square has a front wall and the run is open, first draw the corner (piece 9) at column(9) + offset + 1;
       then draw piece 0 and remember its wall type as the run;
     - if it has no front wall, draw the corner only when the run is open and the previous square's left wall is
       non-zero; the run then ends;
     - a square off the map whose right wall is 0 also ends the run.
   - **Front walls, rightward.** The same to the right, offsets +2 each, corner at column(9) + offset − 1. The off-map
     test reads the left wall.
   - **Side walls.** Three squares each way. Piece 1 (left walls) goes at column(1) + offset (−2 per square, one more
     column out after the first); piece 2 (right walls) is mirrored.
3. **Middle row** (one ahead).
   - Squares −2, −1, 0 across: front piece 3 and left piece 4, both at column + offset, with offsets −6, −3, 0.
   - Then +2, +1, 0: front piece 3 and right piece 5, offsets 6, 3, 0.
4. **Near row** (the party's square).
   - Squares −1, 0: front piece 6 and left piece 7, offsets −7, 0.
   - Then +1, 0: front piece 6 and right piece 8, offsets 7, 0.

The centre square of the middle and near rows is drawn twice, once from each side.

Map lookups use the accessor rule of `geo-format.md` "Edges".

- Off the map, any x > 15 reads as 0 and any x < 0 as 15 (likewise y). It is not a modulo, so x = −2 reads 15.
- While script 0 or 10 runs, an off-map square reads as no wall.
- Checked: the view at 14,4 E looks two squares ahead to x = 16 and matches DOSBox.

## Backdrop

Four full-width bands of the 88-row view:

| Rows | Band |
|---|---|
| 0–42 | sky |
| 43 | horizon 1 |
| 44–45 | horizon 2 |
| 46–87 | ground |

- **Sky:** the sky list entry of $49FD, or of $49FE when the party's square has an event byte ≥ 0x80
  (`geo-format.md` "Event byte"). Slums: $49FD = 11, $49FE = 9 (cyan and white).
- **Horizon 1, horizon 2, ground:** `[?]` RefreshView recomputes these from DS:6F68/6F69 only while DS:84E5 is set;
  otherwise they keep values whose origin is not traced. The screenshots give 15, 7 and 6.
  - With other horizon colours the open views at 9,8 E and 4,12 E differ by 24 pixels, so these bands are visible
    and the values are checked.
  - Other areas may use other colours.

## DrawAreaMap

- The window is 11 × 11 squares. Its top-left square is (clamp(x − 5, 0, 5), clamp(y − 5, 0, 5)), so near an edge the
  party is off-centre.
- Each square is drawn with symbol 256 + 4 + bits, where the bits mark which faces have a wall: 1 N, 2 E, 4 S, 8 W.
- The party arrow is symbol 256 + facing / 2, drawn last.

## Movement (ExploreCommand, MoveParty)

| Key | Action |
|---|---|
| Left | facing + 6 (turn left) |
| Right | facing + 2 (turn right) |
| Down | facing + 4 (about-face) |
| Up | step forward |

- Turns redraw the view, run no script and take no time.
- **Step.** Only the barrier of the party's own face counts (`geo-format.md`):
  - 1: move;
  - 0: no move (bump);
  - 2 or 3: the door menu (`GoldBox.Core.Doors`).
- **Edge.** A step that leaves the grid sets $6DD5 = 1, and the area script's step vector may then take the party
  elsewhere. Unhandled, the party wraps.
- Not modelled yet in the Client:
  - the area script's step vectors (0 and 1), so events and exits don't run;
  - the door menu;
  - the clock;
  - Search mode's double time cost.

## Font

- 8X8D1 block 201 holds 177 glyphs of 8 bytes each, 1 bit per pixel, bit 7 on the left.
- Glyphs 32..63 are ASCII 0x20..0x3F, and glyphs 1..31 are 0x41..0x5F (code − 0x40).
- `[?]` This mapping was read off the glyph shapes and the status line. The program's text routine is not read.

## Verification (2026-11-17, s10)

- **Setup.** DOSBox-X ran PoR v1.3 in EGA mode on Xvfb. Each case used a copy of save A with the party moved to the
  square, loaded from the main menu, and screenshotted before and after the A key. Screens were reduced to EGA indices.
- **Comparison.** The 88 × 88 crops at (24, 24) were compared with `goldbox-tools view-compare` and pinned as SHA-256
  hashes in `ViewDataTests.View_matches_dosbox`. The test needs the player's files; no pictures are stored.
- **3D views** (9): 15,4 W; 15,4 S; 14,4 W; 14,4 N; 14,4 E (edge wrap); 9,8 E (open court, far corners); 6,2 W;
  4,12 E (second sky colour); 3,8 S.
- **Area maps** (5): 15,4; 9,8; 6,2; 4,12; 3,8.
- **Result:** 0 differing pixels in all 14.
- **Client screen** (Xvfb screenshot, save A, against the DOSBox-X screen): the party list, the position line and the
  command line are identical too (0 differing pixels). The braided frame is a stand-in `[?]`, and the text window
  holds our help lines.

Not checked: other areas, especially the three-block New Phlan set and the two-block sets, and the bounded maps of
scripts 0 and 10.
