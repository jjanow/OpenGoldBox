# s60 lane V: DOS presentation (3D view, pictures, icons, sound calls, start-up)

Scope: the ledger's "Presentation only" rows 7, 8, 9, 11, 12, 13, the W-by-citation rows of START segments 0464, 0899,
08AE and GAME.OVR stubs 19, 1C, 3CA, 3CD, 3D0, 3D5, 3DB, 3E0, 3E9, 428, 82, the DOS hall-Load slot-11 path and
DS:49FD / 49FE. Every routine was read on the EXEPACK-expanded image (capstone, the ledger's call graph). Low-level
blitters and decoders are not ported as code: the importer and the Client's renderer build the same pictures, so for
them `read` means each branch was read and its visible effect has a Client counterpart (cited), not a line-by-line port.

Statuses: `seen` matched on screen (DOSBox-X next to the Client on Xvfb, 3000 cycles, EGA indices compared over the
whole 320 × 200 screen unless stated), `exec` oracle + test, `read` branch by branch, `fixed` difference fixed,
`bug?` question for the user, `open` not finished.

## Fixes

1. **Party icons: the head is ORed into the body** (`CombatArt.Party`). The builder 0x1E468 merges the head through
   0x1E315. In EGA mode it ORs the packed pixel bytes (+0x17) and ANDs the see-through masks (+0x13). The masks are
   1 where a pixel is colour 0: the icon loader 0x31C34 reads every block with see-through colour 0 through START
   0x4751 → 0x48E5 → 0x4640. So where head and body both draw, the pixel is head | body. The Client drew the head over
   the body. 32 of the CHEAD × CBODY pairs (both sizes, both poses) differ by one or two pixels; three of them are
   head 11 with body 4, head 3 with body 1, and head 7 with body 16. Test:
   `CombatTests.Party_icon_heads_are_ored_into_the_body_as_the_builder`.
   **D:** in the camp's Alter → Icon on Thrender (head 11, body 3), Parts → W → N gives body 4. DOSBox-X shows pixel
   (46, 113) as 6 (4 | 2), and the whole editor screen is 0 pixels off the Client. The Amiga already ORed, so its
   comment and `por-art-formats.md` are corrected.
2. **The icon editor's frame boxes come from icon slot 25** (`PartyMenuScreen.DrawIcons`). The preview 0x1CE5E blanks
   the band in colour 0 (START 0x6C72). It then draws slot 25's two pictures (COMSPR block 25 and block 153, loaded
   at start-up 0xEB1D) and the icon's two poses (slot 12) over them, colour 0 see-through. The hand-drawn boxes had
   two-row top and bottom edges; COMSPR's edges are one row, with other corner pixels. Rows 57, 78 and the corners
   were off in DOSBox-X; after the fix the screen is 0 pixels off. The `[?]` in `start-menu.md` (§3 and §7) is
   cleared.
3. **A DOS sprite past its block's frames draws nothing** (`ViewPictures.DrawSprite`). The renderer 0x2E7D6 draws
   frame-table entry distance + 1 (2 on the overland). For a new block the loader 0x2E170 first frees and zeroes the
   table (0x2E55D: 0x42 bytes, DS:6B0A := 0, DS:6B1C := 0xFF), so an entry past the block's frames is nil and nothing
   is drawn, not even the buffer flush. Six DOS blocks have two frames, so there is no sprite at distance 2: SPRIT6
   10, 67, 74, 119 and SPRIT8 113, 116 (frame 1 is an 88 × 88 picture). The Client drew the last frame. Under Amiga
   art the clamp to the last frame stays. An entry outside 1..3 stops the program with an error line and quits; this
   is not modelled, and the encounter draw passes only distance + 1 or 2. Test: `ArtSettingTests`, the two-frame
   block case. Read only, not seen on screen (no encounter with these sprites at distance 2 was set up).

## Rows

| Routine | Status | Note |
|---|---|---|
| 0x1E468 icon build | read, fixed, seen | Not "picks a free slot" (0x1F42C picks). Sets disk 3, takes the record at DS:5D92, loads CHEAD + size letter (DS:08C6 + size: S / T) block 0xBD into the **scratch slot 11** and CBODY block 0xBE into slot 0xBF (0x31C34: T adds 0x40; both poses, block and block + 0x80, **CHEAD too**, unlike the Amiga). Then 0x1E315 merges head pose k into body pose k; with argument ≠ 0 it recolours both pictures (identity map, then DS:3CF5[k] / +8 := the low / high nibble of 0xC0 + k, k = 1..6; START 0x649C). It frees slot 11 (0x31BBA) and flushes keys. Callers: the icon editor (0) ×7, Load 0x1FB7F (1), Add ×2 (1), ADDNPC 0x1F403 (1) |
| 0x1E315 head merge | read, fixed, seen | Video mode DS:4A1E 0 / 2 (CGA / Tandy): OR and AND over the head's whole +0x11 bytes. 1 (EGA): OR over +0x11 bytes, AND over slot 11's size / 4 (the 1-bit mask). The Client draws EGA only |
| 0x1CF3D copy to slot 12 | read, seen | (source slot, destination slot, flag): copies both poses' pixel bytes and masks (size / 4 in EGA); with the flag, recolours the destination from the current record as above. Matches `start-menu.md` §3 |
| 0x1CE5E editor preview | read, fixed, seen | Not EGA-only: mode 0 (CGA) fills colour 3 and draws no frames. Other modes fill colour 0 over 96 × 24 at cell (1, row) (START 0x6C72), then slot 25 poses 0 / 1 at cells (1, row) and (4, row) (0x31F68, facing 0) and slot 12 the same, then START 0x6192 |
| 0x2E0CE frame draw | read, seen | A nil picture draws nothing. Flag ≠ 0: START 0x53B1 into the off-screen buffer at (x − 1, y − 1) (the buffer's origin is cell 1, 1), then 0x6192. Flag 0: START 0x5F91 straight to the screen. Callers: the menu routine's animation (flag = its animate flag, cell 3, 3), PICTURE and the encounter close-up (1, cell 3, 3), the end-game animation (1), and the head + body draws 0x2E11E / 0x2E13E / 0x2E765 (0) |
| 0x2E11E, 0x2E13E, 0x2E765 head + body | read | 0x2E765(x, y, both): head (DS:6B01) at (x, y); with both, body (DS:6B06) at (x, y + 5 rows). Opaque and unclipped; header x / y not used. Every DOS HEAD is 88 × 40 at 0,0 and every BODY 88 × 48 at 0,0, so the Client's head rows 0–39 with the body from row 40 are the same picture |
| 0x2E55D picture free | read | Frees entries 1..count (pointer at +8k − 2), zeroes the 0x42-byte table, DS:6B0A := 0, DS:6B1C := 0xFF, so the next load is a new one (`por-art-formats.md` "Playback") |
| 0x2E7D6 sprite renderer | read, fixed | Index outside 1..3: video mode restored, error line, quit. Else entry `index`: nil → nothing. Otherwise into the buffer at the picture's header (x, y) + cell (2, 2), i.e. window cell (3, 3) + header offset, with the mask, then 0x6192. The Client draws at the window + (X·8, Y·8), see-through (`ViewPictures.DrawSprite`) |
| START 0x4640 | read | Mask byte for 8 pixels (4 packed bytes): bit 7 − i set where pixel i equals the given colour (the see-through colour) |
| START 0x46BB | read | CGA map lookup: nibble c of the block's 8-byte map (odd c → low nibble, even → high) |
| START 0x470F / 0x473F | read | Not picture code: BIOS video mode set (INT 10h AH 0; the previous mode kept in DS:4A1C) and restore of the mode START was entered with. Callers: start-up and the fatal-error exits |
| START 0x4751 block read | read | (slot pointer, see-through colour, mask flag, block, name). Frees the old picture, reads the DAX block (N: START 0x8736), allocates (0x6ACE: height, width, items, the mask when flag bit 0 is set), header x / y to +4 / +6 and the CGA map to +9, decodes (0x48E5), frees the read buffer, flushes keys |
| START 0x48E5 decode | read | By mode: CGA, 2 bits per pixel through the CGA map (0x46BB); EGA, 4 plane bytes per 8 pixels (masks DS:25F6) plus the 1-bit mask (0x4640); Tandy, packed nibbles with a byte mask. The importer decodes the EGA nibbles to the same indices (pixel checks in `view3d.md` and `por-art-formats.md`) |
| START 0x4F14 piece draw | read | (buffer, picture, flag, item, row, column): item `item` into the buffer at (column, row · 8). Masked when flag bit 0 is set and the picture has a mask, else opaque. No clipping; dirty rows DS:4A28 and spans DS:4AD0 / 4C20 / 4D70 updated; by mode. Used by the tile draw 0x31B5D and the piece draw 0x32392 |
| START 0x5AC4, 0x5B49, 0x5BD6 | read | The mirror primitives of 0x5C07: bit reverse of an EGA plane byte, 2-bit group reverse of a CGA word (masks DS:25E5–25F3), nibble swap of a Tandy byte |
| START 0x5F91 cell draw | read | Item 0 opaque straight to video memory at (x, y · 8), no clipping, by mode (EGA: planes through ports 3C4 / 3C5, row table DS:5050). Callers: 0x2E0CE flag 0, the piece draw 0x32392 mode 0 (window frames), the introduction |
| START 0x6ACE allocate | read | Not a palette flash: allocates and zeroes a picture record (0x17 + items × per-item size; per-item = h · w · 2, ×2 outside CGA), sets +0 / +2 / +8 / +0x11, and with flag bit 0 a mask (size / 4 in EGA, full size otherwise) at +0x13 |
| START 0x6BD5 free | read | Frees the mask (size / 4 in EGA), then the record; nils the pointer |
| START 0x6C72 rectangle fill | read | Not a recolour: fills (column, row, width columns, height rows) of a buffer with a colour, clipped to the buffer, rows marked dirty. CGA through DS:0D02 and 0x46BB, Tandy DS:0D06, EGA plane bytes 0xFF where DS:25F6[k] & colour. Callers: the 3D backdrop 0x2EA6B, the editor preview |
| START 0x53B1, 0x5C07, 0x6192, 0x649C, 0x704D | seen | s52 read. This session the icon editor (0x31F68 → 0x53B1, 0x6192, recolour 0x649C) and the 3D view and area map were 0 pixels off DOSBox-X |
| 0x2E8F7 | read, seen | Not a wall column draw: **the area map** (DrawAreaMap): top-left square clamp(x − 5, 0, 5), clamp(y − 5, 0, 5); 11 × 11 squares of symbol 0x104 + walls (N 1, E 2, S 4, W 8 through 0x2ED72); then the arrow 0x100 + facing / 2 at the party; flush 0x2EEEA. Save A at 15,4 W: the area map 0 pixels off (rows 0–149; the Client's help lines below are its own) |
| 0x2EA6B backdrop | read | Four 0x6C72 fills of the view buffer: sky 43 rows, horizon 1 one row, horizon 2 two rows, ground 42 rows, 88 wide, colours DS:6A60–6A63 (`view3d.md` "Backdrop") |
| 0x2EB08 | read | DrawWallPiece: piece tables DS:0C8C (first byte), 0C96 (width), 0CA0 (height); cells outside 0..10 clipped, symbol 0 skipped, the index advancing on every cell; symbols from the wall-set table DS:6A58 (0x9C per set) (`view3d.md`) |
| 0x2EEEA | read | START 0x6192 only (flush) |
| 0x32392 piece draw | read | Symbol → set 0 (1–0x2D), 1 (0x2E–0x73), 2 (0x74–0xB9), 3 (0xBA–0xFF), 4 (0x100–0x11E), minus DS:2722[set]. 0 or ≥ 0x11F gives an error line and quits; an unloaded set draws nothing. Mode ≠ 0: 0x4F14 into the buffer with the flag; mode 0: copied into the scratch picture DS:5E4E and drawn straight to the screen (0x5F91: window frames). 3D view pixel-equal (`view3d.md`; seen again this session) |
| START 0x8AE0, 0x8B4C, 0x8C5B, 0x8C85, 0x9E91, 0xA2FA | read | The driver's CS:0000 sound time, CS:006C sound delay (with the clock-back guard), CS:017B suspend, CS:01A5 set auto-stop, CS:1391 timer uninstall and CS:181A gate byte (CS:020D; commands 0 / 1). They match `sound.md` §2.1 line for line; the ledger had not resolved the CS offsets. The command 0x8BBC waits a fixed sound delay(10) whatever the auto-stop value. Auto-stop callers, every one bracketed in Core: cast flight 0x27E08 / 0x27E56 and Hold Person 0x286F5 / 0x2874E (`FightSpells`), START 0x2BB1 / 0x2D07 (`CombatPlayer`), bodies 0x31554 / 0x317D6 (`CombatMap`), missile 0x15011 / 0x15069 (`Fight.MissileFlight`); the start-up 0xEA37 sets 100 (on). Suspend is called by Ctrl-S (0x89E9) and the start-up |
| START 0x4354 configuration | read | See "Start-up" below |
| 0x00D4, 0x02DA, 0x06FB, 0x206A3, 0x20941, 0x20D50, 0x2E03F, 0x2F6FF, 0x2F9BE, 0x3083F, 0x31A8E, 0x31C34, START 0x8AB2, 0x8BBC | read (s52) | No new screen check of the introduction or end game this session. 0x31C34 re-read: the CHEAD / CBODY path loads block + 0x80 for both names; COMSPR / ICON is block and block + 0x80, with ICON recoloured DS:0CD2 → 0CF2 |

## Start-up configuration, START 0x4354 (read whole)

1. Disk check (START 0x8232, N); overlay init on GAME.OVR.
2. `pool.cfg` opened untyped until IOResult is 0; otherwise an insert-disk line and a key (N).
3. POOL.CFG opened as text; failure gives an error line and Halt.
4. Line 1, video: `E` → DS:4A1E := 1, BIOS mode 0x0D, 0x7101(0), 0x7130(0) (text set-up, lane T's rows). `T` →
   DS:4A1E := 2, mode 9, heap end lowered 0x400 paragraphs. Anything else → DS:4A1E := 0, mode 4 (CGA). The mode on
   entry is kept in DS:4A1C / 4A1D (INT 10h AH 0Fh).
5. Overlay buffer: when MaxAvail covers 0x18800 − the current buffer, the buffer is set to 0x18800 bytes; otherwise
   an out-of-memory line and Halt. A non-zero OvrResult also gives a line and Halt.
6. Line 2, sound: `T` → DS:5372 := 0, `P` → 1, else 2.
7. The heap end is lowered 0x400 paragraphs (again for Tandy). Sound suspend (0x8C5B) silences both devices once.
8. Line 3 → DS:547C (data path); line 4 → DS:537A (save path).
9. Line 5: `F` → DS:49D1 := 1 (the full introduction), else 0.

Client: EGA always, the sound device from its settings (`--sound`), the introduction as with `F`. The paths, memory
and disk checks are N (module format, .NET). CGA and Tandy video are not modelled (noted already for 0x649C).

## The DOS hall-Load slot-11 path (PLAN Next (1)): seen

Saves (scratch copy): save A put on block 26's overland at 10,20 N (`goldbox-tools save-overland`). In B, vector 0 is
moved to $B600: PROGRAM 0, EXIT. In C, vector 0 is an EXIT. Steps in DOSBox-X: Load B at the start-up, one step north
(the party menu opens), Remove × 6, Load C (the camp screen, the step drawn), then four steps north. The marker was
matched to every ICON block at every offset (every icon pixel of the best block equal on screen):

| Step | Counter | Marker |
|---|---|---|
| after Load C | 4 | block 0 |
| 1 | 5 | block 128 |
| 2 | 0 | block 0 |
| 3 | 1 | block 128 |
| 4 | 2 | block 1 |

This is the original behaviour as read (`geo-format.md`), the same as FS-UAE showed for the Amiga in s47. The Client
keeps the s48 fix (d) and reloads slots 9–11. It was not re-run on screen; its test is
`OverlandViewTests.A_hall_load_on_the_overland_loads_the_marker_slots_again`.

Slot 11 is also the head's scratch slot in 0x1E468, which frees it at the end. On the overland (DS:49FA > 1) the
callers are covered as follows:

- The editor's end and Load reload ICON 0 there.
- Add (the party menu) is reached only through PROGRAM 0.
- ADDNPC (0x1F248) exists, but neither PROGRAM nor ADDNPC occurs in the overland scripts 25, 26 or 27 (`ecl.py dis`).

So no play path leaves the marker slot freed.

## DS:49FD / 49FE (PLAN Next (1)): still `[?]`, the reason recorded

- **Writers.** The only writer is the head + body loader 0x79E4 (0x79EF / 0x79F5). The only reader is START 0x3379
  (0x33BA / 0x33BE), the shop and temple portrait.
- **Not in the image.** START's expanded image ends at DS:442F (EXEPACK destination 0x10BF paragraphs; the last
  non-zero data byte is DS:442C). DS:49FD (linear 0x111BD) is Turbo Pascal uninitialised data that nothing clears.
- **The unpacker's stack.** The MZ header's start-up stack is SS:SP 0x111A:0x0080 (linear 0x111A0–0x1121F), which
  the EXEPACK stub uses while it unpacks. Its copy of itself (0x5AF bytes) ends at 0x1119F, just below it. DS:49FD /
  49FE are bytes 0x1D / 0x1E of that stack, 0x63 bytes below its top.
- **Pushes.** The stub's own pushes (4 bytes) do not reach them. An interrupt during the unpack would have to push
  99 bytes through its handlers to write them. The loaded file (0xFCCF bytes) does not cover them.
- **Conclusion.** On a real machine they hold whatever memory held before START loaded, unless deep interrupt-handler
  stack use lands there. Neither can be settled statically. DOSBox-X starts with cleared RAM: 0, 0, which the Client
  keeps.

## Ledger corrections (roles)

- 0x1E468: "builds the current record's combat icon into its slot 0xBF (scratch slot 11)", not "picks a free slot".
- 0x1E315: "merges the head into the body (OR pixels, AND masks)".
- 0x2E8F7: "area map draw", not "wall column draw".
- 0x2EB08: "DrawWallPiece".
- START 0x470F / 0x473F: "video mode set / restore".
- START 0x6ACE: "picture record allocate", not "palette flash".
- START 0x6C72: "buffer rectangle fill", not "recolour".
- START 0x4640: "see-through mask byte".
- START 0x46BB: "CGA map nibble".
- START 0x5AC4 / 0x5B49 / 0x5BD6: "mirror primitives".

## Tests

Client 567 passed (566 + 1), Core 1269 passed + 3 skipped, both with `GOLDBOX_POR_DIR` and `GOLDBOX_POR_AMIGA_DIR`
set.

## Questions for the user

None. The three fixes are differences from the program, not original bugs.

## Open

- The six two-frame sprites at distance 2 were not seen in DOSBox-X (read only).
- The introduction, end game, overland view and a mirrored combat icon were not re-compared this session. They rest on
  the earlier checks: s22, s29, s58 and the s19+ overland checks.
- 49FD / 49FE stay `[?]` on real hardware (reason above).

## Ledger evidence

- O:1E468 → W; read whole s60 and seen (icon editor, DOSBox-X 0 px); head ORed (fixed `CombatArt.Party`). Role: builds the record's icon in slot 0xBF through scratch slot 11.
- O:1E315 → W; read whole s60 (EGA OR / AND) and seen (pixel 46,113 = 4 | 2); `CombatArt.Party` (Or).
- O:1CE5E → W; read whole s60 and seen (editor frames from COMSPR 25 / 153, 0 px); `PartyMenuScreen.DrawIcons`.
- O:1CF3D → W; read whole s60 and seen (editor screen 0 px); copy + recolour as `start-menu.md` §3.
- O:2E0CE → W; read whole s60; flag 1 into the buffer at cell − 1, flag 0 through START 0x5F91.
- O:2E55D → W; read whole s60; frees, zeroes the table, resets DS:6B0A / 6B1C.
- O:2E7D6 → W; read whole s60; a nil entry draws nothing (fixed `ViewPictures.DrawSprite` for DOS two-frame sprites); out of range halts (not modelled).
- O:2E11E, O:2E13E → W; read s60; head at (x, y), body at (x, y + 5), opaque.
- START 0x4640, 0x46BB, 0x4751, 0x48E5, 0x4F14, 0x5AC4, 0x5B49, 0x5BD6, 0x5F91, 0x6ACE, 0x6BD5, 0x6C72 → W; read branch by branch s60 (the roles above); the visible effect matches the importer and renderer (pixel checks in `view3d.md` and `por-art-formats.md`, s60 screens).
- START 0x470F, 0x473F → W; read s60 (video mode set / restore; start-up and fatal exits).
- START 0x53B1, 0x5C07, 0x6192, 0x649C → W; seen again s60 (editor, 3D view, area map 0 px).
- O:2E8F7 → W; read s60 (area map draw) and seen (save A 15,4 W, 0 px).
- O:2EA6B → W; read s60 (backdrop bands as `view3d.md`).
- O:2EB08 → W; read s60 (DrawWallPiece as `view3d.md`).
- O:2EEEA → W; read s60 (flush).
- O:32392 → W; read s60 (symbol sets, error halt, buffer / direct modes).
- START 0x8AE0, 0x8B4C, 0x8C5B, 0x8C85, 0x9E91, 0xA2FA → W; read s60 = `sound.md` §2.1 CS:0000 / 006C / 017B / 01A5 / 1391 / 181A; Core brackets every auto-stop caller.
- START 0x4354 → W; read whole s60 (five POOL.CFG lines; Client: EGA, `--sound`, full introduction; the rest N).
- O:79E4 (DS:49FD / 49FE) → no change; the only writer, start values indeterminate on real hardware (`[?]` with reason).
- O:1FB7F path (hall Load on the overland, slot 11) → seen s60 in DOSBox-X: blocks 0, 128, 0, 128, 1 (the original; Client fixed by s48 decision).
