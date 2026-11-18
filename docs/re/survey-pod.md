# Survey: Pools of Darkness (DOS, 1991): formats and ECL

Status: **survey** (2026-11-17). It covers formats and the ECL VM only, with no rules work. Its job is to show what the
module format and the per-game ECL opcode tables must handle beyond Pool of Radiance (PoR). Facts are read from the
files and the program. Each one names its offset or tool. Anything not checked is marked `[?]`.

Main target: **v1.10** (WizardWorks AD&D Collectors Edition vol. 3, three 720K disks). **v1.00** (1991-08, 720K) was
compared against it ("Versions"). Sources: `reverse-engineering/dos/original-images/SOURCES.txt` (game `pod`).
Unpacked copies only; the masters were not touched.

Offsets: "OVR 0xNNNN" is a file offset in `GAME.OVR`. "EXE image 0xNNNN" is an offset in the EXEPACK-unpacked
`GAME.EXE` load image. "DS:NNNN" is the program's data segment (paragraph 0x9E7 of that image).

## Sources

- The v1.10 and v1.00 files. Read with `tools/scripts/dax.py` (unchanged), `ecl.py` (unchanged; then imported
  read-only by a scratch script that swaps in the PoD tables found below), the EXEPACK routine in `gbitem.py`, and
  a plain 8086 disassembler (capstone) on `GAME.OVR` and the unpacked `GAME.EXE`.
- Our PoR specs: `dax-format.md`, `ecl-format.md`, `geo-format.md`, `save-format.md`, `item-format.md`,
  `character-format.md`, `dos-graphics-survey.md` (PoD art depth and the VGA block headers are already covered there).
- Simeon Pilgrim, coab (Curse of the Azure Bonds C# port), `engine/ovr003.cs` (Curse opcode table and per-handler
  operand counts) and `engine/ovr008.cs` (Curse VM memory regions). https://github.com/simeonpilgrim/coab. Read
  only; this is the PoR → Curse bridge in the opcode table.
- Simeon Pilgrim's blog posts on PoD: unpacking the program and its debug command-line switches.
  https://simeonpilgrim.com/blog/2009/10/04/reverse-engineering-pools-of-darkness-part-1/ ,
  https://simeonpilgrim.com/blog/2009/10/06/reverse-engineering-pools-of-darkness-part-2/ ,
  https://simeonpilgrim.com/blog/2010/07/20/pool-of-darkness-cheat-codes/
- Gold Box Companion `formats.zip`: the character layouts for all games ("Character file formats/04. Pools of
  Darkness.txt", "Offsets compared.txt").

## 1. File inventory

**Installer.** Disk 1 holds `INSTALL.EXE` (Turbo Pascal; it prompts for disks and unpacks). The data comes in
self-extracting **PKZIP 1.1** archives that use the *imploding* method (shrinking for the save set): `POD1.EXE`
(disk 1), `POD2.EXE` and `POD3.EXE` (disk 2), and `POD4.EXE` and `PODSAVE.EXE` (disk 3). 7-Zip extracts them. It warns
about the SFX stub, but every extracted size matches the archive listing.
An importer needs the installed directory or an implode decoder. The installed game is 64 files, ~3.5 MB.

| File(s) | Size | Holds | Container |
|---|---|---|---|
| `GAME.EXE` | 64,203 | Program root: run-time, overlay manager, fixed units | MZ, **EXEPACK**-packed, Turbo Pascal 6 |
| `GAME.OVR` | 264,086 | Turbo Pascal overlay file (`FBOV` header), 35 overlay units, incl. the ECL VM | TP overlay |
| `STARTUP.EXE` | 11,488 | Setup: sound card, graphics adapter (EGA / Tandy / VGA), joystick or mouse, save path, **Silver Blades save path**; writes `POOL4.CFG` | TP |
| `CONTROL.EXE` | 4,000 | Launcher: runs the intro animation, `GAME.EXE`, then the end animation | TP |
| `START.BAT` | 76 | Runs `STARTUP`, the optional `SND.COM`, then `CONTROL` | text |
| `VACE.EXE` | 17,664 | Animation player (Turbo C++) for `.BMA` + `.ACF` | — |
| `INTRO.BMA`/`.ACF`, `FINAL.BMA`/`.ACF` | 648,158 / 10,844; 253,780 / 4,362 | Intro and ending animations: frame data plus a command script | VACE formats `[?]` |
| `EGA.DRV`, `TGA.DRV`, `VGA.DRV` | ~30–33 K | Graphics drivers (start with a jump table) | raw code |
| `SOUND.COM`, `ADLIB.COM`, `SBLASTER.COM`, `IBMSND.COM`, `TANEXTX.COM`, `TANSLTL.COM` | 1–12 K | Sound drivers (one is copied to `SND.COM` by setup) | DOS .COM |
| `ECL1.DAX` | 329,456 | **All 56 ECL scripts** | DAX |
| `GEO1.DAX` | 28,349 | All 32 3D maps | DAX |
| `WALLDEF1.DAX` | 11,368 | 6 wall-set blocks of 2,340 bytes (3 × 780) | DAX |
| `8X8D0.DAX`, `8X8D1.DAX` | 1,857 / 91,734 | 8×8 tiles: font/UI (blocks 201–202), wall tiles (6 × 256 tiles) | DAX, VGA |
| `PIC1.DAX`, `BIGPIC1.DAX`, `CPIC1.DAX`, `SPRIT1.DAX` | | Encounter pictures (49), big pictures (7), combat icons (244), approach sprites (29) | DAX, VGA |
| `CHEAD.DAX`, `CBODY.DAX`, `COMSPR.DAX`, `DUNGCOM.DAX`, `WILDCOM.DAX`, `BACK1.DAX`, `SKYGRND.DAX`, `BORDERS.DAX`, `CURSOR.DAX`, `TITLE.DAX` | | Portrait heads/bodies, combat sprites, combat ground sets, backdrops, sky, frame pieces, mouse cursor, title | DAX, VGA |
| `MON1CHA.DAX` | 13,428 | 122 monster records, **510 bytes** each | DAX |
| `MON1ITM.DAX`, `MON1SPC.DAX` | 3,244 / 3,490 | Monster item lists (**17-byte** records) and effect lists (9-byte records) | DAX |
| `ITEM0.DAX` | 4,578 | One block: 254 × 17-byte item records | DAX |
| `ITEMS` | 2,048 | Base item table (same size as PoR's: 2-byte header + 128 × 16 `[?]` layout not re-checked) | raw |
| `MUSIC.DAX`, `P1.SMS`, `P3.SMS` | 26,416 / 5,562 / 1,794 | Music. `P1.SMS` and `P3.SMS` are byte-identical to `MUSIC.DAX` blocks 1 and 3. `.SMS` files start with a 4-char tag | DAX + loose |
| `ZOUNDS.DAX` | 22,636 | 11 sound-effect blocks | DAX |
| `SAVGAMA.PTY`, `VAULTA.DAT`, `CHRDATA1–6.SAV/.THG`, `CHRDATA2/3/6.EFX` | | Shipped ready-made party, save slot A (from `PODSAVE.EXE`) | raw, below |

**DAX container: unchanged from PoR.** A u16 index length, 9-byte entries (`id`, `offset`, `raw`, `comp`) and the same
signed run-length packing. Our check (scratch script, the same as `dax-format.md` "Verification") decoded all **927
blocks in the 25 DAX files**: 0 failures, blocks stored back to back, each file ends at its last block, and no 0x7F/0x80
control bytes. Differences that matter:
- **One file per kind, digit fixed.** There are no `ECL1..ECL8`. Every script is in `ECL1.DAX`, every map in
  `GEO1.DAX`, and so on. The program builds the names with a fixed `1` (`ECL1` at OVR 0x68A0, `GEO1` at OVR 0x3D797,
  `WALLDEF1` at OVR 0x3D620). PoR's "virtual disk" digit (VM $6E12 / DS:5376) is gone. Block ids are global.
- Script and map ids run up to 86, in groups of 16 (1–4, 16–27, 32–42, 48–55, 64–77, 80–86): one group per region.
- The picture families moved to VGA 8-bit (`dos-graphics-survey.md`). The container did not change.

## 2. Maps (GEO)

- **Same layout as PoR** (`geo-format.md`). All 32 blocks are 1026 bytes: a 2-byte prefix, then four 256-byte planes
  (walls N/E, walls S/W, event byte, barrier bits). The loader (OVR 0x3D7C1) accepts only size 0x402 and copies four
  0x100 planes from offset 2.
- **The prefix is no longer 0x0400.** It is 0xDDCC in most blocks, 0x0111 in five and 0x0400 in block 1. The loader
  ignores it, so the importer must too.
- Plane value ranges fit PoR's meaning (wall nibbles, barrier bits ≤ 0x55). The event-byte meaning was not re-checked
  `[?]`.
- **Wall sets.** A `WALLDEF1` block holds up to three 780-byte sets. The loader (OVR 0x3D64D) divides the block size
  by 780 and refuses more than three. One ECL operand loads a whole block (LOADFILES, below). PoR loaded three slots
  separately with LOADPIECES.
- Map ↔ script: 35 of the 56 scripts load a map (36 have LOADFILES; block 2's only map operand is 127, which the
  handler skips). 21 are map-less (overland, menus, cut scenes `[?]`). Which script
  loads which map and wall set is listed by the scratch decoder (LOADFILES operands). Most scripts load the map with
  their own id.

## 3. ECL

### 3.1 Where the scripts live, block layout, vectors

- All 56 scripts are blocks of `ECL1.DAX` (ids 1–86). Raw sizes are 596–7,680 bytes.
- **Block layout as in PoR, base moved:** a u16 prefix `0x1388` (all 56 blocks), then the script, **loaded at VM
  $8000** (Curse uses the same base, per coab). The maximum is 7,680 bytes ($8000–$9DFF), the same window size as PoR.
  The script reader indexes the buffer at DS:87FC with `address − 0x8000` (loader OVR 0x65EB).
- **Five entry vectors** at $8000–$8013, the same shape as PoR (`01 01 lo hi`). The script init (OVR 0x67AC) sets
  PC = $8000 and reads five operands into DS:8800–8808. In every block vector 4 (init) is $8014, the first byte after
  the table. The roles (move, search, pre-camp, camp interrupted, init) are assumed from PoR and Curse `[?]` (not
  traced in PoD).
- The init also clears global VM bytes $002B and $002C. They play the role of PoR's rest-interrupt pair $6DD2/$6DD3
  `[?]`.

### 3.2 Operand encoding and strings: unchanged

The operand loader (OVR 0x65EB) is PoR's, byte for byte in behaviour: type 0 = 1 byte; types 1, 2, 3 and 0x81 = 2 bytes;
0x80 = a length byte plus 6-bit packed text; any other type = 1 byte. The text unpacker (OVR 0x7112) and character map
(OVR 0x6B76) are PoR's too: codes 1–31 → `A`..`_`, 32–63 as is, so scripts carry only upper case. The decoder found
5,510 inline texts (224,067 characters) and none outside $20–$5F. How the display treats case was not traced `[?]`.
The operand arrays (type DS:B0AB+k, low byte DS:B12B+k, high byte DS:B0EB+k) and the 256-byte string slots
(DS:B06E + 256·k) keep PoR's structure at new addresses.

### 3.3 VM memory: **redesigned** (the biggest change)

Region classifier OVR 0x6B9E, reader OVR 0x6F14, writer OVR 0x6D51:

| Range | Storage | Notes |
|---|---|---|
| $0001–$0400 | **1,024 bytes** at far pointer DS:87F8, cell = byte `addr − 1` | Global game state (flags, location, area mode). This is the only VM area the save file keeps |
| $0401–$0800 | **The current character's record** (far pointer DS:9CEE), byte `addr − $401` | Reads copy straight from the 510-byte record (OVR 0x6BEA). Writes go through a whitelist (OVR 0x6C3F): bytes 0x1E–0xAA, words 0xB0 and 0x14B–0x150 (money), bytes 0x137, 0x167, 0x170, 0x1EB, 0x1ED … `[?]` full list |
| $8000–$9DFF | The script, byte per address | Scripts almost never write here (1 SAVETABLE in 56 blocks) |
| anything else | — | Read: **0** (the reader zeroes its result first, OVR 0x6F38; PoR returned stack residue). Write: ignored. No other addresses appear in the scripts |

- **The access width comes from a type byte that each handler passes, not from the region.** Reader and writer take
  it as a parameter: type ≤ 1 → 1 byte, otherwise 2 bytes (little-endian, overlapping: a word write to $0010 also
  changes $0011). Byte reads are zero-extended, except x and y, which are sign-extended. Which type each handler
  passes:
  - The destination operand's type: 04–07, 08, 09 (OVR 0x44D), 0F, 2A (its table read too), 2F/30, 3B.
  - Operand 1's type: SAVETABLE 0x35 (the source value) and FINDCHAR 0x37 (a text, so always a word).
  - VMENU/HMENU: a type byte saved before the tail load, presumably the destination's `[?]`.
  - The character-window writer (OVR 0x6C3F) ignores the width and uses a fixed width per field.
  - Value fetches use the operand's own type.

  So PoR's rules "types 1 and 3 fetch the same" and "destinations ignore the type" do **not** hold in PoD. Type 3
  appears on 49 operands, type 1 on 21,290.
- PoR's four word areas ($4900, $6B00, $9700, $9900) and Curse's three ($4B00, $7A00, $7C00 + script, coab
  `ovr008.cs`) are all replaced. PoR's redirection table into the character record ($6B18 … $6C1B) is gone. The
  record is mapped whole instead, so **every character field has a VM address** ($401 + offset).
- **Special cells moved into the global area.** The read/write switches compare `addr − 1`, so the address is the
  case value + 1:

| VM | Read | Write side effect |
|---|---|---|
| $0010 | 1 if any party member has effect 5 (list walk with the effect test, OVR 0x6F47) `[?]` meaning | — |
| $0011 | Facing: raw outdoors, halved in 3D (DS:A9F5) | Value mod 4 → facing 0/2/4/6, view redraw flag |
| $001A | cell | Non-zero clears DS:A9F2 and calls a redraw `[?]` |
| $0022 | cell (area byte 0x21: 3D/outdoor mode) | On change: switches the game mode (DS:880C 3 or 4); 0 also forgets the loaded wall set |
| $0023 | Facing byte DS:A9F5 | — |
| $002D, $0032 | DS:A9F7, DS:A9F6 (view bytes, as PoR $C04E/$C04F) `[?]` | — |
| $0034, $0035 | Party x, y (DS:A9F3/A9F4, signed) | Move the party, redraw flag |
| $003A | cell (area byte 0x39) | Value < 5: reloads a picture/palette set `[?]` |

  Other script-visible fields found in handlers: area byte 0x12 ($0013) = current map id (LOADFILES), 0x15 ($0016) =
  previous script id (NEWECL), 0x36/0x37 ($0037/$0038) = encounter distance (SETUPMON).
- 381 distinct global bytes are written by the scripts, between $0001 and $02EE.

### 3.4 Control flow

- **Dispatcher:** OVR 0x28CB, a compare chain on DS:B025 (as in PoR). It covers **0x00–0x41: 66 opcodes**, and every
  one has a handler, including 0x1F. Opcodes ≥ 0x42 fall out of the chain without moving the PC. The behaviour is
  presumably PoR's hang `[?]`.
- **IF-skip** (OVR 0x7A4F) keeps its own operand-count table, as in PoR. In PoD it **mis-sizes** 0x15, 0x25, 0x26,
  0x27, 0x2B and 0x31 (opcode byte only), and 0x34 and 0x36 (1 operand of 2). PoR's 0x0C mis-skip is fixed (since
  Curse; `survey-sotsb.md` §3.4: 0x27 and 0x31 are PoD's new mis-skips, 0x34 dates from Curse, the rest from PoR). No IF in
  the scripts is followed by one of these (decoder check), but a VM must still reproduce them.
- **RETURN with an empty GOSUB stack now ends the run** (the handler, OVR 0xCE8, calls the EXIT handler). PoR falls
  through; Curse and SotSB already exit (`survey-sotsb.md` §3.4).
- Variadic opcodes: VMENU (0x15, count = operand 3), ON GOTO/GOSUB (0x25/0x26, count = operand 2), HMENU (0x2B and
  0x31, count = operand 2) and **TREASURE (0x27, count = operand 4, new)**. In each the count is read through the
  value fetch, the PC is stepped back one byte, and the loader is called again (PoR's scheme).

**Decoder check** (scratch script: `ecl.py`'s flow analysis with BASE $8000, the PoD operand counts, the variadic
rules and the IF-skip table). All 56 blocks decode with **0 errors, 0 overlaps and 0 unhandled opcodes**. 98.0 % of
the bytes are reached on hard paths (33,721 instructions). Of the unreached bytes, 4,767 are tables and 615 are dead
code. Unmodified PoR `ecl.py` fails on every block: all vectors point at $80xx, outside its $9900 window. With the
base fixed but PoR's table (worked out by hand, not run), 0x0C, 0x1D, 0x21, 0x22, 0x23, 0x27, 0x29, 0x2C, 0x31, 0x34
and 0x37 would be mis-sized, and 0x1F and 0x3E–0x41 would stop the decoder (no entry).

### 3.5 Opcode table: PoR → Curse → PoD

"Ops" = operands loaded by the handler (PoR from `ecl-format.md`; Curse from coab's handlers; PoD read from each
handler in `GAME.OVR`). "+n" = variadic tail. "Skip" = PoD IF-skip count. "Uses" = decoded instructions in v1.10.
Status: **same** = same operand count and the handler does the PoR/Curse job (checked by structure: helpers called,
fields touched); **checked** = the PoD handler was read for the stated effect; **changed** = different operands or
meaning from PoR. A matching count alone does not prove the same meaning; those rows say so.

| Op | PoR name / ops | Curse ops | PoD ops | Skip | Handler | PoD meaning | Status | Uses |
|---|---|---|---|---|---|---|---|---|
| 00 | EXIT 0 | 0 | 0 | 0 | 0x246 | End run | same | 2006 |
| 01 | GOTO 1 | 1 | 1 | 1 | 0x2D0 | Jump | same | 3268 |
| 02 | GOSUB 1 | 1 | 1 | 1 | 0x2ED | Call | same | 754 |
| 03 | COMPARE 2 | 2 | 2 | 2 | 0x302 | Flags; text compare when either is text | same | 2122 |
| 04–07 | ADD/SUB/DIV/MUL 3 | 3 | 3 | 3 | 0x354 (shared) | Arithmetic; result width from the destination operand's type | same op; width changed | 486 |
| 08 | RANDOM 2 | 2 | 2 | 2 | 0x3EB | Random | same | 243 |
| 09 | SAVE 2 | 2 | 2 | 2 | 0x44D | Store value or text; width = destination type | checked | 4389 |
| 0A | LOADCHAR 1 | 1 | 1 | 1 | 0x49F | Select party member | same `[?]` details | 57 |
| 0B | LOADMON 3 | 3 | 3 | 3 | 0x5C2 | Load monster + combat icon (`CPIC`); list cap 48 (DS:C595), PoR 63 | same, cap changed | 979 |
| 0C | SETUPMON 3 | 3 | **4** | 4 | 0x529 | Sprite, distance (area 0x37), close-up picture, **4th → DS:AEE8** `[?]` | changed | 486 |
| 0D | APPROACH 0 | 0 | 0 | 0 | 0x9E6 | Close in | same | 92 |
| 0E | PICTURE 1 | 1 | 1 | 1 | 0xB2E | Show picture | same | 272 |
| 0F | INPUTNUM 2 | 2 | 2 | 2 | 0xB49 | Number input | same | 43 |
| 10 | INPUTSTR 2 | 2 | 2 | 2 | 0xBA9 | Text input | same | 8 |
| 11, 12 | PRINT, PRINTCLR 1 | 1 | 1 | 1 | 0xC2F (shared) | Text box | same | 4883 |
| 13 | RETURN 0 | 0 | 0 | 0 | 0xCE8 | Pop; **empty stack = EXIT** | checked, changed | 509 |
| 14 | CMPAND 4 | 4 | 4 | 4 | 0xD3F | Two-pair equality | same | 128 |
| 15 | VMENU 3+n | 3+n | 3+n | **0** | 0x107B | Vertical menu | same | 3 |
| 16–1B | IF = ≠ < > ≤ ≥ | 0 | 0 | 0 | 0xE2B (shared) | Conditional skip | same | 4580 |
| 1C | CLRMON 0 | 0 | 0 | 0 | 0x1392 | One routine call (OVR far 00EC:011A) `[?]` same effect | same `[?]` | 3 |
| 1D | PARTYSTR 1 | 1 | **0** | 0 | 0x1887 | **Wait for key**: a one-item "continue" menu | checked, new meaning | 1791 |
| 1E | CHKPARTY 6 | 6 | 6 | 6 | 0x13A0 | Party min/max/avg | same `[?]` selectors | 4 |
| 1F | — (hang) | — | **1** | 1 | 0x2643 | **Journal entry n**: prints a fixed program line that names journal entry n | checked, new | 93 |
| 20 | NEWECL 1 | 1 | 1 | 1 | 0xE4A | Load script; area 0x15 = old id, DS:C580 = new id | checked | 157 |
| 21 | LOADFILES 3 | 3 | **2** | 2 | 0xE9E | o1 < 127 and 3D mode: load GEO map o1 (area 0x12); o2 ≠ 255 and ≠ loaded (DS:A9F8): load **WALLDEF block o2** | checked, changed | 51 |
| 22 | PSURPRISE 2 | 2 | **0** | 0 | 0x18BD | Two-choice menu (list DS:278C, filled at run time); sets "=" or "≠" | checked shape; presumably Yes/No `[?]` | 375 |
| 23 | SURPRISE 4 | 4 | **0** | 0 | 0x26F4 | Clear the text window, refresh the view if position/flags changed, then **EXIT** | checked, new | 751 |
| 24 | COMBAT 0 | 0 | 0 | 0 | 0x1443 | Fight / shop / temple hand-off | same `[?]` | 463 |
| 25, 26 | ONGOTO/ONGOSUB 2+n | 2+n | 2+n | **0** | 0x1591 | Computed jump/call | same | 267 |
| 27 | TREASURE 8 | 8 | **4+n** | **0** | 0x1626 | **Adds** 3 money amounts (dwords DS:A7BE/A7C2/A7C6: platinum, gems, jewelry, matching record 0x14B–0x150), then n item ids (indices into 17-byte records `[?]`; 255 special) | checked, changed | 145 |
| 28 | ROB 3 | 3 | 3 | 3 | 0x179C | Rob | same `[?]` | 1 |
| 29 | ENCMENU 14 | 14 | **0** | 0 | 0x275C | **No-op** (PC + 1). The encounter menu is gone from the VM | checked | 1 |
| 2A | GETTABLE 3 | 3 | 3 | 3 | 0xFC4 | Table read | same | 382 |
| 2B | HMENU 2+n | 2+n | 2+n | **0** | 0x1227 (flag 0) | Horizontal menu | same | 40 |
| 2C | PARLEY 6 | 6 | **1** | 1 | 0x2713 | v ≥ 0x80: sound-unit routine 1 with v & 0x7F; 2 ≤ v ≤ 14: routine 0 with v. **Sound / music** `[?]` which is which | checked shape, new | 137 |
| 2D | CALL 1 | 1 | 1 | 1 | 0x2090 | Built-in routine by raw field: **ids 1, 2, 3, …** (1 = view refresh, 2 = one square forward with edge wrap, 3 = `[?]`). PoR used $2C90/$8000/$C01E … | checked, ids changed | 315 |
| 2E | DAMAGE 5 | 5 | 5 | 5 | 0x1A01 | Damage, party-wiped message | same `[?]` | 48 |
| 2F, 30 | AND, OR 3 | 3 | 3 | 3 | 0xF51 | Bit ops | same | 1871 |
| 31 | SPRITEOFF 0 | 0 | **2+n** | **0** | 0x1227 (flag 1) | **HMENU variant**: draws/clears the text window first, then the same menu | checked, new meaning | 143 |
| 32 | FINDITEM 1 | 1 | 1 | 1 | 0x18FE | "=" if a member carries item type v (item byte 0x2E) | checked | 7 |
| 33 | PRINTRET 0 | 0 | 0 | 0 | 0x1D9E | New line | same | 402 |
| 34 | CLOCK 1 | **2** | 2 | **1** | 0x1D69 | Advance clock (amount, unit), as Curse | same as Curse | 0 |
| 35 | SAVETABLE 3 | 3 | 3 | 3 | 0x102A | Table write | same | 62 |
| 36 | ADDNPC 2 | 2 | 2 | **1** | 0x1E61 | Add NPC | same `[?]` | 19 |
| 37 | LOADPIECES 3 | 3 | **2** | 2 | 0x1ECA | **Find party member by name**: text vs each record's name (only NPC-flagged records, byte 0x147 > 0x7F); index → a (bit 0x80 if record 0x1EE = 0), 255 if none | checked, new meaning | 48 |
| 38 | PROGRAM 1 | 1 | 1 | 1 | 0x2165 | Built-in program: ids 0, 1, 2, 9 (9 = camp, then EXIT) `[?]` others | checked ids | 26 |
| 39 | WHO 1 | 1 | 1 | 1 | 0x1DFB | Pick a character | same | 55 |
| 3A | DELAY 0 | 0 | 0 | 0 | 0x19B0 | Pause | same | 549 |
| 3B | SPELL 3 | 3 | 3 | 3 | 0x1F8D | Spell search | same `[?]` | 9 |
| 3C | PROTECT 1 | 1 | 1 | 1 | 0x2765 | `[?]` Not PoR's glyph line by its calls. It allocates a record, links it through +0x2A/+0x2C and uses TREASURE's helper (stub 55:98), so it is probably item-related (give or create an item) | unknown | 15 |
| 3D | CLEARBOX 0 | 0 | 0 | 0 | 0x1DC7 | Clear picture window | same | 41 |
| 3E | — | 0 (DUMP) | 0 | 0 | 0x222C | Remove the current character (Curse DUMP) | same as Curse | 22 |
| 3F | — | 1 (FIND SPECIAL) | 1 | 1 | 0x225B | "=" if the current character has effect v, else "≠" | checked | 4 |
| 40 | — | 1 (DESTROY ITEMS) | 1 | 1 | 0x22AC | Remove items of type v from the party (bit 0x80 variant `[?]`) | checked | 5 |
| 41 | — | — | **2** | 2 | 0x2405 | **Add experience**: 16-bit amount, shared out among enabled members by class count; sets "can train" (record 0x1EC). Operand 2 = whole party or current `[?]` | checked, new in SotSB (ADD EP) | 111 |

Summary: PoR has 61 opcodes (0x00–0x3D without 0x1F). **49 keep PoR's operand count and job.** The VM core (control
flow, arithmetic, compare, tables, text) is all among them, though the memory model under it changed, and RETURN,
CALL ids and the LOADMON cap differ in detail. **11 change** count or meaning: 0C, 1D, 21, 22, 23, 27, 29, 2C, 31 and
37, plus CLOCK 34 (changed already in Curse). 0x3C is unknown. **5 are added**: 1F (journal, now handled), 3E/3F/40
from Curse, and 41, new in Silver Blades. The Silver Blades survey (`survey-sotsb.md` §8) dates the rest: SotSB still
has PoR's count and job for 0C, 1D, 21, 22, 23, 27, 29, 2C, 31 and 37, so those changes are PoD's own; 0x3C is already
a stub in SotSB, and CLOCK's second operand is Curse's. The encounter menu (29), parley (2C) and surprise (22/23) left the
VM in favour of UI opcodes (wait-for-key, yes/no, journal, menu-in-window). Where the encounter logic lives now is
`[?]`; presumably it is composed from HMENU + COMPARE in the scripts.

## 4. Art

Covered by `dos-graphics-survey.md` (PoD: VGA 256 only, one art set; EGA/Tandy modes reduce it at run time). Added
here:
- Still blocks share one header: `u8 height, u8 width/8, 4 bytes, u16 count, u8 first colour, u8 colours − 1`, then
  6-bit RGB triples, then n/2 + 4 bytes (the EGA remap table `[?]`), then `count` chunky images. The sizes match
  exactly for `CURSOR` (1×8×8, 16 colours), `BORDERS` (11 tiles), `DUNGCOM` (26 × 24×24, 32 colours), `TITLE`
  (320×200, 256 colours) and `8X8D0` block 202.
- `8X8D1` wall-tile blocks: 256 tiles of 8×8, colours 32–143. Each block is 28 bytes longer than that header
  predicts `[?]`. `8X8D0` block 201 has a different header (a font `[?]`).
- `WALLDEF1`: 780-byte sets of tile indices, as in PoR (5 walls × 156 `[?]` not re-checked).
- Palette ranges per family (survey): pictures 32–255, icons 144–255, combat bodies 96–143, sprites 64–95,
  backdrops 144–175, colours 0–15 = EGA set. The module format must keep **per-asset palette ranges** and compose
  them on one 256-colour screen.

## 5. Records

| Record | PoR | Curse | Silver Blades | PoD | Where checked |
|---|---|---|---|---|---|
| Character / monster | 285 | 422 | 439 | **510** (0x1FE) | `CHRDATAn.SAV` and every `MON1CHA` block = 510; VM reader copies 0x1FE (OVR 0x6BEA); GBC `formats.zip` |
| Item, in memory and in `.THG` | 63 | `[?]` | `[?]` | 63 (name 0x00–0x29, next 0x2A, type 0x2E …) | `.THG` sizes 315/378/441 = 5–7 × 63 |
| Item, in DAX (`MON1ITM`, `ITEM0`) | 63 | | | **17** = record bytes 0x2E–0x3E, no name, no pointer | block sizes all multiples of 17; `Move` of 0x11 bytes in the monster loader (OVR 0x12575) and the item loader (OVR 0x19FE1) |
| Effect | 9 | | | 9 (`.EFX`, `MON1SPC`) | sizes |

- The PoD record layout (GBC) moves every field: next pointer 0x1E3 (PoR 0x104), effects 0x143, items 0x1A7, money
  0x14B–0x150 (**three** words: platinum, gems, jewelry; PoR had seven coin kinds), levels 0x151–0x157 (7 classes incl.
  druid), memorised spells as one byte per spell 0x1E–0xAA … Handlers confirm 0x1E3, 0x1A7, 0x147, 0x14B–0x150,
  0x151–0x157, 0x172 (experience) and 0x1EC/0x1EE.
- GBC "Offsets compared" shows **Treasures of the Savage Frontier uses PoD's layout offset for offset** (and Gateway uses
  Curse's). So PoD and TotSF share one engine generation.
- **Character transfer.** `STARTUP.EXE` asks for a *Silver Blades* save path (stored in `POOL4.CFG`). The character
  loader (OVR ≈0x11A40) reads a **439-byte** (0x1B7, Silver Blades size) record into a buffer and builds a 510-byte one
  from it. The save loader has a second branch that opens a `.dat` save and seeks to offset 5,140 (0x1414) for the
  party name list (OVR 0x12C6A): the Silver Blades `SAVGAMx.DAT` (confirmed: its party count sits at 5,140, `survey-sotsb.md` §6). So the chain is PoR → Curse → SSB → PoD, and PoD
  converts only the immediately previous game's record. The field mapping was not read `[?]`. Roster files use a
  `.PC` extension plus `.THG`/`.EFX` (strings at OVR 0x1122E) `[?]`.

**Save game** (writer ≈OVR 0x13460, loader ≈OVR 0x12866):

| Offset | Size | Content |
|---|---|---|
| 0 | 1024 | Global VM area $0001–$0400 |
| 1024 | 5 | x, y, facing, two view bytes (DS:A9F3–A9F7) |
| 1029 | 1 + 1 | Game mode bytes DS:880D, DS:880C |
| 1031 | 2 | Loaded wall-set block (DS:A9F8) |
| 1033 | 2 | DS:A9FA `[?]` |
| 1035 | 1 | Party count `[?]` |
| 1036 | 328 | 8 × 41-byte names of the party files |

The total is **1,364 bytes = `SAVGAMA.PTY`** (the shipped slot A). PoR's `SAVGAMx.DAT` is 13,137 bytes. **PoD saves no
script area and no extra VM areas.** On load the current script must be re-read from `ECL1.DAX`. How the script id
comes back is `[?]`: NEWECL keeps it in DS:C580, outside the saved area; the previous id is at global byte 0x15.
Per member: `CHRDATxn.SAV` (510), `.THG` (n × 63 items), `.EFX` (n × 9 effects; absent when empty). New:
`VAULTx.DAT` (12 bytes in slot A, all zero: presumably the three vault money dwords) and `tmpvault.dat`. The vault is
a party store kept between sessions `[?]`.

## 6. Text

- **Script text** is inline 6-bit packed (5,510 strings in `ECL1.DAX`, upper case only), as in PoR. A few texts use
  type 0x81 (text from VM memory, 280 operands).
- **Program text** (menus, messages, item and spell names, UI) is Pascal strings in `GAME.OVR` code
  segments and in the `GAME.EXE` data segment (e.g. the "continue" menu at DS:0E34). Item names are composed from
  word ids as in PoR (`item-format.md`) `[?]` word table location not looked up.
- The game's journal is printed in the manual. ECL 0x1F only prints its entry number. The module format needs a
  journal-text slot the importer cannot fill from the files.

## 7. The program

- `GAME.EXE`: Turbo Pascal 6 (Borland 1983,90 run-time), **EXEPACK**-packed. The repo's `gbitem.py` routine unpacks it
  to a 72,784-byte image. Simeon's "scrambler" is this EXEPACK stub. DS = image paragraph 0x9E7.
- `GAME.OVR`: TP overlay file with **35 units**. Stub segments sit in the root (header +4 = file offset, +0xC = entry
  count, entries 5 bytes from +0x20). The ECL VM helpers are unit stub 0x55 (OVR 0x65EB, 25 entries). The dispatcher
  and all opcode handlers are unit stub 0x40 (OVR 0x246–0x2F44).
- Helpers: `STARTUP`/`CONTROL` are TP; `VACE` is Turbo C++; drivers are raw .COM/.DRV.
- **Copy protection**: a manual lookup. The prompt asks for a word by journal page and word number (strings at
  OVR ≈0x35B4). Command-line switches skip it and enable debug cheats (Simeon's 2010 post; the cheat words are in
  `GAME.OVR`, e.g. OVR 0x16721).
- Sound: a root sound unit (EXE image 0x5A00) drives the `SND.COM` driver. `MUSIC.DAX` and `ZOUNDS.DAX` hold the data.

## Versions

v1.00 and v1.10 share the DAX files, art and records. Differences: `GAME.EXE`, `GAME.OVR` (263,841 vs 264,086 bytes),
`STARTUP.EXE`, `START.BAT`, `MUSIC.DAX`/`P1.SMS`, the sound drivers (`.EXE` in v1.00, `.COM` in v1.10), and `ECL1.DAX`
**blocks 33, 52 and 66** (content not compared `[?]`). The v1.00 dispatcher sits at the same OVR offset with the same
range 0x00–0x41, and all 56 v1.00 scripts decode cleanly with the same tables. So **one opcode table serves both**.

## 8. Module-format and VM implications (beyond PoR)

1. **Per-game VM profile, not just an opcode table.** It needs the script base address ($8000 vs PoR $9900), the
   memory map (regions, cell width, address → state redirections), the operand-count table, a **separate IF-skip
   table**, the variadic rules (now including TREASURE's count at operand 4), RETURN-on-empty behaviour, the
   CALL/PROGRAM id maps, and what an unmapped read returns (PoD 0, PoR stack residue).
2. **Byte-addressed memory with width from the operand type.** The VM's memory abstraction must allow a
   byte-granular area where reads/writes are 1 or 2 bytes, with overlapping words. The width source is set per
   opcode (usually the destination's type, but SAVETABLE and FINDCHAR use operand 1's). It must not assume PoR's word
   cells.
3. **The character record as a VM window.** $0401–$0800 maps raw record offsets, with a per-game write whitelist.
   So the module's character model must keep **original byte offsets per rules profile**, or the VM needs a per-game
   offset → field map for every field a script may touch.
4. **Single-file ids.** Scripts, maps and wall sets are addressed by global block id. There is no disk digit, and
   wall sets come as one block of up to 3 sets. The module format should key everything by original id per kind and
   drop "disk" (keep it only as a PoR compatibility field).
5. **Item records in two shapes** (17-byte DAX template, 63-byte instance with name and links). Monster records are
   the 510-byte character record. The importer needs per-game record codecs. `monsters.json`/`items.json` should hold
   decoded fields plus the raw bytes for round-trip.
6. **Saves are tiny and script-less.** A PoD module save is global VM bytes + position + mode + party list; the
   script is reloaded by id. The engine save format must handle both PoR's style (script area saved, self-modifying
   scripts survive) and PoD's (reload by id).
7. **New script-visible concepts:** journal entries (text from the manual, not in the files), a party vault (separate
   save file), experience grants from scripts (0x41), find-member-by-name (0x37), yes/no and wait-for-key primitives,
   sound/music cues by id (0x2C), and an area mode switch through VM byte $0022.
8. **Character transfer is chained.** PoD imports Silver Blades' 439-byte records, not PoR's. `transfer.json` and the
   character manager need per-game codecs and a conversion graph (PoR → Curse → SSB → PoD), not one hop.
9. **Art:** one 256-colour screen built from per-family palette ranges. Assets need palette range metadata, and
   animated pictures need XOR-delta frames (survey). The intro and ending are separate `.BMA`/`.ACF` animations
   played by an external program. The importer needs a decoder for them `[?]`, or the module treats them as optional
   cut scenes.
10. **Installer archives** are PKZIP implode. The importer should take an installed directory first. Reading the
    floppy archives needs an implode decoder.
11. TotSF shares PoD's record layout, so this profile probably carries over to Treasures of the Savage Frontier
    `[?]` (check its opcode table).

## Open `[?]`

- Entry-vector roles and the area loop (assumed from PoR/Curse). How the script id is restored after a load.
- Exact meaning of SETUPMON's 4th operand, 0x22's two choices, 0x2C sound vs music, 0x3C, CALL id 3, PROGRAM ids 1/2,
  0x41 operand 2, TREASURE item ids and the 255 case, CLRMON's routine, 0x40 bit 0x80.
- Where the encounter menu and parley logic went (ENCMENU is a no-op).
- Global-area meanings (only the special cells above are known). The character write whitelist in full.
- `8X8D1`'s 28 extra bytes, the `8X8D0` font block, `.BMA`/`.ACF`/`.SMS` formats, the EGA remap table.
- SSB → PoD field mapping, roster file format, vault file layout, the `ITEMS` table layout vs PoR.
- Settled (`survey-sotsb.md` §8): which opcode changes date from Curse, Silver Blades or PoD.
