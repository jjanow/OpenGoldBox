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
- **One-way walls** (program read 2026-11-17). GEO has no one-way flag: a one-way wall is an edge whose two faces
  differ, e.g. a wall with barrier 0 on one face and no wall on the other.
  - Movement reads only the face of the square the party stands on. The forward-step check (0x17469) and the
    edge test before it (0x16D58) call the barrier accessor (0x2EC47) with the party's own x, y and facing. The
    step itself (0x16E06) checks nothing more.
  - Inside the accessor, a face whose wall type is 0 (0x2ED72) leaves the result at 1 (passable), whatever plane 3
    stores. The face on the other side of the edge is never read.
  - So the walled side blocks (barrier 0: the step is refused) or opens the door menu (2, 3), and the other side
    walks through. In the data (counts above) 584 edges are one-way (540 wall vs no wall, 44 wall vs barrier 1);
    54 are doors from one side and open from the other (barrier 1 vs 2 or 3); 2 are doors from one side and walls
    from the other. `geo.py ascii` draws one-way edges as `!`; `geo.py list` counts them.
  - **Confirmed in DOSBox-X** (2026-11-17, s10; captures `oneway_through`, `oneway_blocked`,
    `oneway_barrier_through`, `oneway_barrier_blocked`). No one-way edge is in the Slums, so the test used copies of
    save A whose script area loads another map of disk 2 and whose vectors only count their runs (fixture
    `saves/SAVGAME.DAT`, `SAVGAMF.DAT`). Both kinds of one-way edge:
    - Mendor's Library (map 15), wall vs no wall: from 5,3 facing S (no wall) the party walks to 5,4; turned round,
      from 5,4 facing N (wall, barrier 0) the step is refused.
    - Stojanow Gate (map 9), barrier 1 vs barrier 0: from 4,5 facing S (wall, barrier 1) the party walks to 4,6;
      from 4,6 facing N (wall, barrier 0) the step is refused.
    - A refused step is not silent for the script: vector 0 runs, $49F0/$49F1 get the (unchanged) position and
      vector 1 runs on the same square; only the move and the clock minute are skipped.
  - Opening a door changes both faces (below).
- **Edges: the map wraps** (checked 2026-11-17, program read and DOSBox-X).
  - The three map accessors (barrier GAME.OVR 0x2EC47, wall 0x2ED72, event 0x2EE71) wrap a coordinate outside
    0..15 to the other side, except that while the current ECL block (DS:84DC) is 0 or 10 they return 0 for a
    square off the map. That rule only matters for lookups beyond the edge (e.g. the 3D view); the party's own
    square is always on the map.
  - On a forward step (Up key, 0x16D58) the engine first clears VM $6DD5, and when the face is not impassable and
    the step would leave the grid it sets **$6DD5 = 1** ("left the map") without moving. A face that is impassable leaves
    $6DD5 = 0 even on the map edge (0x16D8B skips the test; program read s11). Then vector 0 (move) runs;
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
- At run time the engine edits plane 3 in memory.
  - Opening a door sets the party's face to 1, and the facing face of the next square too (0x167D4, called twice
    with (x + dx, y + dy, facing + 4 mod 8)). 0x167D4 ignores a square off the map (no wrap), so at the map edge only
    the party's face changes.
  - When a door's menu would offer only Exit, the party's face is set to 0 (0x166CB): it becomes a plain wall. Only
    that face changes.
- **These edits are not saved, and every map load undoes them.** The GEO loader (0x2F9BE) always reads the block from
  `GEO<disk>.DAX` into the map memory; it does not check whether that map is already loaded. It runs in two places:
  - **Load game** (0x1FBC4) reloads map $49C5 when DS:49FA ≤ 1. Vector 4 then runs, and a LOADFILES there loads
    the map again.
  - **LOADFILES** (ECL 0x21, 0x19F1–0x1A37) reloads it whenever the map operand is not 127 or 255 and $49E6 (3D
    view) is set. Vector 4 runs every time a script is entered (0x3EFA, after NEWECL) and after every load
    (0x403E). In the Slums it is `LOADFILES #20` at $9A8B; the `trace_load_step` trace passes there.
  - Flow analysis from vector 4 alone (`ecl.py` rules): 27 of the 29 blocks reach a LOADFILES of a map from it.
    Two of them choose the map: ECL1 block 24 loads 24 or 31, and ECL5 block 3 loads [$6E82]. ECL5 block 5 has
    none in vector 4; it loads computed maps from vectors 0 and 1. ECL6 block 19 has no LOADFILES at all.
  - So leaving an area and coming back re-locks its doors. **Confirmed in DOSBox-X** (2026-11-17, s10, capture
    `door_relock`): in the Slums the door at (9,2) W was bashed open (plane 3 at (9,2) 0x80 → 0x40, party through to
    8,2). It stayed open through an orc fight and the walk back to 15,4. One step east left the Slums (vector 0 sees
    $6DD5 = 1 and runs NEWECL #0 on disk 3, New Phlan, map 0); one step west from New Phlan's 0,4 ran NEWECL #20 back.
    In the Slums again, (9,2) read 0x80 (locked) and all four planes equalled GEO2 block 20.
  - Mid-visit reloads (script read, s10). Over all 29 blocks, eight scripts reach a LOADFILES that loads a GEO map
    from vectors 0, 1 or 3 on hard paths (`ecl.py` rules; LOADFILES #127/#255, and #4/#5/#6 with $49E6 = 0, load no
    map). ECL3 blocks 0 and 11 reach their vector-4 LOADFILES #0 from vector 3 only through an ON GOTO fall-through
    after a two-choice HMENU, which never happens. What each one does:
    - the three wilderness scripts (ECL6 block 25, ECL7 block 26, ECL8 block 27): a random find of a small lair (a
      cave in the west and east, a wooded grove in the centre), answered with Enter, sets $49E6 = 1 and loads GEO map
      25 / 26 / 27, the 3D lair, at a start square from a table. Leaving the lair sets $49E6 = 0 and runs LOADFILES
      #4/#5/#6, which loads a BACPAC picture, not a map, then NEWECL to the same wilderness script;
    - Lizardman Keep (ECL8 block 16): the hidden tunnel down loads map 30 (catacombs); the stairs or tunnel up load
      map 16. The event table at $9B00 sends events 0x0B–0x0F (all on map 16) down and 0x11–0x17 (all on map 30) up;
    - Kuto's Well (ECL8 block 29): climbing down the rungs of the well (29 → 32), climbing back up (32 → 29) and
      being robbed and set free by the bandits after losing to them (32 → 29);
    - Temple of Bane / Wealthy Area (ECL1 block 24): stepping off the map edge (vector 0, $6DD5 = 1) loads the map
      named by a facing table: 24 → 31, 31 → 24, or 14 / 26 before the NEWECL to those areas (an ordinary area
      entry: Kovel Mansion's map 14 has locked doors, which are fresh anyway);
    - Valjevo inner tower (ECL5 block 7): leaving the tower loads map 3;
    - Valjevo hedge maze (ECL5 block 5, maps 3–6): stepping off a map edge loads the map from a per-map facing table
      ($9AEB–$9AFA). On the castle's outer edges the table names **the same map** (map 3 N and W, 4 N and E, 5 E and
      S, 6 S and W), so the map is re-read in place. A "disorientation" event (vector 1, $9C8A) loads a random map
      3 + Random(4), which can also be the current one.
    - So only the hedge-maze script reloads the map the party is on; all the others enter another map, whose doors
      are as stored in GEO. Maps 16, 24–27, 29–32 and 6 have no locked or barred face (`geo.py list`); maps 3, 4, 5
      and 7 do (2, 1, 2 and 1 faces), so in Valjevo Castle these paths re-lock doors in the middle of a visit.
      Traced from the data (s15): of the castle's outer edges only map 6's south faces at (4,15) and (11,15) (wall
      type 11, barrier 1) are passable, so the edge table's in-place reload happens only there (map 6, which has no
      locked face: nothing re-locks). The disorientation is event 7 (vector 1 ONGOTO slot 7 → $9C8A): map 3
      (0–1, 6–8) and (12,11), map 4 (14–15, 5–7) and (6,10), map 5 (3,4) and (15,9), map 6 (9,5) and (1,13); it puts
      the party on map 3 + r at (12,11), (6,10), (3,4) or (9,5) for r = Random(4), so with chance 1/4 the current map
      is re-read and map 3's barred (3,7) E / (3,8) E, map 4's (13,2) E or map 5's (12,7) W / (12,8) W close again.
    - **DOSBox-X (s18, captures `hedge_gate`, `hedge_disorient`)**: patched saves put save A's party on disk 5, map 6,
      with ECL5 block 5 as the script. Through the south gate at (4,15) S: $6DD5 = 1, vector 0 took map 6 from its
      table, LOADFILES #6 re-read the map in place (planes equal GEO5 block 6), CALL $C01E wrapped the party to (4,0)
      S and $6DC9 = 255 cancelled the program's step; vector 1 then ran on (4,0). Onto (9,5) from (9,6) N: vector 1
      took event 7, RANDOM 3 gave 2, map 5 was loaded and the party set at (3,4) (planes equal GEO5 block 5). Both
      replay in the VM (`ecl-compare`, `EclCaptureCompareTests`) cell for cell with RandSeed. Not tried: an in-place
      reload undoing an opened barred door (map 6 has none; the disorientation would have to roll the current map).
  - The restored state is the barrier stored in the GEO block. DOSBox-X: the Slums door at (10,2) N was bashed
    open (plane 3 byte 0x16 → 0x15). It stayed 0x15 through the fight that followed, the camp and the save to
    slot B. After loading slot B it was 0x16 again (locked).

**Door menu** (walking into a barrier 2 or 3 face; GAME.OVR 0x17443–0x1771B, door routines 0x1695F–0x16D57). The
menu is built from three DS flags, DS:6DDE (Bash), DS:6DDF (Pick), DS:6DE0 (Knock), plus Exit, which is always there.
All three are set to 1 after every completed step (FillChar at 0x16E74 in the step routine 0x16E06) and at start-up
(0xE915). So they last while the party stays at the door (turning does not reset them), and a step through an
opened door resets them. Nothing sets DS:6DE0 to 0. Model: `GoldBox.Core.Doors` (`DoorRules`, `DoorMap`).

- **Shown:**
  - Bash if DS:6DDE.
  - Pick if DS:6DDF and some member has a thief level (creature 0x9C > 0, signed; 0x16901 with 6).
  - Knock, barrier 2: if DS:6DE0 and some member has spell 31 (Knock) memorised. The slot byte must be exactly 31;
    a slot chosen but not yet memorised (bit 7 set) does not count (0x16C76).
  - Knock, barrier 3: if DS:6DE0 and some member has a magic-user level (0x9B > 0; 0x16901 with 5). Choosing it
    still needs Knock memorised to open the door. Since DS:6DE0 is never cleared, a party with a magic-user always
    keeps Knock in the menu, so the face is never sealed for it.
  - If only Exit is left, the face becomes barrier 0 (above).
- **Bash** (0x1695F, both types): party members in list order until one succeeds. Each member rolls once, and only
  if their Strength is in the table. Status is not read, so dead or unconscious members try too. A success opens the
  door and the party steps through.
  - Barrier 2: Strength 3–7 needs 1 on 1d6, 8–15 ≤ 2, 16–17 ≤ 3, 18/01–18/50 (and plain 18) ≤ 3, 18/51–18/99 ≤ 4,
    18/00 ≤ 5. Strength below 3 or above 18 is not in the table: no roll and no success (a quirk).
  - Barrier 3: 18/91–18/99 needs 1 on 1d6, 18/00 ≤ 2, 19–20 ≤ 3, 21–22 ≤ 4, 23 ≤ 5, 24 ≤ 7 on 1d8; 25 succeeds
    with no roll. A member with less makes no roll but sets DS:6DDE = 0, and the loop still goes on to the next
    member in the same try. So a strong member later in the list can still open the door, but Bash then leaves the
    menu for later tries.
- **Pick** (0x16BAA), barrier 2: every member in list order rolls 1d100, thief or not, until one succeeds. Success
  needs roll ≤ open-locks skill (creature 0x78) and status okay (0x10C = 0). After the try, successful or not,
  DS:6DDF = 0 and Pick leaves the menu. Barrier 3 (0x176E0): choosing Pick only sets DS:6DDF = 0, with no roll; it
  never opens the door.
- **Knock** (0x16D13, both types): the first member with spell 31 memorised loses that spell slot (set to 0) and
  the party steps through (0x16E06). Without such a member nothing happens. No roll. Unlike Bash and Pick (which set
  both faces to 1 through 0x167D4 before the step), Knock sets no face: the door stays locked in the map, so the
  door menu comes back from that side where its face is locked too (read s52; Core follows; `docs/re/s52-lane-E.md` asks whether to keep it).
- **Step routine** (0x17438, read whole s52): only with DS:49FA = 1 (3D) or 2–4 (overland); in either, VM word
  $6DC9 ≥ 255 (a script cancelled the step) is set back to 0 and nothing else happens; in other modes $6DC9 stays.
  The 3D branch reads the party's own face through 0x2EC47: 1 steps, 2 / 3 build the door menu above (only Exit →
  0x166CB seals the face and the menu is not shown; B / P / K as above, any other answer does nothing), 0 does
  nothing; a step not taken plays the bump sound (DS:2622). Either branch then redraws (START 0x34AB); at the end the
  picture frames (0x2E55D on DS:6ABE, START 0x6BD5 on DS:6B01 / 6B06) are freed and DS:6B00 / 6B05 set to 0xFF.
- After a success the party steps (0x16E06, which resets the three flags). After a failure the step is not taken,
  and the area loop goes on to vector 1 as after any step command.
- Dice: the game's dice routine (far call to GAME.OVR unit stub B0:48, routine 0x2BCCD) with (count, sides). With
  count 1 it is one draw of the generator (`ecl-format.md`, RANDOM).
- **DOSBox-X check** (captures `door_pick_fail`, `door_bash`; save A's party, door at (10,2) N, barrier 2):
  - The menu showed Bash, Pick, Exit. Pick made 6 draws, one per member: the five non-thieves have open locks 0, and
    the thief (6th, 45 %) rolled 94. The 7th draw is vector 1's RANDOM. The next menu showed Bash, Exit only.
  - Bash from that capture's RandSeed: members 1–3 (Strength 17, 18/90, 15) rolled 4, 6 and 2 against 3, 4 and 2,
    so the third opened the door after exactly 3 draws, and RandSeed after the run equals the capture.
  - Plane 3 afterwards equals the model's edit (only (10,2) changes; (10,1)'s S face was already 1).
  - Tests: `tests/GoldBox.Core.Tests/DoorRulesTests.cs`.

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
| GEO5 | 3 | Valjevo Castle, NW quadrant + NW part of the hedge maze | 3 (vector 4 facing N or W, table $9C24), 5 (hedge maze edges), 7 (leaving the tower) | 24, 25, 1 (block 3; two-flag rule: 24 in slots 1–2, 1 in slot 3, s28) | yes |
| GEO5 | 4 | Valjevo Castle, NE quadrant + hedge maze NE | 4 | 24, 25, 1 | yes |
| GEO5 | 5 | Valjevo Castle, SE quadrant + hedge maze SE | 3 (vector 4 facing E or S, table $9C24: so Lee's "ECL Script 3" for this quadrant holds) and 5 (hedge maze edges); block 5 is the hedge maze on maps 3–6 | none of its own: the castle's 24/–/1 stay loaded (s28) | yes |
| GEO5 | 6 | Valjevo Castle, SW quadrant + hedge maze SW | 6 | 24, 25, 1 | yes |
| GEO5 | 7 | Valjevo Castle inner tower (two small levels) | 7 | none: the castle's 24/–/1 stay loaded (DOSBox-X s28) | no (FAQ map has no coordinates) |
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
  of areas 16, 29 and 24. Valjevo Castle (GEO 3–7, from the ECL5 disassembly, s28): script 3 → map 3 or 5 by the
  facing, 4 → 4, 5 (hedge maze) → 3–6 by edge tables, 6 → 6, 7 → 7 (map 3 on the way out); see `view3d.md`.
- While ECL block 0 (New Phlan) or 10 (Valhingen Graveyard) runs, the map accessors return 0 for squares off the map
  instead of wrapping (GAME.OVR 0x2EC5D; see "Edges"). Steps still wrap if a script leaves an edge open and unhandled.
- Map names 24 and 31 (Lee's script list has them the other way round) were settled from the area script and the
  program: see `ecl-format.md`, "Scripts and areas".

## Wilderness (overland)

The overland map is **not** a GEO block and not in any DAX file. It is a table in START.EXE's data segment, read by
GAME.OVR (traced s18; v1.3 offsets):

- **Tiles: DS:3648, 44 columns × 36 rows**, one byte per square, row-major (index y × 44 + x). Every reader multiplies y
  by 0x2C (`mov dx,2Ch / mul dx / … mov al,[di+3648h]` at 0xD7D9, 0x16F79, 0x2DE78). 36 rows: the overland move
  clamps y to 0–35 (0x16F4D), and the 5 × 5 view clamps its top row to 0–31 (0x2DE06) and its left column to 0–39.
  The values are **1-based tile numbers**: the view draws value − 1 (0x2DE87). talonius and DrEvil place the same
  44 × 36 table. The tiles are drawn from `SQRPACI.DAX` (settled s19, below).
- **Three windows.** The wilderness scripts 25 / 26 / 27 switch to view modes 2 / 3 / 4 outdoors (DS:49FA, 0x3F4C),
  and the overland x of the party is VM $49C3 plus **DS:3C76[mode]** = 0 / 13 / 26 (0x16F5E square ahead, 0x2DB94
  marker, 0x2DDC6 view). The square-ahead routine (0x16EE3: $49C3 + DS:2736[facing], $49C4 + DS:273F[facing]) clamps
  x to 0–15 before adding the column and y to 0–35, then classes the tile code there; so each script's x runs 0–15.
- **The view** (0x2DDB7, then 0x2DE39): a 5 × 5 window whose top-left is (column + $49C3 − 2, $49C4 − 2), clamped as
  above; each square is drawn by 0x31B5D with its tile index.
- **The combat arena** of a wilderness fight reads the same table for the square under the party (0xD785,
  `combat-map.md`).

**Special locations** (Lee 12.2.5.2): squares whose tile changes with plot state. **9 records of 6 bytes at DS:0C36**:

```
+0  u16  x   overland column (0–43, the whole table's, not a window's)
+2  u16  y   overland row
+4  u16  tile number (1-based) to draw while the location is shown
```

- The view reads them with a 1-based index (`[di+0C30h]`, `[di+0C32h]`, `[di+0C34h]` with di = 6 × index, 0x2DE94–
  0x2DFF8). For every square of the window it tries indices 1–9 in order; a record on that square whose rule holds
  replaces the tile (its tile − 1), and a later index wins. The rules are code, per index; they read VM cells (zone 0
  + 2 × (cell − $4900), as `[DS:49D2]+0x1CC` is $49E6). What the cells mean is `[?]`:

  | Index | Shown when |
  |---|---|
  | 1, 2 | never (no arm in 0x2DE39) `[?]` used elsewhere: no other reader of DS:0C30–0C3B was found |
  | 3 | $4A8C ≠ 0xFF |
  | 4 | $4AD2 ≠ 0, or $4A7C bit 3 set, or $4A9F = 0 |
  | 5 | $4AA0 bit 1 clear |
  | 6 | always |
  | 7 | $4AA0 bit 0 clear |
  | 8, 9 | $4AB3 > 0xFD (word, unsigned) |

  talonius `BuildWildernessMap` also keys the records by index (its conditions were not compared).
  `OverlandMap.DrawnTile` (Core) implements the table above.
- The tile word − 1 is pushed as a byte, so a stored word above 255 draws its low byte − 1 (one v1.3 record does).
- **What the cells mean** (s19, static: the scripts that write them): $4A8C = 255 is set by the New Phlan council
  (ECL3 block 8 $ACFA) when it asks for the Bivant heir, so site 3 (12,31, tile 2) hides the buccaneer base until
  then; $4A9F = 1 when the party stumbles on the nomad camp (ECL7 block 26 $9BFF), $4AD2 = 1 and $4A7C bit 3 are set
  by the nomad camp's own script (ECL7 block 17 $A1BC, $B2C4 / $B3A0), so site 4 (25,11, grass) shows the camp only
  between finding it and those events; $4AA0 bits 0 / 1 are set by the eastern wilderness script when the Lizardman
  Keep / the kobold caves are found (ECL8 block 27 $9B4A / $9CE0), so sites 7 and 5 hide them until found; $4AB3 =
  254 is set in Yarash's pyramid (ECL7 block 23 $AC53), so sites 8 and 9 change the pyramid island and Phlan's square
  once Yarash is beaten. Site 6 (33,23) always shows its tile.
- The module holds both tables in `rules/overland.json` (module-format.md §6); the round trip rebuilds the 1,584 and 54
  bytes.

### Overland view, move and entry (program read s19; Core `Overland`, Data `PorOverlandArt`)

Checked against DOSBox-X (PoR v1.3, EGA, save A put on the overland with `goldbox-tools save-overland`): 17 views of
120 × 120 pixels match with 0 differing pixels, marker included (`OverlandViewTests`); the position lines matched too.

- **Tiles.** Two sheets of 128 tiles of 24 × 24: the game init loads `SQRPACI.DAX` block 1 (DS:5E52, tiles 0–127) and
  block 2 (DS:5E56, tiles 128–255) (GAME.OVR 0xEAA9 / 0xEACB); the tile draw 0x31B5D takes t < 128 from the low sheet,
  else t & 0x7F from the high one. Outdoors LOADFILES (o3 ≠ 255, $49E6 = 0) copies `BACPAC.DAX` block 1 (40 tiles) over
  low tiles 0–39 (0x1A4E → 0x31A8E, start 0; the loader copies the block's own item count, the 0x28 passed with it is
  not read, `combat-menu.md` "Ground, program read"); the combat ground loads (DUNGCOM's 25 / WILDCOM's 34 tiles at 0,
  RANDCOM's 6 at 34, 0xD82C) overwrite the same tiles, so BACPAC puts them back. Its 40 tiles equal SQRPACI's 0–39
  (tested).
- **Screen.** The view is at pixel (8, 8) (window cells 1–15, no inner box), tile (dx, dy) at (24·dx, 24·dy). The
  window's top-left square is (column + x − 2, y − 2) clamped to 0–39 / 0–31 (0x2DD9B).
- **Marker** (0x2DB87): ICON.DAX blocks 0–2 and 128–130 (animation slots 9–11, loaded when the command routine starts
  in modes 2–4 with DS:49F9 < 2), colour 0 see-through, light magenta → black kept opaque (DS:0CD2 → DS:0CF2). Square:
  column + x → < 2 as is, < 42 → 2, < 43 → 3, else 4; y → < 2 as is, < 34 → 2, < 35 → 3, else 4. Frame: counter
  DS:6DDC (0–5) → ICON block counter / 2 (+ 128 when odd), then counter + 1 mod 6; facings above 3 are mirrored
  (0x31FD2). No idle animation: one frame per redraw. On entry the interface (START 0x36FE) redraws (frame c) and draws
  the marker once more over it (frame c + 1). DOSBox-X after a load: frames 2 + 3; then one per step.
- **Marker counter DS:6DDC** (P, s25: every reference in START and GAME.OVR). Two writers only: the start-up (0 at
  0xE9D3) and the marker routine (+1 mod 6 at 0x2DC42). It is not in the save and Load (0x1F5E8) does not touch it, so
  a load goes on from the session's value. The marker routine runs from the redraw 0x2DCF4 (near call 0x2DE20, only
  in modes 2–4 with $49E6 = 0) and from the interface (START 0x3710). Callers of 0x2DCF4: the area loop (0x3FF5 the
  script switch, 0x40C8 the first pass, 0x414C Search/Look, 0x41D8 the step), Encamp (0x3B48), SPRITEOFF (0x37F6),
  CALL $2C90 (0x3A7E), the encounter draw (0x7A3A / 0x7A67), PICTURE 255 (0x1675) and START 0x3379 (the
  interface draw by DS:49F3: cases 3 and 4 redraw, 0x341F / 0x3441) and 0x36FE (0x370B). START 0x3379 is called by
  LOADFILES / LOADPIECES (0x1B1A, once both have run since the area loop started and DS:4432 is still set), the
  first pass (0x40BE), Encamp after an interrupted rest (0x3B3B) and many menus (shops, camp, items).
  - **After a load** the game loop's first pass (0x403E) sets DS:49F3 := 4, or 3 when $49F2 (the block of the last
    command read, written back at 0x40D6) is 25–27 and $49E6 = 0 (0x405D–0x408E), then runs vector 4. At 3 nothing
    is drawn until the first command's interface: frames c, c + 1 with c the session's value. At 4, START 0x3379
    case 4 runs once (inside a LOADFILES, or at 0x40BE) and then the redraw 0x40C8: two marker draws on the overland.
    A save made on the overland holds $49F2 = 25–27 (0 draws); `save-overland`'s test saves keep save A's $49F2 = 20,
    which is why DOSBox-X showed frames 2 + 3 after loading them at the start-up (0 + 2). The wilderness scripts'
    vector 4 has only a LOADFILES on its overland branch, so 0x1B1A does not draw there. Load does not write DS:49F9 either (writers 0x3F2B, 0xE9BE, 0x173EA): a load at the start-up or in the training hall (3D) leaves 1, so the first command draws the interface.
  - **Script switch** (0x3EFA): the redraw at 0x3FF5 runs when the old mode was outdoors and vector 4 loaded files
    (DS:4431), so crossing into the next window draws the marker once; DS:49F9 keeps the old mode, so the next
    command draws no interface. The same redraw draws the new area's view over whatever the old script left in the
    window: CITY at Phlan's north edge (block 14's vector 4 loads files) shows Phlan's 3D view at once, not the
    site's picture or CLEARBOX blank (Client: `EclVm.ScriptSwitchRedrawn` → `ClientPlayer.SwitchRedrawn`; the live
    vpad run of s25 had found the window black until the next command). A 3D → 3D switch (DS:49F9 = DS:49FA = 1)
    always redraws. **D** (s39, save A with vector 0 = PICTURE 2; NEWECL 20, then CLEARBOX; NEWECL 20): NEWECL's load
    (0x1996 → 0x7814) clears row 24 (0x2CFCA), shows "Loading...Please Wait" there for the disk's time and clears it;
    then the 3D view replaces the picture before block 20's vectors type their text under a blank row 24. After
    CLEARBOX the redraw draws the 88 × 88 view alone (0x2DCF4): the rest of cells 1–15, the view's box among them,
    stays blank, still after the text and two turns. Client (s39): row 24 blank after a NEWECL until the next menu or
    command line (`ClientPlayer.BottomRowClears`; the loading line itself is disk time, not shown), the box ring kept
    blank (`ClientPlayer.ViewBoxBlank`, ended by the screens that draw the whole layout: camp, shop, temple, fight,
    hall, EXIT's window redraw after a PROTECT, and since s41 View's exit, Cast and a load's entry: every START 0x3379 caller, view3d.md "Text window"). Frames: 54 of 85 (picture) and 67 of 85 (CLEARBOX) equal; the
    rest are later than DOSBox-X's grab ended (the Client has no disk time, ~1.2 s ahead) and the help lines.
  - **Every redraw counts** (P + D, s39): the marker routine runs inside 0x2DCF4 whatever called it, so the
    encounter draw's redraws (0x7A3A with the sprite up, 0x7A67 turning the area map off; ecl-format.md "The
    encounter draw 0x7A21"), PICTURE 255's (0x1675: it runs when DS:49F9 > 1 or DS:49FA = 1, so on the overland after
    the first command too) and START 0x3379 cases 3 and 4 (0x28A1 / 0x287F) advance DS:6DDC. On the overland DS:49F3
    is 3 outside the camp (the camp sets 2, 0x196AC, and puts the old value back, 0x197F2), so these 3379 callers
    redraw there: COMBAT's end (0x2635, after DS:49F3 := 3 or 4 at 0x25F7–0x2605: every fight, shop, temple or
    after-combat step), View's exit (0x21D4F), Cast (0x17D96) and its target pick (0x27AA9), Trade (0x22781, 0x231A4).
    The camp's own menus (Memorize 0x182E1, Scribe 0x1865F, Display 0x18BF9, Alter 0x19524) run at DS:49F3 = 2 (case
    2: the camp picture, no 0x2DCF4). **D** (`save-overland` 26 at 8,19 N): V then Escape drew the next marker frame
    at View's exit (the view back with its window frame, case 3); `save-overland` 26 at 10,20 N with vector 0 =
    SETUPMON 0, 2, 0; APPROACH: the view redrawn with the next frame under the sprite at APPROACH.
  - Engine: `EclMemory.MarkerCounter` and `Overland.Redrawn` on the loop's redraws, the switch, CALL $2C90,
    SPRITEOFF, PICTURE 255, the encounter draw and COMBAT's end (s39); `PorGame` loads take the session's counter
    (`markerCounter`, the Client passes its game's) and add the first pass's draws. The command line's View and Cast
    (s41, `AreaLoop.MenuCommand`): neither ends the routine (0x1738A → 0x173D9 → 0x172E3), so the interface
    (+2) is drawn once per routine entry whatever keys come first (`AreaLoop` keeps it drawn until 0x173E7), and
    View's exit, Cast's end after a list and its target pick each draw START 0x3379 case 3 (+1). **D** (s41,
    `save-overland` 26 at 8,19 N, 3000 cycles): V, Escape; C on Thrender (no spells: the message, nothing drawn);
    End × 3, C, Bless and Cure Light Wounds with the target pick (the cast-on-whom prompt), S, Escape: every screen equal
    to the Client's but its help lines and the pick's hints (one DOSBox-X frame drew the spell page before its menu
    line: machine time). Trade (0x2276E / 0x23190, s42): each START 0x3379 (T, and the money pick again after a transfer) steps the marker
    once (**D** s42, the same save: frames alternate per draw; camp.md "Trade").
  - **The Amiga** (P + FS-UAE s46): the counter is hunk 32 +0x1A33 (BSS); its four references are the start-up's
    initialiser (hunk 25 +0x153A, := 0; called once from hunk 10 +0x594) and the marker routine hunk 7 +0x37B6 (slot
    9 + c / 2, frame c % 2 at +0x3844–0x3876; c := (c + 1) mod 6 at +0x387E–0x388A). The marker routine runs from the
    redraw hunk 7 +0x396A (at +0x3B24, modes 2–4 unless $49E6 ≠ 0) and the overland interface hunk 13 +0x5E80. Load
    (hunk 16 +0xFC2) does not touch it; the first pass (hunk 16 +0x51C8) is DOS's 0x403E ($BA := 4, or 3 when $49F2 is
    25–27 with $49E6 = 0; then hunk 13 +0x5B16 case 4 and the redraw). So the Amiga follows the same rule as DOS.
    FS-UAE (disk A's save A put on block 26's overland at 10,20 N, $49F2 = 0, loaded at the start-up): frame c3 (slot
    10, frame 1), the Client's (`--art amiga`, `save-overland` save) silhouette equal. Amiga saves: savegame layout in
    save-format.md "Amiga".
  - **A hall Load on the overland: no extra draws, slot 11 holds ICON 0** (P + FS-UAE + Client s47; settles s46's
    open item, where FS-UAE looked like "c0" and the Client "c4" after the same-area hall Load). The whole path,
    Amiga (training.md "The Amiga's hall Load"): vector 0 runs before the step (the area loop hunk 16 +0x5384–0x5396,
    the step +0x53DA and its redraw +0x53DE, then vector 1 +0x5438), so PROGRAM 0 (hunk 16 +0x4B88) runs the party menu
    (hunk 10 +0x64E: $BA := 0), whose Load (+0x9D0 → hunk 16 +0xFC2) reads the save, sets $BF (DS:49F8) when the
    save's $BA is not 0 and leaves the menu; PROGRAM 0 then draws hunk 13 +0x5B16 by the save's $BA (2: the camp
    screen, no redraw) and the run goes on; the step's redraw is the only marker draw before the next command, whose
    interface does not run ($C0 = 3 from the command routine's end). The difference is in the slots: **Load itself,
    with location type $C1 > 1, loads ICON block 0 into icon slot 11** (hunk 16 +0x1488–0x14A0: hunk 22 +0x193A("ICON",
    block 0, slot 11), the loader's arguments name, block, slot as the interface's three calls at hunk 19
    +0x2AF6–0x2B26), and **DOS does the same** (0x1FBC9–0x1FBE6: with DS:49FA > 1, ICON 0 into slot 11 through
    0x3E9:0x34, the interface's loader). So the step's draw at counter 4 took slot 11 = block 0, the picture of
    counter 0. FS-UAE (s47, disk A with test saves B (block 26's overland at 10,20 N, vector 0 = PROGRAM 0) and C
    (vector 0's code an EXIT), B loaded at the start-up, then keypad 8, Remove × 6, Load C, and four more steps):
    the five markers' light pixels give blocks 0, 128, 0, 128, 1 for counters 4, 5, 0, 1, 2 (the first equal to the
    third, the second to the fourth, the fifth to neither; "two extra draws" would give 0, 128, 1, 129, 2, and 128
    differs from 129). After a start-up Load the slot is loaded again by the first command's interface ($C0 < 2),
    so only a hall Load leaves it (or a start-up Load's first-pass draws at counters 4 / 5, before the interface; the
    s48 fix below covers both, since every Load with location type > 1 now reloads the three slots).
    Client (s47): `PorGame.ReloadSavedMap` (Load's tail) sets `AreaLoop.MarkerSlots[2] = 0` for location type > 1,
    as `IconEditorDone`; the Client's run of the same steps on DOS save A placed alike (`save-overland`, vector 0
    patched the same way, `--art amiga`): all five markers equal to FS-UAE's (0 of 57–64 light pixels differ at the
    `ashot` offset (+2, −1)). DOS not run in DOSBox-X on this path (the same code read). **An original bug, fixed
    (user, s48)**: since s49 `ReloadSavedMap` loads the three slots again (`AreaLoop.ReloadMarkerSlots`: ICON 0, 1, 2
    into 9, 10, 11), so after a hall Load counters 4, 5, 0, 1, 2 draw blocks 2, 130, 0, 128, 1 as on any overland step
    (test `OverlandViewTests.A_hall_load_on_the_overland_loads_the_marker_slots_again`; not re-run on screen). The icon
    editor's slot-11 load (below) is not changed.
  The draw (0x2DC18–0x2DC30) takes icon slot 9 + counter / 2, frame counter % 2; the interface load puts ICON 0, 1, 2
  into slots 9, 10, 11 (0x17285–0x172D5, each slot block b and b + 128). The camp's Alter Icon on the overland ends by
  loading ICON 0 into slot 11 (and 1 into slot 12; icon editor 0x1D84A, start-menu.md §3), so until the next interface
  load counters 4 and 5 draw blocks 0 and 128 (**D**, s25: script 26 at 10,20 N, Encamp, Alter, Icon, Exit, Yes, out of
  camp, then E S W N E S: blocks 0, 128, 0, 128, 1, 129, 0 for counters 4, 5, 0, 1, 2, 3, 4; a program bug, kept).
  Load does the same with location type > 1 (0x1FBC9–0x1FBE6, s47 above).
  Engine: `AreaLoop.MarkerSlots` / `IconEditorDone`, the Client's view equal to DOSBox-X's on all seven screens.
- **Commands** (0x17054, modes 2–4): the line DS:04DF (the 3D line without Area), C (no status check), V, E, S, L as
  in 3D; Up / Right / Down / Left set the facing to 0 / 2 / 4 / 6 and end the command (vector 0, step, redraw,
  vector 1). The routine ends with DS:49F9 := DS:49FA (0x173E7).
- **Step** (0x17438 → 0x16EE3, when $6DC9 < 255): x and y + the facing's deltas as signed bytes, clamped to 0–15 /
  0–35; the stored tile at (column + x, y): 0x4E, 0x7E–0x80, 0x82–0x84, 0xA8–0xAC, 0xAE–0xB3, 0xBC, 0xDD–0xDE,
  0xE1–0xE9, 0xF5 refuse silently; 0x66–0x6C and 0x72 refuse with the river message (cs:0x7F4, flashed on row 24 in
  colour 14 for DS:49E2 × 225 ms, START 0x7DF8 at 0x16FEE) while $4AA2 = 0; else $49C3/$49C4 = x, y and 12 hours
  pass (clock count 12, unit 3), Search or not. The scripts' vector 0 cancels steps off the window's edge ($6DC9 =
  255).
- **Row-24 messages** (P, s25). START 0x7DF8 (0709:0D68) is the combat message routine 0x2233 with a colour
  argument: copy the text (40 chars), clear row 24 columns 0–39 (0x767B), print at column 0 in the colour (0x76B6),
  PauseOnly (0x7E4E: Delay(DS:49E2 × 225) ms; speed 0 does not wait), clear row 24 again. The screen keeps
  everything else (view, party list, text window); the command line comes back at the next command read. Its eight
  callers, all with colour 14: the command routine's two, 0x16FEE (the river, overland step) and 0x170F1 (A where
  $49FB ≠ 0, cs:0x97B, 3D branch only: the overland line has no A), so the river is the overland's only one; the
  other six are outside exploring (0xCEE, and 0x1BA59, 0x1C359, 0x1C393, 0x1C651, 0x1C67F in the party menu /
  training units: training.md, start-menu.md). The overland command branch (0x17277–0x173E7) calls neither 0x7DF8
  nor 0x2233. In the program the arrow sets the facing DS:6AAF in the command routine (0x17397–0x173D4) and the
  message comes inside the step, before the step's redraw (0x41D8), so the view still shows the marker drawn with the
  old facing meanwhile (the marker routine reads DS:6AAF when it draws, 0x2DC14). **D** (s39, `save-overland` 26 at
  8,19 N, Left into the river at 7,19, speed 4): the north marker (unmirrored) stays through the message, the step's
  redraw then draws the west one (mirrored, next frame). Client: `EclMemory.MarkerFacing` (set at every marker draw,
  `Overland.Redrawn`, and by the interface) is the facing the view draws; the interface's composite (frame c + 1 drawn
  alone over frame c, START 0x36FE / 0x3710) stays on screen until the next redraw (`EclMemory.MarkerDrawnTwice`; the
  Client drew frame c + 1 alone after the first command before s39). Frames: 2 of 2 equal during and after the
  message but for cell (16, 16): the overland interface's layout (START 0x36FE, 0x2E03F) has no joint there and the
  Client of s39 drew one (fixed in s43; when the joint comes and goes on the overland: view3d.md "The joint after a
  fight, a shop, a temple and a loot on the overland"). Client:
  `ClientPlayer.Flash` (a `DelayRequest` with the row-24 line in colour 14), the texts `Msg.RiverBlocks` /
  `Msg.NoAreaMapHere` read from GAME.OVR.
- **Position line** (START 0x34C2): x = $49C3 + 13 in mode 3, + 26 in mode 4 (immediates), y = $49C4.
- **What runs on a special square** is the wilderness script, not the program: vector 1 walks its tables (ECL7 block
  26 $9B0A: rows $B04A, counts $B061, columns $B053, cases $B06A → ONGOTO; the other two scripts likewise) and runs the
  site's text, picture and HMENU; leaving sets $49E6 = 1, LOADFILES #127, the position ($C04B–$C04D), the disk and
  NEWECL (tested: Phlan's north edge → CITY → block 14 at 4,0 S, as DOSBox-X). Otherwise a random encounter may come.
  Pictures on the overland are drawn in the 3D view's box over a blank window; sprites over the tiles (DOSBox-X).
- **Live run (s25, Client on Xvfb with the virtual pad, no DOSBox-X):** a `save-overland` save at 26 12,24 N: the
  d-pad travels (S: mirrored marker, 12 hours, the pad's help words), Phlan's north edge gives the site text, picture
  and HMENU (A picks CITY), the 3D view of Phlan follows (fixed, above), walking north out of Phlan brings the site
  menu back and LEAVE returns to the overland; at 26 5,19 east the river message flashes on row 24 in yellow and goes.
  The position line keeps the old square after a script switch, as the program does (view3d.md).
- The CALL ids the wilderness scripts use besides $2C90 ($C01B, $0806, $2C4E, $2C51, $C003, $C009) have no case in the
  handler 0x3A15 (address − 0x7FFF compared with 0xAC91, 1, 2, 0x3A04, 0x401F, 0x4019): no effect in v1.3. The
  SAVETABLEs of ECL7 block 26's vector 4 into $D000 / $6300 once Yarash is beaten land on no VM area (the writer
  0x814A ignores them): no effect in v1.3.
- The special-location table (DS:0C36 in v1.3) sits verbatim in the START.EXE file. v1.3's START.EXE is EXEPACKed, but this table survives the packing
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
   crossing a hedge, which Lee says costs damage, is scripted: ECL5 block 5 vector 0, the move vector, when no edge was crossed ($9AFB, $9B38–$9BD8) asks when the
   wall ahead ($C04E) is type 7 or 9, then DAMAGE (the thorn) and CALL $C01E (the step through) (s15).
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

- Settled (s18, static): ECL5 block 3 loads GEO 3 or 5 by facing (table at $9C24); block 5 loads 3 + Random(4) =
  3–6 at $9CD4, and at $9AD8 a facing table per current map 3–6 (module-format.md §6).
- The WALLDEF piece layout.
- Settled s19: the overland tile file (SQRPACI, BACPAC) and what the special-location cells mean (above).
