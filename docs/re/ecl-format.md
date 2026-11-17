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
    (Left/Right) ran no script at all (RAM snapshot: PC unchanged, only the facing changed);
  - after any run that switched scripts (NEWECL), routine 0x3EFA runs the new script's vector 4, then vector 0 and,
    unless the script changed again, vector 1, and repeats while scripts keep changing;
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
  $6DE1 = 255, $6DD2 = $6DD3 = 0, $49E5 = 0, and after reading the vectors $49E6 = 1. Unless DS:49F8 is set, VM
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
| other | 1 | Loader accepts it and stores the byte; the value fetch returns an undefined value `[?]`. Never used by the scripts |

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
- **String slots count text operands only** (loader 0x74D3): the k-th operand of type 0x80 or 0x81 *in the
  instruction* goes to slot k (DS:6FC8 + 256·k), whatever its operand position. A type-0x80 text of length 0 gives an
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
- **Opcode 0x1F and ≥ 0x3E have no case in the dispatcher.** The PC is not advanced, so the run loop would execute
  the same byte forever `[?]` (not traced). None is reachable in v1.3.

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
- **CLOCK** (0x3807): passes an uninitialised local byte as the operand index to the value fetch, so the amount
  is undefined `[?]` (stack residue); it then adds that many minutes to the clock $49C6–$49CC (0x241A1, unit 1 =
  minutes, the same routine rest uses). Used once in v1.3.
- **INPUTNUM / INPUTSTR** (0x1697 / 0x16DA): the length operand is loaded but never fetched.

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
| 09 | SAVE | SAVE | 2 | 2 | v a | a = v; if v is text, store the text at a | P | 2341 |
| 0A | LOADCHAR | LOAD CHARACTER | 1 | 1 | v | Select current character (< 128) / add monster v−128 as ally (≥ 128) | L | 36 |
| 0B | LOADMON | LOAD MONSTER | 3 | 3 | v v v | Add monster, count, picture to the encounter list | L | 330 |
| 0C | SETUPMON | SETUP MONSTER | 3 | **2** | v v v | Start an encounter: monster, distance 0–2, picture (DOSBox: $6DC0 = distance; see open list) | L | 187 |
| 0D | APPROACH | APPROACH | 0 | 0 | – | Encounter distance −1 ($6DC1, see open list) | L | 97 |
| 0E | PICTURE | PICTURE | 1 | 1 | v | Show picture / head+body / 255 = back to the 3D view | L | 137 |
| 0F | INPUTNUM | INPUT NUMBER | 2 | 2 | v a | Read a number into a; v is ignored (input up to 6 characters, re-asked until 0..65536; low 16 bits stored) | P | 2 |
| 10 | INPUTSTR | INPUT STRING | 2 | 2 | v a | Read text into a (SAVE-text rules); v is ignored: up to 40 characters 0x20–0x7A, upper-cased, empty input stored as one space | P | 20 |
| 11 | PRINT | PRINT | 1 | 1 | s | Print to the text box | L | 289 |
| 12 | PRINTCLR | PRINTCLEAR | 1 | 1 | s | Clear the text box, print | L | 1401 |
| 13 | RETURN | RETURN | 0 | 0 | – | Pop PC; empty stack: fall through | P | 440 |
| 14 | CMPAND | COMPARE AND | 4 | 4 | v1 v2 v3 v4 | "=" if v1 = v2 and v3 = v4, else "≠"; other flags 0 | P | 65 |
| 15 | VMENU | VERTICAL MENU | 3+n | **0** | a s n, s×n | Prompt s, list n choices, choice index → a | P (shape) | 15 |
| 16 | IFEQ | IF = | 0 | 0 | – | Run next instruction if "=" flag, else skip it | P | 690 |
| 17 | IFNE | IF <> | 0 | 0 | – | same, "≠" | P | 634 |
| 18 | IFLT | IF < | 0 | 0 | – | same, "<" | P | 180 |
| 19 | IFGT | IF > | 0 | 0 | – | same, ">" | P | 281 |
| 1A | IFLE | IF <= | 0 | 0 | – | same, "≤" | P | 24 |
| 1B | IFGE | IF >= | 0 | 0 | – | same, "≥" | P | 127 |
| 1C | CLRMON | CLEARMONSTERS | 0 | 0 | – | Reset encounter hostility and treasure | L | 251 |
| 1D | PARTYSTR | PARTYSTRENGTH | 1 | 1 | a | a = party strength (formula below) | P | 18 |
| 1E | CHKPARTY | CHECKPARTY | 6 | 6 | v v a a a a | Min/max/avg of a party stat, or "anyone has effect" | L | 4 |
| 1F | (none) | undefined | – | 2 | – | No handler; PC not advanced `[?]` | P | 0 |
| 20 | NEWECL | NEWECL | 1 | 1 | v | Load script v, stop the current run | P | 79 |
| 21 | LOADFILES | LOAD FILES | 3 | 3 | v v v | 3D: load GEO map v1; outdoors: BACPAC picture (see below) | P | 61 |
| 22 | PSURPRISE | PARTY SURPRISE | 2 | 2 | a a | a1 = 1 if a party member's class is ranger or cleric/ranger, else 0; a2 = 0 | P | 22 |
| 23 | SURPRISE | SURPRISE | 4 | 4 | v v v v | Roll surprise, result 0–2 to $6DCB (formula below; 3 is never stored) | P | 22 |
| 24 | COMBAT | COMBAT | 0 | 0 | – | Fight the loaded monsters, or enter temple/shop per flags | L | 245 |
| 25 | ONGOTO | ON GOTO | 2+n | **0** | v n, t×n | Jump to target v if v < n, else fall through | P | 427 |
| 26 | ONGOSUB | ON GOSUB | 2+n | **0** | v n, t×n | Same as a call | P | 37 |
| 27 | TREASURE | TREASURE | 8 | 8 | v×8 | Coins ×5, gems, jewellery, item list for the next fight/shop | L | 77 |
| 28 | ROB | ROB | 3 | 3 | v v v | v1 = 0: current character, else every party member; money × Real(100 − v2) / 100.0 (Real divide START.EXE 0xC10C; applied by 0x9196 `[?]` rounding), items by v2 (0x92C3) `[?]` item rule; v3 unused | P (partly) | 14 |
| 29 | ENCMENU | ENCOUNTER MENU | 14 | 14 | v v v a v×5 s s s v v | Encounter menu with reactions, texts per distance, move thresholds | L | 94 |
| 2A | GETTABLE | GETTABLE | 3 | 3 | a v a | a2 = cell at (a1 + low byte of v) | P | 271 |
| 2B | HMENU | HORIZONTAL MENU | 2+n | **0** | a n, s×n | Bottom-line menu, choice → a | P (shape) | 204 |
| 2C | PARLEY | PARLAY | 6 | 6 | v×5 a | Choose a parley attitude, its value → a | L | 41 |
| 2D | CALL | CALL | 1 | 1 | a | Run a built-in routine chosen by the raw address ($2C90, $8000, $8001, $BA03, $C018, $C01E; else nothing) | P (ids) | 180 |
| 2E | DAMAGE | DAMAGE | 5 | 5 | v×5 | Attack/damage characters, optional save | L | 48 |
| 2F | AND | AND | 3 | 3 | v v a | a = low byte of v1 AND v2; "=" when the result is 0 | P | 356 |
| 30 | OR | OR | 3 | 3 | v v a | a = low byte of v1 OR v2; flags as AND | P | 208 |
| 31 | SPRITEOFF | SPRITE OFF | 0 | 0 | – | Remove the encounter sprite `[?]` exact | L | 56 |
| 32 | FINDITEM | FIND ITEM | 1 | 1 | v | "=" if a party member carries an item of type v, else "≠" | P | 7 |
| 33 | PRINTRET | PRINT RETURN | 0 | 0 | – | New line in the text box | L | 20 |
| 34 | CLOCK | ECL CLOCK | 1 | 1 | v | Advance the clock by n minutes; bug: n is fetched from an uninitialised operand index | P | 1 |
| 35 | SAVETABLE | SAVE TABLE | 3 | 3 | v a v | Store v1 at (a + v2), v2 full 16 bits | P | 17 |
| 36 | ADDNPC | ADD NPC | 2 | **1** | v v | Add monster as NPC with morale | L | 8 |
| 37 | LOADPIECES | LOAD PIECES | 3 | 3 | v v v | Load wall sets for slots 1–3 (same handler as 21; see below) | P | 35 |
| 38 | PROGRAM | PROGRAM | 1 | 1 | v | 0 training hall, 8 end game, 9 camp (then EXIT: the run ends, 0x3BBC); else nothing | P (ids) | 4 |
| 39 | WHO | WHO | 1 | 1 | s | Prompt, player picks a character (becomes current) | L | 11 |
| 3A | DELAY | DELAY | 0 | 0 | – | Pause for game speed (DS:49E2, 0–9) × 225 ms (Crt-style Delay, START.EXE 0x7E4E/0xA69C); speed 0 = none | P | 188 |
| 3B | SPELL | SPELL | 3 | 3 | v a a | Find a character with spell v (Lee: no-op in practice) | L | 1 |
| 3C | PROTECT | PROTECTION | 1 | 1 | a | Copy-protection phrase check from a | L | 12 |
| 3D | CLEARBOX | CLEAR BOX | 0 | 0 | – | Clear the text box | L | 27 |

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
| anything else | – | special | Read: $00B1, $00FB, $00FC, $033D (direction), $C04B x, $C04C y (signed), $C04D facing/2, $C04E, $C04F; other addresses return an undefined value `[?]`. Write: $03DE, $00B8, $00B9 (sound/other settings `[?]`), $C04B, $C04C, $C04D (value mod 4 → facing 0/2/4/6, by repeated subtraction of 4, 0x8264), $C059, $C05F; other addresses are ignored |

- Writes to $49FD/$49FE (sky/ceiling colour) have extra handling in the writer.

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
| ECL1 | 24 | Temple of Bane (map 24) + Wealthy Area (map 31); see note | 24, 31, [$6E79] | [$6E7D] | 1/3/5 |
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
| ECL5 | 5 | Valjevo Castle hedge maze | [$6E79], [$6E82] | 3, 7 | – |
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

Far calls `9a off seg` go through overlay stub segments in START.EXE (paragraph `seg` of the unpacked image): the
stub header holds the unit's GAME.OVR file offset (dword at +4) and entry `off` holds the routine offset at
`off + 2`; e.g. VM-helper stub 0x2B has file offset 0x74D3, so `2B:25` → 0x7609 and `2B:57` → 0x82DF.

Data segment variables (DS = START.EXE load segment + 0xC7C): PC 49ED, vectors 49E3–49EB, script pointer 49DE,
opcode 6F7F / previous 6F7E, compare flags 6F78–6F7D, GOSUB stack head 6F70, operand arrays 7006/7086/7046,
string slots 6FC8 + 256·k, stop flag 442E, script-changed flag 442F, party-defeated flag 49FF, loading-a-save flag 49F8, disk number 5376
(previous 5377), game speed 49E2, RandSeed 4428/442A, current character pointer 5D92, party list head 5D96. The installed DOSBox-X has no debugger; `tools/scripts/dosbox_state.py` reads these from save
states.

## Verification (2026-11-17)

Run: `python3 -I tools/scripts/ecl.py check <gamedir>` on PoR DOS v1.3 (files as listed in Lee 12.1.3).

1. **Headers:** all 29 blocks start with u16 0x1388, followed by five `01` + type-1 operands, every vector inside
   the block.
2. **Decode:** flow analysis from the five vectors reaches **16,015 instructions**. Of 176,207 script bytes
   (blocks minus prefix): 158,974 (90.2 %) on hard paths (including the 20-byte vector tables), 13,859 (7.9 %) only on
   speculative paths (fall-through after ON GOTO/ON GOSUB), 3,374 (1.9 %) not reached.
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
   does not own are listed per capture (clock, wall-set slot $4AFF, encounter distance $6DC0/$6DC1).
   - Both E9 traces are followed instruction for instruction (PC, opcode, flags before each step): load + step,
     54/54, and then camp + interrupted rest, 96/96 (rolls found by search, since that boot's seed is unknown).
   - The state captures match cell for cell in the four VM areas, plus PC, opcode, previous opcode, flags, GOSUB
     stack (empty or not), disk, position and RandSeed: load A, step, encamp, interrupted rest, door pick, door
     bash, load B and edge wrap.
   - No script runs for the turn. The fight is captured inside COMBAT in both states, so all its changes are
     host-side.
   - Draws by program code the VM does not model, found as the count that reproduces RandSeed:
     - 2 before vector 3 in the rest. That fits the rest loop as read, one 1d100 per 24-tick interval against
       $6DD3 = 24: from the captured seed the two rolls are 93 (no encounter at 12:50) and 12 (interrupted at
       14:50). This also checks the generator and the dice rule on rolls made by the program, not the VM.
     - 6 for the failed pick and 3 for the bash: counts from RandSeed only; what they roll is not read.

## Open `[?]` (DOSBox trace items marked T)

- Settled by program read 2026-11-17 (were T items): vector 2/3 roles, DS:49FF, DS:49F8, text COMPARE, CMPAND /
  AND / OR / FINDITEM flags, LOADFILES/LOADPIECES cases, DS:5376 = $6E12, $6B00-area redirection. Checked by the
  DOSBox comparison ("Verification" 9): RANDOM and the RandSeed formula, SURPRISE's formula and roll order,
  PARTYSTR, the script init on a save load.
- SETUPMON / APPROACH and the encounter distance (DOSBox, 2026-11-17): after SETUPMON with distance d and k
  APPROACHes, the captures hold $6DC0 = d and $6DC1 = 0 (interrupted rest: the script had put 2 in $6DC1 with
  GETTABLE, then SETUPMON …, 2 and two APPROACHes; door bash: SETUPMON …, 1 and one APPROACH). That fits Lee 12.4.2
  ($6DC0 requested, $6DC1 actual distance) and Lee's SETUPMON text (actual distance can be shorter because of
  obstructions), but the handlers are not read. The VM leaves both cells to the host `[?]`.
- Program-side random draws not modelled: how many draws the door pick (6 in the capture) and the bash (3) make,
  and on what. The rest loop's draws follow its read rule.
- LOADPIECES with $49E7 and $49E8 both non-zero: whether an operand of 255 empties slot 1 or 3 there (the VM applies
  the same 255 rule) `[?]`.
- String slots for variadic opcodes: whether the second operand load (the variadic tail) restarts the text count
  at slot 1 `[?]` (the VM continues it; no v1.3 instruction depends on it).
- PARTYSTR reads the level bytes as signed in the record; whether the routine treats −1 as −1 is not read (the VM
  reads them unsigned; party members never hold −1).
- Which character is current right after a save is loaded (DS:5D92) `[?]`; the VM uses the first member.
- The forward-step key sets $6DD5 only when the face ahead is not impassable (`geo-format.md`); the comparison
  harness does not read walls.
- T: Opcode 0x1F / ≥ 0x3E really hang (PC not advanced); never reached in v1.3.
- T: Which value $49E6 has at each wilderness LOADFILES 4/5/6/127/255 (scripts write it; not followed per path).
- Meanings of DS:84D2 / DS:84D3 (set by writes of 0 to $6C00 / $6B00) and of the record bytes in the redirection
  table that `character-format.md` names differently from Lee.
- CLOCK amount (uninitialised operand index; stack residue). One use in v1.3.
- Still "L" (Lee only): LOADCHAR, LOADMON, SETUPMON, APPROACH, PICTURE, PRINT*, CLRMON, CHKPARTY, COMBAT, TREASURE,
  ENCMENU, PARLEY, CALL details, DAMAGE bit fields, SPRITEOFF, PRINTRET, ADDNPC, WHO, SPELL, PROTECT, CLEARBOX; ROB's
  item rule (0x92C3).
- Values returned for reads of unlisted "special" addresses and for unknown operand types (undefined in the
  program; a VM should log them).
- Unreferenced data runs (10 single bytes) — padding or dead tables.
- T (found by the C# VM's headless runs from save A, 2026-11-17): scripts **write** $00B1, $00FB and $00FC, which
  this table lists as read-only specials (writes there would be ignored); the wilderness scripts 25, 26, 27 **read**
  $035F (not listed); block 10 reads $C059 (listed as write-only); block 18 at $A0AB has PRINTCLR with a type-1
  (memory) operand, so what PRINT does with a number is needed. Block 15 writes $6BF2 (inside Lee's
  character-record range).
