# ECL event scripts (Pool of Radiance, DOS v1.3)

Status: **bytecode layout and opcode table verified** (2026-11-17) against all 29 script blocks of PoR DOS v1.3
(`ECL1.DAX` .. `ECL8.DAX`) and against the interpreter in the game program (`GAME.OVR`, see "Program
references"). Every block disassembles along all hard control-flow paths with no decode error and no instruction
overlap (see "Verification"). Operand counts, operand encoding, string packing, the IF-skip rule, the entry vectors and
the VM address regions are confirmed in the program. The VM core (arithmetic, RANDOM, compares, tables, text, memory
redirection, script loading, camp vectors) is read from the program; many game-action opcodes are still taken from
Lee's FAQ; the column "Effect from" says which. DOSBox evidence: RAM snapshots from save states and a per-opcode
trace (method: `docs/re/dosbox-trace-method.md`; captures in `tests/fixtures-private/por/dosbox-v1.3/`, private).

Tool: `tools/scripts/ecl.py` (`list`, `dis`, `check`). C# VM built from this spec: `src/GoldBox.Core/Ecl`
(see `src/GoldBox.Core/README.md`; trace tool `goldbox-tools ecl-run`; DOSBox comparison `goldbox-tools ecl-compare`,
results under "Verification" 9). Container: `docs/re/dax-format.md`. Save area holding the
loaded script: `docs/re/save-format.md` ("ECL area"). Maps loaded by the scripts: `docs/re/geo-format.md`.

## Sources

- Stephen S. Lee, *Pool of Radiance – Exhaustive Game Information* v2.00 (2026-04-02), CC BY-NC 4.0: 12.3.1 script
  overview (script numbers, five entry addresses, $9900 load address), 12.3.2 operand types and string packing,
  12.3.3 opcodes 0x00–0x3D (names, operands, semantics, IF mis-skip list), 12.4.1 flag list, 12.4.2 other VM addresses.
  Facts are restated here in our own words; per-flag meanings are not copied.
  https://gamefaqs.gamespot.com/pc/564785-pool-of-radiance/faqs/73869
- PoR v1.3 `GAME.OVR` and `START.EXE` (Turbo Pascal; `START.EXE` is EXEPACK-packed, unpacked with the routine in
  `gbitem.py`). Read with a plain 8086 disassembler. Only file offsets are cited here, no listings.
- Prior-art disassemblers and VMs listed in `docs/re/prior-art.md` (coab `ovr003.cs`/`ovr008.cs`, DrEvil
  `decode/ecl/ECL_VM.md`, talonius `eclvm.pas`) were **not** used for this spec; it was built from Lee plus the program.
  They remain useful cross-checks.

Mnemonics in this document and in `ecl.py` are our own short names. Lee's names come from the debug mode of *Curse of
the Azure Bonds*; they are given in the table for cross-reference.

## Where the scripts live

- One script per area, stored as one DAX block in `ECL<disk>.DAX`. The block id is the script id (Lee: "script
  number"); it is also the map id the area normally uses (`GEO<disk>.DAX` block of the same id), but a script can load
  other maps (table under "Scripts and areas").
- Block ids are unique over the eight files: 0–29 except 12 (29 blocks). Raw sizes 2,171–7,679 bytes.
- The disk digit of the file comes from a game variable, not from the script id: Lee and `save-format.md` give VM
  $6E12 as the current disk. Scripts set it before switching to a script on another disk (see "Verification" 6).
  The program builds the file names `ECL<d>.DAX` (script loader 0x7814), `GEO<d>.DAX` (map loader 0x2F9BE) and
  `WALLDEF<d>.DAX` (wall-set loader 0x2F6FF) from the disk number byte at DS:5376. **VM $6E12 is that byte:** the
  read redirection (0x7C8F, case 0x312) returns DS:5376, and a write to $6E12 stores the cell and then calls
  START.EXE 0x896A, which copies DS:5376 to DS:5377 (previous disk) and sets DS:5376 to the low byte written
  (writer side effects 0x7F47, case 0x312). Program read 2026-11-17.

## Block layout

```
block offset  VM address     content
0             –              u16 = 0x1388 in all 29 blocks; not loaded (dropped by the loader)
2             $9900          entry vector 0 ("move")      1 byte (0x01 in all blocks, ignored) + operand
6             $9904          entry vector 1 ("search")    same
10            $9908          entry vector 2 ("pre-camp")  same
14            $990C          entry vector 3 ("camp interrupted")
18            $9910          entry vector 4 ("init")
22            $9914 ...      code and inline data, up to $B6FF (7680 bytes from $9900)
```

- **Address base:** block byte *k* (k ≥ 2) is VM address `$9900 + k − 2`. The interpreter reads the script byte at
  VM address A from `script_buffer + (A + 0x6700) mod 0x10000`, i.e. offset `A − $9900`. The save game holds the
  same 7680-byte area (`save-format.md`), and both shipped saves contain block 20 from offset 2 exactly.
- **Vectors:** the script loader (GAME.OVR 0x7689) sets the program counter to $9900 and then five times reads one
  operand starting one byte further on (the byte in front of each operand is never looked at), storing the operand's
  16-bit value into DS:49E3, 49E5, 49E7, 49E9, 49EB. Every block has `01 01 lo hi` there, which also happens to
  read as `GOTO` instructions. In all 29 blocks every vector is operand type 1 and points into the block.
- **Vector roles.** Names follow Lee's list (move, search location, pre-camp check, camp interrupted, initial entry).
  Program evidence (GAME.OVR offsets):
  - vector 4 runs when a script is loaded (routine 0x403E, run at 0x40A8);
  - the area loop (0x40DB–0x424D) reads a command (0x17054). Key "E" runs 0x3B19 (camp). If VM $6DCA (search/look
    mode word) is > 1, any other key runs vector 1 alone with $6DCA = 1 for the run (0x4151). Otherwise it runs
    vector 0 (0x4199), then (unless the script changed or the party is defeated) stores x, y in $49F0/$49F1, takes
    the step (0x17438) and runs vector 1 on the new square (0x4228). Confirmed by the DOSBox-X per-opcode trace
    (2026-11-17, one step west from save A): vector 0 ran to its EXIT, then vector 1 from its start. A turn key
    (Left/Right) ran no script at all (RAM snapshot: PC unchanged, only the facing changed). A step into an
    impassable face still runs vector 0, the $49F0/$49F1 write and vector 1, on the same square (DOSBox-X, s10:
    test vectors that count their runs, captures `oneway_blocked`, `oneway_barrier_blocked`);
  - after any run that switched scripts (NEWECL), routine 0x3EFA runs the new script's vector 4, then vector 0 and,
    unless the script changed again, vector 1, and repeats while scripts keep changing. Each pass first re-reads the
    event byte DS:6AB1 ($C04F) from the map in memory and sets **$6DD5 = 0** (0x3F00–0x3F16), and after vector 4 the
    view redraw (0x3FF5 → 0x2DCF4) re-reads it again, now from the map vector 4 loaded. DOSBox: `edge_exit_east`
    (s10). The script-changed flag is not tested after vector 4 (it is cleared at 0x3FFA before vector 0), so a
    NEWECL inside a vector 4 is followed by the new script's vectors 0 and 1, not its vector 4; the VM does the same
    since s11. One v1.3 vector 4 can do it (ECL5 block 7; "Steps, doors and script switches");
  - routine 0x3B19 (camp) runs vector 2, then the camp menu (0x196A0), which clears a "rest interrupted" flag and
    passes it to Rest (0x17FAB) and Memorise (0x18C22). Rest runs the rest loop 0x24A66, which returns the flag:
    each rest tick it increments a counter (DS:49FB, not a VM cell); when VM $6DD2 is non-zero and the counter reaches
    $6DD2, the counter is reset and a 1d100 roll (dice routine 0x2BCCD) ≤ $6DD3 interrupts the rest (flag = 1,
    0x24C03). One tick = 5 game minutes (clock routine 0x241A1 called with 5 minutes, 0x24B80). The counter is
    reset only when it reaches $6DD2 and at start-up (0xE980), not when a rest starts, so it carries over between
    rests. After the menu is left, 0x3B19 runs vector 3 only if the flag is set, then redraws (0x2DCF4). So vector
    2 = "camp entered", vector 3 = "rest interrupted by an encounter"; scripts set $6DD2/$6DD3 (interval, percent)
    in vector 2. The main loop calls 0x3B19 on key "E" (0x412C); PROGRAM 9 also calls it (0x3B56, saving and
    restoring the PC, and then executes EXIT, 0x3BBC: the script run ends). Confirmed by a DOSBox-X RAM
    snapshot (2026-11-17): encamp ran vector 2 only; vector 3 ran when an 8-hour rest was interrupted.
  - The script init 0x7689 sets $6DD2 = $6DD3 = 0, so rest is never interrupted unless the script sets them.
- **On load** (script init 0x7689): PC = $9900, the GOSUB stack is cleared, the six compare flags are cleared,
  $6DE1 = 255, $6DD2 = $6DD3 = 0, $49E5 = 0, and after reading the vectors $49E6 = 1 (display bytes too: DS:84E0,
  84E2, 84D2, 84D3 and 84C8/84C9 cleared, 0x768F–0x76BE; DS:84E1 is not touched). Unless DS:49F8 is set, VM
  $4A00–$4A1F (32 cells) and $6E79–$6E82 (10 cells) are zeroed. DS:49F8 is set only by the "Load game" command of
  the main menu (0x19D91, after a successful load); the area loop 0x403E then also skips reading the script file
  (the script comes from the save), and 0x7689 clears DS:49F8 again (0x77EC). Also cleared at start-up (0xE979).
  DOSBox-X check (2026-11-17): a save whose stored script had vector 0 changed was loaded, and DS:49E3 held the
  changed vector, so the script really comes from the save. After a load the trace shows vector 4 running (PC
  $9A5E to its EXIT in ECL2 block 20) and $4A00 (255 in the save) kept.
- **No end marker.** Code and data tables are interleaved; the block simply ends. Tables are found by the script
  operands that address them (GETTABLE, SAVETABLE, COMPARE on a script byte, …).

## Operand encoding

Every operand starts with a **type byte**. The operand loader (GAME.OVR 0x74D3) reads `n` operands for an opcode
(count fixed per opcode, below), filling per-operand arrays (DS:7006 type, DS:7086 low byte, DS:7046 high byte,
DS:70C8 + 256·k string slots; index from 1), and leaves the program counter on the next opcode.

| Type | Bytes after the type byte | Meaning (as values are fetched, GAME.OVR 0x7609) |
|---|---|---|
| 0x00 | 1: `v` | 8-bit immediate |
| 0x01 | 2: `lo hi` | VM address; value = contents of that address |
| 0x02 | 2: `lo hi` | 16-bit immediate |
| 0x03 | 2: `lo hi` | VM address; value = contents of that address |
| 0x80 | 1 + n: `n`, then n packed bytes | Inline text (below). Fetched as a *value* it is treated like type 1: VM read at `n \| stale_hi << 8` (0x764D; never done in v1.3) |
| 0x81 | 2: `lo hi` | VM address of a text (Lee: a C string in VM memory); as a *value* it is the 16-bit field itself |
| other | 1 | Loader accepts it and stores the byte as the low byte. The value fetch has no case for it and returns its own uninitialised local (stack residue, undefined). Never used by the scripts |

- **Types 1 and 3 are fetched the same way.** Lee calls 1 "address of an 8-bit value" and 3 "address of a 16-bit
  value"; in the program, the read width comes from the address region (next section), not from the type. Usage:
  type 1 on 13,099 operands, type 3 on 75.
- **Destinations ignore the type.** Opcodes that store a result (e.g. SAVE, GETTABLE, ADD) take the raw 16-bit
  field `lo | hi << 8` of the destination operand as the address, whatever its type (checked for SAVE).
- **Jump targets ignore the type.** GOTO, GOSUB and the ON targets use the raw field `lo | hi << 8` as the new
  program counter. With a type-0 operand only `lo` is written by the loader and `hi` is whatever the previous
  instruction left in that slot, so the target is undefined. No reachable instruction in v1.3 does this.
- **Text packing (type 0x80):** `n` bytes hold ⌊8n/6⌋ 6-bit codes, most significant bit first (3 bytes → 4 codes; a
  trailing partial group yields as many whole codes as it has bits for). Code 0 produces nothing; codes 1–31 map to
  characters 0x41–0x5F (code + 64: `A`..`Z`, `[`, `\`, `]`, `^`, `_`); codes 32–63 are ASCII 0x20–0x3F as is.
  Unpacker GAME.OVR 0x8626, character map 0x7BA6. So scripts only carry upper case, digits and punctuation.
  The decoded text goes to a 255-byte string slot. Lee 12.3.2 describes the same scheme.
- **String slots count text operands only** (loader 0x74D3): the k-th operand of type 0x80 or 0x81 *in one
  loader call* (for a fixed-count opcode: in the instruction) goes to slot k (DS:6FC8 + 256·k), whatever its
  operand position. A variadic tail is a second call and starts again at slot 1. A type-0x80 text of length 0 gives an
  empty slot. Type 0x81 reads the text from VM memory (0x87CE): one character per cell (low byte of each word cell, or each
  byte in the script area) until a cell that is 0; the address $6B00 instead gives the current character's name.

## Instructions and control flow

An instruction is one opcode byte plus its operands. The run loop (GAME.OVR 0x3EB7) sets the program counter (PC,
DS:49ED) to a vector, then repeatedly stores the opcode at PC (DS:6F7F; previous opcode DS:6F7E) and calls the
dispatcher (GAME.OVR 0x3C71, a compare chain over 0x00–0x3D), until a stop flag (DS:442E) is set or DS:49FF is
non-zero. DS:49FF (a DS byte, not VM $49FF) means "party defeated": it is set to 1 and cleared again if any party
member is still in play, by DAMAGE (0x3728–0x3740) and by the after-combat check 0x5A47 (cleared at 0x5AAC/0x5B2D
when a player character on the party side is still able to act, or when DS:84D4 is set). When set, it also ends the
area loop 0x403E (checks at 0x4118–0x423B), which clears it on exit (0x4245); also cleared at start-up (0xE9FC).

- **Fixed operand counts.** Each handler loads its operands first with a constant count (column "Ops" below,
  read from every handler in the dispatch chain).
- **Variadic opcodes** VMENU (15), ONGOTO/ONGOSUB (25/26) and HMENU (2B): the handler loads the fixed part, takes
  the count from the last fixed operand **through the value fetch** (so a memory operand would be dereferenced), steps
  the PC back one byte and loads that many more operands. In v1.3 every such count is a type-0 immediate, so the length
  is static (`ecl.py check` fails on any other type).
  - All four keep only the **low byte** of the count (ON 0x2663, VMENU 0x1C9D, HMENU 0x1E46).
  - The second load is a fresh call of the loader, so the tail fills **operand slots 1..n** and, since the loader
    starts its text counter at 0 on every call (0x74D3), **string slots from 1**. Before it, each handler takes what
    it needs from the fixed part: ON its selector (0x2650), VMENU the destination and a copy of the prompt (slot 1,
    0x1C70–0x1C90), HMENU the destination (0x1E36). Afterwards the fixed operands are gone from the slots.
  - Each operand array has 64 entries; past 64 they run into the next array (type of operand 65 = high byte of
    operand 1, and so on). No v1.3 instruction loads more than 64 operands; the VM keeps separate arrays and does not
    model that overlap (only the reads of "operand 0" that CLOCK makes).
- **IF.** The six IF opcodes (16–1B) take no operands. Each tests one of six flags (DS:6F78..6F7D for =, ≠, <, >,
  ≤, ≥). True: execution continues with the next instruction. False: the next instruction is skipped using a separate
  table of operand counts (column "Skip" below; routine GAME.OVR 0x9379). For six opcodes that table is short:
  0C (2 instead of 3), 36 (1 instead of 2), and 15, 25, 26, 2B (0, i.e. the opcode byte only). Skipping one of those
  resumes inside its operands. Lee lists exactly these six as "not properly skipped". **No IF in the v1.3 scripts is
  followed by one of them** (`ecl.py check`), so the behaviour never fires in the shipped game, but a VM must
  reproduce it to run modified scripts the same way.
- IF followed by IF is legal and common: skipping an IF skips exactly its opcode byte.
- **The skipped opcode becomes the "previous opcode"** (DOSBox, 2026-11-17): after a save load, vector 4 of ECL2
  block 20 ends `IFNE` (false), `GOTO` (skipped), `EXIT`; the state captured after it holds previous opcode
  DS:6F7E = 01 (the skipped GOTO), not 17 (the IF). So the IF-skip leaves the skipped instruction's opcode in
  DS:6F7F, and the run loop copies it to DS:6F7E at the next instruction. Seen in both save-load captures; no
  script reads these cells, so it matters only for state comparisons.
- **COMPARE** stores all six flags from its two values as unsigned 16-bit numbers, comparing operand 1 against
  operand 2 (`IFLT` is true when operand 1 < operand 2) (flag setter GAME.OVR 0x8DBE). If **either** operand is
  text (type ≥ 0x80), string slot 1 is compared with string slot 2 instead (handler 0xE6C, routine 0x8CE8, using the
  Turbo Pascal string compare at START.EXE 0xBB90): byte by byte as unsigned values over the shorter length, then
  the shorter string is less; **case-sensitive**, no trimming. All six flags are set from that order. With one text
  and one number operand, slot 2 holds whatever the last text instruction left there (never done in v1.3: every
  text COMPARE has two text operands). INPUTSTR upper-cases what the player types, so compares against script text
  work.
- **CMPAND** (0x1833) clears all six flags, then sets "=" if v1 = v2 **and** v3 = v4 (16-bit), else sets "≠". The
  other four flags stay 0.
- **AND / OR** (0x1B35): result = low byte of (v1 AND/OR v2); flags are set by the flag setter from (0, result), so
  "=" means result = 0, "≠" result ≠ 0, "<" result ≠ 0, ">" never, "≤" always, "≥" result = 0. The byte result is
  then written to a.
- **FINDITEM** (0x33B0) clears all flags and sets "≠"; if any item of any party member has item-record byte 0x2E (in
  memory; presumably the item type `[?]`) equal to the low byte of v, it sets "=" and clears "≠". The other four flags stay 0.
- **GOSUB / RETURN.** GOSUB pushes the PC (already on the next instruction) onto a linked-list stack (DS:6F70; no
  depth limit) and jumps. RETURN steps the PC past its opcode, then, if the stack is non-empty, pops the PC. **RETURN
  with an empty stack falls through** to the next instruction.
- **EXIT** clears the GOSUB stack, sets the stop flag and ends the run (plus some display bookkeeping).
- **NEWECL** loads the new script, runs no further instruction of the old one (sets the stop flag and a "script
  changed" flag DS:442F that makes the caller run the new script's vector 4 and then its move/search vectors again).
  Bytes after a NEWECL are therefore not reached by fall-through; v1.3 has inline tables right after NEWECL.
- **ON GOTO / ON GOSUB:** if the selector value is < count, jump (or call) to the selector-th target; otherwise
  fall through. Selector and count are the low bytes of their values (0x263E); targets k = 0..count−1 are compared
  with the selector in turn.
- **Opcode 0x1F and ≥ 0x3E hang the game.** The dispatcher has no case for them and returns without advancing the PC.
  The run loop 0x3EB7 then executes the same byte again, and it only leaves when the stop flag DS:442E or DS:49FF
  is set, which only the EXIT and NEWECL handlers (0xDE1, 0x19A0) and DAMAGE / the combat check do. Nothing polls
  the keyboard in between, so the game freezes. DOSBox-X trace (2026-11-17, patched save D, vector 0 pointed at a 0x1F
  byte): after one step key the trace repeated `A200 1F` without end (over a million lines), and later keys had no
  effect. None is reachable in v1.3.

### Arithmetic, RANDOM and tables (program read 2026-11-17)

- **ADD/SUB/DIV/MUL** (one handler, 0xEE9): v1 and v2 are 16-bit values. ADD = v1 + v2, SUB = **v2 − v1**, DIV =
  v1 / v2 (unsigned, quotient only), MUL = low 16 bits of v1 · v2; ADD/SUB wrap at 16 bits. The 16-bit result goes
  through the VM writer, so a destination in the script area keeps the low byte. **DIV by 0** executes an 8086 `div`
  by zero (CPU interrupt 0; with Turbo Pascal's default handler that is run-time error 200, handler not traced
  `[?]`). No v1.3 script divides by an immediate 0; computed divisors not checked.
- **RANDOM** (0xF7A): n = low byte of v; if n < 255 then n = n + 1; result = Random(n) (Turbo Pascal System.Random,
  START.EXE 0xC140), i.e. **0..v inclusive for v < 255, 0..254 for v = 255**; stored as a byte value. Random(n) =
  (high word of the new seed) mod n, seed = seed · 0x08088405 + 1 (32-bit), RandSeed at DS:4428 (low) / DS:442A
  (high) (START.EXE 0xC18F). Randomize (seed from the DOS time, CX:DX of INT 21h/2Ch) is called once, at GAME.OVR
  0xEBD5. Dice: routine 0x2BCCD rolls count × (Random(sides) + 1). **DOSBox check** (2026-11-17, `ecl-compare`):
  starting from the RandSeed in a capture, this generator reproduces the RANDOM results and the RandSeed after the
  run in every state capture with rolls (step, edge wrap, door pick, door bash, interrupted rest).
- **GETTABLE** (0x1BA4): value = VM read at (a1 + low byte of v2) (16-bit add), written to a3. The read width is
  that of the region, so in a word area the index steps whole cells.
- **SAVETABLE** (0x1C02): VM write of v1 at (a2 + v3) — here the index v3 is the **full 16-bit** value.
- **SAVE of text** (0x8426): copies string slot 1 to address a, one character per cell (word cell in $4900/$6B00/
  $9700, byte in the script area), then a 0 cell after it. SAVE to $6B00 sets the current character's name
  (at most 15 characters) instead. $6B00-area text writes go straight to the cells (no character-record side
  effects); other addresses: nothing. A 0-length text would fill a+1..a+255 with leftover stack bytes and put the 0 at a
  (byte counter underflow); v1.3 never SAVEs an empty text.

### Other handlers read in the program (2026-11-17)

- **LOADFILES (0x21) / LOADPIECES (0x37)** (shared handler 0x19BA, three byte operands o1 o2 o3):
  - LOADFILES: if o1 is not 255 or 127 and VM $49E6 ≠ 0 (3D view; the script init sets it to 1), $49C5 = o1, the
    map `GEO<d>.DAX` block o1 is loaded (0x2F9BE) and $6DC9 = 0. If o3 ≠ 255 and $49E6 = 0 (outdoors), a fixed
    picture is loaded from `BACPAC.DAX` (0x31A8E, constant arguments; the operand values are not passed). o2 is
    not used. So ids 4/5/6/127/255 load a GEO block only when $49E6 ≠ 0 at that point; the wilderness scripts
    write $49E6 themselves (0 and 1 in ECL6 block 25 and ECL7 block 26).
  - LOADPIECES: o1 = 127 loads wall set 0 into slot 1 only. Else, if $49E7 and $49E8 are both non-zero, o1 goes to
    slot 1 and o3 to slot 3 (o2 skipped). Else for each slot k = 1..3: o_k ≠ 255 loads `WALLDEF<d>.DAX` set o_k
    into slot k (0x2F6FF), o_k = 255 empties the slot.
  - The view is redrawn once a LOADFILES and a LOADPIECES have both run since the area was entered (DS:4433/4434).
- **NEWECL** (0x1917): $49F2 = current script id, then loads `ECL<d>.DAX` block v (0x7814; retries until the block
  reads) and runs the script init 0x7689 at once.
- **PARTYSTR** (0x200C): sum over party members of (HP + 5·max(AC byte − 60, 0) + 5·max(THAC0 byte − 39, 0)
  + 8·magic-user level + 4·cleric level) / 10 (integer, per member), using record bytes 0x11B, 0x111, 0x110, 0x9B,
  0x96 (`character-format.md`); the byte total is written to a.
- **SURPRISE** (0x240F), values b1..b4 (low bytes): X = b4 + 2 − b1, Y = b2 + 2 − b3 (signed bytes); r1, r2 = 1d6
  each. Result = 2 if r2 ≤ Y; else 1 if r1 ≤ X; else 0. Written to $6DCB. (The code stores 3 when both hold but
  overwrites it with 2, so 3 never occurs.) Which side each value describes is Lee's reading `[?]`. Roll order
  r1 then r2: confirmed by DOSBox (interrupted rest: from the captured seed, r1 = 1 and r2 = 3 give the captured
  $6DCB = 1; the other order would give 2).
- **CLOCK** (0x3807) loads its one operand, then fetches the amount with an uninitialised local byte as the operand
  index, and adds that many minutes to the clock $49C6–$49CC (0x241A1, unit 1 = minutes, the same routine rest uses).
  - The local is the high byte of the stack word just below the handler's frame, i.e. whatever the previous
    handler left there. GOSUB (0xE57) leaves the word it passed to the GOSUB push: AL = 1 and AH = 0 (the operand
    loader returns with AH = 0 after a type-1 operand). So **after GOSUB the index is 0**. Other handlers leave other
    values `[?]` (an IF that skips leaves part of a far return address).
  - The fetch reads operand k at type DS:7005+k, low byte DS:7085+k, high byte DS:7045+k. **Index 0** therefore reads
    type DS:7005, which nothing writes (0), and low byte DS:7085, which is the high byte of operand slot 64. That
    slot is written only by an instruction that loads 64 operands (none in v1.3), so the amount is 0.
  - The one v1.3 use (ECL7 block 17 $9D37, the Nomad Camp) is reached only by GOSUB: **it adds 0 minutes**.
  - DOSBox-X check (2026-11-17, patched save D): CLOCK right after GOSUB added 0 minutes; after an ON GOTO with 64
    targets whose last one had high byte 5, the same CLOCK added 5 (10:50 → 10:55); DS:7005 = 0, DS:7085 = 5.
- **PRINT / PRINTCLR of a number** (handler 0x174F): when the operand type is below 0x80, the value is fetched and
  converted with Turbo Pascal's `Str` (START.EXE 0x1C13: the 16-bit value as an unsigned number, decimal, no
  padding) **into string slot 1**, which is then printed as a text would be. So `PRINTCLR [$6E79]` (ECL1 block 18
  $A0AB) prints the cell's value as digits, and slot 1 keeps the digits afterwards. DOSBox-X check (patched save D):
  PRINTCLR 12345 then PRINT [x] with x = 5 showed `123455`, and slot 1 still held the digits' tail afterwards.
  How the text box shows a text: `view3d.md` "Text window" (program read s12: START 0x77F8).
- **Page prompt under typed text** (P s45, D s46). A text longer than the box's six rows stops at the text routine's
  page prompt (START 0x7A12 → the key wait 0x7C85: row 24 blanked, the prompt in colour 13, one key); the wait does
  not blank the row afterwards, so the prompt stays on row 24 while the next page is typed (the row keeps the command
  line while the first page types) until a menu, a message or another key line draws there. DOSBox-X s46 (scratch
  copy, save A with vector 0 = `PRINT` of a 249-character text, then `HMENU "WAIT"`): the first page, the prompt, a
  key, "LONG TEST TEXT FOR …" typed from row 17 with the prompt still on row 24, then "WAIT" over it. A decoded text
  past 255 characters ran on with stray characters and the script never reached the menu (a string[255] limit `[?]`,
  not followed). Client (`ClientPlayer.PagePromptStands`, s45): the first page with its prompt pixel-equal, the prompt
  standing on row 24 while the next page types (as in DOSBox-X), and "WAIT" over it equal but the help line; the
  typing pace was not compared (the two captures were not timed against each other).
- **INPUTNUM / INPUTSTR** (0x1697 / 0x16DA): the length operand is loaded but never fetched.
- **The ECL line inputs** (P + D s43). INPUTSTR calls START 0x7A73 (0709:09E3) with an empty prompt (the local string
  at 0x16E9), colour 10 and 40 characters; INPUTNUM calls START 0x7BF6 (0709:0B66) with an empty prompt and colour 10,
  which runs the same 0x7A73 with 6 characters (0x7C1F) and converts the line with Val. 0x7A73 blanks row 24 (0x767B),
  draws the prompt at (0, 24) in the colour, then for each key 0x20–0x7A while below the limit draws it in colour 15
  at the next column, the first at the prompt's length + 1 (0x7B0E); Backspace blanks the last; Return or Escape end
  it and blank row 24. No cursor glyph. So both ECL inputs type from column 1 (DOSBox-X: scratch saves, "ABC" and
  "12" from column 1, the text box left as it was). Client: `ClientPlayer.InputNumber` / `InputString` (empty
  prompts), `ExploreScreen.DrawTypedLine` (the typed text at the prompt's length + 1; the "_" after it and the
  character grid in the box are the Client's).
- **VMENU's screen** (P + D s43; handler 0x1C4E). The cursor to (1, 17) (0x1CCE), the prompt through START 0x77F8 in
  (1, 17)–(38, 22) with clear, colour 10 (0x1CD8–0x1CF0: wrapped as every text there, typed only while DS:4A00
  stands), the choices copied into a list, then 0x8C5B (002B:0066) → the list routine 0x2D669 with the window from row
  DS:5E25 + 1 to row 22, columns 1–38, colours 13 / 10 / 15, keypad flag 1, empty prompt and word, no Exit, position 0
  (0x1D51); after the pick the box is blanked (0709:0DD3 = START 0x7E63, 0x1DC2) and the cursor stays after the
  prompt. DOSBox-X (scratch saves D and F): the prompt in green at (1, 17), the rows from (1, 18), the first white and
  the others green, nothing on row 24; a PRINT after the menu went on at column 12 of row 17, after an 11-character
  prompt. The longest v1.3 VMENU needs 5 rows with its prompt, so no v1.3 script pages it, but the routine does
  (**P + D s44**): 0x8C5B calls 0x2D669 once (no loop of its own) and VMENU stores the **position** it leaves through
  002B:0052 (0x1DA6), whatever the key. 0x2D669 (combat-menu.md "Spell lists' Next / Prev") with R = 22 − DS:5E25 rows
  and no headings: the top 0 on entry (position 0; the word is the list routine's global DS:6B29, so a VMENU leaves
  it 0, or where its Next / Prev left it, for the next list: DOSBox-X s45, por-art-formats.md "Creation's list
  labels"), the cell DS:6B2B := 1; row 24 is the menu line, empty word +
  " Next" while N − R > top + " Prev" while top > 0 (no Exit), so the words from column 1, the lit one white, the
  other's capital white and the rest green; nothing when the list fits. Inside the routine's own loop: Home / End (keypad
  7 / 1) move the highlight inside the window, wrapping there; N / P (hotkeys only while shown) and PgDn / PgUp (only
  while the word shows) move the window by R, at most to N − R (the last window is full and overlaps), at least 0, the
  highlight keeping its row in the window; ',' / '.' move the lit word; other letters (E too: no Exit) do nothing.
  Return, space and **Escape** end it: Escape makes 0x2D669 return no entry (0x2D937) but leaves the position, so
  VMENU stores the highlighted entry as for Return. DOSBox-X (scratch saves, nine entries under a one-row prompt: rows
  18–22, "NEXT" at (1, 24); End × 5 back on the first; End, N → ECHO–INDIA with FOXTROT lit, "PREV"; A and E nothing;
  PgUp → ALPHA–ECHO, BRAVO lit; Escape → PRINT showed 1. Seven entries under a two-row prompt: rows 19–22; End, PgDn
  → DELTA–GOLF, ECHO lit; Return → 4). Client: `ClientPlayer.VerticalMenu` (`ChoiceRequest.ListRow`, `Paging` with
  the routine's words as the spell lists'), `ExploreScreen.ListAnswer` / `DrawVertical` (the window math shared with
  the list pages, `ListWindowSteps`; Up / Down as Home / End and the pad's shoulders paging, as the spell lists, user
  s30; a click on a row picks it, on Next / Prev pages, a right click is Escape: ours). The Client's first-letter
  hotkeys on the rows are gone (the routine has none). Tests `VmenuPagingWhoTests`. **Amiga** (P + FS-UAE s45):
  VMENU's handler hunk 16 +0x2AE2 prints the prompt in (1, 17)–(38, 22) in pen 28 (hunk 16 +0x2B84–0x2B9E), copies
  the choices and calls hunk 4 +0x1A4C (the copy of 0x8C5B) → the list routine hunk 13 +0x2B76 with pens 26 / 28 / 16,
  the window from the cursor row + 1 (hunk 32 +0x2D0) to row 22, columns 1–38, no Exit; then it stores the position
  (hunk 16 +0x2C3E) whatever the key, as DOS. The list routine's keys (hunk 13 +0x2E88–0x2F44): keypad 8 / 2 move the
  highlight (wrapping in the window; DOS keypad 7 / 1), keypad 9 / 3 page while Prev / Next show (DOS PgUp / PgDn),
  N / P page, Escape (and E only as an Exit word's hotkey) returns no entry, a click on a row lights it and a second
  click picks it. FS-UAE (disk A's save A, a step's VMENU as the DOS tests): nine entries under "PICK ONE:", rows
  18–22, "NEXT" from column 1 (N in pen 16, the rest 28), the lit row the list routine's bar; keypad 2 × 4 → ECHO,
  once more back on ALPHA, keypad 8 → ECHO; keypad 3 → ECHO–INDIA with INDIA lit, "PREV"; keypad 9 back; A and E
  nothing; P → ALPHA–ECHO with BRAVO lit; Escape → the PRINT showed 1. Seven entries under the two-row prompt: rows
  19–22; keypad 2, keypad 3 → DELTA–GOLF, ECHO lit. WHO: the box blanked, row 24 "WHO GOES:" and Select, the member
  pick with keypad 1 (next member; the first two presses → the third member lit, as DOS's End), keypad 2 and E
  nothing, Return ends it with that member kept lit. Client `--art amiga` (DOS test saves alike): rows 16–23 equal
  pixel for pixel in every state compared once the lit row is drawn as the bar (`Chrome.Lit`, s45; it was white text),
  row 24 equal but the Client's pressed gadget on the current word.

### Encounters (program read 2026-11-17)

Handlers (GAME.OVR): SETUPMON 0x10FA, LOADMON 0x1196, APPROACH 0x155D, PICTURE 0x159C, CLRMON 0x1FA3,
COMBAT 0x24E1, TREASURE 0x26F6, ENCMENU 0x2C3E, PARLEY 0x3311, SPRITEOFF 0x37E8. Code: `src/GoldBox.Core/Game`
(`EncounterHost`, `EncounterRules`). VM cells touched: $6DC0, $6DC1, $6DCA, $6DE2, $6E6C and the ENCMENU / PARLEY
destination. Everything else below lives in the data segment (DS), not in VM memory.

**View distance** (routine 0x7955, called with the party's x, y, facing). Outdoors ($49E6 = 0) it returns 2 and also
stores $6DC1 = 2 itself. Indoors it counts squares in the facing direction, at most 2: while the face of the square
in that direction has wall type 0 (planes 0/1, accessor 0x2ED72), count one and move one square on. Any drawn wall
stops it, also a door or an open door; the barrier plane is not read. Off the map the wall accessor wraps
(`geo-format.md` "Edges").

**SETUPMON v1 v2 v3** (0x10FA): DS:6F81 = low byte of v1 (the sprite, a `SPRIT<d>.DAX` block), $6DC0 = v2 (16 bits),
DS:6F82 = low byte of v3 (the close-up), $6DC1 = min($6DC0, view distance), then the encounter is drawn (0x7A21:
the sprite at distance 1–2; at distance 0 the close-up, from `PIC` when $6DE1 = 255, else head $6DE1 + body).
The two cells are stored straight into the area, not through the VM writer. DOSBox: `encmenu_clamp` (ENCMENU uses
the same start) holds $6DC0 = 2, $6DC1 = 1 with a wall one square ahead.

The sprite renderer takes a one-based frame slot. The 3D call at 0x7AA8 passes distance + 1. Other view modes
pass slot 2 at 0x7AB7. The renderer at 0x2E7D6 looks up that slot in the loaded frame table. The Client uses the
last available frame if the chosen art has fewer frames than the requested slot (s31).

**The encounter draw 0x7A21** (P s39; args: a far pointer to DS:84C8, distance, close-up, sprite; callers SETUPMON
0x1188, APPROACH 0x158C, ENCMENU 0x2D88 and its approaches 0x3033 / 0x30A7 / 0x31B4 / 0x323B / 0x32A8, all with
that pointer). DS:84C8 = the sprite is loaded and up, DS:84C9 = a close-up is up:
1. 84C9 = 0 and 84C8 set: the view is redrawn under the old sprite (0x7A3A → 0x2DCF4; on the overland the marker
   too, DS:6DDC + 1).
2. 84C9 = 0: if 84C8 = 0, the area map is turned off with a redraw when it is shown (DS:6AAC, 0x7A5B–0x7A67), the
   sprite's frames are loaded into DS:6ABE (0x7A87), 84C8 := 1, DS:84E3 := 1; then the sprite is drawn (slot as
   above). While 84C8 stays set the frames are not loaded again, so a later SETUPMON in the run draws the first
   sprite (SPRITEOFF does not clear 84C8).
3. 84C9 set and DS:84EA = $6DE1 (byte against the word; 255 = 255 for a PIC close-up): return (0x7ABC–0x7AD6).
4. Distance 0, DS:49FA = 1, not (inside ENCMENU, DS:84E9, with the monster pictures off, DS:49F6): DS:84EA := $6DE1,
   DS:84E0 := 1, the PIC (0x7B1B–0x7B56) or head + body (0x79E4, DS:84E1 := 0 at 0x7B77), 84C9 := 1.

So with 84C9 set nothing is drawn but a close-up with another head. PICTURE v ≠ 255 sets 84C9 (0x15F3) but not 84EA,
which only 0x7B06 writes (never cleared; 0 from the start-up's memory). 84C8 / 84C9 are cleared together (a two-byte
fill, START 0AF8:16A8) by EXIT (0xDC6), the script init (0x76B2), PROTECT (0x3BC9), COMBAT (0x261F) and PICTURE 255
(0x1689–0x168E, with or without its redraw), so they last one ECL run at most. **D** (s39, save A with vector 0 =
the code, step Up): SETUPMON 1, 0, 2; SETUPMON 1, 0, 25 left PIC 2 up (the sprite at slot 1, then PIC 2, then nothing);
PICTURE 2; SETUPMON 1, 2, 25 left PIC 2 up with no sprite; on the overland (`save-overland` 26 at 10,20 N, vector 0 =
SETUPMON 0, 2, 0; APPROACH) the sprite went up over the tiles, the view was redrawn with the next marker frame, and
the sprite drawn again at slot 2. Engine: `EncounterHost` draw with `EclMemory.EncounterSpriteUp` / `CloseUpUp` /
`CloseUpHead` / `EncounterSprite` / `AreaMapShown`; the Client's `ShowEncounterSprite` draws a sprite at distance 0
(slot 1 in 3D, slot 2 on the overland) where no close-up follows (before s39 the Client showed the close-up on the
overland at distance 0), and an overland sprite goes over the tiles without the box (it was boxed whenever the
stand-in text was kept). Frames: on the overland all 12 Client frames equal but for the window frame's cell (16, 16)
(the overland interface's layout has no joint there, the Client then drew one: fixed in s43, view3d.md) and the help lines; 9 of 11 and 14 of 16 in 3D (the
rest the Client's help lines).

The DOS sprite loader (0x2E170) builds the mask from source index 0 at 0x2E4CE, then remaps EGA index 13 to
opaque black through DS:0C6C/0C7C at 0x2E508. The remap leaves the mask intact. The Client applies both when
drawing DOS encounter sprites (s32).

**APPROACH** (0x155D): if $6DC1 > 0, $6DC1 − 1 and redraw. At 0 nothing.

**LOADMON v1 v2 v3** (0x1196). Nothing when DS:4435 (monsters added since CLRMON) is ≥ 63. Else:
1. Load monster v1 (low byte) with the monster loader 0x1EEAA: `MON<d>CHA.DAX` block v1 (285 bytes), then icon size
   0xC0 by race byte 0x2E (1, 3, 5 → 1; 2, 4, 6, 7 → 2), then **five 1d6 rolls** (dice 0x2BCCD): bytes 0xC1, 0xC2,
   0xC3, 0xC5, 0xC6 = (r + 8) · 16 + r, and 0xC4 = 0xC4; then effects (`MON<d>SPC`) and items (`MON<d>ITM`).
2. Load combat icon v3 (`CPIC`) into icon slot DS:6F83.
3. Append the record to the **party list** (DS:5D96; same list as the party), icon slot in byte 0xBF, DS:4435 + 1.
   Then copies (not reloads, so no more rolls) for k = 2 .. max(low byte of v2, 1) while DS:4435 < 63.
4. DS:6F83 + 1, DS:84E7 = 1 ("monsters loaded").

So PARTYSTR, FINDITEM, PSURPRISE and ENCMENU's speed rule see the loaded monsters until the after-combat step removes
them (GAME.OVR 0x672A, called from 0x6AA5: every creature past the party size $6E3E at the fight start, and every
hostile one; `combat-format.md`). CLRMON does not. DOSBox (`loadmon_combat`, state taken on the fight screen, combat list read from the
heap): CLRMON + LOADMON 2, 6, 2 gave six identical records after the six party members, byte 0xBF = 8, DS:4435 = 6,
DS:6F83 = 9, DS:84E7 = 1, and colour bytes that are exactly the first five 1d6 rolls from the RandSeed at the menu,
in the order 0xC1, 0xC2, 0xC3, 0xC5, 0xC6 (race 0 keeps 0xC0 = 0).

**CLRMON** (0x1FA3): DS:4435 = 0, DS:84E7 = 0, DS:6F83 = 8 (first monster icon slot), the 28 bytes of money at
DS:67F4 zeroed, the treasure item list DS:6810 freed. The monster records stay in the list (Lee says the same).

**ENCMENU** (0x2C3E), operands: sprite, distance, close-up, destination a, five reactions (for combat, wait, flee,
advance, parley), three texts (distance 0, 1, 2), move1, move2.
1. Fastest and slowest movement over the whole party list (0x9484): record byte 0x11C, doubled when the creature has
   effect 0x27, else halved when it has 0x2A. Quirk: both start from the first entry's byte before its effects.
2. Start as SETUPMON ($6DC0, $6DC1, draw).
3. Loop: text for $6DC1 = d is the first non-empty of slots d+1, d+2, … wrapping to slot 1 (so a missing text falls
   back to the next *farther* one; Lee says the nearer one). Menu items: combat, wait, flee, advance when d > 0 indoors, else
   combat, wait, flee, parley; the 4th item of the second menu is choice 4. Reaction r = low byte of the operand for the
   choice:

| r | Combat | Wait | Flee | Advance | Parley |
|---|---|---|---|---|---|
| 0 | 1 | 1 | 2 if slowest ≥ move1, else 1 | 1 | 1 |
| 1 | 1 | message "both wait", again | 2 | close in (d > 0) or "both wait"; again | 3 if d = 0, else close in, again |
| 2 | 1 if move2 ≤ fastest, else 0 + "monsters flee" | 0 + "monsters flee" | same | same | same |
| 3 | 1 | close in or "both wait"; again | 2 | close in or "both wait"; again | 3 if d = 0, else close in, again |
| 4 | 1 | 3 if d = 0, else close in, again | 2 | same as wait | same as wait |
| ≥ 5 | the menu ends, nothing is written | | | | |

   Numbers are the value written to a through the VM writer: 0 monsters flee, 1 combat, 2 party flees, 3 parley (Lee's
   names). "Close in" = $6DC1 − 1 and redraw. "Again" re-enters the loop after the draw (0x2D8D), so the text and the
   menu follow the new distance. The two messages are fixed program strings. While the loop runs DS:84E9 = 1. Every
   reaction operand in v1.3 is an immediate 0–4.
4. DOSBox (`trace_encmenu`, `encmenu_advance`): menu at $6DC0 = 2, $6DC1 = 1; Advance with reaction 1 gave $6DC1 = 0
   and the parley menu; Flee with reaction 0 and move1 = 6 (slowest member 6) took the ONGOTO target for 2.

**COMBAT** (0x24E1, the hand-off; the fight 0x9881, the after-combat step 0x6AA5, the shop 0x7212 and the temple
0x5280 are in `combat-format.md`):
- If DS:84E7 = 0 (no LOADMON since CLRMON) and DS:84D4 = 0 (no duel): $6E6C = 1 → $6E6C = 0, shop; else $6DE2 = 1 →
  $6DE2 = 0, temple; else the after-combat step alone. So shop and temple are checked only without monsters, shop first.
- Else: $6DC1 = min($6DC1, view distance) (outdoors this leaves 2), fight, after-combat step, outdoors the outdoor
  picture and three icons are reloaded, DS:84D4 = 0.
- Always: DS:49F3 = 4 (3D) or 3, $6DCA = $6DCA AND 1, DS:84C8/84C9 and DS:84E0 cleared.

**TREASURE v1..v8** (0x26F6): v1..v7 are stored (zero-extended) as the seven dwords at DS:67F4, replacing the old
values (Lee: added). v8 (low byte) < 128: the records of `ITEM<d>.DAX` block v8 are put in front of the treasure
list one by one (so they end up in reverse order; a missing file quits to DOS). 128–254: v8 − 128 random items; each
type is rolled with 1d100 (≤ 60: a second 1d100 gives the type directly for 1–47 and 50–59, 45 becomes 59, 60–90
a 1d10 picks 34–38, 91–94 → 73, 95–97 → 93, 98–100 → 77; 48 and 49 leave the variable unset (stack residue: 0 on the sampled v1.3 paths, `item-format.md`); 61–85 → 61;
86–92 → 62; 93–98 a 1d16 picks 67/70/71/78/79/84/92; 99–100 → 69), the item itself is made by 0x25C99 (not read,
`[?]` its draws), and afterwards START 00BA:0442 runs on every treasure item: that is the item naming routine
(`item-format.md`, image 0x0FE2), which writes the cached name. 255: no items.

**PARLEY v1..v5 a** (0x3311): five-item menu (haughty, sly, nice, meek, abusive), low byte of the chosen value
written to a.

**PICTURE v** (0x159C): v ≠ 255 shows `PIC` block v when $6DE1 = 255, else head $6DE1 + body v (0x79E4); outdoors the
window is cleared first; DS:84E0 := 1 (0x15F8), and a head + body clears DS:84E1 (0x79E7). v = 255 redraws the view
if a picture or the sprite is shown (DS:84E0 / 84E3), in the 3D view or with DS:49F9 > 1 (0x1659–0x1675), and then
clears both and sets DS:84E1 (0x1684, the picture animation's arm, por-art-formats.md §3). Display only; no VM cell. The
block is read from `PIC<disk>` (0x3D0:2A into DS:6ABE, drawn at cell (3, 3) by 0x3D0:25) of the current disk only:
a block that file lacks draws nothing and the window keeps what it showed (DOSBox-X s22; Client `ViewPictures.Find`
since s23, also for HEAD/BODY/SPRIT). DOSBox-X s24 (save A, disk 2): PICTURE 3 (only in PIC4) over the 3D view leaves
the view; PICTURE 2 then PICTURE 3 leaves picture 2 up until the step redraws the view. The Client keeps the last
picture drawn until the next view redraw (`ExploreScreen`; before s24 it showed the view there).

**SPRITEOFF** (0x37E8): if the sprite is shown (DS:84E3), redraw the view and clear DS:84E3 and DS:84E0.

### Party, damage and text-box opcodes (program read s11, 2026-11-17)

Handlers (GAME.OVR): LOADCHAR 0x1026, CHKPARTY 0x217B, ROB 0x2AF7, DAMAGE 0x34BB, PRINTRET 0x3831, CLEARBOX
0x385A, WHO 0x3879, ADDNPC 0x38C4, SPELL 0x3949, CALL 0x3A15, PROTECT 0x3BC3. "Party list" = DS:5D96 (the party
plus any creatures LOADMON appended), next pointer at record +0x104; "current" = DS:5D92. These replace Lee's
descriptions; where they differ from Lee it is said.

**LOADCHAR v** (0x1026). DS:4430 = 1. n = low byte of v AND 0x7F; walk n steps along the party list from the head
(stopping at the end); if the walk ends on a record, it becomes current (so n ≥ list length leaves the current
character unchanged). If bit 0x80 of v is set **and** DS:84D2 and DS:84D3 are both set (a write of 0 to $6C00 and
one to $6B00 since the script init, which clears both at 0x7699): if the current character is the one the player
had selected (DS:4436) DS:4430 is cleared; the current character is **removed from the party list** (0x1CC10 with
(0, 1)): unlinked, its combat icon slot freed (0x31BBA with record 0xBF), the record freed, the predecessor (or the
new head when it was first) becomes current, and $6E3E (party size) − 1; then START.EXE 0x1307 (`[?]` not read, presumably a display refresh) and DS:84D2 / DS:84D3 are cleared. So bit 0x80 is "dismiss this NPC" (the v1.3 scripts write
`SAVE #0 [$6B00]`, `SAVE #0 [$6C00]`, then `LOADCHAR n+128`); Lee's "add monster v−128 as an ally" is wrong.
Without both flags bit 0x80 only selects (e.g. ECL1 block 18 $AD24, after editing a loaded monster).
- DS:4436 is set to the current character each time the area loop reads a command (0x40EA, 0x4188). **EXIT
  (0xDAC) and PROGRAM (0x3B5C) restore** the current character from DS:4436 when DS:4430 is set, and clear it;
  NEWECL does not. The area entry 0x403E clears DS:4430. **After a Load, DS:4436 is stale** until the command read
  that follows vector 4: DOSBox (`party_opcodes`, s11) loaded a save whose vector 4 ran LOADCHAR and EXIT, and
  DS:5D92 then held 4000:05AF (the value DS:4436 had at the main menu), which is no party record. The VM gives no
  current character there (`EclParty.Selected` = null until `EclVm.CommandRead`). DOSBox also showed LOADCHAR #1,
  `SAVE #0` to $6B00 and $6C00, LOADCHAR #129 removing the second member ($6E3E 6 → 5). After a script switch, 0x3EFA restores the character that
  was current at the start of the pass, after vector 1 (0x4016).

**WHO s** (0x3879): clears the text area (1,17)–(38,22), then the character pick menu (START.EXE 0x37E8, prompt
= string slot 1, no Exit item) writes the chosen record straight into DS:5D92. DS:4430 is not set, so the choice
outlives EXIT. The picker loops until the key is Return, Escape, E or S (set at START 0x37BF). Each pass draws the
party (0x1307) and runs the menu 0x2CC8C (`shop-format.md` "The menu routine"; keypad flag 1, so 7 / 1 act as
Home / End): extended Home → the member before the current one (the head wraps to the last), End → the next (the
last wraps to the head); a non-extended key 0 or E writes **nil**. Escape makes the menu return 0 (s15), so Escape
writes nil and the loop goes on (0 is not an exit key); Select or Enter then leaves WHO with **no current
character**; Home would pick the last member, End would read the interrupt table through the nil pointer. E never
comes back from this menu ("Select" is its only item). The VM takes a pick of −1 (`IEclHost.NoCharacter`) as that
nil (s15). **Screen (P + D s44):** after the operands, 0x3879 blanks (1, 17)–(38, 22) (0709:0DD3 = START 0x7E63; the
text cursor is left where it was) and calls 00BA:2C48 = START 0x37E8 with the Exit flag 0 (`push 0`): the same member
pick as Trade and camp Cast, its item line "Select" alone (00BA:2C41). Row 24: the prompt in colour 13, then SELECT; the
party list with the pick lit (the current member first); nothing in the text box. The menu's keypad flag is 1 (its
[bp+0xA] is the timed-repeat flag, 1 only in camp / loot). DOSBox-X (scratch save): "WHO GOES: SELECT", Thrender lit;
End twice → Rhiannon lit; E nothing; Return → a PRINT after WHO went on at the cursor left by the PRINT before it,
Rhiannon still lit after EXIT. Client: `ClientPlayer.PickCharacter` → `MemberPick.Run` with
`ClientResources.SelectMenu` (nobody picked → `IEclHost.NoCharacter`).

**CHKPARTY s, v, a1..a4** (0x217B). The selector s is the raw field when operand 1 has type 1 (an address names the
field), else its value; e = low byte of v.
- s = 0: "does anyone have effect e": walk the party list until a member has an effect with id e (effect list at
  record +0x7F, id byte 0, next at +5; START.EXE 0x2D39). a1 = a2 = a3 = 0, a4 = 1 or 0.
- s = $6BA7 (record byte 0x79, find/remove traps) or $6C1B (record 0x11C, movement): over the whole party list, a1 = minimum (start
  255), a2 = maximum (start 0), a3 = sum div count (byte), a4 = 0.
- Any other s: nothing is written. Writes go through the VM writer in the order a1, a2, a3, a4 (a later one wins
  when the addresses repeat). v1.3 uses exactly these three selectors (4 uses).

**ADDNPC v1, v2** (0x38C4). Monster m = low byte of v1. If $6E3E ≤ 7 (0x1F248): a new record is loaded as LOADMON
does (0x1EEAA: `MON<d>CHA/ITM/SPC` block m, icon size, **five 1d6** icon colours), then record 0xAB = Random(256)
(START.EXE 0xC140), and the portrait: the classes with a level byte 0x96 + k > 0 (k = 0..6, signed) are listed
(none: class 2), **1d(count)** picks one; record 0xBC = DS table (0x8D7 for class 0, 0x8EB for 5, 0x8F5 for 6,
0x8E1 otherwise) [5·g + **1d5**] (portrait body), g = record 0x9E (gender, signed); record 0xBB (portrait head) = DS:0x8C7 [8·g + **1d8**] (DS tables in START.EXE's data segment, indexed 1-based: heads DS:08C8–08D7, bodies base + 1 … base + 10; read
from the player's program by `PorProgram.NpcPortraitBases` through the code that indexes them, s15). The record is
appended at the end of the party list and **becomes current** (0x1F42C: icon slot 0xBF = lowest of 0..7 not used by
a list member, $6E3E + 1, then the level stats 0x2AA87, spells-effects.md "Level stats", called at 0x1F55A; Core: `EclVm` through `Effects.LevelStats`, checked on every v1.3 ADDNPC record against the routine under emulation, `LevelStatsProgramTests`), then 0x1E468 (`[?]` the
combat icon load, by its calls; no random draws in it). Then, whether or not a record was added: current record 0x84
(morale) = (low byte of v2 / 2) OR 0x80, record 0x10E (hostile) = 1 if m = 24 else 0, and START.EXE 0x1758 and 0x1307
(`[?]` not read; 0x1307 is called after most changes to a character, presumably a display refresh). With a full party the morale lands on whoever is current.

**SPELL v, a1, a2** (0x3949). Lee: "no-op in practice"; the program shows why. Both destinations are taken as the
**low byte only** of their raw fields (`mov [bp−9], al`), so the writes go to $00xx, which is outside every VM
area (ignored, except the special cells $00B1/$00B8/$00B9/$00FB/$00FC). The search: the "found" byte [bp−8] is
never initialised; the party loop runs only while it is 0 (stack residue `[?]`). When it runs, it scans the
**first** list member only: index i = 1, 2, … compares record byte 0x17 + i with the low byte of v; a match stops
the scan, and after i = 0x52 the index becomes 255 and the scan stops too, both by setting "found", which also ends
the member loop. Then VM write ($00 | low(a1)) = i, VM write ($00 | low(a2)) = member count (1 after one member;
0 when the loop did not run, with i = 1). The one v1.3 use (ECL3 block 14 $9ECA, "cast knock" at the Kovel Mansion
door) writes to $007D / $007E, so $6E7D keeps its value (0 from the script init unless set) and the script's
"no one has knock" test (`COMPARE [$6E7D], #255`) never fires: the door then opens through `LOADCHAR [$6E7E]` and
`SAVETABLE #0, [$6B20], [$6E7D]`.
- DOSBox (`party_opcodes`, s11): `SPELL #40, [$6EB8], [$6EB9]` after a SAVE wrote 1 to $00B8 and 0 to $00B9 (DS:84CE /
  84D0) and left $6EB8/$6EB9 alone: the residue was non-zero there and the search did not run. The VM's policy
  default follows that (`EclVmPolicy.SpellFoundResidue`).
- Settled (s18): the byte is the absolute stack cell SS:3EDA (handler BP 3EE2 − 8, the same for every handler),
  which the previous instructions' handlers, their callees and any interrupt taken at the run loop's depth may
  write. The residue probe (`dosbox-trace-method.md`) read it in ECL3 block 14's own code ($9E6E–$9ED2: SAVE,
  PRINTCLR, the four-choice lock HMENU, ONGOTO, SPELL, copied into a Slums test save) as **0xCA** on two boots,
  so the search did not run there either. Both outcomes write only $007D / $007E, which the VM ignores, so the door
  script behaves the same with any residue (`EclPartyOpcodeTests`). No v1.3 outcome depends on it; the policy
  default (non-zero) matches both DOSBox samples.

**DAMAGE f, n, s, b, t** (0x34BB). Values as low bytes. Damage d = n d s + b (dice 0x2BCCD), **rolled first**.
- f AND 0x40: whole party; else a target index r = 1d($6E3E) is rolled next (also in the attack form).
- f AND 0x80 (spell/trap with a saving throw): bonus = f AND 0x1F, save type = t AND 7.
  - Whole party: for each list member, f AND 0x20: d to everyone, no save; else d unless the member saves.
  - Else, t AND 0x80: the current character; save type (t AND 7) − 1, no save when t AND 7 = 0.
  - Else the r-th list member (walk from the head; r ≤ 1 is the head): d unless it saves.
  - The save (0x2BC4D): 1d20; 1 fails, 20 saves; else roll + record 0x101 (signed) + bonus (signed byte), after the
    effect handlers of trigger class 12 (0x2B1E4: ids 8, 9, 10, 0x11, 0x14, 0x21, 0x24, 0x2D, 0x2E, 0x31, 0x3D,
    0x6F, 0x7D, 0x5A, 0x61) may change it, saves when ≥ record 0x6D + type.
- Else (attack form): f attacks (none when f = 0). Each: r = 1d($6E3E), the r-th list member; to-hit 0x2BB39: 1d20
  (≤ 1 misses; 20 counts as 100), effect handlers of trigger class 16 (ids 0x19, 0x47, 0x25, 0x2F, 0x30, 0x59) may
  change it; hits when roll + t > record 0x111 (AC byte); on a hit d is applied. After every attack d is rolled
  again (also after the last).
- Applying d (0x959F, then START.EXE 0x2DC8): nothing for a creature with status 6 (dead). A death message
  when HP + 10 < d (16-bit), else a "hit for d points" message (text box). Then, with d as a **byte**:
  HP ≥ d: HP − d; else over = d − HP, new HP 0. Status: 6 (dead) if over > 9, or new HP 0 and status was 1
  (animated); else 5 (dying) if over > 0; else 4 (unconscious) if new HP = 0. A creature whose status is then not
  0 or 1 gets in play (0x10D) = 0 and HP 0 (in combat also the side count DS:6814); otherwise HP = new HP.
- DOSBox (`party_opcodes`): `DAMAGE #128, #1, #1, #0, #128` hit the current character for 1 (hit message,
  HP 11 → 10) with exactly two draws (1d1, then the target 1d6 that the current-character form ignores).
- Afterwards DS:49FF = 1 unless some list member has in play ≠ 0 (that ends the run and the area loop): then the
  party-killed end screen and a 3-second pause. The current character is restored.
- **The lines and the end, P + D (s22).** The line routine 0x959F (unit 0x74D3) builds two spaces, the name and the
  death text, or the hit text, the damage and the points text. Before printing: when the cursor row is above 22 it
  goes to row 17, the box is cleared by the print and a white press-enter line (row 24, colour 15) waits first; then
  the column is set to 38, so each line starts on a new row (the wrap skips the two spaces: the name at column 1),
  and the line goes through the text routine in the script box (1, 17)–(38, 22) in **colour 15** (white). So the
  first line of a step lands on row 18, and a sixth line needs a new page: the text routine's own page prompt
  (colour 13) waits, then the box is blanked and the line printed on row 17. After the line the damage is applied,
  cells (17, 1)–(38, 15) are blanked (0709:0DD3) and the party list drawn again (00BA:0767): the HP and name colours
  change line by line, and the **position line on row 15 is gone** until the next redraw point (the step after the
  vector, a command). **Every DAMAGE** then ends (0x37C0) with DS:5D92 restored and the white press-enter line (colour
  15, row 24) and a key, wipe or not; with the party killed the window, the message and the 3-second pause come first
  (start-menu.md §8). DOSBox-X: test saves whose step runs `DAMAGE 0xE0, 1, 1, 0, 0` (1 point to all six: lines of
  two, one and two rows fill rows 18–22, the page prompt, the other three from row 17, the white line) and `DAMAGE 0xE0, 1, 1, 200, 0` (six death lines, the
  wipe). Client: `ClientPlayer.DamageReport` / `DamageEnd` (host call `DamageEnd` after the opcode), pixel-equal on
  every screen of both runs.
- `[?]` The effect handlers of the two trigger classes (not read; they live with the combat code).

**ROB v1, v2, v3** (0x2AF7). v1 = 0: the current character; else every list member. Money: each of the seven
words 0x88, 0x8A, …, 0x94 = Trunc(word × r), r = Real(100 − low(v2)) / 100.0 (six-byte Real, START.EXE 0xC11A /
0xC10C / 0xC106 / 0xC11E = Trunc). Items (0x92C3), with chance c = low byte of **v2** (operand 2 is fetched again;
v3 is never read): for each item of the character in list order, **c is first reduced** (and the reduced value is
kept for the following items): by 90 (floor 0) when item weight (+0x37) > 255, else by 50 (floor 0) when > 24;
then 1d100 ≤ c loses the item (START.EXE 0x20A9: unlinked and freed, no stat recalculation). One 1d100 per item,
also when c = 0. The Real arithmetic gives floor(word · (100 − v2) / 100): DOSBox (`party_opcodes`) ROB #0, #10 took
10 copper to 9 (r = 0.9 is inexact; a chopped product would give 8), 5 silver to 4 and 2 platinum to 1, with one
roll per item (RandSeed).

**PRINTRET** (0x3831) and **CLEARBOX** (0x385A) load no operands (they step the PC themselves).
- The text cursor is DS:5E24 (column) / DS:5E25 (line); EXIT puts it at column 1, line 17 and clears DS:84DE; it
  also clears DS:84C8/84C9 and the close-up flag DS:84E0 (0xDC6–0xDD7; por-art-formats.md §3).
- PRINTRET: column 1, line + 1, DS:84DF = 0.
- CLEARBOX: clears the **picture window** (1,1)–(15,15) (not the text box) and sets DS:84DE = 1.
- **PROTECT a** (0x3BC3) is not a copy-protection check: it draws a line of glyphs. 84C8/84C9 and 84E0 cleared; if
  DS:84DE is set and the line is > 13, the cursor goes to column 2, line 5 (inside the cleared window) and DS:84E2
  = 1; DS:84DF = 1; then for i = 0, 1, …: g = (VM read at raw a + i) + 1 as a byte; while g > 1 draw glyph g at the
  cursor (START.EXE 0x726F with its flag 0: g is the text font's glyph number, no case mapping; colour 15; columns
  above 39 and rows above 24 not drawn) and column + 1; then line + 1, the column left where the line ended (an empty
  line, first byte 0 or 255, still moves the line down). Not through the text routine, so it never types.
  DS:84DE: CLEARBOX sets it (0x3872), EXIT clears it (0xE31), the start-up too (0xE9EF). DS:84DF: PROTECT sets it,
  PRINTRET clears it (0x3848), but both PRINTRET branches do the same (column 1, line + 1), so it changes nothing.
  DS:84E2: cleared by EXIT (0xDDC) and the script init (0x7696). **EXIT with DS:84E2 set** (0xD4D–0xDA7, display
  only): START 0x41F8 (a window: the inside blanked, 0x4047, and a frame, 0x3F10, no title) for (1, 1)–(15, 15), and
  when $49E6 ≠ 0 (DS:49D2 + 0x1CC; the 3D view) again for (3, 3)–(13, 13); no key wait (s29; earlier notes said a
  prompt). So the runes are gone when the run ends.
  **Uses in v1.3** (12, `ecl.py dis`; P, s29): every one sits between LOADFILES #255,#16,#255 and #255,#2,#255, which
  load nothing (o1 = 255; o2 unused; o3 = 255). ECL4 block 21 $9BD7: CLEARBOX, then three lines (glyph data $AF1E,
  $AF25, $AF34) with PRINTRET between, after a PRINTCLR text: the cursor is below 13, so the lines go to (2, 5), then
  (1, 7) and (1, 9) in the cleared picture window. ECL5 blocks 3 and 4, ECL7 block 23 (no CLEARBOX): a PRINTCLR of
  an empty text, an empty line (its only effect: the cursor one row down, then the next PRINTCLR homes it), then
  PRINTCLR / PRINT texts each followed by a line of glyphs at the cursor, so the runes follow the text on its row in
  the text box (ECL5 block 4 $A45E: rows 17 and 19). Client: `ClientPlayer.DrawGlyphs` (text-box rows into
  `ClientPlayer.Text`, which a later clear removes; picture-window rows into `ViewGlyphs`, drawn over CLEARBOX's blank
  of cells (1, 1)–(15, 15)); test `RuneTextTests` runs both uses from the game's scripts and checks the pixels against
  8X8D1 block 201.
  **Outside the box and the window (P, s30; no v1.3 use, the 12 lines all land in rows 17–22 or cells 1–15 of the
  window):** the glyph routine draws any cell up to column 39 and row 24, so a line can run over the frame. Reachable:
  rows 23 and 24 (a PROTECT at row 22 moves the cursor to 23, the next to 24, the one after to 25, not drawn) and, in
  the cleared window, a line longer than 14 glyphs from (2, 5) runs over the frame column 16 into the party list's side
  (columns 17–39). Row 16 is never reached: with CLEARBOX standing every line whose cursor is below row 13 starts again
  at (2, 5), and without it the cursor stays in the text rows. What draws over them: row 24 by the next menu (0x2CC8C
  draws its prompt and words, then blanks the rest of the row to column 39, 0x2CC16–0x2CC41), message (START 0x2233
  clears the row) or key prompt (START 0x7C85, also the text routine's page prompt at 0x7A26: 0x767B blanks the row's
  40 cells first, 0x7CAB); cells 0–16 of rows 0–16 by EXIT's window redraw (START 0x41F8 for (1, 1)–(15, 15) draws
  the frame one cell outside); the text box's clear (0x7E63 on the box) does not reach row 23. Client:
  `ClientPlayer.ScreenGlyphs`, drawn last by `ExploreScreen`, row 24 cleared when a menu, key or text prompt or the
  command line comes (`BottomRowDrawn`), the window's frame cells by EXIT (`TextHome`); tests in `RuneTextTests`.
  `[?]` When the program draws the other frame cells and the party list again (a full redraw, START 0x3379, or the
  party list 00BA:0767) is not followed: the Client keeps those glyphs until the next area command.
  **Amiga v1.0** (P, s38; Amiga `program`, hunks numbered from 0 in file order; no emulator run): the runes come from
  a disk font of their own, `protect.font` (disk A, `fonts/protect.font` and `fonts/protect/8`), not from the text
  font (`poolrad.font`, used for everything else). Start-up opens both with OpenDiskFont (hunk 1 +0x1C4 / +0x1D4 from
  the TextAttrs at hunk 2 +0x4E and +0x64, kept at hunk 32 +0x20 and +0x6A). The ECL dispatch (hunk 16 +0x4D12, opcode
  at hunk 32 +0x1AA7) sends 0x3C to hunk 16 +0x4C06, PROTECT as on DOS: 84C8/84C9 and 84E0 cleared; with 84DE (hunk 32
  +0x2F75) set and the row (hunk 32 +0x2D0) above 13 the cursor goes to column 2, row 5 and 84E2 (+0x2F79) := 1; 84DF
  (+0x2F76) := 1; each byte b read from the VM is drawn as character b + 1 through the glyph routine (hunk 25 +0xA4E,
  the copy of START 0x726F: column, row, colour, count 1, character, flag 0) and the column moves on; at the end the
  row goes down one. Two differences: the line ends only on a byte 0 (`cmp.b`/`bls` at hunk 16 +0x4CAA), so a byte
  255 draws character 0 and goes on where DOS stops (no effect in v1.3: all twelve lines end in 0, and the Amiga
  `ecl.dax` holds the same lines byte for byte in blocks 3, 4, 21, 23); and the colour is **28**, not 15 (`pea $1C`
  at hunk 16 +0x4CCE). With its flag clear the glyph routine (hunk 25 +0xB6E) sets the rastport's font to
  `protect.font`, takes a colour below 16 as pen colour + 16 − colour mod 2 (28 stays 28: a pen of the interface
  colours 16–31, which start-up loads from hunk 2 +0 and nothing changes later), skips columns above 39 and rows above
  24, and per glyph does SetAPen, Move(column · 8, row · 8 + 6), SetDrMd(0) and Text(character, 1): **JAM1**, so
  only the glyph's set bits are drawn and the cell's other pixels stay as they were (DOS draws the cell whole), then
  it puts `poolrad.font` back. The font (8 × 8, baseline 6, not proportional, characters 32–255) holds the DOS
  glyphs 65–139 redrawn, all but four bit for bit (docs/re/por-art-formats.md §4 "Runes"). **Client**: the importer
  reads the font from the player's disk A (optional, like `intro`) and the pen and colours from `program`
  (`PorAmigaRunes`, module `art/amiga/protect/0/8`, category `font`); under Best / Amiga `ClientArt.Runes`
  (`RuneGlyphs`) draws PROTECT's glyphs with it, set bits only, in the text box, the picture window and elsewhere
  (`ExploreScreen`); Original DOS, or a module without the font, keeps the DOS font's glyphs in colour 15. The line
  still ends on 0 or 255 (DOS is the rules reference); the text around it is the Amiga text font under Amiga only
  (docs/re/por-art-formats.md §4 "Text font"). Tests: `PorAmigaArtTests` (the reading, the font against DOS block 201),
  `AmigaRuneTests`. `[?]` Not seen in an Amiga emulator.
  **Amiga CLEARBOX and EXIT's redraw** (P, s39): CLEARBOX (0x3D: hunk 16 +0x4FC4 → +0x47AE) adds 1 to hunk 32 +0xB4 (the PC step `[?]`), blanks
  (1, 1)–(15, 15) through the box blank (hunk 25 +0x197A, the copy of START 0x4047: SetAPen(0), SetDrMd(JAM1),
  RectFill of pixels (8, 8)–(127, 127)) and sets 84DE, as on DOS. EXIT (hunk 16 +0x1AD8) with 84E2 (hunk 32 +0x2F79)
  set draws the window (1, 1)–(15, 15), and with the 3D view's flag (hunk 32 +0x98 → +0x1CC) (3, 3)–(13, 13), through
  the window routine (hunk 25 +0x19F4: colour 15, style 0, an empty title), which blanks (x0, y0)–(x1 + 1, y1 + 1) the
  same way and then blits its frame over the blank's extra row and column (por-art-formats.md §4 "Window frame").
  Pen 0 is black in start-up's colours and in palette tables 2–4, which the explore screen sets (table 0, whose entry
  0 is not black, is the combat screen's, hunk 13 +0x5F0E, and the character screens'; the 3D view's redraw loads
  table 3 again, hunk 7 +0x3890): the fill is the Client's colour 0 under every setting, no change. The frame is the
  Amiga's own art, drawn under Amiga (por-art-formats.md §4 "Window frame").
  **D** (s30, DOSBox-X frame by frame, save A on disk 2 with this tool's own script: PRINTCLR, CLEARBOX,
  the three glyph lines of ECL4 block 21 copied into the script area with PRINTRET between, PRINTCLR, a one-word
  HMENU): CLEARBOX blanks cells (1, 1)–(15, 15) (the view's box too), the runes land at (2, 5), (1, 7), (1, 9), the
  text box below unchanged: the Client's frame at the menu equal but for its own help hint; after the answer EXIT
  draws the blank window with its frame and the (3, 3)–(13, 13) box, then the 3D view, then the command line (~0.2 s of
  drawing, no Delay; the Client goes to the last frame, equal). **Other rows** (s30): row 16 is out of reach (with
  DS:84DE set a line above 13 is sent to (2, 5); without it the cursor starts at 17). PRINTCLR, six PRINTRETs and
  PROTECT put a line on **row 23**, over the text box's bottom frame (glyph cells drawn whole), and it **stays after
  EXIT** (DOSBox-X); the next PROTECT went to row 24 from the column the first ended on and was gone under the
  HMENU before any frame showed it. Both agree with the program reading above: the Client keeps row 23 in `ClientPlayer.ScreenGlyphs` past EXIT
  and removes a row-24 line with the next menu.

**CALL $C018** (0x3AFA): only when DS:49FA = 1, the wall type ahead DS:6AB0 ($C04E) is read again (0x2ED72 at
x, y, facing). DS:49FA (view mode = the save's location type) is 1 at a new game (0xE9C3) and is set by 0x3EFA
before each vector 4: 1, then after vector 4 2 / 3 / 4 when $49E6 = 0 in block 25 / 26 / 27; Load reads it from the
save (0x1F862); nothing else writes it. Save writes it as the location type byte (0x1FF9D). DS:49F9 keeps the
value from before the switch (0x3F28).

### Steps, doors and script switches (program read s11)

- **Forward key** (0x16D58): $6DD5 = 0; the barrier of the party's own face (0x2EC47) is read; **only when it is
  not 0** (passable 1, doors 2/3, or no wall) is the target checked, and a target off the grid sets $6DD5 = 1 (x/y
  clamped, i.e. unchanged). So an **impassable face on the map edge leaves $6DD5 = 0**: the scripts' edge exits do
  not fire, and the step is refused as any impassable step.
- **Step** (0x17438, called between vector 0 / the $49F0 write and vector 1): only when DS:49FA = 1 and $6DC9 < 255
  (else $6DC9 = 0, no step). Barrier 1: move (0x16E06). 2 / 3: the door menu runs **inside the step**; on success
  the party moves through in the same step, on failure or Exit it stays. Then the area loop runs **vector 1**
  either way. So a step into a door face runs vector 0, the door menu, and vector 1 (on the new square after an
  opening, else on the same square). DOSBox: `door_bash` (vector 0, bash, step, vector 1, RandSeed exact) and
  `door_pick_fail` (failed pick, then vector 1 on the same square) replay this order.
- **Script switch** (0x3EFA), each pass: event byte re-read, $6DD5 = 0, current character saved, DS:49F9 = DS:49FA,
  DS:49FA = 1, vector 4 (**DS:442F is not tested afterwards**), view mode, redraw, DS:442F = 0 (0x3FFA), vector 0,
  and unless DS:442F was set again vector 1 and the saved current character restored; repeat while DS:442F is set.
  The run loop never clears DS:442F; only 0x3FFA and the area entry 0x403E (0x4062) do.
  - The redraw 0x2DCF4 after vector 4 runs (0x3FD2–0x3FF5) when DS:49F9 = 1 and DS:49FA = 1, or when (DS:49F9 > 1
    or DS:49FA = 1) and DS:4431 ≠ 0. DS:4431 = "LOADFILES / LOADPIECES ran": set by their handler (0x19C8), cleared
    by NEWECL (0x19AA) and the area entry (0x4053). So leaving the wilderness redraws only when vector 4 loaded files,
    and entering it (1 → 2..4) never does. 0x2DCF4 itself re-reads the event byte only when DS:49FA = 1 or $49E6 ≠ 0.
    Before vector 4, when DS:49F9 > 1 and DS:49FA = 1, a message is printed (0x3F9E–0x3FCD). Modelled in
    `EclVm.FollowScriptChanges` (s15).
  - **NEWECL in a vector 4** after a switch: the newest script's vectors 0 and 1 run, not its vector 4.
  - **NEWECL in a vector 4 on area entry / Load** (0x403E): nothing runs at once (0x40A8 runs vector 4 and goes on
    to the command loop). DS:442F stays set, so after the next key's vector 0 (or vector 1 in search mode) 0x3EFA
    runs the newest script's vectors 4, 0, 1.
  - v1.3 has one such vector 4: ECL5 block 7 (Valjevo inner tower) $9A3E → $A82D, `NEWECL #0` to civilised Phlan,
    taken only when $4AE0 = 1. $4AE0 = 1 is set at $A824 right before PROGRAM 8 (end game) after Tyranthraxus, and
    cleared at $A8FD before that NEWECL; so it fires only for a save made in between (the end game's own flow
    reaches $A82D from the same vector without a switch).

## Opcode table

"Ops" = operands the handler loads (program); "Skip" = operands the IF-skip consumes (program); a `+n` means the
variadic tail. Operand names: `v` value (any type, dereferenced), `a` address (raw field), `s` text, `t` jump target.
"Uses" = decoded instructions in the 29 v1.3 blocks (hard and speculative paths). "Effect from": **P** = checked in the program, **L** = Lee 12.3.3
only. `[?]` = unknown or unconfirmed.

| Op | Ours | Lee | Ops | Skip | Operands | Effect (short) | From | Uses |
|---|---|---|---|---|---|---|---|---|
| 00 | EXIT | EXIT | 0 | 0 | – | End the run, clear GOSUB stack | P | 981 |
| 01 | GOTO | GOTO | 1 | 1 | t | Jump | P | 1695 |
| 02 | GOSUB | GOSUB | 1 | 1 | t | Push return PC, jump | P | 915 |
| 03 | COMPARE | COMPARE | 2 | 2 | v1 v2 | Set =,≠,<,>,≤,≥ from v1 vs v2 (unsigned) | P | 1504 |
| 04 | ADD | ADD | 3 | 3 | v1 v2 a | a = v1 + v2 (16-bit wrap) | P | 317 |
| 05 | SUB | SUBTRACT | 3 | 3 | v1 v2 a | a = v2 − v1 (subtrahend first) | P | 76 |
| 06 | DIV | DIVIDE | 3 | 3 | v1 v2 a | a = v1 / v2 unsigned, quotient only; v2 = 0 is a CPU divide fault | P | 29 |
| 07 | MUL | MULTIPLY | 3 | 3 | v1 v2 a | a = low 16 bits of v1 · v2 | P | 12 |
| 08 | RANDOM | RANDOM | 2 | 2 | v a | a = Random(v+1) = 0..v (v = low byte; 255 gives 0..254) | P | 144 |
| 09 | SAVE | SAVE | 2 | 2 | v a | a = v; if v has type ≥ 0x80 (0x80, 0x81), store string slot 1 at a (0xFF6) | P | 2341 |
| 0A | LOADCHAR | LOAD CHARACTER | 1 | 1 | v | Current = list member (v AND 0x7F); bit 0x80 after `SAVE #0` to $6B00 and $6C00 removes it from the party; EXIT restores the selected character | P | 36 |
| 0B | LOADMON | LOAD MONSTER | 3 | 3 | v v v | Load monster v1 (5 × 1d6 icon rolls), append max(v2, 1) copies to the combat list (cap 63), combat icon v3 ("Encounters") | P | 330 |
| 0C | SETUPMON | SETUP MONSTER | 3 | **2** | v v v | Sprite v1, $6DC0 = v2, close-up v3; $6DC1 = min(v2, view distance) ("Encounters") | P | 187 |
| 0D | APPROACH | APPROACH | 0 | 0 | – | If $6DC1 > 0: $6DC1 − 1 and redraw | P | 97 |
| 0E | PICTURE | PICTURE | 1 | 1 | v | Picture v ($6DE1 = 255) or head $6DE1 + body v; 255 = back to the view | P (shape) | 137 |
| 0F | INPUTNUM | INPUT NUMBER | 2 | 2 | v a | Read a number into a; v is ignored (input up to 6 characters, re-asked until 0..65536; low 16 bits stored) | P | 2 |
| 10 | INPUTSTR | INPUT STRING | 2 | 2 | v a | Read text into a (SAVE-text rules); v is ignored: up to 40 characters 0x20–0x7A, upper-cased, empty input stored as one space | P | 20 |
| 11 | PRINT | PRINT | 1 | 1 | s | Print to the text box; a number (type < 0x80) is printed as decimal digits via slot 1 (P, 0x174F); layout P (`view3d.md` "Text window") | P | 289 |
| 12 | PRINTCLR | PRINTCLEAR | 1 | 1 | s | Cursor to (1, 17), clear the text box, print (numbers as for PRINT; layout as PRINT) | P | 1401 |
| 13 | RETURN | RETURN | 0 | 0 | – | Pop PC; empty stack: fall through | P | 440 |
| 14 | CMPAND | COMPARE AND | 4 | 4 | v1 v2 v3 v4 | "=" if v1 = v2 and v3 = v4, else "≠"; other flags 0 | P | 65 |
| 15 | VMENU | VERTICAL MENU | 3+n | **0** | a s n, s×n | Prompt s, list n choices, choice index → a | P (shape) | 15 |
| 16 | IFEQ | IF = | 0 | 0 | – | Run next instruction if "=" flag, else skip it | P | 690 |
| 17 | IFNE | IF <> | 0 | 0 | – | same, "≠" | P | 634 |
| 18 | IFLT | IF < | 0 | 0 | – | same, "<" | P | 180 |
| 19 | IFGT | IF > | 0 | 0 | – | same, ">" | P | 281 |
| 1A | IFLE | IF <= | 0 | 0 | – | same, "≤" | P | 24 |
| 1B | IFGE | IF >= | 0 | 0 | – | same, "≥" | P | 127 |
| 1C | CLRMON | CLEARMONSTERS | 0 | 0 | – | Monster count 0, "monsters loaded" off, icon slot 8, treasure cleared; records stay listed | P | 251 |
| 1D | PARTYSTR | PARTYSTRENGTH | 1 | 1 | a | a = party strength (formula below) | P | 18 |
| 1E | CHKPARTY | CHECKPARTY | 6 | 6 | s v a a a a | s = 0: a4 = anyone has effect v (a1–a3 = 0); s = $6BA7 / $6C1B: min, max, average of record 0x79 / 0x11C; else nothing | P | 4 |
| 1F | (none) | undefined | – | 2 | – | No handler; PC not advanced, the game hangs (DOSBox) | P | 0 |
| 20 | NEWECL | NEWECL | 1 | 1 | v | Load script v, stop the current run | P | 79 |
| 21 | LOADFILES | LOAD FILES | 3 | 3 | v v v | 3D: load GEO map v1; outdoors: BACPAC picture (see below) | P | 61 |
| 22 | PSURPRISE | PARTY SURPRISE | 2 | 2 | a a | a1 = 1 if a party member's class is ranger or cleric/ranger, else 0; a2 = 0 | P | 22 |
| 23 | SURPRISE | SURPRISE | 4 | 4 | v v v v | Roll surprise, result 0–2 to $6DCB (formula below; 3 is never stored) | P | 22 |
| 24 | COMBAT | COMBAT | 0 | 0 | – | Monsters loaded: limit $6DC1 to the view, fight, after-combat. Else shop ($6E6C = 1), temple ($6DE2 = 1) or after-combat. Then $6DCA AND= 1 | P (entry) | 245 |
| 25 | ONGOTO | ON GOTO | 2+n | **0** | v n, t×n | Jump to target v if v < n, else fall through | P | 427 |
| 26 | ONGOSUB | ON GOSUB | 2+n | **0** | v n, t×n | Same as a call | P | 37 |
| 27 | TREASURE | TREASURE | 8 | 8 | v×8 | Set the 7 money values; v8 < 128 item list, 128–254 that many − 128 random items, 255 none | P (partly) | 77 |
| 28 | ROB | ROB | 3 | 3 | v v v | v1 = 0: current character, else every list member; money words × (100 − v2) / 100, truncated (DOSBox); each item lost on 1d100 ≤ c, c = v2 reduced by 90/50 for heavy items (kept for later items); v3 unused | P | 14 |
| 29 | ENCMENU | ENCOUNTER MENU | 14 | 14 | v v v a v×5 s s s v v | Start as SETUPMON, then the menu loop; result 0–3 → a ("Encounters") | P | 94 |
| 2A | GETTABLE | GETTABLE | 3 | 3 | a v a | a2 = cell at (a1 + low byte of v) | P | 271 |
| 2B | HMENU | HORIZONTAL MENU | 2+n | **0** | a n, s×n | Bottom-line menu, choice → a | P (shape) | 204 |
| 2C | PARLEY | PARLAY | 6 | 6 | v×5 a | Five-item menu; low byte of the chosen value → a | P | 41 |
| 2D | CALL | CALL | 1 | 1 | a | Run a built-in routine chosen by the raw address ($2C90, $8000, $8001, $BA03, $C018, $C01E; else nothing). $8000 = mirror duel (a copy of the current character as the enemy, the others out of the fight), $8001 = solo fight; both GAME.OVR 0x8F42, which sets DS:84D4 (so the next COMBAT fights) and $6DE6. $C01E = one square forward with edge wrap (0x8EAE; the town scripts' edge exit, not the encounter flee). Table in `combat-format.md` "CALL ids" | P | 180 |
| 2E | DAMAGE | DAMAGE | 5 | 5 | v×5 | Damage n d s + b to a random member, the current one or the whole party, with a saving throw (f bit 0x80) or f to-hit rolls; party wiped sets DS:49FF | P (effect hooks `[?]`) | 48 |
| 2F | AND | AND | 3 | 3 | v v a | a = low byte of v1 AND v2; "=" when the result is 0 | P | 356 |
| 30 | OR | OR | 3 | 3 | v v a | a = low byte of v1 OR v2; flags as AND | P | 208 |
| 31 | SPRITEOFF | SPRITE OFF | 0 | 0 | – | If the encounter sprite is shown: redraw the view, clear the sprite/picture flags | P | 56 |
| 32 | FINDITEM | FIND ITEM | 1 | 1 | v | "=" if a party member carries an item of type v, else "≠" | P | 7 |
| 33 | PRINTRET | PRINT RETURN | 0 | 0 | – | Text cursor to column 1 of the next line | P | 20 |
| 34 | CLOCK | ECL CLOCK | 1 | 1 | v | Advance the clock by n minutes; bug: n comes from an uninitialised operand index, 0 after GOSUB, which gives n = high byte of operand slot 64 (0 in v1.3) | P | 1 |
| 35 | SAVETABLE | SAVE TABLE | 3 | 3 | v a v | Store v1 at (a + v2), v2 full 16 bits | P | 17 |
| 36 | ADDNPC | ADD NPC | 2 | **1** | v v | Party < 8: load monster v1, append it to the party as current (portrait rolls); then current 0x84 = 0x80 OR v2/2 | P | 8 |
| 37 | LOADPIECES | LOAD PIECES | 3 | 3 | v v v | Load wall sets for slots 1–3 (same handler as 21; see below) | P | 35 |
| 38 | PROGRAM | PROGRAM | 1 | 1 | v | 0 training hall (0x19AE0), 8 end game (0x20941), 9 camp (then EXIT: the run ends, 0x3BBC); else nothing (training.md) | P | 4 |
| 39 | WHO | WHO | 1 | 1 | s | Prompt, player picks a character (becomes current; survives EXIT) | P | 11 |
| 3A | DELAY | DELAY | 0 | 0 | – | Pause for game speed (DS:49E2, 0–9) × 225 ms (Crt-style Delay, START.EXE 0x7E4E/0xA69C); speed 0 = none | P | 188 |
| 3B | SPELL | SPELL | 3 | 3 | v a a | Search the first member's spell bytes for v (skipped when the residue byte is non-zero, as in DOSBox: 0xCA at the v1.3 site); results written to $00xx (low address bytes only): no effect in v1.3 | P, D | 1 |
| 3C | PROTECT | PROTECTION | 1 | 1 | a | Draw the glyph line at a (bytes + 1 until 0/255), used for rune texts | P | 12 |
| 3D | CLEARBOX | CLEAR BOX | 0 | 0 | – | Clear the picture window (1,1)–(15,15); the next PROTECT starts inside it | P | 27 |

Handlers shared in the dispatcher: 04–07, 11/12, 16–1B, 21/37, 25/26, 2F/30. The dispatcher at GAME.OVR 0x3C71
leads to every handler.

## VM memory map

The VM address space is 16-bit. The region classifier (GAME.OVR 0x7BCE), reader (0x82DF) and writer (0x814A) agree:

| Range | Cell | Storage | Notes |
|---|---|---|---|
| $4900–$4CFF | 16-bit | 1024 words | Global game state. $49C3.. clock/location, $4A00–$4AFF script flags (Lee 12.4.1). Saved (save offset 1) |
| $6B00–$6EFF | 16-bit | 1024 words | Some addresses are redirected to the current character record or to program variables (list below); the rest is encounter/combat/shop/scratch state. Saved (offset 2049) |
| $9700–$98FF | 16-bit | 512 words | Scratch (Lee). Saved (offset 4097) |
| $9900–$B6FF | 8-bit | 7680 bytes | The script itself. Scripts read their tables here and may write into it. Saved (offset 5121) |
| anything else | – | special | Listed below ("Special addresses"); other reads return stack residue, other writes are ignored |

- Writes to $49FD/$49FE (sky/ceiling colour) store the cell and set the redraw flag DS:84E8 (writer 0x8182).
- $6BF2 (written by ECL2 block 15 vector 4) is a plain cell: neither the read list nor the write side effects
  below have a case for it, and no code in GAME.OVR or START.EXE reads that cell directly. The write has no effect beyond the cell.

**Special addresses** (outside the four areas; reader 0x82DF from 0x837A, writer 0x814A from 0x81E7; program read
2026-11-17). Several are written by the scripts but ignored by the DOS program.

| Address | Read | Write |
|---|---|---|
| $00B1, $00FB, $00FC | DS:6F6E, DS:6F6A, DS:6F6C (words). No program code writes them, so they read 0 (DOSBox: 0, also right after a script wrote 77/78/79) | Case present, stores nothing |
| $033D | Facing byte DS:6AAF (0, 2, 4, 6), unsigned | Ignored |
| $035F | Case present, sets no value: stack residue (see below) | Ignored |
| $03DE | Residue | Word to DS:84CC: the sound CALL $BA03 plays (8 → one effect, 10 → another, 0x3AC6) |
| $00B8, $00B9 | Residue | Words to DS:84CE, DS:84D0; no program code reads them (no effect) |
| $C04B, $C04C | x, y: DS:6AAD, DS:6AAE, both sign-extended | Byte to x / y, and the redraw flag DS:84E6 |
| $C04D | Facing / 2 | Value mod 4 (repeated subtraction of 4) → facing 0/2/4/6, redraw flag DS:84E6 |
| $C04E, $C04F | DS:6AB0, DS:6AB1 (wall ahead, event byte) | Ignored |
| $C059 | Case present, sets no value: residue | Low byte to DS:6F68, flag DS:84E5 |
| $C05F | Residue | Low byte to DS:6F69, flag DS:84E5 |
| $D022 | Residue | Case present, stores nothing |
| others | Residue | Ignored |

- **$C059 / $C05F are 3D-view background colour codes.** The redraw routine 0x2DCF4, when DS:84E5 is set, maps $C05F
  to two EGA colours (8 → 0 and 8; 0x41 → 0 and 7; anything else → 15 and 7) and $C059 to one (0x0B → 8, 9 → 6,
  0xDB → 6, 6 → 1, anything else → 6). The script init sets DS:6F68 = 0x41, DS:6F69 = 9 and the flag (0x76A3);
  start-up sets 0xDB / 0xE8 (0xEA1F). CALL $2C90 redraws the view when any of the flags DS:84E0, 84E3,
  84E5, 84E6, 84E8 is set (0x3A54).
- **Stack residue.** For an address with no value (rows above), the reader returns its uninitialised local, i.e.
  whatever the stack slot holds. That slot is the one every reader call made from the same depth uses, so the value
  is the **last value read at that depth**, unless a deeper call in between overwrote it. In COMPARE (0xE6C) both
  operand fetches call the reader from the same depth, and the handlers that run between two COMPAREs in the
  scripts below have shallow stacks: an undefined read in COMPARE returns the previous COMPARE operand read. DOSBox-X
  check (patched save D): after `COMPARE [x], #8`, `COMPARE [$035F], #3` gave "=" with x = 3 and ">" with x = 5.
- **$035F in v1.3**: the three wilderness scripts (ECL6 block 25 $9A0E, ECL7 block 26 $9A77, ECL8 block 27 $9A33)
  loop over a table after their direction ON GOSUB and set $6DC9 = 255 when `COMPARE [$035F], [$6E7A]` is equal.
  The residue there is the loop index (block 25: the loop's `COMPARE [$6E79], #8`) or the loop bound $6E7B (block
  26: 28 or 19; block 27: 8). No table entry equals it (nor 0), so **the test never fires** and $6DC9 is not set
  there.
- **$C059 in v1.3**: ECL4 block 10 vector 4 saves `[$C059]` into $4A16 when $4A00 = 0, and vector 0 writes $4A16 back
  to $C059 right before NEWECL, whose script init resets DS:6F68. So the residue only ends up in $4A16 (saved with
  the game); it has no visible effect. That SAVE's residue comes from LOADFILES/LOADPIECES' deeper calls `[?]`.

**$6B00-area redirection** (program read 2026-11-17). "Record" = the current character's record (far pointer
DS:5D92; offsets as in `character-format.md`). A **read** tries the list in routine 0x7C8F first and falls back to the
cell. A **write** always stores the 16-bit cell, then applies the side effects in routine 0x7F47. So an address with
a read case returns the record value, not what was written, unless it also has a write case.

| Address | Read (0x7C8F) | Write side effect (0x7F47) |
|---|---|---|
| $6B00 | cell | value 0 sets DS:84D3 `[?]` |
| $6B18 | record 0x14, byte | – |
| $6B20–$6B70 | cell | low byte to record 0x18 + (A − $6B20) (0x18..0x68) |
| $6B9B | record 0x6E, byte | – |
| $6BA0 | record 0x73, signed byte | – |
| $6BA5–$6BAC | record 0x77..0x7E, bytes | – |
| $6BB8 | record 0x84, byte | low byte to record 0x84; a value > 0xB2 is first reduced by 0x32 |
| $6BBB, $6BBD, $6BBF, $6BC1, $6BC3 | record 0x88, 0x8A, 0x8C, 0x8E, 0x90, word | word to the same field |
| $6BD8 | record 0xA0, signed byte | – |
| $6BF7 | record 0xB8, word | word to record 0xB8 |
| $6BF9 | record 0xBA, byte | low byte to record 0xBA |
| $6C00 | 1 if record 0x10D ≠ 0, else 0x80 | value ≥ 0x80: record 0x10D = 0, and 0x87 also sets record 0x10C = 7; value 0 sets DS:84D2 `[?]` |
| $6C0C | 0x80 if record 0x10E = 0 and 0x10F ≠ 0; 0x81 if 0x10E = 1; else 0 | 0: 0x10E = 0x10F = 0; 0x80: 0x10E = 0, 0x10F = 1; 0x81: both 1 |
| $6C0D | undefined (case present, value never set) | – |
| $6C1B | record 0x11C, byte | – |
| $6DB1, $6DB4 | 0-based position of the current character in the party list | – |
| $6DCF | from record 0x15: 3→0, 4→5, 5→10, 6→15, 7→20, 8–12→25, 13→30, 14→35, 15→40, 16→50, 17→55, 18–25→60; other values undefined | – |
| $6E12 | disk number DS:5376 | sets DS:5376 (see "Where the scripts live") |
| $6E22, $6E24, $6E26 | cell | value > 0x80: load wall set (value AND 0x7F) into slot 1, 2, 3 (0x2F6FF) |
| $6E3E | cell (read through the same storage) | – |
- Meanings of individual addresses: Lee 12.4.1–12.4.2 and `save-format.md` "Known GBVM cells". Not duplicated here.

## Scripts and areas

From `ecl.py list` (decoded LOADFILES/NEWECL/LOADPIECES operands on reachable code; `[$addr]` = computed).
Area names as in `geo-format.md` (Lee's chapter 6).

| File | Block | Area | LOADFILES (map) | NEWECL to | LOADPIECES |
|---|---|---|---|---|---|
| ECL1 | 18 | Podal Plaza | 18 | 2, 9, 26, 29 | 1/3/5 |
| ECL1 | 24 | Temple of Bane (map 24) + Wealthy Area (map 31); see note | 24, 31, [$6E79] = 14, 24, 26 or 31 by facing (table at $99A8; 14 and 26 after switching the disk) | [$6E7D] | 1/3/5 |
| ECL2 | 9 | Stojanow Gate | 9 | 6, [$6E79] | 1/9/23 |
| ECL2 | 15 | Mendor's Library | 15 | [$6E7F] | 1/19/23 |
| ECL2 | 20 | Slums | 20 | 0, 29 | 2/4/1 |
| ECL3 | 0 | New Phlan, civilised area | 0, 255 | 8, 11, 20, 21, 26, 27 | 127/127/127 |
| ECL3 | 8 | Civilised area (council) | 0 | 0 | 127/127/127 |
| ECL3 | 11 | Civilised area (training hall) | 0 | 0 | 127/127/127 |
| ECL3 | 14 | Kovel Mansion | 14 | 24, 26 | 1/3/5 |
| ECL4 | 2 | Cadorna Textile House | 2 | 15, 18, 26 | 1/3/20 |
| ECL4 | 10 | Valhingen Graveyard | 10 | 26 | 21/22/20 |
| ECL4 | 21 | Sokal Keep | 21, 255 | 0 | 1/5/9 |
| ECL5 | 3 | Valjevo Castle (NW + SE) | 255, [$6E82] = map 3 or 5 by facing (table at $9C24) | 5, [$6E79] | 24/25/1 |
| ECL5 | 4 | Valjevo Castle (NE) | 4, 255 | 3 | 24/25/1 |
| ECL5 | 5 | Valjevo Castle hedge maze | [$6E79] = 3 + Random(4) ($9CD4), [$6E82] = by facing from a table per map 3–6 ($9AD8; unset on another map) | 3, 7 | – |
| ECL5 | 6 | Valjevo Castle (SW) | 6 | [$6E79] | 24/25/1 |
| ECL5 | 7 | Valjevo Castle inner tower | 3, 7 | 0, 5 | – |
| ECL6 | 1 | Buccaneer Base | 1 | 25 | 2/6/6 |
| ECL6 | 19 | Silver dragon lair | – | 25 | – |
| ECL6 | 25 | Western wilderness | 4, 25, 127 | 1, 19, 25, 26, 28 | 17/17/17 |
| ECL6 | 28 | Zhentil Keep Outpost | 28 | 25 | 1/2/9 |
| ECL7 | 17 | Nomad Camp | 17 | 26 | 7/8/23 |
| ECL7 | 22 | Yarash's Pyramid 1 | 22 | 23, 26 | 1/3/255 |
| ECL7 | 23 | Yarash's Pyramid 2–3 | 23, 255 | 22 | 1/3/255 |
| ECL7 | 26 | Central wilderness | 5, 26, 127 | 0, 2, 10, 14, 17, 18, 22, 24, 25, 26, 27 | 7/8/23 |
| ECL8 | 13 | Kobold Caves | 13 | 27 | 17/18/255 |
| ECL8 | 16 | Lizardman Keep (+ catacombs) | 16, 30 | 27 | 9/17/23, 9/20/23 |
| ECL8 | 27 | Eastern wilderness | 6, 27, 127 | 0, 13, 16, 26, 27 | 17/17/17 |
| ECL8 | 29 | Kuto's Well (+ catacombs) | 29, 32 | [$6E82] | 3/20/1, 18/17/1 |

- Map ids 255 and 127, and the small ids 4/5/6 loaded by the three wilderness scripts, are not GEO blocks of those
  disks. LOADFILES loads a GEO block only while $49E6 ≠ 0; outdoors ($49E6 = 0) it loads a fixed `BACPAC.DAX`
  picture instead (see "Other handlers").
- Maps 24 and 31 (settled 2026-11-17, script read of ECL1 block 24 plus the program): **24 = Temple of Bane,
  31 = Wealthy Area**, as `geo-format.md` has it; Lee's script list labels map 31 "Temple of Bane", which is the
  slip. Evidence: (1) the program writes $49C5 directly only in LOADFILES (GAME.OVR 0x1A1E) and the GEO loader
  (0x2FAF2), not in NEWECL (search for direct stores; the only script writes, in blocks 14 and 24, save and restore
  it around a call), so on entry from block 26 or 14 the init vector ($B6A3) sees $49C5 ≠ 24 and loads map 31; it loads 24
  only when the save was made in map 24. (2) The edge table at $99A8/$99B0 (indexed by facing, +4 in map 31): map
  24 leads only east, to map 31; map 31 leads north to Kovel Mansion (block 14), east to the central wilderness
  (block 26) and west to map 24. So 31 is the outer district and 24 the inner one. (3) In the search vector, map 31
  events are handled before $A3D4 and map 24 events from $A3D4 on; the temple / altar texts are almost all in the
  map-24 part (counted, 16 of 18 mentions).
- This replaces the byte-pattern scan behind `geo-format.md`'s "Loaded by" column: block 3 picks map 3 or 5 from a
  facing table, and block 5 loads computed maps only (the scan found nothing there).

## Program references (PoR v1.3 `GAME.OVR`, file offsets)

| Offset | What |
|---|---|
| 0x3C71 | Opcode dispatcher (compare chain on DS:6F7F) |
| 0x3EB7 | Run loop: PC = argument, execute until stop flag |
| 0x403E | On load: runs vector 4 |
| 0x3EFA | After a script switch: vector 4, then 0 and 1 |
| 0x4100–0x424D | Main-loop call sites of vectors 0 and 1 |
| 0x3B19 | Runs vector 2, then conditionally vector 3 (camp command) |
| 0x7689 | Script init: vectors, stack and flag reset, $4A00–$4A1F clear |
| 0x74D3 | Operand loader (the overlay unit that holds the VM helpers starts here; its stub segment is START.EXE para 0x2B) |
| 0x7609 | Operand value fetch |
| 0x7B82 | lo/hi → word |
| 0x7BCE | Address region classifier |
| 0x82DF / 0x814A | VM read / VM write |
| 0x8626 / 0x7BA6 | Text unpacker / 6-bit character map |
| 0x8DBE | Compare-flag setter |
| 0x8E24 | GOSUB push |
| 0x9379 | IF-skip (second operand-count table) |
| 0x0D47–0x3BC3 | Opcode handlers (targets of the dispatcher's near calls; e.g. 0xE6C COMPARE, 0xEE9 ADD–MUL, 0xF7A RANDOM, 0x19BA LOADFILES/LOADPIECES) |
| 0x7C8F / 0x7F47 | $6B00-area read redirection / write side effects |
| 0x87CE / 0x8426 | Text from VM memory / SAVE of text |
| 0x8CE8 | Text compare → flags |
| 0x7814 | Script file loader (`ECL<d>.DAX`) |
| 0x196A0 / 0x24A66 | Camp menu / rest loop (sets "rest interrupted") |
| 0x2BCCD | Dice roll (count × (Random(sides)+1)) |
| 0x7955 / 0x7A21 | Encounter view distance / encounter draw |
| 0x9484 | Fastest / slowest movement of the party list |
| 0x1EEAA | Monster loader (`MON<d>CHA/ITM/SPC`) |
| 0x174F / 0x3807 | PRINT/PRINTCLR handler / CLOCK handler |
| 0x2DCF4 | 3D-view redraw (applies the $C059/$C05F colour codes when DS:84E5 is set) |
| START.EXE 0x1C13 | Number → text (`Str` of an unsigned 16-bit value, no padding), used by PRINT |

Far calls `9a off seg` go through overlay stub segments in START.EXE (paragraph `seg` of the unpacked image): the
stub header holds the unit's GAME.OVR file offset (dword at +4) and entry `off` holds the routine offset at
`off + 2`; e.g. VM-helper stub 0x2B has file offset 0x74D3, so `2B:25` → 0x7609 and `2B:57` → 0x82DF.

Data segment variables (DS = START.EXE load segment + 0xC7C): PC 49ED, vectors 49E3–49EB, script pointer 49DE,
opcode 6F7F / previous 6F7E, compare flags 6F78–6F7D, GOSUB stack head 6F70, operand arrays (operand k from 1:
type 7005+k, high byte 7045+k, low byte 7085+k; so "operand 0" reads 7005, 7045 = type of operand 64, 7085 = high
byte of operand 64), string slots 6FC8 + 256·k, special-address bytes ($C059 6F68, $C05F 6F69, $00FB 6F6A, $00FC
6F6C, $00B1 6F6E, $03DE 84CC, $00B8 84CE, $00B9 84D0), redraw flags 84E0/84E3/84E5/84E6/84E8, stop flag 442E,
script-changed flag 442F, party-defeated flag 49FF, loading-a-save flag 49F8, disk number 5376
(previous 5377), game speed 49E2, RandSeed 4428/442A, current character pointer 5D92, party list head 5D96. The installed DOSBox-X has no debugger; `tools/scripts/dosbox_state.py` reads these from save
states.

## Verification (2026-11-17)

Run: `python3 -I tools/scripts/ecl.py check <gamedir>` on PoR DOS v1.3 (files as listed in Lee 12.1.3).

1. **Headers:** all 29 blocks start with u16 0x1388, followed by five `01` + type-1 operands, every vector inside
   the block.
2. **Decode:** flow analysis from the five vectors reaches **16,015 instructions**. Of 176,207 script bytes
   (blocks minus prefix): 164,522 (93.4 %) on hard paths (including the 20-byte vector tables), 8,311 (4.7 %) only on
   speculative paths (fall-through after ON GOTO, IF-skip past a mis-skipped instruction), 3,374 (1.9 %) not reached.
   The instruction after a GOSUB or ON GOSUB is a hard edge: the game resumes there when the routine returns, and
   ON GOSUB also falls through when the selector is out of range (s10; before, ON GOSUB's next instruction was
   counted as speculative: 158,974 hard, 13,859 speculative, same 16,015 instructions). `EclAnalysis.ByteCoverage`
   gives the same three numbers (`PorScriptTests`).
   - **0 decode errors on hard paths, 0 on speculative paths, 0 overlapping instructions** (no jump or call target
     lands inside another instruction), 0 unknown opcodes reached, 0 type-0 jump targets.
   - The unreached bytes: 56 runs / 3,162 bytes are tables addressed by script operands; 61 runs / 202 bytes decode
     as instructions but are never jumped to (mostly a lone EXIT after a GOTO or RETURN); 10 single bytes at block
     ends or between tables are unexplained filler.
   - Speculative paths are cut where they reach a byte that some operand addresses: 10 such cuts (`check -v`
     lists them). These are ON GOTO fall-throughs into the table that follows them; decoding them as code gives
     garbage (bad types, jumps out of the script).
3. **Operand types used:** 0x00 9,176; 0x01 13,099; 0x02 43; 0x03 75; 0x80 2,556; 0x81 263. No other type on reached
   code.
4. **Text:** 2,556 inline texts, 92,334 packed bytes → 121,983 characters; 227 are empty (length 0). External check
   of the codec and of operand boundaries: of the 1,551 decoded texts with ≥ 20 characters, 287 occur verbatim
   (whitespace-normalised) in Lee's FAQ, which quotes part of the game's text.
5. **IF-skip hazard:** no IF is followed by 0C, 15, 25, 26, 2B or 36 on any reached path.
6. **Disk of NEWECL targets:** of 73 immediate NEWECL operands, 32 are preceded (within 5 instructions) by
   `SAVE #d, [$6E12]`, and in all 32 block *n* is in `ECL<d>.DAX`; 40 others target a block in the same file; one
   (ECL2 block 9 → 6) sets $6E12 elsewhere `[?]`.
7. **Save cross-check:** both shipped saves (`SAVGAMA`, `SAVGAMJ`) hold block 20 of `ECL2.DAX` from block offset 2,
   7,677/7,677 bytes equal, the last 3 bytes of the 7,680-byte area zero (also `save-format.md` check 3). Block
   offset 0 instead of 2 does not match.
8. **Program:** the operand count of every handler in the dispatch chain equals the table above (Ops); the IF-skip
   table equals the Ops column except for the six opcodes marked bold. Both counted directly from GAME.OVR.

9. **DOSBox comparison** (2026-11-17, `goldbox-tools ecl-compare GAMEDIR`; method in
   `docs/re/dosbox-trace-method.md`): the C# VM replays every golden capture and reproduces it. The cells the VM
   does not own are listed per capture (clock, wall-set slot $4AFF). Since the encounter host (`EncounterHost`) the
   distance $6DC0/$6DC1 is compared too.
   - Both E9 traces are followed instruction for instruction (PC, opcode, flags before each step): load + step,
     54/54, and then camp + interrupted rest, 96/96 (rolls found by search, since that boot's seed is unknown).
     The encounter trace (load, camp, interrupted rest, ENCMENU, Advance, Flee) follows 95/95.
   - The state captures match cell for cell in the four VM areas, plus PC, opcode, previous opcode, flags, GOSUB
     stack (empty or not), disk, position and RandSeed: load A, step, encamp, interrupted rest, door pick, door
     bash, load B, edge wrap, the three encounter-menu states (`encmenu_clamp`, `encmenu_advance`, and since s10 `encmenu_flee`, whose party move is script code: $C04B–$C04D from the flee path at $B225), the edge exit `edge_exit_east` (s10: CALL $C01E, NEWECL to ECL3 block 0 and its vectors), the open-items save D (CLOCK, special reads, COMPARE residue; s9), and the four one-way wall steps (patched saves E and F,
     two walked through, two refused; s10).
   - The open-items trace (s9) is followed 34/34 up to the first instruction of the 0x1F hang, where the VM stops
     with `UnhandledOpcode`.
   - No script runs for the turn. The fight is captured inside COMBAT in both states, so all its changes are
     host-side.
   - Draws by program code the VM does not model, found as the count that reproduces RandSeed:
     - 2 before vector 3 in the rest. That fits the rest loop as read, one 1d100 per 24-tick interval against
       $6DD3 = 24: from the captured seed the two rolls are 93 (no encounter at 12:50) and 12 (interrupted at
       14:50). This also checks the generator and the dice rule on rolls made by the program, not the VM.
     - 6 for the failed pick and 3 for the bash: read from the door routines since (one 1d100 per member for
       Pick, one 1d6 per member with Strength in the table for Bash, until a success; `geo-format.md` "Door menu").

## Open `[?]` (DOSBox trace items marked T)

- Settled by program read 2026-11-17 (were T items): vector 2/3 roles, DS:49FF, DS:49F8, text COMPARE, CMPAND /
  AND / OR / FINDITEM flags, LOADFILES/LOADPIECES cases, DS:5376 = $6E12, $6B00-area redirection.
- Settled in session 9 (program read plus the open-items capture, patched save D): opcodes 0x1F / ≥ 0x3E hang;
  the CLOCK amount (0 in v1.3); PRINT of a number; the special addresses ($00B1/$00FB/$00FC, $033D, $035F, $C059,
  $C05F, $03DE, $00B8/$00B9, $D022, $C04B sign) and what $035F / $C059 give the v1.3 scripts; $6BF2 is a plain
  cell; the variadic tail reloads operand and string slots from 1; menu counts take the low byte; SAVE copies text
  for any type ≥ 0x80. Checked by the
  DOSBox comparison ("Verification" 9): RANDOM and the RandSeed formula, SURPRISE's formula and roll order,
  PARTYSTR, the script init on a save load.
- Settled 2026-11-17 (program read + `encmenu_*` captures): the encounter distance, SETUPMON, APPROACH, LOADMON,
  CLRMON, ENCMENU, COMBAT's entry, PARLEY, PICTURE, SPRITEOFF ("Encounters"). Settled in session 10
  (`combat-format.md`): the monsters LOADMON appended are removed by the after-combat step (0x672A); the top level of
  the fight (0x9881), the after-combat step (0x6AA5, with the class split of the experience checked against the
  `combat_win` save), the structure of the shop (0x7212) and temple (0x5280); CALL $C01E (0x8EAE, step forward; the
  encounter flee in ECL2 block 20 is script code, and `encmenu_flee` now replays); TREASURE's per-item call 00BA:0442
  is the naming routine. Still open: the fight's actions and the loot/shop/temple details (`combat-format.md` "Open");
  TREASURE's item maker 0x25C99; the item type for a second roll of 48/49 (sampled s18 and s24: 0 at all seven v1.3
  sites, `item-format.md`); the icon slot DS:6F83 before the first
  CLRMON; COMBAT's $6DC1 limit is not discriminated by a capture (the distance was already within the view).
- Program-side random draws: the rest loop and the door menu follow their read rules (door model
  `GoldBox.Core.Doors`, `geo-format.md`); the comparison harness still searches the door draw counts.
- LOADPIECES with $49E7 and $49E8 both non-zero: whether an operand of 255 empties slot 1 or 3 there (the VM applies
  the same 255 rule) `[?]`.
- PARTYSTR reads the level bytes as signed in the record; whether the routine treats −1 as −1 is not read (the VM
  reads them unsigned; party members never hold −1).
- Which character is current right after a save is loaded (DS:5D92) `[?]`; the VM uses the first member.
- The forward-step key sets $6DD5 only when the face ahead is not impassable (0x16D58, "Steps, doors and script
  switches", `DoorMap.LeavesMap`); the comparison harness applies it from the map planes since s11. Not checked in
  DOSBox (no capture steps into an edge wall); program read only.
- Valjevo hedge maze (ECL5 block 5): the edge table's in-place reload (map 6's south gate) and the event-7
  disorientation were run in DOSBox-X from patched saves (s18, `hedge_gate`, `hedge_disorient`) and replay in the
  VM (`geo-format.md` "Mid-visit reloads"). Settled.
- T: Which value $49E6 has at each wilderness LOADFILES 4/5/6/127/255 (scripts write it; not followed per path).
- Meanings of DS:84D2 / DS:84D3 (set by writes of 0 to $6C00 / $6B00) and of the record bytes in the redirection
  table that `character-format.md` names differently from Lee.
- CLOCK when the previous handler is not GOSUB: the operand index is other stack residue `[?]` (never in v1.3).
- Settled in session 11 (program read): LOADCHAR, CHKPARTY, DAMAGE, ADDNPC, WHO, SPELL, PROTECT, PRINTRET,
  CLEARBOX, ROB's item rule, CALL $C018, the impassable edge face ($6DD5), the step into a door face, NEWECL in a
  vector 4 ("Party, damage and text-box opcodes", "Steps, doors and script switches"). DOSBox capture
  `party_opcodes` (s11) checks LOADCHAR, ROB (money rounding included), SPELL, CHKPARTY and DAMAGE. Still open:
  DAMAGE's effect handlers (trigger classes 12 and 16), PROTECT's glyph drawing (display, not captured).
- The text box layout of PRINT/PRINTCLR: program read in s12 (`view3d.md` "Text window").
- Stack residue outside COMPARE: what an undefined read returns in other handlers (e.g. block 10's `SAVE [$C059]`,
  after LOADFILES/LOADPIECES) and what an unknown operand type's value fetch returns. The VM uses
  `EclVmPolicy.UndefinedValue` and logs it; no v1.3 outcome depends on it.
- Unreferenced data runs (10 single bytes) — padding or dead tables.
