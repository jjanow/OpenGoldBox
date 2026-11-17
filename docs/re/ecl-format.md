# ECL event scripts (Pool of Radiance, DOS v1.3)

Status: **bytecode layout and opcode table verified** (2026-11-17) against all 29 script blocks of PoR DOS v1.3
(`ECL1.DAX` .. `ECL8.DAX`) and against the interpreter in the game program (`GAME.OVR`, see "Program
references"). Every block disassembles along all hard control-flow paths with no decode error and no instruction
overlap (see "Verification"). Operand counts, operand encoding, string packing, the IF-skip rule, the entry vectors and
the VM address regions are confirmed in the program. What most opcodes *do* beyond moving operands is taken from Lee's
FAQ and is only partly checked in the program; the column "Effect from" says which. No DOSBox trace yet.

Tool: `tools/scripts/ecl.py` (`list`, `dis`, `check`). Container: `docs/re/dax-format.md`. Save area holding the
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
  The program builds the file name as `ECL` + a disk number held at DS:5376 + `.DAX` in the NEWECL handler;
  the link between DS:5376 and VM $6E12 is not traced `[?]`.

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
  - the main-loop code at 0x4100–0x424D runs vector 0 then vector 1 (0x4199, 0x4228) and vector 1 alone at 0x4151;
    which player actions lead to each call site (step, Look) is not traced `[?]`;
  - after any run that switched scripts (NEWECL), routine 0x3EFA runs the new script's vector 4, then vector 0 and,
    unless the script changed again, vector 1, and repeats while scripts keep changing;
  - routine 0x3B19 runs vector 2, then calls another routine that fills a local flag, and runs vector 3 only if that
    flag is set. The main loop calls 0x3B19 when the menu choice code is 0x45 ("E", the camp command). This supports
    Lee's "pre-camp" / "camp interrupted" reading; what sets the flag is not traced `[?]`.
- **On load** (script loader 0x7689): the GOSUB stack is cleared, the six compare flags are cleared and, unless a flag
  at DS:49F8 is set (presumably "restoring a save" `[?]`), VM $4A00–$4A1F are zeroed (Lee: per-area scratch flags).
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
| 0x80 | 1 + n: `n`, then n packed bytes | Inline text (below) |
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

## Instructions and control flow

An instruction is one opcode byte plus its operands. The run loop (GAME.OVR 0x3EB7) sets the program counter (PC,
DS:49ED) to a vector, then repeatedly stores the opcode at PC (DS:6F7F; previous opcode DS:6F7E) and calls the
dispatcher (GAME.OVR 0x3C71, a compare chain over 0x00–0x3D), until a stop flag (DS:442E) is set or DS:49FF is
non-zero `[?]` meaning of the latter.

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
- **COMPARE** stores all six flags from its two values as unsigned 16-bit numbers, comparing operand 1 against
  operand 2 (`IFLT` is true when operand 1 < operand 2) (flag setter GAME.OVR 0x8DBE). Text operands (type ≥ 0x80)
  are compared as strings by another routine `[?]` details.
- **GOSUB / RETURN.** GOSUB pushes the PC (already on the next instruction) onto a linked-list stack (DS:6F70; no
  depth limit) and jumps. RETURN steps the PC past its opcode, then, if the stack is non-empty, pops the PC. **RETURN
  with an empty stack falls through** to the next instruction.
- **EXIT** clears the GOSUB stack, sets the stop flag and ends the run (plus some display bookkeeping).
- **NEWECL** loads the new script, runs no further instruction of the old one (sets the stop flag and a "script
  changed" flag DS:442F that makes the caller run the new script's vector 4 and then its move/search vectors again).
  Bytes after a NEWECL are therefore not reached by fall-through; v1.3 has inline tables right after NEWECL.
- **ON GOTO / ON GOSUB:** if the selector value is < count, jump (or call) to the selector-th target; otherwise
  fall through. The selector is compared as a byte.
- **Opcode 0x1F and ≥ 0x3E have no case in the dispatcher.** The PC is not advanced, so the run loop would execute
  the same byte forever `[?]` (not traced). None is reachable in v1.3.

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
| 04 | ADD | ADD | 3 | 3 | v1 v2 a | a = v1 + v2 | L | 317 |
| 05 | SUB | SUBTRACT | 3 | 3 | v1 v2 a | a = v2 − v1 (Lee: subtrahend first) | L | 76 |
| 06 | DIV | DIVIDE | 3 | 3 | v1 v2 a | a = v1 / v2, integer, no remainder, no zero check | L | 29 |
| 07 | MUL | MULTIPLY | 3 | 3 | v1 v2 a | a = v1 · v2 | L | 12 |
| 08 | RANDOM | RANDOM | 2 | 2 | v a | a = random 0..v (v ≤ 255) | L | 144 |
| 09 | SAVE | SAVE | 2 | 2 | v a | a = v; if v is text, store the text at a | P | 2341 |
| 0A | LOADCHAR | LOAD CHARACTER | 1 | 1 | v | Select current character (< 128) / add monster v−128 as ally (≥ 128) | L | 36 |
| 0B | LOADMON | LOAD MONSTER | 3 | 3 | v v v | Add monster, count, picture to the encounter list | L | 330 |
| 0C | SETUPMON | SETUP MONSTER | 3 | **2** | v v v | Start an encounter: monster, distance 0–2, picture | L | 187 |
| 0D | APPROACH | APPROACH | 0 | 0 | – | Encounter distance −1 | L | 97 |
| 0E | PICTURE | PICTURE | 1 | 1 | v | Show picture / head+body / 255 = back to the 3D view | L | 137 |
| 0F | INPUTNUM | INPUT NUMBER | 2 | 2 | v a | Read a number of up to v digits into a | L | 2 |
| 10 | INPUTSTR | INPUT STRING | 2 | 2 | v a | Read text of up to v characters into a | L | 20 |
| 11 | PRINT | PRINT | 1 | 1 | s | Print to the text box | L | 289 |
| 12 | PRINTCLR | PRINTCLEAR | 1 | 1 | s | Clear the text box, print | L | 1401 |
| 13 | RETURN | RETURN | 0 | 0 | – | Pop PC; empty stack: fall through | P | 440 |
| 14 | CMPAND | COMPARE AND | 4 | 4 | v1 v2 v3 v4 | Flags from (v1 vs v2) and (v3 vs v4); only = / ≠ meaningful (Lee) | L | 65 |
| 15 | VMENU | VERTICAL MENU | 3+n | **0** | a s n, s×n | Prompt s, list n choices, choice index → a | P (shape) | 15 |
| 16 | IFEQ | IF = | 0 | 0 | – | Run next instruction if "=" flag, else skip it | P | 690 |
| 17 | IFNE | IF <> | 0 | 0 | – | same, "≠" | P | 634 |
| 18 | IFLT | IF < | 0 | 0 | – | same, "<" | P | 180 |
| 19 | IFGT | IF > | 0 | 0 | – | same, ">" | P | 281 |
| 1A | IFLE | IF <= | 0 | 0 | – | same, "≤" | P | 24 |
| 1B | IFGE | IF >= | 0 | 0 | – | same, "≥" | P | 127 |
| 1C | CLRMON | CLEARMONSTERS | 0 | 0 | – | Reset encounter hostility and treasure | L | 251 |
| 1D | PARTYSTR | PARTYSTRENGTH | 1 | 1 | a | a = party strength | L | 18 |
| 1E | CHKPARTY | CHECKPARTY | 6 | 6 | v v a a a a | Min/max/avg of a party stat, or "anyone has effect" | L | 4 |
| 1F | (none) | undefined | – | 2 | – | No handler; PC not advanced `[?]` | P | 0 |
| 20 | NEWECL | NEWECL | 1 | 1 | v | Load script v, stop the current run | P | 79 |
| 21 | LOADFILES | LOAD FILES | 3 | 3 | v v v | Load map v (3D) or outdoor picture; refresh view (Lee) | L | 61 |
| 22 | PSURPRISE | PARTY SURPRISE | 2 | 2 | a a | Zero two surprise modifiers | L | 22 |
| 23 | SURPRISE | SURPRISE | 4 | 4 | v v v v | Roll surprise; result 0–3 to $6DCB (Lee; Lee notes swapped modifiers) | L | 22 |
| 24 | COMBAT | COMBAT | 0 | 0 | – | Fight the loaded monsters, or enter temple/shop per flags | L | 245 |
| 25 | ONGOTO | ON GOTO | 2+n | **0** | v n, t×n | Jump to target v if v < n, else fall through | P | 427 |
| 26 | ONGOSUB | ON GOSUB | 2+n | **0** | v n, t×n | Same as a call | P | 37 |
| 27 | TREASURE | TREASURE | 8 | 8 | v×8 | Coins ×5, gems, jewellery, item list for the next fight/shop | L | 77 |
| 28 | ROB | ROB | 3 | 3 | v v v | Take money % and items from one / all characters | L | 14 |
| 29 | ENCMENU | ENCOUNTER MENU | 14 | 14 | v v v a v×5 s s s v v | Encounter menu with reactions, texts per distance, move thresholds | L | 94 |
| 2A | GETTABLE | GETTABLE | 3 | 3 | a v a | a2 = byte/cell at (a1 + v) | L | 271 |
| 2B | HMENU | HORIZONTAL MENU | 2+n | **0** | a n, s×n | Bottom-line menu, choice → a | P (shape) | 204 |
| 2C | PARLEY | PARLAY | 6 | 6 | v×5 a | Choose a parley attitude, its value → a | L | 41 |
| 2D | CALL | CALL | 1 | 1 | a | Run a built-in routine chosen by the raw address ($2C90, $8000, $8001, $BA03, $C018, $C01E; else nothing) | P (ids) | 180 |
| 2E | DAMAGE | DAMAGE | 5 | 5 | v×5 | Attack/damage characters, optional save | L | 48 |
| 2F | AND | AND | 3 | 3 | v v a | a = v1 AND v2; sets flags for IF =/≠ | L | 356 |
| 30 | OR | OR | 3 | 3 | v v a | a = v1 OR v2; sets flags | L | 208 |
| 31 | SPRITEOFF | SPRITE OFF | 0 | 0 | – | Remove the encounter sprite `[?]` exact | L | 56 |
| 32 | FINDITEM | FIND ITEM | 1 | 1 | v | Flags = "someone carries item type v" | L | 7 |
| 33 | PRINTRET | PRINT RETURN | 0 | 0 | – | New line in the text box | L | 20 |
| 34 | CLOCK | ECL CLOCK | 1 | 1 | v | Advance time (Lee: buggy, uses a wrong variable) | L | 1 |
| 35 | SAVETABLE | SAVE TABLE | 3 | 3 | v a v | Store v1 at (a + v2) | L | 17 |
| 36 | ADDNPC | ADD NPC | 2 | **1** | v v | Add monster as NPC with morale | L | 8 |
| 37 | LOADPIECES | LOAD PIECES | 3 | 3 | v v v | Load wall sets for slots 1–3 (same handler as 21) | P (shared), L | 35 |
| 38 | PROGRAM | PROGRAM | 1 | 1 | v | 0 training hall, 8 end game, 9 camp; else nothing | P (ids) | 4 |
| 39 | WHO | WHO | 1 | 1 | s | Prompt, player picks a character (becomes current) | L | 11 |
| 3A | DELAY | DELAY | 0 | 0 | – | Pause `[?]` length | L | 188 |
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
| $6B00–$6EFF | 16-bit | 1024 words | Some addresses are redirected to the current character/monster record (routine 0x7C8F; Lee: $6B00–$6C1B) `[?]` exact list; the rest is encounter/combat/shop/scratch state. Saved (offset 2049) |
| $9700–$98FF | 16-bit | 512 words | Scratch (Lee). Saved (offset 4097) |
| $9900–$B6FF | 8-bit | 7680 bytes | The script itself. Scripts read their tables here and may write into it. Saved (offset 5121) |
| anything else | – | special | Read: $00B1, $00FB, $00FC, $033D (direction), $C04B x, $C04C y (signed), $C04D facing/2, $C04E, $C04F; other addresses return an undefined value `[?]`. Write: $03DE, $00B8, $00B9 (sound/other settings `[?]`), $C04B, $C04C, $C04D (0–3 → facing 0/2/4/6), $C059, $C05F; other addresses are ignored |

- Writes to $49FD/$49FE (sky/ceiling colour) have extra handling in the writer.
- Meanings of individual addresses: Lee 12.4.1–12.4.2 and `save-format.md` "Known GBVM cells". Not duplicated here.

## Scripts and areas

From `ecl.py list` (decoded LOADFILES/NEWECL/LOADPIECES operands on reachable code; `[$addr]` = computed).
Area names as in `geo-format.md` (Lee's chapter 6).

| File | Block | Area | LOADFILES (map) | NEWECL to | LOADPIECES |
|---|---|---|---|---|---|
| ECL1 | 18 | Podal Plaza | 18 | 2, 9, 26, 29 | 1/3/5 |
| ECL1 | 24 | Wealthy Area + Temple of Bane (maps 24, 31; see note) | 24, 31, [$6E79] | [$6E7D] | 1/3/5 |
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
  disks: per Lee, outdoors LOADFILES loads a 2D map picture from `BACPAC.DAX` instead `[?]` (not checked).
- Lee's script list calls 24 "Wealthy Area (and Temple of Bane)" and map 31 "Temple of Bane"; `geo-format.md`
  has the two names the other way round (24 Temple of Bane, 31 Wealthy Area) `[?]` which is right.
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

Data segment variables (DS = START.EXE load segment + 0xC7C): PC 49ED, vectors 49E3–49EB, script pointer 49DE,
opcode 6F7F / previous 6F7E, compare flags 6F78–6F7D, GOSUB stack head 6F70, operand arrays 7006/7086/7046,
string slots 6FC8 + 256·k, stop flag 442E, script-changed flag 442F. Useful as DOSBox debugger breakpoints.

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

## Open `[?]` (DOSBox trace items marked T)

- T: Vector roles 2 and 3 (camp), and the condition under which vector 3 runs.
- T: DS:49FF (stops the run loop) and DS:49F8 (skips the $4A00–$4A1F reset) meanings.
- T: Opcode 0x1F / ≥ 0x3E really hang (PC not advanced); never reached in v1.3.
- T: COMPARE / IF on text operands; CMPAND, AND, OR, FINDITEM flag setting (only COMPARE checked).
- T: LOADFILES outdoors (ids 4/5/6/127/255) vs `BACPAC.DAX`; DS:5376 vs $6E12 as the ECL disk number.
- T: Exact write width / redirection for $6B00-area addresses that map onto the character record.
- Semantics of all "L" rows beyond operand shape: from Lee only. Each needs a program read or a trace before the VM
  relies on it, in particular SUB operand order, RANDOM range, DIV by zero, SURPRISE modifiers, ROB weights,
  DAMAGE bit fields, ENCMENU reaction codes, CLOCK bug.
- Values returned for reads of unlisted "special" addresses and for unknown operand types (undefined in the
  program; a VM should log them).
- Unreferenced data runs (10 single bytes) — padding or dead tables.
