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
  $49FB ≠ 0; then the program refuses with a short message (cs:0x97B, row 24 in colour 14 for DS:49E2 × 225 ms,
  START 0x7DF8; geo-format.md "Row-24 messages").

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
  - Valjevo Castle's LOADPIECES 24, 25, 1 (ECL5 blocks 3, 4 and 6; WALLDEF5 holds only blocks 1 and 24) needs the
    two-flag rule: block 6, the castle's entry from Stojanow Gate, sets $49E7 = $49E8 = 1 in its vector 4 (ECL5 6
    $9C5A/$9C60) before its own LOADPIECES, and only the two ways out clear them (block 6 $9B7F/$9B85 on the south
    gate back to ECL2 block 9, block 7 $A8D9/$A8DF on the way to civilised Phlan). So 24 fills slots 1–2 and 1 goes
    to slot 3; set 25 is never read in play (DOSBox-X s28, below). Sweep of every v1.3 ECL block (`ecl.py dis`, s28):
    the only other writes of the two cells are the graveyard's (ECL4 10: 1 at $9A72/$9A78 before its LOADPIECES,
    0 at $9932/$9938 on the way out), and the only NEWECLs from outside ECL5 into disk 5 are Stojanow Gate's (ECL2 9
    $9CA0 and $A5A0, both to block 6); inside the castle blocks 3–7 switch only among themselves, apart from those two exits.
  - Entered with fresh VM memory (the Client's `--area`) at script 3 or 4 the flags are clear and the script asks for
    block 25: `PorViewArt` then leaves the slot as it is and logs a diagnostic (the game would stop there).
  - `PorAreaEntryTests` enters every script 0..32 this way. Script 19 (the overland) loads no map and is refused.
- **The tile-block loader 0x32270** (read whole s63; arguments block, symbol block; callers the start-up 0xE600 and
  the wall-set loader 0x2F6FF):
  - a symbol block outside 0–4 (the set constant at the unit's CS:0000, bits 0–4) returns at once. The WALLDEF size
    check keeps every wall set inside slots 1–3, so neither caller is refused;
  - the file name is `8x8d` + the disk byte DS:5376 (Str, width 1); the block is read (START 0464:0111, picture
    class 0x0D, flag 1) into the far pointer DS:5E3A + 4 × slot;
  - a nil pointer is fatal: 0464:00FF, an error line naming the file stem, the disk, a dash and the block (Str,
    width 3) written to the screen, one key (0899:0059), 0464:00FF again and the quit 0899:0000 (the module holds every block, so the
    engine throws instead: `PorViewArt`);
  - then the key-buffer flush 0899:0104 (START 0x8A94) (after a load; the quit does not come back).
  - The start-up (0xEA7B) loads block 202 into symbol block 4 and 203 into block 0, from disk 1 (DS:5376 is 1
    there; only 8X8D1 holds 201–203, so another disk would stop the game).
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
    (0x3A8D). The backdrop bytes DS:6A60–6A63 are written only by this redraw (0x2DD63/6F/7B and 0x2EA4C) and by the
    main program's 0x2EA4C(11, 0, 7, 6) after the party menu (START 0x138), which the game loop's script init makes
    stale before any redraw (start-menu.md §1, s51), so the colours always follow the current bytes. Scripts that write them: ECL4 block 10 (Valhingen
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
  - A: area map on/off (DS:6AAC) when $49FB = 0, else a short message (cs:0x97B at 0x170F1, START 0x7DF8: row 24,
    colour 14, DS:49E2 × 225 ms, then row 24 cleared);
  - C cast (0x17CD2; in the 3D branch only when the current member's status 0x10C is 0, 0x170FC; the overland branch
    calls it at once, 0x17320), V view (0x21AED): both loop back to the key read (0x17172 → 0x17266, the position
    line drawn, 0x17073): no vector, no step, no time. View's exit (0x21D4F) and Cast after a list (0x17D96) or its
    target pick (0x27AFA) draw START 0x3379 for DS:49F3 (s41: `AreaLoop.MenuCommand`, `ClientPlayer.View` / `Cast`,
    `CharacterView.Run` with `Venue.Explore`: Items offers Use; `CampScreens.CastMenu` with `CampContext.InterfaceRedraw`).
    Keys V / C, pad Start / R3, a click on the word; the shared cell moves to the word, so View opens on that cell
    (3D: its 3rd word, overland: its 2nd);
  - S: $6DCA bit 0 toggles (Search mode); no time;
  - L (Look): $6DCA |= 2, 10 minutes (clock unit 2), the routine returns;
  - E (Encamp): returns;
  - arrows: Left/Right/Down turn (facing + 6 / + 2 / + 4, wall ahead read again, view redrawn, no script, no time);
    Up calls the forward key 0x16D58 and returns;
  - other keys: 0x23819 (party member selection, read s49 `shop-format.md` "Party selection"): Home / End the
    previous / next member; any other extended key (PgUp, PgDn, Ins, Del, the F-keys, Alt-letters, the digit keys 3 / 5 / 9 and
    backslash of either row) makes the **first** member current (PgUp seen in DOSBox-X s49). The Client wires Home / End only (see that section).
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
  Then the effect timeout 0x23DCC(count, unit) runs (read: camp.md "Effect timeout"; outside camp every creature of
  the list ages its timed effects by the same duration). Core: `AreaLoop.AdvanceClock` with `AreaLoop.EffectTimeout`,
  wired by `PorGame` to the session's flags (`PorGame.Camp`).
- **Redraw 0x2DCF4** (3D): reads the event byte of the party's square again (DS:6AB1), switches the area map off
  where $49FB ≠ 0, recomputes the backdrop. Which view (P s42, 0x2DCFA–0x2DDBD): the mode is DS:49FA, made 1 when
  $49E6 ≠ 0; mode 1 draws the 3D view from the map in memory, modes 2–4 the overland, any other nothing. So location
  type 1 with $49E6 = 0 is the 3D view (`AreaLoop.Draws3D`; before s42 the Client showed "NO VIEW" there, e.g. a
  `save-overland` save with its location type set back to 1: the program stops on that one with its cannot-load-the-wall-set error,
  and a save A with $49E6 = 0 gets it back from vector 4, DOSBox-X s42).
- **No map loaded: the map buffer** (P + D s43). The GEO map lives in one 0x400-byte buffer, GetMem'd at the start-up
  (GAME.OVR 0xE667–0xE673, far pointer DS:6A5C) and never cleared; only the GEO loader (0x2FA66–0x2FAC8) and the
  door edits (0x166E3–0x168F0) write it, the accessors (0x2ECCA–0x2EED1) read it. Load reads map $49C5 only for
  location type < 2 (0x1FBB3), so a save of location type 2–4 leaves the buffer as it was, and with $49E6 ≠ 0 the
  redraw draws the 3D view from it. DOSBox-X: scratch save H (save A, location type 2, $49E6 1, every vector EXIT)
  loaded at the start-up: the buffer (state dump) all zeros, the 3D view of an empty map (sky and ground only);
  scratch save G (slums) loaded, its step's hall, Remove × 6, Load of H: the buffer equal to GEO2 block 20 byte for
  byte and the view the slums' corridor at 15,4 W. That screen is the overland interface's (START 0x36FE, DS:49FA 2):
  its layout 0x2E03F draws the windows (1, 1)–(15, 15), (17, 1)–(38, 15) and (1, 17)–(38, 22) in that order, so no
  inner box and no joint at (16, 16) (the text box's top edge runs through it; also on the real overland, save
  `save-overland 26 10,20,N`; after View's exit START 0x3379 draws the 3D layout and the joint is back); the redraw
  0x370B draws the 3D view (its 3D branch draws no marker), then 0x3710 the marker alone (0x2DB87, frame DS:6DDC,
  which goes on by one: DS:6DDC 1 after the start-up Load) over the view's top-left corner at the marker's overland
  cell; the position line has the overland x,y and no CAMPING (0x3701 sets DS:49F3 = 3 first); the overland command
  line. A step (12 hours) redraws only the 3D view, which goes over the marker's inner part; the rest stays.
  `[?]` On a machine whose memory was not zero the start-up buffer would hold whatever DOS left there. Client:
  `PorGame.Loaded` puts the session's map (`Program.cs` hands the running game's to the party menu's Load and to a
  hall Load of another area) or `AreaMap.Unread` (zeros) in memory when the Load reads none; `AreaLoop.OverlandLayout`
  / `InterfaceMarker` / `InterfaceMarkerCovered`, `ExploreScreen` (no box, no joint, the marker under or over the
  view), `ClientPlayer.CampingLine` (no CAMPING while the interface is due). Both DOSBox-X screens (H at the start-up,
  before and after a step east; the hall Load) equal the Client's but the help lines. Tests
  `EntryScreenTests.A_load_that_reads_no_map_draws_the_map_buffer_with_the_interfaces_marker`,
  `A_load_that_reads_no_map_keeps_the_sessions_map_in_the_buffer`.
- **The overland's (16, 16) and its camp** (D s43, `save-overland 26 10,20,N`, a start-up Load): the overland screen
  has no joint at (16, 16) (the layout above; before s43 the Client drew one there, the only difference on that
  screen). Encamp: the joint is back, the window (cells 1–15) is blank with the 3D view's box and the camp picture in
  it (the Client kept the tiles around the box before s43); after Exit the overland again with the joint (the camp
  drew its windows, and the interface is not drawn again). A load of an overland save keeps the saved wall-ahead byte
  (the buffer is not read there). Client: `AreaLoop.OtherLayoutDrawn` from the camp, the venues and a fight
  (`ClientPlayer.OtherLayout`), `ExploreScreen.OverlandBoxed`, `PorGame.FinishEntry`. Test
  `EntryScreenTests.An_overland_load_keeps_its_wall_ahead_byte_and_the_camp_brings_the_joint_back`.
- **The joint after a fight, a shop, a temple and a loot on the overland** (P + D s44). Every one draws START 0x3379
  with the 3D layout (0x32CA, joint at (16, 16)): the shop and temple case 1, the loot menu case 6, and COMBAT's
  handler ends with case 3 on the overland whatever ran (DS:49F3 := 3 when DS:49FA ≠ 1, then 00BA:27D9, GAME.OVR
  0x25F7–0x2635). START 0x3379 draws the inner box with 0x32CA(1) in cases 1, 2, 4 and 6, without it (0x32CA(0)) in 3,
  and each window is cleared inside, so on the overland the shop, temple and loot screens show a blank window with the
  3D view's box in it (empty for the shop and temple, the chest for the loot), not the tiles. The joint then stays: the
  interface (START 0x36FE, no joint) is drawn only at a command routine entry with DS:49F9 < 2 (0x17285), and DS:49F9
  is written only by the command routine's end (0x173E7, := DS:49FA), the area loop's restart (0x3F28, := the old
  DS:49FA) and the start-up (0xE9BE, := 1); on the overland it is 2–4 after the first command, so only a load or an
  entry from a 3D area brings the joint-less layout back. DOSBox-X: save A put on block 26's overland at 10,20 N with
  vector 0 pointed at test code past the script's end ($B300) that runs once (a flag at $B6F0): one monster 1 fought
  (Alt-Q) then its loot, a COMBAT with only TREASURE 100 copper (the loot), CLRMON + $6E6C = 1 + COMBAT (the shop),
  $6DE2 = 1 (the temple): the joint on each screen, after Exit and after the next step; the window blank with the box.
  The Client equals each screen pixel for pixel but the help lines (and the chest's twinkle frame on the loot screen);
  before s44 it kept the tiles around the box and drew no box for the shop. `ExploreScreen.OverlandBoxed` (the camp,
  `Venue.Shop`, `Venue.Loot`). Tests `EntryScreenTests.A_shop_or_temple_on_the_overland_brings_the_joint_back_for_good`,
  `The_loot_menu_on_the_overland_shows_the_chest_in_the_box_and_the_joint_stays`.
- **The same on the Amiga** (P + FS-UAE s45; `program` read, FS-UAE on disk A's save A put on block 26's overland at 10,20 N
  as `save-overland` does, vector 0 the same run-once test code, the Client `--art amiga` on the DOS test saves). The
  Amiga frames the screen as whole windows (por-art-formats.md §4 "Window frame"), so the "joint" is which window is
  drawn last over a shared cell. The overland interface (hunk 13 +0x5E80, the copy of START 0x36FE: $ba := 3, then
  hunk 25 +0x1D4E, the copy of 0x2E03F) draws the windows (1, 1)–(15, 15), (17, 1)–(38, 15) and (1, 17)–(38, 22) in
  that order: column 16 is the party window's left side, row 0 over columns 17–38 that window's top, (16, 16) the
  text box's top edge. The 3D layout (hunk 13 +0x5A82, the copy of 0x32CA) draws (1, 1)–(38, 22), (1, 17)–(38, 22),
  (1, 1)–(15, 15) and with its flag the box (3, 3)–(13, 13); hunk 13 +0x5B16 (the copy of START 0x3379) uses it in
  cases 1, 2, 4 and 6 (with the box) and 3 (without), as DOS. (Hunk 25 +0x3918, a four-window layout with the party
  window, has no caller.) Seen: after the Load the overland with the three windows; the shop (step) with the 3D
  layout, a blank window, the box; Exit, the overland with the 3D layout kept (a random encounter followed in
  FS-UAE, so the next step was not compared); a TREASURE-only COMBAT's summary window, then the loot menu with the
  3D layout and the chest in the box (both equal to the Client but the help lines and the pressed gadget). Client before s45: the 3D layout's frames on the overland too (column
  16, row 0 over the party list and (16, 16) differed); now `ExploreScreen` draws the three windows under Amiga
  while the interface layout stands (`overlandLayout`). Equal to FS-UAE but the party, the marker's frame (the
  session's marker counter, geo-format.md "Marker counter", Amiga read and seen s46), the help
  lines and the pressed gadget.
- **The shop and temple box shows the last head + body** (P s45, DOS and Amiga; FS-UAE s45). START 0x3379 case 1 draws the
  layout with the box, then loads and draws the head and body last given to the head + body loader (0x33BA–0x33D0:
  DS:49FD / 49FE → 03D0:003E, then 03D0:0039 at (3, 3)); only GAME.OVR 0x79E4 writes them (0x79EF / 0x79F5: PICTURE
  with $6DE1 ≠ 255, a portrait close-up). The Amiga is the same (hunk 13 +0x5B5C–0x5B8A with hunk 32 +0xC4 / +0xC5,
  written only by hunk 4 +0x5DC). A part is loaded only when not 0xFF and present in the file (0x2E6A7; hunk 19
  +0x1592); DOS opens the current disk's HEAD / BODY, the Amiga its one head.dax / body.dax. Start values: the
  Amiga's hunk 32 is BSS (zero), so a fresh session's shop or temple shows head 0 (head.dax has it, body.dax has no
  block 0: the head alone). FS-UAE: the overland shop showed that head in the box. DOS's DS:49FD / 49FE (P + D s46)
  lie past START's initialised data (DS 0–0x442F) and nothing writes them at start-up: the Turbo Pascal System init
  (START 0xAF80) clears no data, so they hold what memory held when START loaded. DOSBox-X, whose RAM starts cleared:
  0, 0 (a state at the party menu after a plain boot; the residue probe at the first heal list, por-art-formats.md
  "Creation's list labels"); a real machine `[?]` (whatever was there). HEAD3 and HEAD4 have a block 0, HEAD2 (the
  slums, DOSBox-X s22's shop) has none. **After a portrait** (DOSBox-X s46, scratch saves: save A with vector 0 =
  `SAVE 5 → $6DE1`, `PICTURE 2`, then the temple or the shop as test saves G / F): the temple and the shop show head 5
  + body 2 in the box, the temple again after the heal list (the probe logged 05 02); the Client pixel-equal but its
  help lines (rows 12–13). **The Amiga after a portrait** (P + FS-UAE s46): hunk 4 +0x5DC has two callers, PICTURE
  with $6DE1 ≠ 255 (hunk 16 +0x238E) and the close-up (hunk 4 +0x7C8), as DOS. FS-UAE (disk A's save A put on block 26's
  overland at 10,20 N, vector 0 = `SAVE 8 → $6DE1`, `PICTURE 8`, CLRMON, an empty TREASURE, `SAVE 1 → $6E6C`, COMBAT,
  loaded at the start-up, one step north): the shop's box shows head 8 + body 8; the Client (`--art amiga`, the same
  code on a `save-overland` save) equal: 0 of 7744 box pixels differ. Client: `ClientPlayer.VenuePortrait`
  (start 0, 0), `ViewPictures.DrawVenuePortrait` (each part the art has; the Amiga art any disk), drawn in the
  Shop venue's blank box; FS-UAE's box equal pixel for pixel.
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
  (PROTECT's runes) in the same font: the font pointer DS:5E20 has one writer, the start-up (0xE650), so the runes
  are 8X8D1 block 201 glyphs (65–139 in v1.3's twelve lines). The Amiga v1.0 draws them from a font of its own, `protect.font`, in
  pen 28, set bits only (ecl-format.md "PROTECT"; the Client does so under Best / Amiga). Client: `TextFont.RawGlyph` (U+E000 + glyph in a text
  cell); `[?]` a glyph number past 176 reads past the font (none in v1.3; the Client draws it blank). Columns above 39 or rows above 24 are not drawn. In CGA mode (DS:4A1E = 0) the colour goes
  through a 16-entry table at DS:25D5; in EGA it is used as is.

## Text window (START 0x77F8; program read s12, read whole and ported s47)

Arguments: box (x0, y0, x1, y1) in text cells, colour, clear flag, string. PRINT and PRINTCLR (GAME.OVR 0x174F) use
the box (1, 17)–(38, 22) and colour 10; PRINTCLR first puts the cursor at (1, 17) and passes clear = 1. The text
cursor is DS:5E24 (column) / DS:5E25 (row); it stays where the last print left it (PRINTRET: next row, column 1;
EXIT: (1, 17), no clear). Every counter of the routine is a **byte**: the position p (BP−0x103), the word's end i
(BP−0x104), the length (BP−0x101), and the cursor itself.
0. A box with x0 > 39, y0 > 24, x1 > 39 or y1 > 39 (y1 is tested against 39, not 24) returns at once: nothing is
   cleared or drawn (0x7813–0x782B). The string is copied into a local string[255] (0x780E, at most 255 characters);
   an empty one ends after step 1 (0x788D).
1. A cursor outside the box goes to (x0, y0); with clear the box is blanked (0x7E63) and the cursor goes there too.
2. The string is cut into words. From position p: skip characters of the delimiter set (DS:0D16, a set constant:
   `! , - . : ; ?`), then characters that are neither delimiters nor spaces, then, unless that stopped on a space,
   the delimiters right after. The word runs from p to that last character (so it includes the space or delimiter
   that ended it); each scan stops at the string's last character.
3. If column + (word length − 1) > x1: column := x0, row + 1, and spaces at p are skipped (0x77A4). If the row is then
   below y1 and text is left, the cursor goes to (x0, y0), a page prompt (a fixed string just before the routine) is
   drawn on row 24 in colour 13, the program waits for a key (GetKey 0x7CC1: a key already waiting answers it at
   once, below), blanks the box and prints the word (the prompt stays on row 24 under the next page: DOSBox-X s46,
   ecl-format.md "Page prompt under typed text"); otherwise the next
   pass prints it on the new row. (A test at 0x79A9 for a word ending exactly on x1 can never be true.) After the
   key, 0899:0104 (START 0x8A94) is called: it starts with a jump past its own `while KeyPressed do ReadKey` loop and
   returns (7 instructions under emulation), so **no typed-ahead key is dropped** (P s47).
   - **Below the box with only one character left** (the row passed y1 but p = length: the last word is one character
     and came after a full box): no prompt; the next pass draws that character at (x0, y1 + 1), i.e. on row 23 over the
     frame for PRINT (P s47; **D** s47: six 37-letter words then "X": the X on row 23, column 1, then the menu).
   - **A box narrower than one cell** (x1 < x0) whose last word is one character: the word never fits, p = length,
     and the row counts on (a byte) for ever with nothing drawn: a hang (P s47, under emulation; no caller passes such
     a box).
4. Else the word is drawn (0x7737), one cell per character; while DS:4A00 is set each character is
   followed by Delay(DS:49E2 × 10) (START 0x7782 → the Crt-style Delay START 0xA69C, calibrated milliseconds; s15).
   0x7737 loops `while p <= i` with p a byte, so **a word ending at character 255 never ends**: p wraps from 255 to
   0 and the loop goes on drawing the string's length byte (character 255, glyph 255 mod 64 = 63, the '?' glyph) and
   the text again from its start, column + 1 each time (a byte: past 39 the glyph routine draws nothing, at 256 it
   comes back to column 0), with the typing wait, for ever. Every string of 255 characters ends in such a word, so
   **a text of 255 characters or more hangs the program** after its last word; the row it was on then shows, from
   one lap on, the same 40 cells for ever (columns 0 and 39 over the frame), and no key does anything. **D** s47
   (DOSBox-X, scratch save A, vector 0 = PRINTCLR of a 255-character text, then HMENU "WAIT"): the first page, the
   prompt, then "TEXT FOR THE ?LONG TEST TEXT FOR THE PR" on row 18 from column 0 to 39, unchanged 25 s later and
   after Return; the WAIT menu never came. The same text cut to 254 characters printed and reached the menu. The ECL
   texts of v1.3 are at most 205 characters (ECL6 block 19 $9B70, all blocks scanned s47) and memory texts stop at 255
   cells, so the shipped scripts never reach it. **Original bug, fixed (user, s48)**: the Client's counter goes past
   255, so the word ends and the print returns (a byte counter in a loop up to 255 was the bug); the text is still
   cut to 255 characters. The program's hang stays available to the oracle comparison only
   (`TextWindow.OriginalWordEndHang`, `PrintProgramTests`). Client s49 (test save `long-text` of
   `por_test_saves.py`: PRINTCLR of a 255-character text, then HMENU "WAIT"): both pages printed, the last word
   ending at character 255, and the WAIT menu came. The narrow-box hang above is not changed (no caller passes such
   a box). The ECL unpacker cuts a longer text to 255 (`ecl-format.md` "Text packing").
   **Typing state** (P, s29; combat-menu.md "The panel's text" lists the writers): DS:4A00 is 1 from the start-up
   (0xE974) and again after every fight (0x6BAF); PRINT / PRINTCLR set 1 for their text and leave 0 (0x1763 / 0x17DC);
   ENCMENU sets 1 for its loop and leaves 0 (0x2C4F / 0x32E0). So PRINT and ENCMENU's texts (the distance text and
   the two fixed messages, all through 0x77F8 at 0x2EBC, 0x2FE7, 0x305C, 0x3104, 0x315A, 0x31DD) always type; any other
   0x77F8 text (the camp's messages, a shop's, the fight panel's, the party-destroyed message) types only while the
   flag stands: after the start-up or a fight, until the next PRINT or ENCMENU. Client: `EclMemory.TypesText` (not
   saved, start value 1), read by `ClientPlayer.Text` (every text through the explore screen's text window, camp
   included), the fight panel and the party-destroyed / party-killed windows; our own notices never type.
   **Which windows type (P, s30**; every far call to 0x77F8 in START.EXE and GAME.OVR listed, then each routine's
   callees followed for a path to it): the text routine's callers are START 0x1583 (the status panel), START 0x228C
   (= 00BA:16EC, the panel / camp message) and in GAME.OVR PRINT 0x174F, 0x1C4E, ENCMENU 0x2C3E, DAMAGE 0x34BB and
   its line 0x959F, 0x4636, the temple 0x5280, the loot menu 0x644B, the NPC share 0x6866, the after-combat 0x6AA5
   (party destroyed), the shop 0x7212, the attack text 0x12AF4 and the View unit's Items 0x21F32 (its 0x21DAA,
   0x22D28 Sell, 0x22F8C Id: box (1, 21)–(38, 22), colour 14). So:
   - **NPC share** (0x695E, the line at 0x69F0): types while the flag stands. It runs before 0x6BAF, so it has the
     value the fight began with. Client: `TradeContext.TypesInto` (the window grows as it types; row 24 keeps the
     fight's last line meanwhile, `ClientPlayer.KeepsCombatRow24`; **D** s39, combat-format.md "NPC treasure share").
   - **Loot**: the menu (0x659D, 0x2CC8C) never types; the Exit question's two texts (0x666C clear + colour 10,
     0x6693 colour 15, box (1, 17)–(38, 22)) type while it stands; its Yes / No is the menu helper 0x8B57 (002B:0061),
     no 0x77F8. The Take, Pool, Share, coin and item lists (0x62B6, 0x6184, 0x25191, 0x252C3, 0x25974, 0x2D669) reach
     no 0x77F8; their messages are row-24 START 0x2233 (= 00BA:1693) strings.
   - **Summary** (0x5E94): every line is START 0x76B6 (0709:0626: one 0x726F per character, no 0x4A00 test), the
     key the menu routine: never typed (the Client shows it at once; `AfterCombatTypingTests`).
   - **Party menu** (0x19AE0 with Create 0x19EEB, Modify 0x1BA1E, Drop 0x1B8B6, Add 0x1C9EC, 0x19D9C, 0x19E0E) and
     View's page (0x21084), Spells (0x239D6 → 0x270A2 → 0x2D669), Trade 0x23190, money Drop 0x234E1 / 0x21547:
     no path to 0x77F8, so never typed (row-24 messages through START 0x2233, the rest START 0x76B6 and the menu
     routine). Only View → Items (0x21F32) types: its Drop question (0x223FC) and the refusal of 0x21DAA (0x21E53),
     in (1, 21)–(38, 22) colour 14, while the flag stands (1 at the start-up party menu, 0 in the training hall
     after a PRINT). The Client's party-menu Items commands other than Trade, Sell and Id are not built (our notice); Trade's
     refusal goes on row 24 (START 0x2233) after its pick; Sell / Id
     (shop) print into the program's box (1, 21)–(38, 22) over the Items page since s41 (camp.md "View"), typing
     with the flag like every 0x77F8 text.
   The status panel's weapon name types (s29, `CombatChooser.Typing`).
5. After the string a cursor past x1 goes to column x0 of the next row.
The window never scrolls. Each glyph goes through 0x726F with the upper-case flag: glyph UpCase(c) mod 64 ("Font").
Code: `GoldBox.Client.Play.TextWindow` (`PrintDos`, s47: every branch above; cells drawn outside the area are kept and
drawn over the frame; the hang parks the game for good, `ClientPlayer.HangInPrint`; the glyph as `DosGlyph`).
Oracle: `tools/scripts/por_print_oracle.py` runs 0x77F8 under unicorn on 403 made-up strings, boxes and cursors (49
hang) and records every glyph, clear, prompt and Delay; `PrintProgramTests` matches them cell for cell. Client vs
DOSBox-X s47: the 255-character run-on and the row-23 "X" pixel-equal (the Client's help line apart).
Only the script box parks the game on a hang (`ClientPlayer.Text`); the other 0x77F8 windows (the full-screen
party-destroyed / party-killed boxes, the fight panel, View's Items box) get program strings far below 255 characters
and return after one lap, and their full-screen copies (`WindowLines`) leave out cells drawn outside the area.
Symptom patches the port replaced: the Client printed any length (no cut, no hang); dropped a glyph outside the box
(`[?]` in the old `Put`); upper-cased with .NET's `ToUpperInvariant` instead of UpCase mod 64 (a '`', '{' or byte
above 0x7F drew its own glyph or none); had no box check; counted with ints.

CLEARBOX (0x385A) blanks the picture window (1, 1)–(15, 15), not this box. The next view redraw (0x2DCF4: a
command's or a script switch's) draws the 88 × 88 view alone, so the rest of the window, the view's box among it, stays
blank (**D** s39, geo-format.md "Script switch"; Client `ClientPlayer.ViewBoxBlank`). **What draws the box again
(P s41, every far call of START 0x3379 and of the window routine START 0x41F8 in GAME.OVR):** START 0x3379 in cases
1, 2, 4 and 6 (0x32CA with 1 draws the windows (1, 1)–(15, 15) and (3, 3)–(13, 13), each with its frame; case 3, the
overland, draws no box). Its 28 callers: the area's first pass (0x1B1A / 0x40BE: a load's or new game's entry at
state 4), COMBAT's end (0x2635: every fight, shop, temple, loot), the temple (0x51A0, 0x528B, 0x5479, 0x548A), the
treasure and loot menus (0x6295, 0x631E, 0x639B, 0x645C), the shop (0x721E, 0x743C, 0x744D), PROGRAM 0 (0x3B95), the
camp's entry and an interrupted rest (0x196C7, 0x3B3B) and its Cast, Memorize, Scribe, Display, Alter (0x17D96,
0x182E1, 0x1865F, 0x18BF9, 0x19524), the end game (0x209DD, 0x20D47), View's exit (0x21D4F), the item and money
Trade (0x22781, 0x231A4) and Cast's target pick (0x27AFA). Besides: EXIT after a PROTECT into the window (0xD75 /
0xDA7), the script switch from the overland into a 3D area (0x3FCD: DS:49F9 > 1 and DS:49FA = 1) and PICTURE only
with $49E6 = 0 (0x15C7–0x15E8: so not in the 3D view; on the overland a PICTURE draws the box and CLEARBOX alone does
not, D s41). **D** (s41, a scratch save with vector 0 = CLEARBOX, Up: the ring blank): C on a fighter (the no-spells
message, no list) leaves it blank; V and Escape, and C on Brother Sean with the list and Escape, bring it back.
Client (s41): `ClientPlayer.InterfaceDrawn` (START 0x3379 by `AreaLoop.DrawInterface`: the hall's end, a load's
entry, View's exit, Cast) ends it, with the venues, the fight and EXIT as before; the CLEARBOX run's screens equal
DOSBox-X's but the help lines (test `ExploreScreenTests.View_and_cast_on_the_command_line_by_key_pad_and_mouse`).
The same calls blank the text box (1, 17)–(38, 22) (START 00BA:272A's second window, every case but 0 and 5). Since
s47 the Client follows COMBAT's end (0x2635, `EncounterHost.InterfaceRedraw` → `AreaLoop.DrawInterface`) and the
venue entries and redraws (0x721E, 0x743C, 0x744D, 0x528B, 0x5479, 0x548A, 0x645C: `ClientPlayer.VenueDrawn`), so a
loot's exit question no longer shows in the next shop (DOSBox-X s47, `combat-format.md` "Between a loot and the next
venue").
A CLEARBOX in a loaded game's vector 4 after its LOADFILES pair (0x1B1A drew the layout before it) leaves the ring
blank: since s42 the Client draws the layout inside the pair as well (`EclVm.EntryInterfaceDue`, training.md). The clear (START 0x7E63 on cells 1–15) takes
the view's own box (cells 2 and 14) too, which EXIT draws again only after a PROTECT went into the window
(ecl-format.md "PROTECT"); since s29 the Client blanks all of cells 1–15 in the 3D view as well (before: the 88 × 88
view only) and draws PROTECT's runes there. **D** (s30): DOSBox-X blanks the same cells and the Client's frame is equal
(ecl-format.md "PROTECT").

## Keys while the program is busy (START 0x89E9; program read s22) — P

The program reads the keyboard only through the Crt unit (KeyPressed START 0xA6FD, ReadKey 0xA70F: BIOS INT 16h, so
keys pressed meanwhile wait in the BIOS type-ahead buffer) and through these START routines (unit 0899):
- **GetKey 0x89E9** (0899:0059; the menu routine 0x2CC8C polls KeyPressed then calls it, the page prompt 0x7C85 calls
  it directly): ReadKey; Ctrl-S (0x13) switches the sound off / back on (DS:5372 / 5373); when ParamStr(1) equals the
  word at START 0x89E3 (the debugging word, training.md "J"), Ctrl-D (4) toggles DS:49F7 and Ctrl-C (3) quits
  (0x8990). Then, **unless the key is 0** (an extended key's first byte), `while KeyPressed: key := ReadKey`: every
  key waiting is read and **only the last one is returned**. A waiting extended key read in that loop comes back as
  its bare scan code (Up 0x48 'H', Right 0x4D 'M', Home 0x47 'G', End 0x4F 'O', …). The menu routine (0x2CE4F) and
  the AI's check (0xBBA5) call GetKey again when they get 0, for the scan code, and that call drains the rest the same
  way: such a read takes **the whole buffer** and gets its **last byte**, as a character when the first key was not
  extended, as an extended code when it was. Every other caller calls GetKey **once** (the key wait 0x7C85 at 0x7CC1,
  line input START 0x7ABB, START 0x43B6 / 0x81EF / 0x826B, GAME.OVR 0x24F84 number input, 0x2F7A6, 0x2FA45, 0x32352,
  0x32416): an extended key first gives it 0 and nothing more is read; Crt keeps the scan code, so the next GetKey
  reads that as a **character** (and drains whatever follows).
- **0x8A94** (0899:0104), called at 17 places (2 in START, 15 in GAME.OVR: START 0x48DA, the page prompt 0x7A29, the picture loader 0x2E170, the
  icon and tile loaders 0x31A8E / 0x31C34, LoadChar 0x1EAD2, the monster loader 0x1EEAA, Load 0x1F5E8, Alt-Q 0x9C74,
  the title waits 0xD4 / 0x6FB, …): **does nothing in v1.3** — right after its prologue a jump skips its
  `while KeyPressed: GetKey` loop to the return.
- **0x8AB2** (0899:0122): when a key waits, GetKey, result dropped: so the whole buffer is emptied. Called only by the
  fight start 0xE402 (three times, between its setup calls) and its setup loop 0xDFC6. The AI's key check 0xBB81
  (combat-ai.md) reads a waiting key the same way (GetKey: the last key; a 0 reads the scan code) and acts on Alt-M (0x32),
  space (0x20) and Alt-X (0x2D) only.
- No other reader found: no INT 21h function 0Ch (`mov ah, 0Ch` or `mov ax, 0Cxxh` before an `int 21h`) and no
  write to the BIOS buffer pointers (0040:001A / 001C) in START.EXE or GAME.OVR.

So nothing clears the buffer while text types: the typing (0x7737: glyph, then Delay(DS:49E2 × 10) per character),
PauseOnly and DELAY (Delay START 0xA69C: a busy loop on the BIOS tick byte) read no keys, and every key pressed
meanwhile waits; the **next key read takes the last of them** (a waiting key answers the page prompt at once, then the
next GetKey takes the rest). The s21 note "DOS clears its key buffer while text types" was the drain seen from the
outside: of several keys pressed during a text only the last acted.

**D** (s24, DOSBox-X, 3000 cycles, `START STING` at the start-up party menu; keys typed during the hall word's message,
J's pause of 225 ms): X then C opened Create (the last key); X then Right opened **Modify** (Right's scan code 0x4D
read as 'M'); Right then C did nothing and C was used up (an extended 0x63, which the menu ignores); 14 X then C opened
Create; 15 X then C (16 waiting) and 16 X then C did not: the BIOS buffer holds **15 keys and a key pressed when it is
full is lost** (not the oldest). Right pressed at the character-name prompt (line input, one GetKey) typed **M**.

**Client** (`ActionInput.ReadingKeys`): while the game logic waits on a plain pause (`DelayRequest` without
`ReportInput`) it reads no keys: a key or pad button pressed then waits (up to 15, later ones lost) and that frame acts
on nothing; the first frame of a key read (a menu, a key wait, the rest loop's poll, the command line) takes them all
(its own new press, if any, joins them last) and gets what GetKey makes of them: the last press, except that a key
after an extended first key reads as an extended code and an extended key after a character key reads as its scan
code's character (`DosKeys`: the BIOS bytes of each key name, US layout, keypad with NumLock on). A single press, or a
last one with no BIOS bytes (a pad button, a held key's repeat), is read as itself. A key wait or a line input
(`KeyRequest` not shown as a menu, `TextRequest`) reads once: with an extended key first, that press is read alone and
its scan code waits ahead of the rest as a character (`ReadingKeys(…, scanRead: false)`). Not followed: a key pressed
at a line input while it reads (no pause) acts as the Client's own: the arrows move its character grid, where the
original would type H, P, K or M. Mouse clicks during a pause are
dropped. A
fight's build reads the buffer once and drops the key (0xE402's reads), and the AI's key check reads it at every AI
turn (Core `FightKit.AiKey` → `MonsterAi.ReadKey`, `ActionInput.TakeAiKey`): the action `FightManual` (space, the
pad's right-stick press) puts the party back under the player's control, `FightQuickMagic` (Alt-M, left trigger)
reads as 0x32 and `FightAltX` (Alt-X, left-stick press) as 0x2D (s23), any other key is taken with no effect. The
check compares the byte only, extended or not, so the '2' key acts as Alt-M and '-' as Alt-X there (0xBBB0, 0xBC5F). The
mouse has no way to them during a pause (clicks are dropped). Enter does not end a pause. The Client types the text window's and the fight
panel's texts while DS:4A00 is set, as the program does (s25, s29); pauses and typing are its busy time. QoL
`SkipPauses` (settings, off = original): Enter or a click ends a pause, and keys pressed during it are dropped (the
Client's behaviour before s22).

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
  command line are identical too (0 differing pixels). Since s21 the frame is drawn from the program's frame symbols
  (below) and matches as well; the text window holds our help lines.

**Other areas (s11).** Save A's party placed in another area (`PorSaveGame.PlaceInArea`, tool `goldbox-tools
save-area`; the load also needs the disk in cell $6E12 and the area's wall-set ids at $4AFA–$4AFC), loaded from the
main menu in DOSBox-X, screenshots reduced with `tools/scripts/ega_screen.py`, compared with `view-compare` (`--keys U`
replays a step through the area loop). 0 differing pixels in all 11, pinned in `OtherAreaViewTests`:
- New Phlan (script 0, bounded map, three-block wall set 127 → WALLDEF3 0) at 2,4 E: view and area map;
- Valhingen Graveyard (script 10, bounded, two-block set 21 with the two-flag rule, $C05F = 8 / $C059 = 11) at 0,4 E
  and 9,9 E: view, area map, and one step on (vector 0 sets $49FD = 8: black sky);
- Kobold Caves (script 13, $49FB set) at 14,14 N: view, A refused (view stays), one step.
- The Client's whole screen for New Phlan equals DOSBox-X except our help lines (frame since s21).

**Position line** (START.EXE 0x34AB, program read s11): "x,y " only where $49FB = 0, the facing, HH:MM (both padded
to two digits), then " camping" (DS:49F3 = 2) or " search" ($6DCA bit 0). In Kobold Caves the line is the facing, the time and the search word. The
command line highlights the item whose hotkey is the last letter key read (after loading slot C: CAST).
**When it is drawn (s21):** only at fixed points: a turn or a member pick in the command routine (GAME.OVR 0x17266,
0x173D9), after the step or its bump (0x17707, 0x17734), the camp (0x197F5, rest 0x1804A), the full redraw of
interface states 2–4 (START 0x3379; state 1 and 6 do not draw it) and ECL CALL $2C90 when it redraws (0x3A83,
s23: with DS:4431 set and one of the redraw flags DS:84E0 picture, 84E3 sprite, 84E5 backdrop (script init 0x76AD,
writes to $C059/$C05F), 84E6 position (writes to $C04B–$C04D), 84E8 sky (writes to $49FD/$49FE) up; only that CALL
clears 84E5/84E6/84E8; Core `EclMemory.ViewRedrawFlags`). The Home / End pick on the command line (0x173BF: 0x23819,
the party list 0x173D0, the line 0x173D9) runs no command; the Client does the same (also a click on a name; on the
pad the triggers, since its shoulders are Look / Encamp on this line and the commands are checked first). In fact
every key of the command routine ends at the line: in 3D at 0x17266 (turns, the step, A, C, V, S, L, a pick), on the
overland at 0x173D9. **DOSBox-X s24** (test saves whose step runs, then waits in a one-word HMENU): `DAMAGE; CALL
$2C90` right after a load leaves the line off (DS:4431 is 0); `LOADFILES; LOADPIECES; DAMAGE; CALL $2C90` draws it
(84E5 from the script init); `LOADFILES; LOADPIECES; CALL; DAMAGE; CALL` leaves it off (flags cleared by the first
CALL); a write to $C04B or to $49FD between DAMAGE and the second CALL draws it. Home / End after the slums → New
Phlan exit (stale "15,4 E") pick the previous / next member and show "0,4 E". The Client matched all of them pixel for
pixel (help lines apart). Each DAMAGE line blanks
cells (17, 1)–(38, 15) before the party list is drawn again (GAME.OVR 0x96D0), which takes the position line off
until the next of those points (DOSBox-X s22; ecl-format.md "DAMAGE"). A command whose script
switches skips the step, so the line keeps the old square until the next command (DOSBox-X: slums → New Phlan shows
"15,4 E", New Phlan → slums "0,4 W"; the overland entry the same). The party list (START 0x1307) is likewise drawn only
at such points: LOADCHAR's current member does not show until a redraw (Client: the player's pick stays marked).

**Where the party list is drawn while exploring (P s39, D s39).** The list routine is START 00BA:0767 (linear
0x1307); the screen set-up START 00BA:27D9 (0x3379) draws it in its cases 2 (camp, 0x286A), 3 and 4 (overland and
3D, 0x28AE / 0x288C) and 6 (loot, 0x28EA), not in 0 or 1. Their callers while exploring:
- the area's entry and a script switch: LOADFILES + LOADPIECES once both ran after an entry (0x1B1A / 0x1B27), else
  the loop's 0x40BE; a NEWECL switch's end 0x402B (0x3EFA, after the new script's init and vector);
- a member picked with Home / End, at the command line (0x17254 / 0x17261, overland 0x173D0) or in a menu that takes
  them (HMENU's wrapper 0x8BCE / 0x8BDB, the camp 0x1976F, Magic 0x18C92, Alter 0x194EF, Order 0x1902B): 0x23819, then
  the list;
- ECL: DAMAGE after each line (0x96DD), LOADCHAR only when it removes an NPC (bit 0x80 with DS:84D2 / 84D3, 0x10E7),
  ADDNPC (0x3940), COMBAT's redraw (0x2635); the temple 0x5280 and the shop 0x7212 on entry (0x5298, 0x722B) and at
  the end of every pass of their menu, a pick and the exit pass too (0x5497, 0x745A); the heal list 0x4F4E after its redraw (0x51A0 / 0x51AD);
  the treasure Take 0x62B6 and the loot menu 0x644B only after a Home / End pick (0x6361, 0x66FA / 0x6717);
- the area's Cast when a list or a spell was shown (0x17D96), View (0x21D4F), Encamp's entry (0x196C7, case 2) and its
  exit only after an interrupted rest (0x3B3B; a plain Exit redraws the view alone, 0x3B48); in the camp the rest's heal
  with anyone healed (0x246CB), Order (0x1904F), Drop (0x19234) and Icon (0x19524);
- StatusDone's end (0xECB3 → 0xED5D); the effect handlers 15 (0xF246) and 44 (0xFDC3), called through the handler
  table on the clock's effect timeout: after their 1-point non-weapon damage (0x2C22E) they draw the list when
  DS:49F3 ≠ 5 (0xF28A / 0xF299, 0xFE0A / 0xFE19); START 0x2EEA (00BA:234A), the healed message (START 0x16EC) then
  the list outside a fight (00BA:239E / 00BA:23AE), called by handlers 50 (0xFF56) and 62 (0x10146) and the Cure Light
  Wounds routine 0x28083 (spells-effects.md). Client (s39): Core raises `EffectEvent.HandlerDamageListed` after
  handlers 15 / 44's damage and `Healed` from handlers 50 / 62 and Cure Light Wounds; `OutsideEffectMessages` shows
  the healed words and releases a held name at both.

**The list's values (s39).** The program's list shows names, colours, AC and HP as they were at its last drawing;
memory changes in between (a heal before its message, damage before its list call) show only at the next of the
points above. Client: `ClientPlayer.ReleaseRosterNames` copies the members' records at each of those points and
`Chrome.PartyList` draws the copies (`RosterAsDrawn`); a member that joined since is drawn from memory. START's member
pick draws the list with its pick lit and DS:5D92 unchanged (00BA:2CA1), which stays until the next drawing
(`ReleaseRosterNamesLit`). DOSBox-X runs: rest with revive, handler 62 and 15 136 of 136; camp Cure Light Wounds 43 of
43; revive / step / turn / re-encamp / icon editor 85 of 86 (one row-24 transition frame); Drop's yes/no 35 of 42 (the
misses then: the first 4–5 characters of a typed message seemed to come at once in DOSBox-X). **D** (s41, save A's camp:
Alter, Drop, No; the grab that keeps grabbing while a key is held): DOSBox-X types the message from its first
character, ~42 ms apart at speed 4 as the Client, so the earlier misses were the old blocking grab losing ~0.27 s after
each key, not the program (the typing 0x7737 draws each glyph straight into both video pages, then Delay(DS:49E2 × 10)).
31 of 38 Client frames equal, the misses the help lines and the run's first frame.
Not drawn: a step (0x16D58, the move 0x17438), a turn (0x2ED72 + 0x2EEF4), the area map toggle (0x2EEF4), Search and
Look (flags and the clock 0x241A1), the view's rebuild 0x2DCF4, PRINT and PICTURE: none of them reaches 0x0767 or
0x3379 through direct calls (call graph read from each; indirect handler calls are not followed `[?]`). **D** (s39, the
s38 revive save; revive in the camp's rest, Exit, Left, Up, Right, Up, A, A, Encamp): Darkstar's name stays light red
through the exit, both turns, both steps and the area map on and off, and comes back cyan on Encamp's first frame of
the list. Client: `ClientPlayer.ReleaseRosterNames` at these points (camp entry and interrupted exit, Order, Drop, Icon,
the shop's and temple's passes, DAMAGE, an area or script change, a change of the party's size (LOADCHAR's NPC
removal, ADDNPC), a pick shown on the list; LOADCHAR's change of the current member no longer releases it, and a hold
remembers the member the list shows); ADDNPC itself is not applied in the Client.

**Frames (s21, START 0x3F10 via the window routine 03F1:02E8):** 8 × 8 symbols 0x114 + 3 · style + piece, piece 0 the
corners, 1 the left / right columns, 2 the top / bottom rows; symbols 276… are 8X8D1 block 202 tiles 20… Every window
seen uses style 0 (styles 1–3 are tiles 23–30 `[?]` where used).

**Lairs on disks 6–8 (s24).** Save A placed with `save-area` (the 3D maps of the wilderness scripts 25 and 27 with
$4A9E = 255, as their vector 4 needs), loaded in DOSBox-X: ECL6 block 25 at 2,9 N, ECL8 block 27 at 3,11 W and
0,7 S, ECL8 block 13 at 14,7 N, ECL7 block 23 at 7,0 E, 4,9 S and 13,13 S. 0 differing pixels in all 7, pinned in
`OtherAreaViewTests.Lair_views_match_dosbox` (through `PorArea` and `PorGame`). Block 23's vector 4 loads no wall
set, so its view shows the saved ones: `PorArea.FromSave` (the tools' and tests' load) now re-reads them before
vector 4 as `PorGame` has done since s23 (0x1FBEE); before, `view-compare` showed an open view there.

**Valjevo Castle (s28).** Save A placed with `save-area` (`--walls 24,-1,1 --cell 49E7=1 --cell 49E8=1`, the state
block 6's entry leaves; `--cell 49C5=M` for the hedge-maze script 5, which loads no map in its vector 4 so the Load
re-reads map $49C5), loaded from the main menu in DOSBox-X (`MACHINE=ega`). Block 3's vector 4 takes map 3 or 5 from
the facing (table $9C24: N, W → 3; E, S → 5); blocks 4, 5 and 7 load no wall set, so the saved 24/–/1 show (the castle
wall types 11, 14, 15 come from set 1 in slot 3; types 1–10, the walls 3, the hedges 7–9 and the tower's 2–4, 6 and
10, from set 24's two blocks in slots 1–2).
Views: block 6 map 6 at 1,8 N and 5,8 E (hedges); block 4 map 4 at 14,10 N and 8,7 W (hedges) plus one step west;
block 3 map 3 at 1,9 W and map 5 at 13,7 E; the hedge maze (block 5) on map 3 at 8,8 N, 4 at 6,10 S, 5 at 5,6 E and
6 at 9,7 W; the inner tower (block 7, map 7, sky 8) at 2,5 S, 7,9 N and 10,5 W. 0 differing pixels in all 14,
pinned in `OtherAreaViewTests.Valjevo_views_match_dosbox` (through `PorArea` and `PorGame`; no diagnostic). Nothing
needed fixing. A save made in the hedge maze holds map 3–6 in $49C5 and block 5 in $49F2. **P + D (s39):** Load
(0x1F5E8) does not write DS:84DC; the area entry after it sets DS:84DC := low byte of $49F2 (0x4067–0x4070), the
block current at the last command read (written back at 0x40D6 / 0x40FA; NEWECL first stores the old block there,
0x1926). So such a save runs as block 5. DOSBox-X: after loading `save-overland` saves ($49C5 26, save A's $49F2 20)
the state read DS:84DC = 20. Engine since s39: `PorSaveGame.CurrentBlock` ($49F2) is the VM's current block after a
load (before: $49C5); the tests and `overland-compare` that took the overland's column from it take it from the view
mode now.
