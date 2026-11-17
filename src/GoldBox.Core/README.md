# GoldBox.Core: ECL virtual machine

A headless VM for the event scripts (ECL) of Pool of Radiance, DOS v1.3. Built from our own spec,
`docs/re/ecl-format.md`, only. Code lives in `Ecl/`.

## What it covers

- **Decoder and flow analysis** (`EclDecoder`, `EclAnalysis`): the same rules as `tools/scripts/ecl.py`. On the
  29 v1.3 blocks it gives the same totals as `ecl.py check` (16,015 instructions, 0 errors, 0 overlaps).
- **VM** (`EclVm`): decodes at run time from the live script area at the PC, so scripts that write into
  $9900–$B6FF behave as in the game. It keeps the program's details:
  - operand slots persist between instructions, so a type-0 operand used as an address keeps an old high byte;
  - string slots: the k-th text operand of an instruction fills slot k and keeps it until a later text replaces
    it (so a COMPARE of a text with a number sees an old slot 2, as in the game);
  - variadic counts go through the value fetch;
  - the IF-skip uses its own, shorter operand counts for 0C, 36, 15, 25, 26 and 2B, and resumes inside the
    operands; the skipped opcode becomes the "previous opcode" (DOSBox);
  - RETURN on an empty stack falls through;
  - the script init sets its cells ($6DE1, $6DD2/$6DD3, $49E5, $49E6) and keeps $4A00–$4A1F and $6E79–$6E82
    only while DS:49F8 ("loading a save", `EclVm.LoadingSave`) is set;
  - NEWECL writes $49F2, loads and initialises the new script at once and stops the run; `FollowScriptChanges`
    then runs its vectors 4, 0 and 1;
  - PROGRAM 9 ends the run after the camp, DIV by zero is a fault.
- **Memory** (`EclMemory`):
  - the 16-bit areas $4900, $6B00 and $9700, and the 8-bit script area (writes keep the low byte);
  - the disk byte DS:5376 (`Disk`), which is VM $6E12;
  - the $6B00-area redirection table: reads of the listed addresses come from the current character's record,
    writes store the cell and then apply the side effects; $6E22/$6E24/$6E26 load wall sets;
  - the text rules of SAVE and type-0x81 operands ($6B00 = the current character's name);
  - the special addresses ($C04B–$C04F party position, plus the other listed reads and writes);
  - reads of unlisted addresses return a policy value and are logged.
- **Party** (`EclParty`, `EclCharacter`): the 285-byte records and their item records. Used by the redirection
  table, PARTYSTR, PSURPRISE and FINDITEM. `GoldBox.Data.Por.PorParty` loads it from the files a save names.
- **Random numbers** (`TurboPascalRandom`): the game's generator. Seed it from a capture to reproduce its rolls.
- **Save loader** (`GoldBox.Data.Por.PorSaveGame`): fills the memory and the position from `SAVGAMx.DAT`;
  `LoadGame` does what the main menu's Load does (init with DS:49F8 set, then vector 4).
- **Host** (`IEclHost`): everything outside the VM, such as text, menus, combat, shops, files, pictures and random
  numbers. The VM passes the decoded values and writes back only what the host returns. `ScriptedEclHost` records
  the calls and answers from queues, for tests and tools. With `StopWhenInputMissing` it ends a run where the
  game would wait for the player (`EclStopReason.AwaitingInput`).
- **Trace** (`JsonlTraceWriter`): one JSON line per step, with these fields:
  - script, disk, vector, PC, opcode, the raw operands, memory reads and writes, host calls, IF outcome, flags,
    stack depth, next PC and diagnostics;
  - also load/run/end events.

  There are no timestamps, so two runs diff cleanly. `pc`, `op` and `fin` (flags before the step) correspond to
  the PC, opcode and flags of the game's port-E9 trace (`docs/re/dosbox-trace-method.md`). `flags` holds the
  flags after the step.

## Opcode coverage

| Kind | Opcodes |
|---|---|
| In the VM, checked in the program (P) | EXIT, GOTO, GOSUB, RETURN, COMPARE, IFEQ–IFGE, ON GOTO, ON GOSUB, SAVE, NEWECL, ADD, SUB, MUL, DIV, AND, OR, CMPAND, GETTABLE, SAVETABLE, RANDOM, PARTYSTR, PSURPRISE, SURPRISE, FINDITEM, LOADFILES, LOADPIECES (host loads the files), INPUTNUM, INPUTSTR (host reads the keys), PROGRAM 9's end of run |
| Host, result written by the VM | VMENU, HMENU (shape P), CHKPARTY, PARLEY, ENCMENU, SPELL |
| Host, no result | LOADCHAR, LOADMON, SETUPMON, APPROACH, PICTURE, PRINT, PRINTCLR, PRINTRET, CLEARBOX, DELAY, WHO, CLRMON, COMBAT, TREASURE, ROB, DAMAGE, SPRITEOFF, ADDNPC, CLOCK, CALL, PROGRAM, PROTECT |
| No handler | 0x1F, ≥ 0x3E: logged, then the run stops (or hangs, by policy) |

Host opcodes taken from Lee alone carry a `[?]` comment that points to their row in the spec. PARTYSTR, PSURPRISE
and FINDITEM fall back to the host when no party is attached (test hook).

## Undecided behaviour (`EclVmPolicy`)

Every open choice the VM has to make is one named property of `EclVmPolicy`. The defaults are choices, not facts:

- unhandled opcodes: stop (default) or hang;
- the value returned for undefined reads;
- SAVE from a type-0x81 source: copy the text or store the address (10 uses);
- the longest text read from memory;
- whether $C04B is signed;
- the width of the VMENU/HMENU counts.

Settled items (SUB order, flag rules, text compare, DIV by zero, the disk byte, the redirection table, …) have no
switch. Other `[?]` marks in the code, with no switch: the IF-skip filling operand slots, the text slot count of
a variadic tail, LOADPIECES' 255 rule when $49E7/$49E8 are set, PARTYSTR's level sign, CLOCK's amount, DS:84D2/84D3.

## Tools and tests

```
dotnet run --project src/GoldBox.Tools -- ecl-check GAMEDIR
dotnet run --project src/GoldBox.Tools -- ecl-run GAMEDIR SCRIPT [--vector N|all|init] [--save SAVGAMA.DAT]
    [--steps N] [--follow] [--menu 0,1,..] [--random 3,5,..] [--seed N] [--randseed HEX] [--out trace.jsonl]
dotnet run --project src/GoldBox.Tools -- ecl-compare GAMEDIR [FIXTURES] [--capture NAME] [--trace-out DIR]
```

`ecl-run` works as follows:

1. It loads block SCRIPT from `ECL1`–`ECL8.DAX`, optionally on top of a save game's state (with its party).
2. It runs the chosen vectors headless. The default is vector 4 (init).
3. It writes the trace to stdout or to `--out`. `--random` feeds Random(n) answers, `--randseed` the game's
   generator.

`ecl-compare` replays the private DOSBox-X golden captures (`GoldBox.Data.Por.Dosbox`, method in
`docs/re/dosbox-trace-method.md`) and prints, per capture, a match or the first divergence.

`tests/GoldBox.Core.Tests` contains three kinds of tests:

- **Synthetic tests.** They use bytecode built with a small assembler (`Support/EclAsm.cs`) and need no game files.
- **Real-data tests.** They need the player's PoR v1.3 folder in the `GOLDBOX_POR_DIR` environment variable and
  are skipped without it. They check:
  - decode totals;
  - the save cross-check;
  - every vector of every block run headless from save A;
  - seeded exploration runs;
  - script switches.
- **Capture tests.** They also need the private captures in `tests/fixtures-private/por/dosbox-v1.3` (found above
  the test binaries) and run the DOSBox comparison on each one.

Set `GOLDBOX_POR_DIR` to a folder holding your own PoR v1.3 files (`ECL1.DAX`–`ECL8.DAX`, `SAVGAMA.DAT`,
`SAVGAMJ.DAT`, `CHRDATA1-6.*`), outside the repo. Traces of the real game contain its text: keep them out of the repo.
