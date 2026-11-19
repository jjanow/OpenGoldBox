# Survey: Secret of the Silver Blades (DOS, 1990): formats and ECL

Status: **survey** (2026-11-18). Formats and the ECL VM only, no rules work. Its job is to place Silver Blades (SotSB)
between Pool of Radiance (PoR, `ecl-format.md`) and Pools of Darkness (PoD, `survey-pod.md`), so that each ECL change
in the module format's VM profiles (`docs/module-format.md` §5.1) can be dated. Facts are read from the files and the
program; each one names its offset or tool. Anything not checked is marked `[?]`. A quick check of Curse of the Azure
Bonds (Curse, 1989) was added where it dates a change (section 8).

Main target: **v1.30** (1990-10, two 360K disks). The WizardWorks AD&D Collectors Edition vol. 1 disk (1992, "v1.3")
holds byte-identical `SECRET1.EXE`/`SECRET2.EXE` archives, so it is the same release. v1.00, v1.10 and v1.20 were
compared against it ("Versions"). Curse: v1.2 (1989-07, four 360K disks). Sources:
`reverse-engineering/dos/original-images/SOURCES.txt` (games `sotsb`, `cotab`). Unpacked copies only; the masters were
not touched.

Offsets: "OVR 0xNNNN" is a file offset in SotSB v1.30 `GAME.OVR` unless another game is named. "DS:NNNN" is the
program's data segment. VM addresses are written `$NNNN`.

## Sources

- The SotSB v1.00–v1.30 files and the Curse v1.2 files. Read with `tools/scripts/dax.py` and `geo.py` (both unchanged;
  they handle SotSB as is), `gbpic.py` (unchanged, art check), the EXEPACK routine in `gbitem.py` (for `START.EXE`,
  which holds the overlay stubs), and a plain 8086 disassembler (capstone) on `GAME.OVR`.
- `tools/scripts/ecl.py`, unchanged, imported read-only by a scratch script that swaps in the SotSB tables of section
  3 (BASE $8000, script end $9DFF, the operand counts and the IF-skip table). This is the same method as the PoD
  survey. All tables the swap needs are in this document.
- Our PoR spec `ecl-format.md` and the PoR v1.3 `GAME.OVR` (for side-by-side reads), `survey-pod.md`, `survey-dqk.md`.
- Gold Box Companion `formats.zip` record sizes (via `prior-art.md`): Curse 422, SotSB 439, PoD 510.

## 1. File inventory

The two disks hold `INSTALL.COM` and two PKZIP self-extracting archives, `SECRET1.EXE` and `SECRET2.EXE`. 7-Zip
extracts both (it warns about the SFX stub; every size matches the listing). The install is 50 files, ~1.2 MB. There is
no separate launcher: `START.EXE` is the program root and `GAME.OVR` its overlay file.

| File(s) | Holds | Container |
|---|---|---|
| `START.EXE` (72,097) | Program root, overlay stubs | MZ, EXEPACK-packed, Turbo Pascal |
| `GAME.OVR` (278,528) | Turbo Pascal overlay file (`TPOV` header; PoR's is `FBOV`), incl. the ECL VM | TP overlay |
| `ECL1.DAX`–`ECL3.DAX` | 22 scripts: ids 3, 16, 32–34 / 17, 48–52, 64–66, 68 / 80–82, 96–99 | DAX |
| `GEO1.DAX`–`GEO3.DAX` | 17 maps: ids 16, 32–34 / 48–50, 64–66, 68 / 80–82, 96–98 | DAX |
| `WALLDEF1–3.DAX`, `8X8D1–3.DAX`, `8X8D5.DAX` | Wall sets (blocks of 780, 1,560 or 2,340 bytes), 8×8 wall tiles; `8X8D5` holds only block 201 (the font, as in PoR) | DAX |
| `PIC1–5.DAX`, `BIGPIC1–3.DAX`, `CPIC1.DAX`, `SPRIT1.DAX`, `CHEAD.DAX`, `CBODY.DAX`, `COMSPR.DAX`, `DUNGCOM.DAX`, `SKY.DAX`, `TITLE.DAX` | Pictures, big pictures, combat icons, approach sprites, combat heads/bodies, combat sprites, combat ground, sky, title | DAX, EGA |
| `MON1CHA.DAX`, `MON1ITM.DAX`, `MON1SPC.DAX` | 70 monster records of **439** bytes; item lists (63-byte records); effect lists (9-byte records) | DAX |
| `ITEM1–3.DAX` | Placed treasure (63-byte item records) | DAX |
| `ITEMS` | Base item table, 2,050 bytes (PoR's size; content differs) | raw |
| `SOUND.DAX` | Sound data `[?]` | DAX |
| `SAVGAMA.DAT`, `CHRDATA1–6.SAV`, `CHRDATA1/2/4.SFX` | Shipped party in slot A (section 6) | raw |

**DAX container: unchanged from PoR.** `dax.py` reads every file; all 665 blocks of the 37 DAX files extract
(scratch run of `dax.py extract`). Differences from PoR:
- **Disk files stay, three disks.** `ECL`, `GEO`, `WALLDEF`, `8X8D` and `ITEM` keep a digit. The program builds the
  digit from the disk byte DS:7420 (script loader OVR 0x93CF, item names OVR 0x2A9E, map loader OVR 0x40A74, wall-set
  loader OVR 0x40794, wall tiles OVR 0x43D5D; for the tiles disk 5 is read as 1). The font file `8X8D5` is opened by
  name from `START.EXE` (image 0x557A). Monster files are always `MON1…`: the `CHA` name is built from a constant "1"
  (OVR 0x25BDA); `ITM`/`SPC` presumably the same `[?]` (not read).
- **Ids in groups of 16 per region** (1–4, 16–34, 48–68, 80–99), with ids unique over the three disks. Curse v1.2
  already does this (ECL1 80–82, ECL2 1–4, ECL3 16–21, …, ECL6 64–69). PoD keeps the groups and drops the digit.

## 2. Maps (GEO)

- **Same layout as PoR.** All 17 blocks are 1,026 bytes: u16 **0x0400**, then the four 256-byte planes (walls N/E,
  walls S/W, event byte, barriers). `geo.py list` reads them unchanged: header 0400 everywhere, wall nibbles, barrier
  bytes, one-way edges and locked doors as in PoR. PoD is the game that changes the prefix (0xDDCC etc.).
- **Wall sets as in PoR.** LOADFILES (0x21) and LOADPIECES (0x37) share one handler (OVR 0x18AA, 3 operands), as in
  PoR: LOADFILES loads GEO block o1 when o1 ≠ 255/127 and the 3D flag $4BE6 ≠ 0 (map id → $4BC5); LOADPIECES loads
  WALLDEF ids into slots 1–3 with PoR's rules (127 in slot 1 → block 0; 255 → slot empty; the two-flag case fills only
  slots 1 and 3, flags $4BE7–$4BE9). No outdoor branch was seen in LOADFILES (PoR's BACPAC case); SotSB has no
  `BACPAC` file. `geo.py refs` lists every script's LOADFILES/LOADPIECES. Script-to-map: the same id in most areas,
  plus block 48 loading maps 49 and 48, block 52 loading map 50 and block 99 loading map 98; scripts 17, 49, 50 load
  no map.
- **Edges.** The three map accessors (barrier OVR 0x3FCCB, then two more at OVR 0x3FDFC and 0x3FF00 `[?]` which is
  wall and which is event) wrap a coordinate outside 0..15, except while the **running script** (DS:A4C2) is 0 or
  **80**, when they return 0 off the map. PoR's rule has the same shape with scripts 0 and 10 (`geo-format.md`
  "Edges"). SotSB has no script 0, so only script 80 is affected. The step and `$7ED5` "left the map" logic was not
  re-read `[?]`.
- **Script-driven mode switch (new).** A write to $4BE6 that changes its value sets the game mode to 4 and keeps the
  old mode (OVR 0x9F68: DS:67E7 → DS:67E8). PoR's writer has no such case (PoR OVR 0x8174 tests only $49FD/$49FE).
  PoD's $0022 is the same idea in its new memory map.

## 3. ECL

### 3.1 Where the scripts live, block layout, vectors

- Scripts are blocks of `ECL<d>.DAX`, d = disk byte DS:7420. 22 blocks; raw sizes 680–7,678 bytes.
- **Block layout as in PoR, base moved:** u16 prefix **0x1388** in all 22 blocks, then the script, **loaded at VM
  $8000**. The script loader (OVR 0x939F) clears 0x1E00 = **7,680** bytes at DS:67D2, then copies block bytes from
  offset 2 (the prefix is dropped). The region classifier (OVR 0x9774) gives the script area **$8000–$9DFF**. Every
  read of a script byte uses `buffer + addr − 0x8000` (e.g. OVR 0x9099, IF-skip OVR 0xAFBC).
- **Five vectors** `01 01 lo hi` at $8000–$8013 in every block; init (vector 4) is $8014 in all 22. The script init
  (OVR 0x922C) sets PC = $8000 and reads five operands into DS:67D7–67DF. It also sets $7EE1 = 255 and
  $7ED2 = $7ED3 = 0, and, unless DS:67F0 is set, clears $4C00–$4C1F and $7F79–$7F82. These are PoR's cells ($6DE1,
  $6DD2/$6DD3, $4A00–$4A1F, $6E79–$6E82) moved with their regions (3.3). Vector roles assumed from PoR `[?]`.
- A missing block makes the loader try again (OVR 0x9426 jumps back while the block is shorter than 2 bytes; the
  loader then always returns 1, OVR 0x9472, so NEWECL's failure branch OVR 0x1890 is dead code); there
  is no failure exit `[?]` whether the DAX reader asks for a disk meanwhile.

### 3.2 Operand encoding and strings: unchanged

The operand loader (OVR 0x9073) is PoR's: types 1, 2, 3 and 0x81 take 2 bytes, 0x80 a length byte plus packed text,
anything else 1 byte. The arrays moved (type DS:8FE9+k, low byte DS:9069+k, high byte DS:9029+k, string slots
DS:8FAC + 256·k). The value fetch (OVR 0x91AB) is PoR's too: types 1, 3 and 0x80 go through the memory reader with the
address only, types 2 and 0x81 give the raw word. So **types 1 and 3 fetch the same way and the width comes from the
address region**, as in PoR. Inline texts: 2,100, all within $20–$5F (decoder).

### 3.3 VM memory: PoR's regions, moved

Classifier OVR 0x9774, reader OVR 0xA0F1, writer OVR 0x9F3A (the same structure as PoR's 0x7BCE / 0x82DF / 0x814A):

| SotSB range | Cell | PoR range | Shift |
|---|---|---|---|
| $4B00–$4EFF | 16-bit, 1,024 words (DS:67C6) | $4900–$4CFF | +$0200 |
| $7C00–$7FFF | 16-bit, 1,024 words (DS:67CA), with the character redirections | $6B00–$6EFF | +$1100 |
| $7A00–$7BFF | 16-bit, 512 words (DS:67CE) | $9700–$98FF | −$1D00 |
| $8000–$9DFF | byte, the script | $9900–$B6FF | −$1900 |
| anything else | special cells, else stack residue | same | none |

- **Special cells (region 4) are PoR's**: $00B1, $00FB, $00FC, $033D (facing), $035F, $C04B/$C04C (x, y),
  $C04D, $C04E/$C04F, and on write $00B8/$00B9, $03DE (sound), $C059/$C05F (view colours), $D022 (reader OVR 0xA19D,
  writer OVR 0x9FF9). One change: **$C04D reads the raw facing when $4BE6 = 0 and facing / 2 otherwise** (OVR 0xA20C);
  PoR always halves it. PoD's $0011 has the same rule.
- **Unmapped read = stack residue**, as in PoR: the reader's result local is never set for an address with no case
  (OVR 0xA1DD, case $035F, sets nothing).
- New in region 0: a read of **$4BEA** returns the facing byte DS:89D9 (OVR 0xA10C).
- **Character view**: PoR's redirection scheme, same case offsets relative to the area base. Reads (OVR 0x9830) take
  `addr − $7C00` and map it into the **439-byte** record (e.g. case 0x18 → record 0x19, 0xA0 → 0xEE, 0xBB–0xC3 → words
  0x103–0x10B, 0xF7 → 0x14E, 0x11B → 0x1B6); the offsets inside the record are SotSB's. New cases over PoR: 0x15,
  0x19, 0x72, 0x73, 0x76, 0xC5, 0xC9–0xD0 (each a pair of record bytes chosen by a flag), 0xD6, 0x119. Writes (OVR
  0x9D20) keep PoR's cases, with the byte range at 0x1B–0x65 (PoR 0x20–0x70), and add 0xC5 and 0x119. The disk cell is **$7F12** (case 0x312, PoR $6E12); writing $7F22/$7F24/$7F26
  with a large value loads a wall set into slot 1/2/3, as PoR's $6E22–$6E26 (comparison against 0x80, OVR 0x9ED2).
- So a SotSB VM is PoR's VM with the region bases moved and a different record behind the redirection table. Script
  addresses differ by the shifts above; the addresses inside special handlers ($C04x, $03DE …) do not.

### 3.4 Control flow

- **Dispatcher:** OVR 0x458B, a compare chain on DS:8F63, like PoR's. It covers **0x00–0x41 except 0x1F**. 0x1F and
  ≥ 0x42 fall out of the chain without moving the PC (PoR's hang; the run loop was not re-read `[?]`).
- **0x3C is a stub.** Its handler (OVR 0x4248) is an empty frame: it loads no operand and does not move the PC, so it
  behaves like an unhandled opcode, presumably the same hang `[?]` (not run in DOSBox). No SotSB script uses it.
  Curse's 0x3C (PROTECT) loads 1 operand and Curse's ECL1 block 80 uses it once.
- **RETURN with an empty stack ends the run.** The handler (OVR 0x170A) pops when the stack is non-empty and calls the
  EXIT handler (OVR 0xC0B) otherwise. Curse already does this (Curse OVR 0x133A → 0x8F8). PoR falls through.
- **IF-skip** (OVR 0xAFB0) keeps a separate operand-count table:
  - 1: 01, 02, 0A, 0E, 11, 12, 1D, 20, 2D, 32, **34**, **36**, 38, 39, 3C, 3F, 40
  - 2: 03, 08, 09, 0F, 10, 1F, 22, 41
  - 3: 04–07, 0B, **0C**, 21, 28, 2A, 2F, 30, 35, 37, 3B
  - 4: 14, 23 · 5: 2E · 6: 1E, 2C · 8: 27 · 14: 29
  - anything else (incl. **15, 25, 26, 2B**): the opcode byte only.

  **Mis-skips: 15, 25, 26, 2B (variadic, skip 0), 34 (1 of 2) and 36 (1 of 2).** PoR's 0C mis-skip is fixed (3).
  Curse v1.2's table is the same without 41 (Curse OVR 0xA1C2). The 34 mis-skip appeared when CLOCK took a second
  operand (Curse). The 3C entry (1) does not match its stub handler, which matters only for a skipped 3C. No IF in the
  SotSB scripts is followed by a mis-sized opcode (decoder check).
- **Variadic opcodes** as PoR: VMENU (count = operand 3), ON GOTO/GOSUB and HMENU (count = operand 2).

**Decoder check** (scratch table swap over `ecl.py`, section "Sources"). All 22 blocks decode with **0 errors, 0
overlaps, 0 unhandled opcodes**. 95.8 % of the 137,918 script bytes are reached on hard paths (15,322 instructions);
of the rest, 5,344 bytes are tables and 424 dead code. The result is the same with 0x3C left out of the table. Every
block's header is `0x1388` + five `01 01` vectors. The same swap decodes Curse v1.2's 25 blocks cleanly once 0x3C has
Curse's 1 operand (96.9 % on hard paths).

### 3.5 Opcode table: PoR → Curse → SotSB

"PoR" = PoR operand count (`ecl-format.md`). "Curse" = Curse v1.2 count, read from each Curse handler the same way.
"SotSB" = operands loaded by the v1.30 handler. "Skip" = SotSB IF-skip count. "Uses" = decoded instructions in v1.30.
Status: **same** = PoR's count, and where "read" the handler does PoR's job; **changed** / **new** as in the other
surveys. The program carries a debug name list (OVR 0x431CE): the PoR/Curse names in PoR order, with no entry for 0x1F
and LOAD PIECES moved up next to LOAD FILES, then DUMP, FIND SPECIAL, DESTROY ITEMS and ADD EP. Aligned on that order,
ADD EP is the 65th name and so 0x41. The names in the table follow `ecl-format.md`; the names from that list are given
for 3E–41.

| Op | PoR name / ops | Curse | SotSB | Skip | Handler | SotSB | Status | Uses |
|---|---|---|---|---|---|---|---|---|
| 00 | EXIT 0 | 0 | 0 | 0 | 0xC0B | End run, clear GOSUB stack | same (read) | 710 |
| 01, 02 | GOTO, GOSUB 1 | 1 | 1 | 1 | 0xCA1, 0xCC0 | PC from the raw field | same (read) | 2163, 1268 |
| 03 | COMPARE 2 | 2 | 2 | 2 | 0xCD7 | text compare when either is text | same | 1169 |
| 04–07 | ADD … MUL 3 | 3 | 3 | 3 | 0xD54 | | same | 316 |
| 08, 09 | RANDOM, SAVE 2 | 2 | 2 | 2 | 0xDF7, 0xE55 | | same | 152, 1407 |
| 0A | LOADCHAR 1 | 1 | 1 | 1 | 0xEA3 | as PoR, plus DS:A4D1 = "no member v" (§10.4) | same (read) | 39 |
| 0B | LOADMON 3 | 3 | 3 | 3 | 0x1019 | list cap 63 (OVR 0x1038), as PoR | same (read) | 349 |
| 0C | SETUPMON 3 | 3 | 3 | **3** | 0xF7D | sprite, $7EC0/$7EC1 (PoR $6DC0/$6DC1), close-up | same (read), skip fixed | 236 |
| 0D, 0E | APPROACH 0, PICTURE 1 | 0, 1 | 0, 1 | 0, 1 | 0x1473, 0x14B3 | | same | 89, 125 |
| 0F, 10 | INPUTNUM, INPUTSTR 2 | 2 | 2 | 2 | 0x15B0, 0x15F6 | | same | 1, 6 |
| 11, 12 | PRINT, PRINTCLR 1 | 1 | 1 | 1 | 0x166E | | same | 480, 1179 |
| 13 | RETURN 0 | 0 | 0 | 0 | 0x170A | **empty stack: EXIT** (as Curse) | changed (Curse) | 278 |
| 14 | CMPAND 4 | 4 | 4 | 4 | 0x175D | | same | 70 |
| 15 | VMENU 3+n | 3+n | 3+n | 0 | 0x1BC4 | | same, mis-skip kept | 2 |
| 16–1B | IF = … ≥ 0 | 0 | 0 | 0 | 0x17BF | flags DS:8F5C–8F61; false → IF-skip | same (read) | 1976 |
| 1C | CLRMON 0 | 0 | 0 | 0 | 0x1F1F | | same | 239 |
| 1D | PARTYSTR 1 | 1 | 1 | 1 | 0x1F87 | | same | 0 |
| 1E | CHKPARTY 6 | 6 | 6 | 6 | 0x212C | | same | 13 |
| 1F | — | — | — | 2 | none | no handler | same | 0 |
| 20 | NEWECL 1 | 1 | 1 | 1 | 0x1837 | old id → $4BF2 (before the fetch), new id (low byte) → DS:A4C2 (read) | same | 78 |
| 21, 37 | LOADFILES, LOADPIECES 3 | 3 | 3 | 3 | 0x18AA | section 2; LOADPIECES has a third flag $4BE9 (§10.4) | changed (read) | 20, 19 |
| 22 | PSURPRISE 2 | 2 | 2 | 2 | 0x2507 | | same | 0 |
| 23 | SURPRISE 4 | 4 | 4 | 4 | 0x25A3 | PoR's rule; result written to $02CB (lost, §10.4) | same (read) | 0 |
| 24 | COMBAT 0 | 0 | 0 | 0 | 0x266B | | same | 258 |
| 25, 26 | ONGOTO, ONGOSUB 2+n | 2+n | 2+n | 0 | 0x2990 | | same, mis-skip kept | 186, 4 |
| 27 | TREASURE 8 | 8 | 8 | 8 | 0x2A48 | | same | 91 |
| 28 | ROB 3 | 3 | 3 | 3 | 0x2FDC | | same | 7 |
| 29 | ENCMENU 14 | 14 | 14 | 14 | 0x311C | | same | 6 |
| 2A | GETTABLE 3 | 3 | 3 | 3 | 0x1B1A | | same | 194 |
| 2B | HMENU 2+n | 2+n | 2+n | 0 | 0x1D8E | | same, mis-skip kept | 163 |
| 2C | PARLEY 6 | 6 | 6 | 6 | 0x3819 | | same | 1 |
| 2D | CALL 1 | 1 | 1 | 1 | 0x3FD1 | **raw ids** compared as `field − 0x7FFF`: $2DCB (view refresh), $B203 (sound, as PoR $BA03), $C018, $C01E (one square forward, as PoR), $6803 `[?]`. No $2C90, $8000, $8001 | same (read), ids changed | 152 |
| 2E | DAMAGE 5 | 5 | 5 | 5 | 0x398D | | same | 41 |
| 2F, 30 | AND, OR 3 | 3 | 3 | 3 | 0x1AAB | | same | 701, 584 |
| 31 | SPRITEOFF 0 | 0 | 0 | 0 | 0x3D11 | PC + 1, sprite flags as PoR | same (read) | 0 |
| 32 | FINDITEM 1 | 1 | 1 | 1 | 0x38B8 | | same | 1 |
| 33 | PRINTRET 0 | 0 | 0 | 0 | 0x3D6C | | same | 184 |
| 34 | CLOCK 1 | **2** | 2 | **1** | 0x3D37 | amount, unit | changed (Curse), mis-skip | 1 |
| 35 | SAVETABLE 3 | 3 | 3 | 3 | 0x1B78 | | same | 5 |
| 36 | ADDNPC 2 | 2 | 2 | 1 | 0x3E52 | | same, mis-skip kept | 5 |
| 38 | PROGRAM 1 | 1 | 1 | 1 | 0x41A1 | **ids 0 and 9 only**: 0 runs a program routine (OVR 0x41E5; training as in PoR `[?]`); 9 runs a routine with the PC saved around it (camp, as PoR's 9), then EXIT **only when $4C38 = 0** (OVR 0x422E). Any other id loads its operand and does nothing: the scripts use 0 (block 16) and **3** (block 3, the start script, $82E3), so PROGRAM 3 is a no-op. PoR: 0, 8, 9, and 9 always exits | same (read), ids changed | 2 |
| 39 | WHO 1 | 1 | 1 | 1 | 0x3DEC | | same | 15 |
| 3A | DELAY 0 | 0 | 0 | 0 | 0x3961 | PC + 1, one delay call | same | 267 |
| 3B | SPELL 3 | 3 | 3 | 3 | 0x3EC5 | | same | 4 |
| 3C | PROTECT 1 | 1 | **stub** | 1 | 0x4248 | loads nothing, PC not moved (3.4) | changed | 0 |
| 3D | CLEARBOX 0 | 0 | 0 | 0 | 0x3D97 | | same | 23 |
| 3E | — | 0 (DUMP) | 0 | 0 | 0x424F | DUMP: removes the current character from the party (§10.4) | new (Curse), read | 2 |
| 3F | — | 1 (FIND SPECIAL) | 1 | 1 | 0x4282 | "=" if the current character has effect v, else "≠" (OVR 0x42BA) | new (Curse), read | 0 |
| 40 | — | 1 (DESTROY ITEMS) | 1 | 1 | 0x42D3 | every member's items of type v removed (§10.4) | new (Curse), read | 1 |
| 41 | — | — | **2** | 2 | 0x43FF | ADD EP: o1 = 0 current character, else every party member; o2 = 16-bit amount, divided by the member's number of record bytes 0x111–0x117 that are > 0 and added to the dword experience at record 0x12C when record 0x1A7 ≠ 0 | **new in SotSB**, read | 40 |

Summary: SotSB has **65 dispatched opcodes** (0x00–0x41 without 0x1F), one of them a stub (0x3C). Of PoR's 61,
**59 keep PoR's count**; CLOCK (0x34) has a second operand and 0x3C is a stub. Four are added (3E–40 from Curse, 41
new). RETURN, the CALL
id set and the PROGRAM ids differ in behaviour. Every opcode PoD changes in count or meaning (0C, 1D, 21, 22, 23, 27,
29, 2C, 31, 37, 3C) is still PoR's in SotSB, except 0x3C, which SotSB already stubs. The table is identical in
v1.00–v1.30 (section "Versions").

## 4. Art

All picture families are **PoR's DOS EGA formats**: `gbpic.py` decodes every block of `PIC1–5`, `BIGPIC1–3`, `CPIC1`,
`SPRIT1` (3 distance frames each), `CHEAD`, `CBODY`, `COMSPR`, `DUNGCOM`, `SKY`, `TITLE` and `8X8D1–3` with no
failure. The only block it skips is `8X8D5` block 201, the 1-bit font, as in PoR. Encounter pictures are 88×88 with
1–8 frames, big pictures 304×120, title 320×200. `dos-graphics-survey.md` already lists SotSB as EGA 16 only. The PoR
importer's art path should take SotSB as it is. Per-asset palette work starts with the VGA games (PoD, DQK).

## 5. Records

| Record | PoR | Curse | SotSB | PoD | Where checked |
|---|---|---|---|---|---|
| Character / monster | 285 | 422 | **439** (0x1B7) | 510 | `CHRDATA1–6.SAV` and all 70 `MON1CHA` blocks = 439; Curse `SAVE\CHRDATA*.SAV` = 422 |
| Item, in DAX and in memory | 63 | `[?]` | **63** | 17 in DAX, 63 in memory | `MON1ITM` and `ITEM1–3` block sizes are all multiples of 63 |
| Effect | 9 | 9 (`.FX`) | 9 (`.SFX`) | 9 (`.EFX`) | file sizes 9 and 27 |

- ADD EP (0x41) and the character view (3.3) touch record 0x111–0x117 (class levels), 0x12C (experience, dword),
  0x1A7 and the next-pointer at 0x19D (party walk, OVR 0x44D1). The full layout is GBC's `[?]` (not re-read).
- The member files are `CHRDATxn.SAV` + `.SFX` (effects); no item file is shipped (the party carries nothing) `[?]`
  the item file's extension.

## 6. Save game

Writer OVR ≈0x26AE0 (the block writes from OVR 0x26B15):

| Offset | Size | Content |
|---|---|---|
| 0 | 1 | Disk byte DS:7420 (also copied to $7F12 just before the write, OVR 0x26B07) |
| 1 | 2048 | $4B00–$4EFF, one u16 per address |
| 2049 | 2048 | $7C00–$7FFF |
| 4097 | 1024 | $7A00–$7BFF |
| 5121 | 5 | Position DS:89D7 (x, y, facing, two view bytes) |
| 5126 | 1 + 1 | Game mode bytes DS:67E8, DS:67E7 |
| 5128 | 12 | Wall-set slots 1–3, two words each (DS:89DC–89E7: the WALLDEF id per slot, 0xFFFF = empty) |
| 5140 | 1 | Party count |
| 5141 | 328 | 8 × 41-byte names of the member files |

The total is **5,469 bytes = the shipped `SAVGAMA.DAT`**, whose party count (6) sits at 5,140. That is the offset PoD's
loader seeks to in a SotSB save (`survey-pod.md` §5), which confirms the layout from the other side.

- **No script bytes are saved.** PoR saves its 7,680-byte script area (13,137 bytes). Curse v1.2's `SAVGAMA.DAT` is
  13,149 = 5,469 + 7,680, and its writer has a 0x1E00-byte block write (Curse OVR 0x1FD44). So **SotSB is the first
  game that reloads the script by id**.
- **The script id is kept in VM word $4BF2.** NEWECL stores the old id there (OVR 0x184E) and the area loop stores the
  running id after each run (OVR 0x48D3, 0x4AEE). On entry the area loop loads script $4BF2 (OVR 0x4A50). If $4BF2 = 0
  (a new game), it starts **script 3 on disk 1 or script 17 on disk 2**, chosen by DS:67E9 (OVR 0x4A1C) `[?]` what
  sets it (presumably the new-party versus imported-party start).
- Before writing, the program also copies DS:67D6 to $4BFC and a view value to $4BFF (OVR 0x26AE0); the loader
  reverses it (OVR 0x2647D) `[?]` meanings.

## 7. The program

- `START.EXE`: Turbo Pascal, EXEPACK-packed (79,824-byte image after `gbitem.py`'s unpacker). It holds the overlay
  stubs: header +4 = the unit's file offset in `GAME.OVR`, +0xC = entry count, 5-byte `INT 3F` entries from +0x20.
  The ECL helpers are stub 0x7D (loader = entry 2 at OVR 0x9073, value fetch OVR 0x91AB, script init OVR 0x922C,
  script loader OVR 0x939F, reader OVR 0xA0F1, writer OVR 0x9F3A, IF-skip OVR 0xAFB0). The dispatcher and the
  handlers are in the unit at OVR 0xC0B–0x4D07 (stub paragraph 0x50; EXIT is its first routine, the dispatcher sits
  at OVR 0x458B).
- `GAME.OVR` begins `TPOV` (PoR's `FBOV`); the file layout is otherwise the same kind.

## Versions

| Version | Differs from v1.30 in |
|---|---|
| v1.00 (1990-06) | `START.EXE`, `GAME.OVR`, `ECL2.DAX` block 48, `ECL3.DAX` block 82 |
| v1.10 (1990-06) | `START.EXE`, `GAME.OVR`, `ECL2.DAX` block 48 (same as v1.00's) |
| v1.20 (1990-07) | `START.EXE`, `GAME.OVR` |
| v1.3 WizardWorks (1992) | nothing (byte-identical archives) |

In every version the dispatcher covers the same opcodes with the same operand counts (scan of each handler), the
IF-skip table has the same entries (v1.00, v1.10, v1.20 and v1.30 compared), and the decoder check passes with 0
errors (v1.00/v1.10: 137,912 bytes, 15,321 instructions). So **one VM profile serves every SotSB release**; the script
changes are content only `[?]` (blocks 48 and 82 not compared in detail).

## 8. Dating the ECL changes: PoR → Curse → SotSB → PoD → DQK

Curse cells are from the quick Curse v1.2 check (dispatcher Curse OVR 0x3BEF, handler counts, IF-skip Curse OVR
0xA1C2, classifier Curse OVR 0x8929, RETURN Curse OVR 0x133A, save size and writer). Anything else about Curse is
from the coab notes in `survey-pod.md` or `[?]`.

| Feature | PoR (1988) | Curse (1989) | SotSB (1990) | PoD (1991) | DQK (1992) |
|---|---|---|---|---|---|
| Script base, prefix, area | $9900, 0x1388, 7,680 | **$8000**, 0x1388, 7,680 | same as Curse | same | $8000, none, 12,003 |
| Memory map | 4 word/byte areas $4900/$6B00/$9700/$9900 | **same areas moved**: $4B00, $7C00, $7A00, $8000 | same as Curse | **byte globals $0001–$0400 + character window $0401–$0800** | as PoD (translated character view) |
| Operand width | from the region | from the region | from the region | **from the operand type** | from the type |
| Unmapped read | stack residue | stack residue (reader Curse OVR 0x9117 sets nothing for unknown cases) | stack residue | **0** | 0 |
| Special cells | $C04B… | `[?]` | PoR's set; $C04D raw outdoors (new), $4BEA facing, $4BE6 mode switch | moved into the globals; same ideas | as PoD |
| Character view | $6B00 redirections, 285-byte record | `[?]` | $7C00 redirections, 439-byte record | raw 510-byte window, write whitelist | 58 translated fields |
| Disk files | 8 disks | 6 disks, ids in groups of 16 | 3 disks, groups of 16 | **one file per kind** | HLIB, no disks |
| Opcodes | 0x00–0x3D w/o 1F (61) | **+3E, 3F, 40**; CLOCK 2 ops | **+41** (ADD EP); **3C stubbed** (65 dispatched) | +1F (journal); 0C, 1D, 21, 22, 23, 27, 29, 2C, 31, 37, 3C changed (66) | as PoD (66) |
| IF-skip mis-skips | 0C, 15, 25, 26, 2B, 36 | 15, 25, 26, 2B, **34**, 36 (0C fixed) | as Curse | 15, 25, 26, **27**, 2B, **31**, 34, 36 | **none** |
| RETURN on empty stack | falls through | **EXIT** | EXIT | EXIT | EXIT |
| CALL ids | raw: $2C90, $8000, $8001, $BA03, $C018, $C01E | `[?]` | raw: $2DCB, $B203, $C018, $C01E, $6803 | **small ids 1, 2, 3** | 1, 2, 3 |
| PROGRAM ids | 0, 8, 9 (9 exits) | `[?]` | 0, 9 (9 exits only if $4C38 = 0) | 0, 1, 2, 9 | 0–3, 9 (9 exits on a flag) |
| LOADMON list cap | 63 | `[?]` | 63 | 48 | `[?]` |
| Script saved with the game | yes (13,137) | yes (13,149) | **no: reload by id ($4BF2)** (5,469) | no (1,364) | no |
| GEO prefix | 0x0400 | `[?]` | 0x0400 | 0xDDCC etc. (ignored) | 6-byte header |
| No-wrap scripts | 0, 10 | `[?]` | 0, 80 | `[?]` | `[?]` |
| Art | EGA | EGA | EGA | **VGA 256** | VGA |

So the late-engine VM (PoD/DQK) is reached in three steps: **Curse** moved the regions and added the Curse opcodes
and RETURN-exits; **SotSB** added ADD EP, stubbed PROTECT and stopped saving the script; **PoD** rebuilt the memory
model (byte globals, type-driven width, record window), turned the encounter opcodes into UI primitives and moved to
single files. DQK then fixed the IF-skip.

## 9. Module-format and VM implications

**Built (s24): the profile `ecl-sotsb-1.3`** (`EclProfiles.Sotsb13`, `EclOpcodes.Sotsb`). It holds
the layout, the four regions (script region not saved), the opcode rows of 3.5 and the IF-skip chain of 3.4 as data,
`ReturnOnEmptyStack = Exits`, the CALL ids, PROGRAM ids 0 and 9 with the exit guard $4C38
(`ProgramExitGuardCell`), the disk cell $7F12 and the banked kinds. 0x3C has no row (its handler neither loads an
operand nor moves the PC); it keeps the name PROTECT and IF-skip 1. Since s28 the headless VM runs the profile
(section 10). `EclDecoder` / `EclAnalysis` take a profile, `goldbox-tools ecl-check GAMEDIR --vm ecl-sotsb-1.3`
and `ecl.py … --vm sotsb` decode the 22 v1.30 blocks with 0 errors, 0 overlaps, 0 unhandled opcodes and 0 IF-skip
hazards (137,918 bytes, 15,322 instructions, 132,135 bytes on hard paths). `SotsbProfileTests` checks this on the
player's files (`GOLDBOX_SOTSB_DIR`, skipped without). The scripts use CALL $2DCB, $B203 and $C01E only, and
PROGRAM 0 and 3. The IF-skip chain, the CALL compares and the PROGRAM cases were re-read in v1.30 for this.


1. **`ecl-por-1.3` is the template for a SotSB profile, not the PoD one.** A SotSB VM is PoR's code with: script base
   $8000, the three word areas moved (table 3.3), the IF-skip table of 3.4, RETURN-exits, CLOCK with 2 operands, the
   0x3C stub, 3E–41 handlers, SotSB's CALL and PROGRAM ids, and a redirection table into the 439-byte record. The
   regions, special cells, residue and width rule stay PoR's. In `EclProfile` (s17) the layout, regions, disk cell,
   opcode and IF-skip tables, width rule (`FromAddressRegion`) and unmapped read (`StackResidue`) are already data
   that fits SotSB. The enum members it needed exist since s24 (above); the redirection list, the special-cell
   changes and the handlers followed in s28 (section 10).
2. **Region base offsets are a profile field, not a constant.** The same special-cell addresses ($C04B…) sit next to
   moved areas, so "move every address" does not work; the profile must give each region's base.
3. **Banks stay.** SotSB keeps disk digits for ECL, GEO, WALLDEF, 8X8D and ITEM (3 disks), and a fixed `MON1`. Its
   script and map ids are unique across disks (as PoR's), so the PoR import rules for banks apply unchanged.
4. **Saves reload by id from SotSB on.** The engine save must already handle "no script bytes, script id in a VM
   cell" for the second game of the series. The new-game start (script 3 or 17 by a program flag) is the same kind of
   start-up question as PoR's (`module-format.md` §18).
5. **The no-wrap rule is per running script.** PoR {0, 10}, SotSB {0, 80}; the accessors test the running script id,
   not the map. The module already keys it that way (`noWrapOffMap` in `areas/<id>.json`, one area per script), so a
   SotSB importer only needs its own id set.
6. **Character transfer:** Curse 422 → SotSB 439 → PoD 510. SotSB's import of Curse characters and the field mapping
   were not read `[?]`.

## 10. Handlers in the headless VM (s28)

`EclHandlerSet.Sotsb` runs in `EclVm` / `EclMemory`: PoR's handlers, with SotSB's branching off where the program
differs. Everything below was read from v1.30 `GAME.OVR` for this; the VM code cites the same offsets. Tests:
`SotsbVmTests` (one per handler, and with `GOLDBOX_SOTSB_DIR` every vector of the 22 blocks headless from the shipped
`SAVGAMA.DAT` and its six `CHRDATA` members, plus a seeded exploration: no fault, no unhandled opcode, no access to
PoR's $4900–$4AFF or $6B00–$6EFF).

### 10.1 Memory

- Areas by role: globals $4B00 (DS:67C6), character view $7C00 (DS:67CA), $7A00 (DS:67CE), script $8000 (DS:67D2).
  The fetch is `buffer + A − 0x8000`.
- Region 0: a read of **$4BEA** gives the facing byte DS:89D9 (OVR 0xA10C). A write of $4BFD / $4BFE sets the sky
  redraw flag DS:A4CE (OVR 0x9F61, PoR's DS:84E8). A write to **$4BE6** that changes it copies the game mode DS:67E7
  to DS:67E8 and sets DS:67E7 = 4 before the store (OVR 0x9F68); `EclMemory.GameMode` / `PreviousGameMode`.
- Special reads (OVR 0xA19D): PoR's list. $00B1 / $00FB / $00FC read DS:8F52 / 8F4E / 8F50, which no byte of
  `GAME.OVR` or `START.EXE` writes (searched): 0, as in PoR `[?]` (not run). **$C04D** is the raw facing while
  $4BE6 = 0, else facing div 2 (OVR 0xA20C). $035F and $C059 have a case with no value (residue), as in PoR.
- Special writes (OVR 0x9FF9): PoR's list and rules ($03DE → DS:A4B0, $00B8 → A4B2, $00B9 → A4B4; $C04B/$C04C →
  x / y and DS:A4CC; $C04D → facing (v mod 4) × 2; $C059 / $C05F → DS:8F4C / 8F4D and DS:A4CB; $00FB, $00FC, $00B1,
  $D022 nothing). $C05A (written by blocks 80–82) has no case.
- Texts (OVR 0xA628 reads a type-0x81 text, OVR 0xA264 is SAVE's text store): $7C00 is the current character's name
  (at most 15 characters), as PoR's $6B00. A word-area text ends at a **0 word** (OVR 0xA664 compares the word; the
  character is the low byte). An empty text stores only the terminator (OVR 0xA29A skips the loop; PoR's byte
  counter underflows there).

### 10.2 The $7C00 character view

Reader OVR 0x9830 (k = address − $7C00; the record is the current character's, pointer DS:7D38). An offset with no
case reads the cell; a case that sets no value reads residue.

| k | Value | k | Value |
|---|---|---|---|
| 0x15 | rec 0x13 | 0xC9, 0xCA, 0xCB, 0xCC, 0xCF, 0xD0 | f · rec a + rec b (signed bytes), (a, b) = (0x11D, 0x116), (0x118, 0x111), (0x11E, 0x117), (0x11A, 0x113), (0x11B, 0x114), (0x11C, 0x115) |
| 0x18 | rec 0x19 | 0xD6, 0xD8 | rec 0x11F, 0x120 (signed) |
| 0x19 | rec 0x1B | 0xF7 | word rec 0x14E |
| 0x72, 0x73 | rec 0x6B, 0x6C (signed) | 0xF9 | rec 0x150 |
| 0x76 | rec 0x70 | 0x100 | 0 while DS:A4D1 is set; else rec 0x1A6 mapped 0→1, 3→6, 4→5, 5→4, 6→3, 7→7, 8→2, other → 0 |
| 0x9B | rec 0xE9 | 0x10C | 0x80 if rec 0x1A8 = 0 and rec 0x1A9 ≠ 0; 0x81 if rec 0x1A8 = 1; else 0 |
| 0xA0 | rec 0xEE (signed) | 0x10D | residue (case, no value) |
| 0xA5–0xAC | rec 0xF3–0xFA (code: 0xF2 + k − 0xA4) | 0x119, 0x11B | rec 0x1B5, 0x1B6 |
| 0xB8 | rec 0xFF | 0x2B1, 0x2B4 | members before the current record in the list (OVR 0x97CF; the list length if it is not listed) |
| 0xBB–0xC5 (odd) | words rec 0x103–0x10D | 0x2CF | PoR's $6DCF table keyed by rec **0x1B** (3→0 … 18–25→60), else residue |
| | | 0x312 | the disk byte DS:7420 |
| | | 0x33E | the cell $7F3E (party size) |

f (OVR 0x3CBB6): 1 when L > rec 0xEF (signed), else 0; L (OVR 0x3CB44) is 0 unless rec 0x6B = 6, then the first
non-zero of rec 0x111–0x116 (rec 0x117 when all six are 0). `[?]` meaning: presumably a dual-class human whose new
class has passed the old one. 0xCD and 0xCE have no case.

Checked against the program (s28): `tools/scripts/sotsb_view_oracle.py` runs OVR 0x9830 and 0x9D20 under unicorn on
random party records; `SotsbViewProgramTests` compares every handled read (516, all but the two residue cases) and
18,480 writes (record bytes, disk bytes, DS:A4B7, wall-set loads) with `EclMemory`: all equal.

Writer OVR 0x9D20, after the cell is stored: k = 0 with value 0 → DS:A4B7 (LOADCHAR's removal arm, PoR DS:84D3);
0x1B–0x65 → rec 0x1E + (k − 0x1B); 0xB8 → rec 0xFF (v − 0x32 when v > 0xB2); 0xBB–0xC5 odd → words rec 0x103–0x10D;
0xF7 → word rec 0x14E; 0xF9 → rec 0x150; 0x100 with v = 1 → rec 0x1A6 = 0, rec 0x1A7 = 1; 0x10C with 0 / 0x80 /
0x81 → rec 0x1A8, 0x1A9 = (0, 0) / (0, 1) / (1, 1); 0x119 → rec 0x1B5; **0x312 → the old disk to DS:7421, then
DS:7420 = (low byte + 1) div 2** (START 0640:0AB0); 0x322 / 0x324 / 0x326 with v > 0x80 → wall set v AND 0x7F into slot
1 / 2 / 3 (OVR 0x4077E). There is no case like PoR's $6C00 = 0 (DS:84D2): its SotSB twin DS:A4B6 is only ever
cleared in `GAME.OVR` and absent from `START.EXE` (byte search), so LOADCHAR's removal looks unreachable `[?]`.

### 10.3 Control flow and the script init

- **Script init** OVR 0x922C: DS:A4C6, A4C8, A4B6, A4B7 cleared, $C059 / $C05F colours 0x41 / 9 and DS:A4CB set, PC =
  $8000, the GOSUB list and the flags cleared, **$7EE1 = 255, $7ED2 = $7ED3 = 0, $4BE5 = 0**, the five vectors read;
  then, unless DS:67F0 is set, $4C00–$4C1F and $7F79–$7F82 cleared. **No $4BE6 write** (PoR sets $49E6 = 1). These
  writes go straight into the arrays (no writer cases apply to them). DS:8F67 = 8 and DS:90AA = 0 `[?]`.
- **RETURN** OVR 0x170A: pops when the GOSUB list is non-empty, else calls EXIT (0x0C0B).
- **EXIT** OVR 0x0C0B: as PoR (restore the selected character when DS:59B6, the GOSUB list freed, stop flag, text
  cursor to column 1 row 17). DS:59B6 / DS:59BB are PoR's DS:4430 / DS:4436.
- **NEWECL** OVR 0x1837: $4BF2 = the running id (byte DS:A4C2) before the operand's value is fetched; the new id is
  the value's low byte; load, script init, stop flag DS:59B4 and changed flag DS:59B5 set.
- **0x3C** has no row (empty frame at OVR 0x4248): an unhandled opcode in the VM. Its run-time behaviour stays `[?]`.
- **PROGRAM** OVR 0x41A1: compares the operand's **low byte**; 9 runs camp with the PC saved around it, then EXIT only
  while $4C38 = 0 (OVR 0x422E). Camp (OVR 0x4136) runs script vectors 2 (DS:67DB) and 3 (DS:67DD) through OVR 0x47FD
  `[?]` (host side, not modelled). DS:A4D4 is saved and restored around the handler `[?]`.
- The area loop / script switch (`FollowScriptChanges`) is PoR's (vectors 4, 0, 1) without PoR's event re-reads,
  edge cell and view-mode rules for SotSB: OVR 0x4A50 … was not read `[?]`.

### 10.4 Opcodes

- **CLOCK** OVR 0x3D37: two plain value fetches (low bytes) to OVR 0x2BA0D (amount, unit); no residue index as in
  PoR. `IEclHost.AdvanceClock`; the clock routine and the unit codes `[?]`.
- **LOADCHAR** OVR 0xEA3: PoR's walk (member v AND 0x7F becomes current when listed), and DS:A4D1 = 0 when it is,
  1 when not (`EclMemory.MemberMissing`, read by $7D00). Removal with bit 0x80 and DS:A4B6 and A4B7 set: OVR 0x20E40
  with (0, 1), then the party list redraw OVR 0x38915.
- **Removal** OVR 0x20E40(a, b): the current record is unlinked; its predecessor becomes current, or the new head
  when it was the head; $7F3E − 1 when a = 0; with b ≠ 0, rec 0x155 goes to OVR 0x42CC0 first `[?]` (presumably the
  combat icon); a nil current then becomes the head. This is PoR's 0x1CC10 rule (`EclPartyRules.RemoveCurrent`).
- **SURPRISE** OVR 0x25A3: PoR's rule (dice OVR 0x36FAE = PoR's: count × (Random(sides) + 1)), but the result goes
  through the writer to the constant **$02CB** (OVR 0x2658), which is no cell and no special write case: lost.
  PoR wrote $6DCB; the constant was not moved with the regions. No script uses SURPRISE.
- **LOADFILES / LOADPIECES** OVR 0x18AA: all three values fetched (low bytes), DS:59B7 set. LOADFILES (DS:59B9):
  o1 not 255/127 and $4BE6 ≠ 0 → $4BC5 = o1, map o1 loaded (OVR 0x40A6D), $7EC9 = 0; no outdoor branch.
  LOADPIECES (DS:59B8): o1 = 127 → set 0 into slot 1 only. Else with $4BE7 and $4BE8 ≠ 0: slot 1 = o1 (255 empties);
  then with **$4BE9 ≠ 0** slots 2 and 3 get o2 / o3 only when not 255/127 (else untouched), with $4BE9 = 0 slot 3 = o3
  (255/127 empties). Otherwise slot k = o_k, **255 or 127 empties it**. After both ran with DS:67E8 = 3 a redraw
  follows (OVR 0x1A68, display).
- **FINDITEM** OVR 0x38B8: PoR's (item list rec 0x161, next +0x2A, type byte +0x2E).
- **DUMP (3E)** OVR 0x424F: PC + 1; the removal OVR 0x20E40(0, 1) on the current character; the new current one
  becomes the selected one (DS:59BB); party list redraw (OVR 0x38915).
- **FIND SPECIAL (3F)** OVR 0x4282: flags cleared; "=" when the current character's effect list (rec 0xFB, next +5)
  holds an effect whose byte 0 is the operand's low byte (OVR 0x3AA19), else "≠".
- **DESTROY ITEMS (40)** OVR 0x42D3: for each member in list order, each item whose byte +0x2E is the operand's low
  byte is unlinked and freed (OVR 0x3966C) with that member current; a readied one (byte +0x34 ≠ 0) goes through
  OVR 0x28D70 first `[?]` (the unready routine, not read; the VM leaves the record as it is); after each member OVR
  0x3A292 runs on it `[?]` (not read); the old current character is restored.
- **ADD EP (41)** OVR 0x43FF: amount = operand 2 (16-bit, fetched first), who = operand 1's low byte. Text cursor to
  column 1 row 17, then the whole-party message (OVR 0x43AF) or the one-member message with the name (OVR 0x43DB),
  shown through START 0540:04B9 and 0540:0BF7 `[?]` (`IEclHost.ExperienceMessage`). Per member: share = amount div
  (count of rec 0x111–0x117 above 0 as signed bytes; the loop runs k = 0 … 6), **a divide fault when the count is 0**
  (even with rec 0x1A7 = 0: the div comes first); when rec 0x1A7 ≠ 0 the dword at rec 0x12C gets the share (add
  with carry). `[?]` rec 0x1A7's meaning.
- **CALL** OVR 0x3FD1: ids as the profile lists ($2DCB view refresh, $B203 sound by $03DE 8 / 10, $C01E one square
  forward via OVR 0xAD2B, $C018 re-reads the wall ahead only while $4BE6 = 0, $6803 cycles an animation table
  DS:89F2 / 89F4 / 89F5 `[?]`); effects are the host's.
- **Not re-read for the 439-byte record**: PARTYSTR, PSURPRISE, CHKPARTY, ROB, DAMAGE, SPELL and ADDNPC run PoR
  record offsets in our code, so under SotSB they go to the host with a `[?]` diagnostic. The scripts use CHKPARTY
  13×, ROB 7×, DAMAGE 41×, SPELL 4×, ADDNPC 5×.

## Open `[?]`

- 0x3C and 0x1F behaviour at run time (hang assumed from the handler shape; not run in DOSBox).
- CALL $C018 and $6803; DESTROY ITEMS' unready (OVR 0x28D70) and per-member routine (OVR 0x3A292); PROGRAM 0 details;
  the record handlers of 10.4's last item; the clock routine OVR 0x2BA0D; the SotSB area loop; DS:67E9 (new-game start
  choice); $4BFC/$4BFF in the save; which accessor is wall and which event; the step / "left the map" logic.
- Curse: special cells, CALL/PROGRAM ids, LOADMON cap, no-wrap scripts, GEO prefix (not read).
- The SotSB record layout and the Curse → SotSB character import; the item file extension; `SOUND.DAX`; the
  `MON1ITM`/`MON1SPC` name builders.
