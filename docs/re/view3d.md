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
- **Horizon 1, horizon 2, ground** (program read s11, redraw 0x2DCF4): computed from the colour bytes behind $C05F
  (DS:6F69) and $C059 (DS:6F68) while DS:84E5 is set:
  - horizon 1 (0x2DC4E): $C05F 8 or 0x41 → 0, else 15;
  - horizon 2 (0x2DC82): $C05F 8 → 8, else 7;
  - ground (0x2DCB6): $C059 0x0B → 8, 6 → 1, else (9, 0xDB, …) 6.
  - The script init sets 0x41 / 9 and the flag (0x76A3), so every area starts with 15, 7, 6 (the Slums values
    measured in DOSBox-X). Writes to $C059/$C05F set the flag (0x82BB/0x82CD); CALL $2C90 clears it after its redraw
    (0x3A8D). The backdrop bytes DS:6A60–6A63 are written only by this redraw (0x2DD63/6F/7B and 0x2EA4C, called from
    it alone), so the colours always follow the current bytes. Scripts that write them: ECL4 block 10 (Valhingen
    Graveyard: $C05F = 8, $C059 = 11 → 0, 8, 8) and ECL8 block 29 (Kuto's Well). Code: `BackdropRule`.

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
- The area loop around these keys (vectors, step, doors, clock) is below ("Area loop").

## Area loop (GAME.OVR 0x403E; program read s11)

Code: `GoldBox.Core.Explore.AreaLoop` (one command at a time), `GameClock`, `AreaHost`; session
`GoldBox.Data.Por.PorGame`. Replays of `step_west`, `turn_left`, the four `oneway_*`, `door_bash` and
`edge_exit_east` from their `before` dumps give the `after` dumps cell for cell, clock and RandSeed included
(`AreaLoopTests`).

- **Command routine 0x17054.** Every command read starts with $6DC9 = 0. With the 3D command line (DS:04B6):
  - A: area map on/off (DS:6AAC) when $49FB = 0, else a short message (cs:0x97B, colour 14);
  - C cast (0x17CD2, needs status 0), V view (0x21AED): not built;
  - S: $6DCA bit 0 toggles (Search mode); no time;
  - L (Look): $6DCA |= 2, 10 minutes (clock unit 2), the routine returns;
  - E (Encamp): returns;
  - arrows: Left/Right/Down turn (facing + 6 / + 2 / + 4, wall ahead read again, view redrawn, no script, no time);
    Up calls the forward key 0x16D58 and returns;
  - other keys: 0x23819 (party member selection `[?]`, not read).
- **Area loop 0x40DB.** After a command returns, $49F2 = the current block. While $6DCA > 1 or the key was E (and
  DS:49FF is clear): E runs the camp (0x3B19); otherwise $6DCA bit 0 is kept, $6DCA = 1, the view redrawn, vector 1
  runs alone, then $6DCA = the kept bit (Look). Else vector 0 runs; unless the script changed or the party is
  defeated, $49F0/$49F1 = x, y, the step 0x17438, a redraw, vector 1.
- **Forward key 0x16D58.** $6DD5 = 0; if the party's own face is not impassable and the square ahead is off the grid,
  $6DD5 = 1 and the position is clamped to the edge (where it already is). An impassable face on the map edge leaves
  $6DD5 = 0.
- **Step 0x17438.** If $6DC9 ≥ 255 (set by the script in vector 0) the step is cancelled and $6DC9 = 0. Otherwise
  barrier 1 moves; 2 or 3 opens the door menu (`geo-format.md`, one try per step); 0 refuses. A refusal or failed
  try plays the bump sound (DS:2622).
- **Move 0x16E06.** One square ahead with wrap; wall ahead (0x2ED72) and event byte (0x2EE71) read again; door flags
  set to 1; clock + 1 minute (unit 1), or **+ 10 minutes** (unit 2) when $6DCA bit 0 (Search) is set.
- **Clock 0x241A1** (count, unit): cells $49C6–$49CC (segment, minute ones, minute tens, hour, day, month, year); adds
  1 to cell `unit` `count` times, carrying after each add with the limits at DS:363A (10, 10, 6, 24, 30, 12, 256); a
  year cell at 256 or more is not wrapped, every creature of the party list gets record word 0x30 (age) + 1 instead.
  Then 0x23DCC(count, unit) runs `[?]` (not read; likely effect durations).
- **Redraw 0x2DCF4** (3D): reads the event byte of the party's square again (DS:6AB1), switches the area map off
  where $49FB ≠ 0, recomputes the backdrop.
- **Camp menu 0x196A0** (s12): DS:49F3 = 2 (saved and restored), a camp message in the text box, then the menu
  routine with a "Camp:" prompt and the DS menu line (v1.3 DS:0508; six words: save, view, magic, rest, alter,
  exit) until E (or key 0). S saves (0x1FD3E) and asks whether to quit, V views (0x21AED), M magic (0x18C22), R rest
  (0x17FAB), A alter (0x1948A); Home / End (extended keys only) select a member (0x23819). The Client builds View;
  the others say they are not built yet (`[?]` not read), so no rest is interrupted.
- The time is not drawn on the explore screen in our DOSBox-X screenshots of save A (the position line holds only
  x,y and facing).

## Font

- 8X8D1 block 201 holds 177 glyphs of 8 bytes each, 1 bit per pixel, bit 7 on the left.
- Glyphs 32..63 are ASCII 0x20..0x3F, and glyphs 1..31 are 0x41..0x5F (code − 0x40).
- The glyph routine (START 0x726F: column, row, colour, count, character, flag) draws text with the flag set as
  glyph UpCase(character) mod 64, which is this mapping (s12); with the flag clear the byte is the glyph number
  (PROTECT's runes). Columns above 39 or rows above 24 are not drawn. In CGA mode (DS:4A1E = 0) the colour goes
  through a 16-entry table at DS:25D5; in EGA it is used as is.

## Text window (START 0x77F8; program read s12)

Arguments: box (x0, y0, x1, y1) in text cells, colour, clear flag, string. PRINT and PRINTCLR (GAME.OVR 0x174F) use
the box (1, 17)–(38, 22) and colour 10; PRINTCLR first puts the cursor at (1, 17) and passes clear = 1. The text
cursor is DS:5E24 (column) / DS:5E25 (row); it stays where the last print left it (PRINTRET: next row, column 1;
EXIT: (1, 17), no clear).
1. A cursor outside the box goes to (x0, y0); with clear the box is blanked (0x7E63) and the cursor goes there too.
2. The string is cut into words. From position p: skip characters of the delimiter set (DS:0D16, a set constant:
   `! , - . : ; ?`), then characters that are neither delimiters nor spaces, then, unless that stopped on a space,
   the delimiters right after. The word runs from p to that last character (so it includes the space or delimiter
   that ended it); each scan stops at the string's last character.
3. If column + (word length − 1) > x1: column := x0, row + 1, and spaces at p are skipped (0x77A4). If the row is then
   below y1 and text is left, the cursor goes to (x0, y0), a page prompt (a fixed string just before the routine) is
   drawn on row 24 in colour 13, the program waits for a key, blanks the box and prints the word; otherwise the next
   pass prints it on the new row. (A test at 0x79A9 for a word ending exactly on x1 can never be true.)
4. Else the word is drawn (0x7737), one cell per character; while DS:4A00 is set (PRINT sets it) each character is
   followed by a delay of DS:49E2 × 10 ms `[?]` (the Client shows the text at once).
5. After the string a cursor past x1 goes to column x0 of the next row.
The window never scrolls. Code: `GoldBox.Client.Play.TextWindow`.

CLEARBOX (0x385A) blanks the picture window (1, 1)–(15, 15), not this box; `[?]` when the 3D view is drawn there
again is not read (the Client keeps it blank until the next command).

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

**Other areas (s11).** Save A's party placed in another area (`PorSaveGame.PlaceInArea`, tool `goldbox-tools
save-area`; the load also needs the disk in cell $6E12 and the area's wall-set ids at $4AFA–$4AFC), loaded from the
main menu in DOSBox-X, screenshots reduced with `tools/scripts/ega_screen.py`, compared with `view-compare` (`--keys U`
replays a step through the area loop). 0 differing pixels in all 11, pinned in `OtherAreaViewTests`:
- New Phlan (script 0, bounded map, three-block wall set 127 → WALLDEF3 0) at 2,4 E: view and area map;
- Valhingen Graveyard (script 10, bounded, two-block set 21 with the two-flag rule, $C05F = 8 / $C059 = 11) at 0,4 E
  and 9,9 E: view, area map, and one step on (vector 0 sets $49FD = 8: black sky);
- Kobold Caves (script 13, $49FB set) at 14,14 N: view, A refused (view stays), one step.
- The Client's whole screen for New Phlan equals DOSBox-X except the braided frame (stand-in `[?]`) and our help lines.

**Position line** (START.EXE 0x34AB, program read s11): "x,y " only where $49FB = 0, the facing, HH:MM (both padded
to two digits), then " camping" (DS:49F3 = 2) or " search" ($6DCA bit 0). In Kobold Caves the line is the facing, the time and the search word. The
command line highlights the item whose hotkey is the last letter key read (after loading slot C: CAST).

Not checked yet: the Valjevo Castle and wilderness-lair areas, the frame picture.
