# Survey: The Dark Queen of Krynn (DOS, 1992)

Status: **survey** (2026-11-17). This is a formats and ECL survey for the module format and the per-game VM tables.
It covers no rules (combat, spells, THAC0). Facts marked **P** were read in the program or checked against the files
by us. **C** marks facts taken from prior art without our own check. `[?]` means unverified.

Target: v1.1, WizardWorks AD&D Collectors Edition Vol. 2 (`reverse-engineering/dos/original-images/dqok/`, see
`SOURCES.txt`). v1.0 (1992-05) was compared where noted. The German release was not looked at.

All file offsets are into the unpacked files (`DQK.EXE`, `*.GLB`, `*.TLB`) as they come out of the installer.

## Sources

- The v1.1 and v1.0 files themselves, read with throwaway scripts (capstone for 8086 disassembly). Only offsets
  are cited here.
- tomtombombadil/CYD-GoldBoxGameEngine (MIT), `docs/SPEC.md` "Dark Queen" sections and `src/engine/{hlib,geo,ecl}.h`
  and `profile.cpp` (`kDarknessEcl`, `kQueenEcl`). https://github.com/tomtombombadil/CYD-GoldBoxGameEngine.
  This is the only prior art that reads DQK. We used it as a hypothesis list. Every opcode count below was then
  read again in `DQK.EXE`.
- Gold Box Companion `formats.zip` (`Character file formats/07. The Dark Queen of Krynn.txt`,
  `Offsets compared.txt`, `Item lists/Info.txt`), from http://gbc.zorbus.net. Used for the character layout.
- Our PoR specs: `ecl-format.md`, `geo-format.md`, `dax-format.md`, `character-format.md`, `dos-graphics-survey.md`.

## 1. Install and file inventory

The disks hold `INSTALL.EXE`, a quick-start party (`SAVGAMA.QSV`, `VAULTA.DAT`), and two PKZIP self-extracting
archives (`DQKINST1.EXE`, `DQKINST2.EXE`). Both archives use the Implode method. `7z x` unpacks disk 1, and `unzip`
unpacks the v1.0 disk 2, where 7z fails. The install keeps three subfolders, `DISK1`–`DISK3`, next to `DQK.EXE`.
`START.BAT` passes the sound settings to the program as arguments `[?]`.

There are no DAX files. Every resource container is an **HLIB library** (section 2), stored in files named
`.GLB` (data) or `.TLB` (pictures).

| File | Bytes | Container / kind | Holds |
|---|---|---|---|
| `DQK.EXE` | 449,664 | MZ + RTLink/Plus overlays | the whole program (section 7) |
| `DISK3/ECL.GLB` | 456,526 | HLIB DATA, directory | 47 ECL scripts, stored raw (section 3) |
| `DISK3/GEO.GLB` | 48,114 | HLIB DATA, directory | 20 3D maps, stored raw (section 4) |
| `DISK2/MONCHA.GLB` | 14,671 | HLIB DATA, directory | 98 monster records, packed (section 6) |
| `DISK1/SOUNDS.GLB` | 24,904 | HLIB DIG4 | 13 digitised sound effects `[?]` |
| `DISK1/ITEM.DAT` | 4,318 | flat | item records `[?]` |
| `DISK1/ITEMS.DAT` | 2,048 | flat, 128 × 16 | item-type table (the same shape as PoR's ITEMS) `[?]` |
| `DISK1/GAME.FON` | 1,152 | flat | 8×8 font, 144 glyphs × 8 bytes `[?]` |
| `DISK1/TITLE.TLB`, `DISK3/FINAL.TLB` | | HLIB of HLIB | title and ending pictures and animations |
| `DISK2/PICA/PICB/PICC.TLB` | | HLIB of HLIB | encounter pictures (ids 5–116 in PICA, …) |
| `DISK2/BIGPIC.TLB` | | HLIB of HLIB | 3 big pictures (ids 240–242) |
| `DISK2/SPRIT.TLB` | | HLIB of HLIB | encounter sprites |
| `DISK2/8X8DB.TLB`, `8X8DC.TLB` | | HLIB of HLIB | wall sets (ids 1–9 and 10–17) |
| `DISK2/BACK.TLB` | | HLIB of HLIB | 3D-view backdrops |
| `DISK2/DUNGCOM.TLB`, `WILDCOM.TLB` | | HLIB of HLIB | combat ground tiles |
| `DISK2/CPIC.TLB`, `DISK3/CBODY.TLB`, `DISK2/COMSPR.TLB` | | HLIB TILE | combat icons, icon bodies, combat sprites |
| `DISK2/TOPVIEW.TLB` | 1,552 | HLIB TILE | area-map tiles |
| `DISK3/FRAME.TLB`, `GEN.TLB`, `DISK1/ALWAYS.TLB` | | HLIB TILE | screen frame, UI parts, cursor and markers |
| `*.XMI`, `*.ADV`, `INSTR.AD`, `SFXDQ.VOC` | | Miles AIL | music per device family (AD, RO, PC, TY × 3 songs), drivers, digitised effects |
| `SAVGAMA.QSV` | 7,965 | flat, not packed | saved game (quick-start party) `[?]` layout |
| `VAULTA.DAT` | 866 | flat | the vault `[?]` |

What is different from PoR's DAX:
- **No 2-byte block prefix and no per-block compression flag.** ECL and GEO entries are the raw bytes. MONCHA
  entries carry their own packed size (section 6).
- **One file per resource type.** PoR spreads scripts and maps over `ECL1..8.DAX` and picks the file by a disk
  number. DQK has one `ECL.GLB` and one `GEO.GLB`, so there is no "current disk" variable.
- **The run-length code differs by one.** DQK runs repeat `1 − c` times (as unsigned: 257 − c), not 256 − c (P,
  decoder at `DQK.EXE` 0x3B8C; section 6).
- **The 8-bit art is a separate container family.** It is not a DAX block type (section 5).

v1.0 vs v1.1 (P): all `DISK2` art and `MONCHA.GLB` are byte-identical. `ECL.GLB` is 56 bytes shorter in v1.0 and
decodes with the same opcode table. `GEO.GLB` differs in 4 bytes. `DQK.EXE` is 1,100 bytes shorter and has the same
66-entry dispatcher (bound check at file 0x3292E).

## 2. HLIB container

The format was confirmed by parsing every `.GLB` and `.TLB` file (P). It matches CYD's description (C).

```
offset  size        content (little-endian)
0       4           "HLIB"
4       u32         library size (= file size for a top-level file)
8       u16         count of entries (directory included)
10      u8          0 in every file
11      u8          1 = entry 0 is a directory
12      4           kind: "DATA", "TILE", "DIG4" or "HLIB" (a library of libraries)
16      (count+1) × u32   entry offsets from the library start; the last one = size
```

- **Directory** (entry 0 when byte 11 = 1): u16 n, then n × (u16 id, u16 entry index). The program asks for entries
  by id, the way PoR asks for a DAX block number. Ids are sparse. ECL ids run from 1 to 67, and MONCHA ids from 1 to
  119.
- **Nesting:** a picture is an entry that is itself an HLIB of kind TILE. Section 5 covers it.
- The program's library code reports problems by name in its error messages. The names include load, count, type,
  item size and decode, plus insert, delete and resize. So the same code also edits libraries, as Unlimited Adventures
  does `[?]`.
- **A text decoder that no file uses yet.** The library code has a static prefix-code text decoder: code lengths are
  3–8 bits, with an alphabet ordered by letter frequency, around `DQK.EXE` 0x1BFC6–0x1C0E1. We found no DQK data file
  that uses it `[?]`.

## 3. ECL scripts

### Where the scripts live

- `ECL.GLB`: 47 scripts with ids 1–67. Sizes range from 1,348 to 12,000 bytes, and 37 of them are over 9 KB. PoR's
  largest is 7,679.
- Each entry is loaded as it is at **VM address 0x8000** (script init `DQK.EXE` 0x402C1 sets PC = 0x8000). There is
  no prefix. Byte k is VM 0x8000 + k.
- The program's region check accepts script addresses 0x8000–0xAEE2 (P, classifier 0x40637). That gives a
  **12,003-byte script area**. The largest script is 12,000 bytes.

### Block layout and entry vectors (P)

The block opens with five vectors, `01 01 lo hi` each (bytes 0–19). The code follows at 0x8014. As in PoR, the byte
before each operand is ignored. The init routine reads the five operands and stores them in DS:B1C8, B2E8, B298, AF41
and AF43. The roles are the same as PoR's (move, search, pre-camp, camp interrupted, init):

- **Init** (vector 4): runs when a script loads (0x32DED → run loop 0x32C66 with DS:AF43). It runs again for each
  script switch.
- **Step** (0x32D14): the move vector, then the search vector.
- **Camp** (0x3285B): runs vector 2, then the camp menu, then vector 3 if the rest was interrupted.

On load, game bytes 0x2B and 0x2C are cleared (probably the rest-interrupt interval and chance, as PoR's
$6DD2/$6DD3 `[?]`). Unless a saved game is being loaded, game bytes 0x97–0xC8 (50 bytes) are cleared too. PoR clears
32 cells, $4A00–$4A1F.

### Operand encoding (P: loader 0x31D33, value fetch 0x31E32)

The type bytes are the same as PoR's: 0x00 (1 byte), 0x01, 0x02, 0x03 and 0x81 (2 bytes), and 0x80 (length + packed
text). Their meaning changed in four ways:

| Type | DQK | PoR |
|---|---|---|
| 0x00 | 8-bit immediate | same |
| 0x01 | address of a **byte** | address; width taken from the address region |
| 0x02 | 16-bit immediate | same |
| 0x03 | address of a **word** (little-endian) | same as 0x01 |
| 0x80 | inline text, 6-bit packed | same packing |
| 0x81 | address of a text in VM memory | same |

- **The type sets the width.** The VM read (0x40B57) and write (0x40992) take the type as an argument: type ≤ 1 is
  a byte, anything else is a word. A destination's width is its own operand's type. PoR took the width from the
  address region.
- **The skip can change.** A memory operand still counts as a value when an opcode reads a count, so a variadic
  count could come from memory. Every count in the shipped scripts is a type-0 immediate.
- **Text** uses PoR's packing: 4 codes per 3 bytes, MSB first. Code 0 is skipped, codes 1–31 become 0x41–0x5F, and
  codes 32–63 are kept as they are (unpacker 0x40DA2, character map 0x40614). So scripts only hold upper case. Whether
  the print path changes case on screen is `[?]`.
- **String slots** are 40 bytes apart (DS:B45F + 40·k). Slot 1 is DS:B487 and slot 2 is DS:B4AF. Text is appended
  character by character with no length check. Inline texts in the shipped scripts decode to up to 240 characters, so
  a long text runs on into the next slots `[?]`. A VM must not cut text at 40 characters.
- Use in the 47 scripts: type 0 17,017; type 1 26,955; type 2 68; type 3 419; type 0x80 8,469; type 0x81 518.

### VM memory (P: classifier 0x40637, reader 0x40B57, writer 0x40992)

| VM address | Region | Backing |
|---|---|---|
| 0x0001–0x0400 | game block, 1 KB | a far buffer (DS:B20F), byte or word per the operand type |
| 0x0401–0x0800 | the selected character, as seen by scripts | **translated**: offset = address − 0x401, see below |
| 0x8000–0xAEE2 | the script itself | the script buffer (DS:AE5C) |
| anything else | none | reads give 0, writes are dropped |

"Game byte N" in this document means VM address N. There is no counterpart to PoR's word areas ($4900, $6B00,
$9700) or its read/write redirections.

- **Special game-block cells.** The reader handles offsets 0x10, 0x11, 0x23, 0x2D, 0x32, 0x34, 0x35 and 0x3D
  itself: party x and y, facing (twice, in two encodings), a party-wide test and a map lookup at x, y `[?]`. The
  writer handles 0x11, 0x1A, 0x22, 0x34, 0x35 and 0x3A. Writing x, y or facing moves the party; the other writes
  switch indoor and outdoor, among other things `[?]`.
- **Character region.** Reads go through a translation routine (0x406CD). Offsets 0x1E–0xAA map straight onto the
  record at +0xCE (the memorised-spell area). 58 other offsets are separate cases, computed or remapped
  (0x10–0x1D, 0xAD–0xB2, 0xC8–0xCA, 0x137–0x170, 0x1EB–0x1FC; table at 0x4071A). Writes have their own routine
  (0x41E1E). So scripts see a **virtual character record** in an older layout, not DQK's C struct. CYD reads it as
  Pools of Darkness's record offsets (C).
- SAVE of a text: into the game block it copies the string with a NUL. Into 0x401 it sets the character's name
  (record + 0x68). Into anything else it does nothing (0x40D19).

### Run loop, dispatcher and IF (P)

- The run loop (0x32C66) sets PC (DS:B7F9, a VM address). It stores the opcode at DS:B1C7 (the previous one at
  DS:AE69) and calls the dispatcher. It stops on the stop flag (DS:ACBE) or on three other flags (DS:B7A7, DS:C2DC, and a key check while DS:AE59 is
  set) `[?]`.
- **Dispatcher:** at 0x32A3E. It is a bound check `opcode ≤ 0x41` and a jump table of **66 entries** at file 0x32A54
  (code segment offset 0xD34). Opcodes ≥ 0x42 hit the default case, which returns without moving the PC. As in PoR,
  the same byte then runs again forever.
- **IF** (0x16–0x1B): six flags, DS:B2DF + opcode, from DS:B2F5 ("=") to B2FA. A false IF calls the skip routine
  (0x41645). **That routine knows every opcode's real size.** It re-reads the count of the variadic opcodes (15:
  3 + n, 25/26/2B/31: 2 + n, 27: 4 + n) and has the right fixed counts for 0C, 36 and the rest. **PoR's six mis-skips
  (0C, 15, 25, 26, 2B, 36) are gone.** Opcode 00 and unknown bytes skip the opcode byte only.

### Running our PoR decoder (`ecl.py`)

We loaded the module with BASE = 0x8000 and no prefix, and fed it the GLB entries. 42 of the 47 scripts abort with
an exception. In the other five, the hard paths reach 3–64% of the bytes. The decoder then stops at opcodes it
doesn't know (0x3F–0x41 and "opcodes" read from the middle of operands). The first wrong count derails it: 0C reads
4 operands, 21 and 37 read 2, 1D, 22 and 23 read none.

### Our DQK decoder

A throwaway decoder used the counts below, followed GOTO, GOSUB and ON targets and both IF edges, and stopped at
00, 01, 13, 20 and 23. It decodes **all 47 scripts with zero errors** and reaches 434,618 of 456,124 bytes (95%).
The rest are tables and texts that operands address. The v1.0 file gives the same result. Opcodes 1E, 34 and 40
never occur.

### Opcode table

- **Ops** is the count of operands the handler loads (P, every handler). n means a variadic tail. **PoR** is PoR's
  count. **Skip** is the IF-skip count (P, 0x41645).
- **Status**: **same** = PoR's count and effect as far as read. **changed** = a different count or effect. **new** =
  not in PoR.
- Names are our own. The PoR names come from `ecl-format.md`. "(C)" marks a role we took from CYD's names without
  reading enough of the handler.
- Handler offsets are file offsets in `DQK.EXE`.

| Op | Name | Ops | PoR | Skip | Handler | Status | Notes |
|---|---|---|---|---|---|---|---|
| 00 | EXIT | 0 | 0 | 0 | 0x31ECE | same | clears the GOSUB stack; restores the selected character |
| 01 | GOTO | 1 | 1 | 1 | 0x31F5E | same | |
| 02 | GOSUB | 1 | 1 | 1 | 0x31F7B | same | |
| 03 | COMPARE | 2 | 2 | 2 | 0x31F8D | same | a text compare when either operand is text |
| 04–07 | ADD SUB DIV MUL | 3 | 3 | 3 | 0x31FE2 | same (detail) | SUB = v2 − v1 as in PoR; **DIV also writes the remainder to game byte 0x29** |
| 08 | RANDOM | 2 | 2 | 2 | 0x3208A | same (detail) | n = v + 1 unless 255, as in PoR; the generator is the C runtime's, not Turbo Pascal's `[?]` |
| 09 | SAVE | 2 | 2 | 2 | 0x320E8 | same | width from the destination's type |
| 0A | LOADCHAR | 1 | 1 | 1 | 0x32142 | same `[?]` | |
| 0B | LOADMON | 3 | 3 | 3 | 0x42C63 | same `[?]` | monster from MONCHA.GLB |
| 0C | SETUPMON | **4** | 3 | 4 | 0x43071 | changed | sprite, distance (game byte 0x38; 16 → 2), picture, 4th value to DS:C2E7 `[?]`; distance clamped by the view into game byte 0x37 |
| 0D | APPROACH | 0 | 0 | 0 | 0x4310F | same | |
| 0E | PICTURE | 1 | 1 | 1 | 0x32272 | same `[?]` | |
| 0F | INPUTNUM | 2 | 2 | 2 | 0x4313C | same `[?]` | |
| 10 | INPUTSTR | 2 | 2 | 2 | 0x43188 | same `[?]` | |
| 11 | PRINT | 1 | 1 | 1 | 0x41F93 | same `[?]` | shares a handler with 12 |
| 12 | PRINTCLR | 1 | 1 | 1 | 0x41F93 | same `[?]` | |
| 13 | RETURN | 0 | 0 | 0 | 0x32289 | same (detail) | **an empty stack runs the EXIT handler** (PoR falls through) |
| 14 | CMPAND | 4 | 4 | 4 | 0x322DE | same `[?]` | |
| 15 | VMENU | 3+n | 3+n | 3+n | 0x42151 | same (skip fixed) | |
| 16–1B | IFEQ … IFGE | 0 | 0 | 0 | 0x323C6 | same (skip fixed) | correct skip for every opcode |
| 1C | CLRMON | 0 | 0 | 0 | 0x3251E | same | |
| 1D | PRESSKEY | **0** | 1 | 0 | 0x330A3 | changed | PoR's PARTYSTR is gone; shows a fixed prompt and waits (C name) |
| 1E | CHKPARTY | 6 | 6 | 6 | 0x32532 | changed | **loads 6 operands and does nothing**; unused |
| 1F | JOURNAL | 1 | – | 1 | 0x43572 | new | prints a numbered message built with a format string, then waits (C name) |
| 20 | NEWECL | 1 | 1 | 1 | 0x4204D | same | |
| 21 | LOADFILES | **2** | 3 | 2 | 0x420AC | changed | map id → game byte 0x13, map from GEO.GLB; 2nd = wall/backdrop "look", 255 = keep |
| 22 | YESNO | **0** | 2 | 0 | 0x330E4 | changed | PoR's PSURPRISE is gone; a yes/no question that sets "=" or "≠" |
| 23 | END | **0** | 4 | 0 | 0x33122 | changed | PoR's SURPRISE is gone; clears the text box, redraws, then runs EXIT (ends the stream) |
| 24 | COMBAT | 0 | 0 | 0 | 0x3253B | same shape `[?]` | fight, or shop / vault / temple paths |
| 25 | ONGOTO | 2+n | 2+n | 2+n | 0x326B1 | same (skip fixed) | |
| 26 | ONGOSUB | 2+n | 2+n | 2+n | 0x326B1 | same (skip fixed) | |
| 27 | TREASURE | **4+n** | 8 | 4+n | 0x423E7 | changed | the count is the 4th operand (C: one coin type) |
| 28 | ROB | 3 | 3 | 3 | 0x42BB8 | same `[?]` | |
| 29 | (none) | **0** | 14 | 0 | 0x33169 | changed | **no-op** (PC + 1); PoR's ENCMENU is gone; still used 59 times |
| 2A | GETTABLE | 3 | 3 | 3 | 0x3245A | same | |
| 2B | HMENU | 2+n | 2+n | 2+n | 0x422B7 | same (skip fixed) | |
| 2C | SOUND | **1** | 6 | 1 | 0x33140 | changed | PoR's PARLEY is gone; plays sound or song v (C: songs 0x20–0x27) |
| 2D | CALL | 1 | 1 | 1 | 0x327DA | same (detail) | **small ids 1, 2, 3**, not machine addresses; others do nothing |
| 2E | DAMAGE | 5 | 5 | 5 | 0x4256C | same shape `[?]` | C: the saving-throw handling differs |
| 2F | AND | 3 | 3 | 3 | 0x323DE | same | |
| 30 | OR | 3 | 3 | 3 | 0x323DE | same | |
| 31 | WHATMENU | **2+n** | 0 | 2+n | 0x422B7 | changed | PoR's SPRITEOFF is gone; HMENU with a fixed prompt box first |
| 32 | FINDITEM | 1 | 1 | 1 | 0x431FB | same `[?]` | |
| 33 | PRINTRET | 0 | 0 | 0 | 0x32788 | same | |
| 34 | CLOCK | **2** | 1 | 2 | 0x32755 | changed | byte + word to the clock routine (unit and amount `[?]`); PoR's uninitialised-index bug is gone; unused |
| 35 | SAVETABLE | 3 | 3 | 3 | 0x324C9 | same | |
| 36 | ADDNPC | 2 | 2 | 2 | 0x4333B | same (skip fixed) | |
| 37 | FINDNPC | **2** | 3 | 2 | 0x434A8 | changed | PoR's LOADPIECES is gone; finds an NPC party member by name (text), index or 255 → dst; bit 0x80 when record byte 0x18B is 0 |
| 38 | PROGRAM | 1 | 1 | 1 | 0x328CF | same (detail) | ids 0–3 and 9 act; **1 = ending** (C); 9 = camp, then EXIT when game byte 0x24 = 1; 4–8 nothing (PoR: 0 training, 8 end, 9 camp) |
| 39 | WHO | 1 | 1 | 1 | 0x432A0 | same `[?]` | |
| 3A | DELAY | 0 | 0 | 0 | 0x32746 | same (detail) | a fixed 600 (units `[?]`), not scaled by game speed |
| 3B | SPELL | 3 | 3 | 3 | 0x428CF | `[?]` | not read |
| 3C | TREASURESET | **1** | 1 | 1 | 0x4361E | changed | PoR's PROTECT is gone; builds the treasure list from a built-in table, tier 1–9 `[?]` |
| 3D | CLEARBOX | 0 | 0 | 0 | 0x327AD | same `[?]` | |
| 3E | (select) | 0 | – | 0 | 0x329BB | new | recomputes the selected character and keeps it after EXIT `[?]` |
| 3F | HASEFFECT | 1 | – | 1 | 0x329E5 | new | "=" if the selected character has effect v, else "≠" |
| 40 | DESTROYITEMS | 1 | – | 1 | 0x433AC | new | removes items of type v & 0x7F from every member; bit 0x80 limits it to items whose byte 0x30 is set `[?]`; unused |
| 41 | XPAWARD | 2 | – | 2 | 0x429D9 | new | v2 × 100 experience; v1 = 0: the selected character, else every enabled member; split by number of classes |

In short: **66 handled opcodes (0x00–0x41) against PoR's 61 (0x00–0x3D without 0x1F).**
- **Same count and role as PoR: 48.** Several still need a handler read; they are marked "same `[?]`".
- **Changed: 13** (0C, 1D, 1E, 21, 22, 23, 27, 29, 2C, 31, 34, 37, 3C). Most keep the opcode number but have a
  different role; the rest are the same role with a new count.
- **New: 5** (1F, 3E–41). PoR has no handler for 1F and hangs on 3E and above.
- **Within the 48:** 11 differ only in the IF skip ("same (skip fixed)": 15, 16–1B, 25, 26, 2B, 36). 6 rows
  have changed details ("same (detail)": 04–07 for DIV, RANDOM, RETURN, CALL, PROGRAM, DELAY). The rest are
  unchanged as far as read.

CYD's notes say this table is Pools of Darkness's (C). Our counts agree with CYD's `kDarknessEcl` on every opcode.

## 4. Maps (GEO.GLB)

20 maps, ids 2–66. Layout checked on every entry (P):

```
offset  size     content
0       u8       width  (10–39)
1       u8       height (10–39)
2       6        header bytes, meaning [?] (values 1–0x11, 0x20 and 0xFF seen; CYD: wall-set slots)
8       w×h      plane 0  (PoR: walls N/E)
8+n     w×h      plane 1  (PoR: walls S/W)
8+2n    w×h      plane 2  (PoR: event byte)
8+3n    w×h      plane 3  (PoR: barriers / doors)
```

- **The size is per map** (n = w × h). It runs from 10×10 to 39×20. The largest is 28×28 = 784 squares. PoR's maps
  are fixed at 16×16 (1,026 bytes with the prefix).
- **The layout is planar, as in PoR.** The E face of (x, y) and the W face of (x+1, y) agree in 98% of interior edges
  in the planar reading, against about 35% in an interleaved one.
- **Bit meanings** (wall type nibbles, the event byte, barrier pairs) are assumed to follow PoR and CYD `[?]`.
- **Edges** don't wrap according to CYD (C). We did not check this in the program `[?]`.
- **Wall sets** live in `8X8DB.TLB` (ids 1–9) and `8X8DC.TLB` (ids 10–17). The 2nd LOADFILES operand selects the
  "look". How the six header bytes map to slots is `[?]`.
- **Overland.** The wilderness of Taladas is held inside a script (ECL.GLB 5) and drawn on BIGPIC pictures 240/241,
  with the script moving the party (C). It has no GEO entry. No separate wilderness map format was found.

## 5. Art (TLB)

Our dos-graphics-survey found the 8-bit TLB container. The details below were verified on all 19 TLB files (P, part
census and colour-entry sizes); the kind meanings are from CYD (C).

- **Organisation.** A top file is either an HLIB of kind HLIB (one sub-library per picture, found by id through the
  directory) or an HLIB of kind TILE (one part per icon). A picture library holds a colours entry and its parts
  (frames).
- **Part header (8 bytes):** u16 height, i16 x, i16 y (hot spot), u8 width in units, u8 kind. VGA kinds count
  4-pixel units and store mode-X planes.
- Kinds found, with counts over all files:
  - 0x10 planar: 366
  - 0x11 masked: 21
  - 0x12 RLE: 78
  - 0x15 keyed (0xFF transparent): 1,158
  - 0x17 sprite runs: 197
  - 0x18 colours: 175
  - 0x19 not decoded, `[?]`: 7
- The RLE kind 0x12 uses the same run rule as section 6 (C).
- **Colours entry (kind 0x18):** u8 flags, u8 0, u16 first, u16 count, u8 cycles, u8 0x18. Then come count × RGB
  with **8 bits per gun**, cycles × 4 bytes of colour-cycle ranges, and count EGA nibbles. The EGA nibbles are the
  16-colour version of each entry; the EGA install draws the VGA pictures through them (C). Entry sizes match this
  formula in every case we checked (P).
- Palette ranges are per library, for example:
  - combat icons: 144–255
  - wall sets: from 32
  - title: 0–255 with one cycle range
- **Fonts:** the text font is `GAME.FON`, 1,152 bytes (144 × 8) `[?]`. There is no font block inside the art files.
- PoR has 4-bit EGA blocks inside DAX with 6-bit palettes or none. DQK has 8-bit chunky mode-X art, an 8-bit RGB
  palette per picture with colour cycling, and hot spots per part.

## 6. Records

- **Character: 414 bytes (0x19E), a C struct.** The layout follows GBC's file (`Character file formats/07`); GBC
  notes it is closest to Unlimited Adventures. The size is confirmed by the monster loader, which copies 0x19E bytes
  (P, near 0x35762).
  - 0x00–0x43: run-time pointers (next record, effects, items, 13 equipped slots, combat).
  - 0x44: 32-bit experience.
  - 0x4C: a single coin type (steel).
  - 0x58: race (Krynn races). 0x5A: class.
  - 0x68: name, 15 characters + NUL.
  - 0x9B: NPC flag.
  - 0xA5–0xAB: levels of seven classes, including knight.
  - 0xCE: memorised spells, 137 bytes.
  - 0x18B: enabled. 0x19A–0x19D: knight order, god, robe.
  - PoR's record is 285 bytes, opens with a Pascal string, and has no pointers.
- **Monster:** `MONCHA.GLB`, 98 entries (ids 1–119). Each entry is a u16 **big-endian** unpacked size followed by a
  run-length stream (P, loader near 0x356FB, decoder 0x3B8C):
  - byte c < 0x80: copy c + 1 bytes;
  - c = 0x80: nothing;
  - c > 0x80: repeat the next byte 257 − c times.

  All 98 entries decode exactly to their stated size, 416–598 bytes. That is a 414-byte character record plus a
  variable tail (items and effects `[?]`). PoR uses three files per monster disk (CHA, SPC, ITM) of fixed-size
  blocks.
- **Items:** `ITEMS.DAT` = 128 × 16 bytes, the same row size as PoR's ITEMS `[?]`. The layout of `ITEM.DAT` (4,318
  bytes) is `[?]`. GBC's item list for DQK uses the common 63-byte item record (C).
- **Save:** `SAVGAMA.QSV`, 7,965 bytes, not packed (P).
  - It holds **no copy of the script**: no vector pattern occurs in it. PoR saves the whole 7,680-byte script area.
  - On load, the script is read again from `ECL.GLB`, and the init skips the 50-byte clear (P, init 0x402C1).
  - Party members go to separate files (the program builds `chrdat` + letter names and a `.pty` name) `[?]`.
  - The layout is `[?]`.
- **Vault:** `VAULTA.DAT`, 866 bytes `[?]`.

## 7. Text

- **Script text** is inline in ECL, 6-bit packed (section 3), 8,469 texts.
  - Most belong to PRINT and PRINTCLR (7,338). The rest are menu items (VMENU, HMENU, WHATMENU), WHO prompts,
    FINDNPC names, SAVE and COMPARE.
  - Scripts hold no other string table.
- **Program text** is NUL-terminated C strings in the data segment (`DQK.EXE` file 0x63A20 onward). This covers
  menus, item and spell names, combat messages, and the journal-entry and copy-protection prompts.
- The journal's paragraph text is not in the files. JOURNAL prints only an entry number (C, consistent with the
  handler's format string).

## 8. The program

- **Packaging (P).** `DQK.EXE` is an MZ file:
  - header 0x2400 bytes, 2,291 relocations;
  - the root image ends at 0x218D5;
  - then RTLink/Plus overlays (Pocket Soft; the overlay manager's messages are in the root). No EXEPACK.
- **Overlay descriptors (P).** 13 descriptors of 18 bytes at root segment 0x1D99:0x0C1F (file 0x209AF):
  - Each holds a link segment, a file paragraph, a size in paragraphs and a relocation count.
  - An overlay's file data is its relocation table (count × 4 bytes), then its code from the next 16-byte boundary.
    We use this formula `[?]` because every handler and helper decodes coherently with it.
  - Descriptor 12 is the initialised data segment (DS = file 0x63A20).
  - Code in other overlays is called through 10-byte stubs in segment 0x1D99: a call to the manager, a far jump to
    the link address, and an overlay index.
- **ECL code (P).**
  - Dispatcher, loader, value fetch and most core handlers: overlay 3, link segment 0x2033 (file 0x31D20).
  - Game-action handlers and the VM memory helpers: overlay 6, link segment 0x25A4 (file 0x402C0).
- **Compiler: C.** Prior art lists a C reconstruction of DQK by talonius (`prior-art.md` 6.2). The code looks like
  Borland C/C++ (`[?]`): far procedures called as `push cs` + near call, and a long-multiply helper that takes its
  operands in DX:AX and CX:BX. No runtime banner string was found.
- **Copy protection: a word lookup in the Adventurer's Journal.** The prompts ask for a page, a heading and a word
  number (data segment near file 0x6AC49). The headings appear to be stored obfuscated `[?]`. No disk check was
  found `[?]`.
- **Video.** The program runs only in the mode it was installed for (`dos-graphics-survey.md`).

## 9. Module-format and VM implications (what DQK needs that PoR doesn't)

1. **Opcode tables per game, with roles, not only counts.** The same opcode number changes role: 1D, 22, 23, 29,
   2C, 31, 37 and 3C. The VM needs one handler table per profile (`ecl.opset: "dqk"`). It must not assume PoR's
   names.
2. **The IF-skip table belongs to the profile.** PoR needs its six mis-skips reproduced; DQK skips correctly,
   including variadic tails read through the value fetch. RETURN on an empty stack is also per profile: PoR falls
   through, DQK exits.
3. **Variadic count position per opcode.** 2nd for 25/26/2B/31, 3rd for 15, **4th for 27**. PoR has no 4th-operand
   case.
4. **Operand width from the operand type.** In DQK, type 1 is a byte and type 3 a word. In PoR the width comes from
   the address region. Make this a profile switch in the operand fetch and store.
5. **A memory map per game.**
   - DQK: game block 0x0001–0x0400 (1 KB, bytes), a character window 0x0401–0x0800, the script at 0x8000.
   - Unmapped addresses read 0 and drop writes.
   - A small set of special cells (x, y, facing, indoor flag, …) are live engine state, not storage.
   - The module format should declare these regions and special cells per profile. Imported saves and scripts keep
     the original addresses.
6. **A per-profile "script view of a character".** DQK scripts address a virtual record (direct spell bytes plus 58
   translated fields) that differs from the stored 414-byte struct. The character model needs a mapping table per
   profile, separate from the save layout.
7. **No disk-numbered files, and sparse ids.** Scripts and maps are looked up by id in one library. `scripts/` and
   `areas/` keyed by original id already fit; drop any "disk" field for this profile.
8. **Script size up to 12,003 bytes, loaded at 0x8000 with no prefix.** Make the script base address, the prefix
   length and the size cap part of the profile (PoR: $9900, 2, 7,680).
9. **Saves without the script.** DQK reloads the script on load, so self-modifying script state is not saved. PoR
   saves it. Our save format must keep "script bytes saved" optional per profile, with the 50-byte clear rule.
10. **Maps of any size, w × h up to 39, no wrap (C).** `areas/<id>.json` needs width, height and a wrap flag. It
    also needs the 6 map-header bytes kept verbatim until they are decoded.
11. **Wilderness drawn by a script.** The overland is a big picture plus script logic. No map file holds it. The
    format must allow an area that is "script + picture" with no grid.
12. **Art:**
    - 8-bit paletted images with per-picture palettes, colour-cycle ranges and an EGA fallback table;
    - hot spots per frame;
    - transparent key 0xFF;
    - pictures as nested libraries, for frames and animations.

    PNG with an explicit palette plus JSON metadata (cycles, hot spot, EGA map) covers it. Don't flatten to RGB on
    import, or cycling is lost.
13. **Text:**
    - script text is upper-case-only 6-bit, with no length cap at 40;
    - program text is C strings in the data segment;
    - the journal text exists only on paper.

    `text/<lang>.json` must hold both, and the importer must take program strings from the EXE by offset per
    release (v1.0 and v1.1 differ in size).
14. **New effects the VM must host:**
    - YESNO and HASEFFECT set compare flags;
    - END ends the run;
    - JOURNAL prints a numbered entry;
    - SOUND takes music and effect ids;
    - XPAWARD splits experience by class count;
    - DESTROYITEMS, FINDNPC, TREASURESET;
    - DIV also writes its remainder to game byte 0x29;
    - PROGRAM ids differ (1 = ending).
15. **Records:**
    - one coin type;
    - seven classes including knight;
    - knight order, god and robe;
    - moons, kept in the game block (C);
    - a monster record that is a character record plus a variable tail.

    The shared character model needs these as optional per-profile fields.
16. **Containers:** the importer needs an HLIB reader (directory, nesting, raw DATA entries, the 257 − c run rule,
    big-endian unpacked sizes in MONCHA) alongside DAX.

## Open `[?]`

- Handlers not read past their operand count:
  - SPELL (3B);
  - the details of COMBAT, TREASURE, DAMAGE, LOADMON, PICTURE and PRINT;
  - 3E;
  - the 4th SETUPMON operand;
  - CLOCK's unit;
  - DELAY's units;
  - PROGRAM 0, 2 and 3.
- The meaning of every special game-block cell, the 58 translated character fields, and the six GEO header bytes.
- GEO edge wrap and the bit meanings, checked in the program.
- The layouts of `SAVGAMA.QSV`, `VAULTA.DAT` and `ITEM.DAT`; the MONCHA tail.
- Whether on-screen text changes the case of script text. Overflow of long texts past the 40-byte slots.
- Which files, if any, use the prefix-code text decoder in the library code.
- The compiler (Borland C assumed). The obfuscation of the copy-protection headings.
- No DOSBox-X run was made for this survey. None of the above has been checked under emulation.
