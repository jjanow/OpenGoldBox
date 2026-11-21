# GoldBox.Core: ECL virtual machine

A headless VM for the event scripts (ECL) of Pool of Radiance, DOS v1.3. Built from our own spec,
`docs/re/ecl-format.md`, only. Code lives in `Ecl/`.

## What it covers

- **Decoder and flow analysis** (`EclDecoder`, `EclAnalysis`): the same rules as `tools/scripts/ecl.py`. On the
  29 v1.3 blocks it gives the same totals as `ecl.py check` (16,015 instructions, 0 errors, 0 overlaps).
- **VM** (`EclVm`): decodes at run time from the live script area at the PC, so scripts that write into
  $9900–$B6FF behave as in the game. It keeps the program's details:
  - operand slots persist between instructions, so a type-0 operand used as an address keeps an old high byte;
  - string slots: the k-th text operand of a loader call (an instruction, or a variadic tail, which starts again at
    1) fills slot k and keeps it until a later text replaces it (so a COMPARE of a text with a number sees an old
    slot 2, as in the game);
  - variadic counts go through the value fetch (low byte); the tail reloads operand and string slots from 1;
  - the IF-skip uses its own, shorter operand counts for 0C, 36, 15, 25, 26 and 2B, and resumes inside the
    operands; the skipped opcode becomes the "previous opcode" (DOSBox);
  - RETURN on an empty stack falls through;
  - the script init sets its cells ($6DE1, $6DD2/$6DD3, $49E5, $49E6) and keeps $4A00–$4A1F and $6E79–$6E82
    only while DS:49F8 ("loading a save", `EclVm.LoadingSave`) is set;
  - NEWECL writes $49F2, loads and initialises the new script at once and stops the run; `FollowScriptChanges`
    then runs its vectors 4, 0 and 1;
  - PROGRAM 9 ends the run after the camp, DIV by zero is a fault;
  - CLOCK passes the high byte of operand slot 64 (its operand index is 0 after GOSUB), PRINT of a number prints
    its decimal text and leaves it in string slot 1.
- **Memory** (`EclMemory`):
  - the 16-bit areas $4900, $6B00 and $9700, and the 8-bit script area (writes keep the low byte);
  - the disk byte DS:5376 (`Disk`), which is VM $6E12;
  - the $6B00-area redirection table: reads of the listed addresses come from the current character's record,
    writes store the cell and then apply the side effects; $6E22/$6E24/$6E26 load wall sets;
  - the text rules of SAVE and type-0x81 operands ($6B00 = the current character's name);
  - the special addresses ($C04B–$C04F party position, $033D facing, $00B1/$00FB/$00FC reading 0 and ignoring
    writes, the $C059/$C05F view colour bytes, the other listed writes);
  - undefined reads ($035F, $C059, unlisted addresses) return the stack residue the VM models (inside COMPARE: the
    previous COMPARE operand read) or else a policy value, and are logged.
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
| Host, result written by the VM | VMENU, HMENU (shape P), CHKPARTY, PARLEY, ENCMENU (null = no write), SPELL |
| Host, no result | LOADCHAR, LOADMON, SETUPMON, APPROACH, PICTURE, PRINT, PRINTCLR, PRINTRET, CLEARBOX, DELAY, WHO, CLRMON, COMBAT, TREASURE, ROB, DAMAGE, SPRITEOFF, ADDNPC, CLOCK, CALL, PROGRAM, PROTECT |
| Game-side host (`Game/EncounterHost`, P) | SETUPMON, APPROACH, LOADMON, CLRMON, ENCMENU, COMBAT entry, TREASURE, PARLEY, PICTURE, SPRITEOFF |
| No handler | 0x1F, ≥ 0x3E: the original hangs; the VM logs it and stops with the PC on the byte (or spins, by policy) |

Host opcodes taken from Lee alone carry a `[?]` comment that points to their row in the spec. PARTYSTR, PSURPRISE
and FINDITEM fall back to the host when no party is attached (test hook).

## Game-side encounter host (`Game/`)

`EncounterHost` implements the encounter opcodes as read from the program (`ecl-format.md` "Encounters"). It wraps
another `IEclHost` (for example `ScriptedEclHost`), which still answers text, the other menus, files and random
numbers, so the LOADMON icon rolls and TREASURE's type rolls come from the same generator as RANDOM.

- **State it owns:** the VM cells $6DC0/$6DC1 (requested and actual distance), $6DCA, $6DE2 and $6E6C. The
  data-segment state (sprite, close-up, monster count, icon slot, "monsters loaded", duel flag, treasure money and
  items, display flags) is in `EncounterState`. LOADMON appends the monster records to the party list, as the
  program does.
- **Rules** (`EncounterRules`, pure functions): view distance (walls from an `IWallMap`, e.g. `MapPlanes`), party
  speed with effects 0x27/0x2A, distance texts, menu kind, the reaction table, icon colours, random item types.
- **Interfaces:**
  - `IEncounterUi` is the player-facing side (sprite and pictures, the encounter and parley menus, the two fixed
    messages, combat icons); `ScriptedEncounterUi` is the headless one.
  - `ICombatRunner` takes the fight, the after-combat step, the shop and the temple; the random item maker is
    `TreasureMaker` (GAME.OVR 0x25C99, its array read from START.EXE by `GoldBox.Data.Por.PorProgram`); `RecordingCombatRunner` only logs.
  - `IEncounterData` gives the monster and item-list records; `GoldBox.Data.Por.PorEncounterData` reads
    `MON<d>CHA/ITM/SPC` and `ITEM<d>`.
- `goldbox-tools ecl-compare` replays every capture through it.

## Undecided behaviour (`EclVmPolicy`)

Every open choice the VM has to make is one named property of `EclVmPolicy`. The defaults are choices, not facts:

- the value returned for undefined reads (stack residue outside COMPARE);
- the longest text read from memory;
- how an unhandled opcode is reported: stop at once (default) or spin until the step budget runs out (the
  original hangs either way, so this one is not a `[?]`).

Settled items (SUB order, flag rules, text compare, DIV by zero, the disk byte, the redirection table, $C04B sign,
SAVE of a 0x81 text, menu count width, …) have no switch. Other `[?]` marks in the code, with no switch: the IF-skip
filling operand slots, LOADPIECES' 255 rule when $49E7/$49E8 are set, PARTYSTR's level sign, CLOCK after anything
but GOSUB, DS:84D2/84D3.

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
Extra files in that folder are fine: the tests that pin the importer's totals (`PorModuleTests`) import a copy holding
only the v1.3 release's files (`PorData.ReleaseDir`), so the test saves B–I of `tools/scripts/por_test_saves.py`
(and the `FINAL2.DAX` it adds) or a played game's saves don't change them.

The Amiga tests also need `GOLDBOX_POR_AMIGA_DIR`: the unpacked disks with `program`, `intro` and the `fonts` folder
(`protect.font`, `protect/8`, `poolrad.font`, `poolrad/8`) from disk A and the `.dax` files from disk B (found as the importer finds them: any
case, up to three folders deep). A folder without `program` or `pic.dax` skips the Amiga tests, one without `intro`
skips the intro tests, one without `fonts` the rune and text font tests, each with a reason naming the missing file
(`PorAmigaData`, `[PorAmigaIntroFact]`, `[PorAmigaRuneFontFact]`, `[PorAmigaTextFontFact]`; the Client's `AmigaRuneTests` return without it).

`tests/GoldBox.Client.Tests` reads the same variables. Build new rigs from `Support/TestRig.cs`: `TestRig.Player`
makes a `ClientPlayer` with `TypesText` off, so a rig that steps through scripts gets PRINT's text at once instead of
one typing delay per character (the Client itself keeps typing on, as the program does).

Temp folders: the tests write under `<temp>/goldbox-tests/` (on this machine a tmpfs with 1M inodes). Imports go
through `Support/TestImportCache.cs`: one folder per importer build (a hash of `GoldBox.Data`) and game files, reused
and touched by later runs. When the Core test assembly loads, it removes its own entries there (and in the Client tests'
`client-module-cache` / `client-amiga-cache`) that no run has used for three hours (a day before s42). New tests that need a large folder should
use `TestImportCache`, not a new GUID folder per run.

## Doors (`Doors/`)

`DoorRules` and `DoorMap` model the door menu of the 3D view (`docs/re/geo-format.md` "Door menu"): which choices
it offers, the Bash / Pick / Knock rolls (same generator and draw order as the program, so a capture's RandSeed
reproduces them), the menu flags, and the plane-3 edits (open both faces, seal one face). `DoorMap` also holds the
step test that reads only the party's own face (one-way walls). `DoorRulesTests` replays the `door_pick_fail` and
`door_bash` captures.
