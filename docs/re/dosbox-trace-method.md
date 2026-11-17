# Capturing VM state and ECL traces from DOSBox-X (Pool of Radiance, DOS v1.3)

How to get the original game's ECL VM state, and a per-opcode trace, out of DOSBox-X, so a headless VM can be
compared against it. Written 2026-11-17 for DOSBox-X 2026.01.02 (Ubuntu package) and PoR v1.3 (WizardWorks image).
Rules for running emulators (virtual screen, no sound, one at a time): `AGENTS.md`.

## What the installed DOSBox-X can do

- **No debugger.** The build has no internal debugger: `--help` does not list the debug options, the debugger's
  command set is absent from the binary and it does not link a curses library; `-break-start` boots normally.
  So no breakpoints, no memory view, no heavy logging. Building DOSBox-X with `--enable-debug` would add them; not done.
- **Save states work** and are the memory dump. With `[dosbox] savefile=<path>` and `compresssaveparts=false`,
  F12+S writes a zip (about 39 MB) and F12+L loads it again (`forceloadstate=true` avoids refusals after a restart;
  `saveremark=false`, already set by `dosbox_xvfb.sh`, avoids a dialog). The zip entry `Memory` is a 0x88-byte
  header followed by guest RAM from physical address 0 (checked: the BIOS data area and the DOS memory chain sit at
  the expected places after the header).
- **Port E9 logging works:** `[dosbox] bochs debug port e9 = true` logs text written to port 0xE9, one line per
  newline, as `Bochs port E9h: <text>` in the `[log] logfile`. Used for the trace below.

## VM state from a save state: `tools/scripts/dosbox_state.py`

```
python3 -I tools/scripts/dosbox_state.py dump  STATE.sav [--json] [--areas OUTDIR]
python3 -I tools/scripts/dosbox_state.py check STATE.sav SAVGAMx.DAT
python3 -I tools/scripts/dosbox_state.py diff  A.sav B.sav
```

- Finds the game's data segment from the DOS memory chain: the block owned by `START`; DS = PSP + 0x10 + 0xC7C.
- Reads, through far pointers the game keeps in DS (as the VM reader GAME.OVR 0x82DF does): VM $4900 area (DS:49D2),
  $6B00 area (DS:49D6), $9700 area (DS:49DA), the script at $9900 (DS:49DE), and the current GEO map (DS:6A5C, four
  256-byte planes, the same layout as the GEO block without its 2-byte header).
- DS variables: PC 49ED, vectors 49E3–49EB, opcode 6F7F / previous 6F7E, compare flags 6F78–6F7D, GOSUB stack
  head 6F70, stop flag 442E, script-changed 442F, DS:49F8 (loading a save), DS:49FF (party defeated), disk 5376,
  RandSeed 4428 (dword), current ECL block 84DC, 3D-view flag 49FA, party x/y/facing/wall/event 6AAD–6AB1, and since
  s11 the party list from the heap (DS:5D96, next pointer +0x104: name, HP, status, money, item count, record) and
  DS:84CE/84D0/84D2/84D3/4430/4436/5D92. Meanings:
  `ecl-format.md`.
- `--areas OUTDIR` writes the areas as binary files in the save-file layout plus `vars.json`.
- `check` compares the four areas with a game save: a state taken right after loading save A matches `SAVGAMA.DAT`
  cell for cell except $6DD2/$6DD3, which the script init clears. This is the self-test of the address mapping.

Workflow: build a test copy (`por_dosbox_game.py IMAGE OUTDIR`), start it with `dosbox_xvfb.sh` plus
`CONF_EXTRA` holding the save-state lines above, drive it with `dosbox_xvfb.sh key`, and press F12+S (xdotool
`keydown F12 key s keyup F12` on the focused window) before and after the input under test. Copy the state file away
after each save; the path is fixed. Then `diff` the two states.

Game saves are a second, coarser oracle: the save file holds the same four areas (`save-format.md`), so "do X, save
in camp, diff the save" works without any emulator feature, but only at points where the game lets you save.

## Per-opcode trace

The run loop (GAME.OVR 0x3EB7) executes one instruction per pass. Its body starts at 0x3ED3 with a 6-byte copy of
the current opcode into the previous-opcode cell. The trace hook replaces those 6 bytes with `INT 66h` and 4 NOPs:

- `python3 -I tools/scripts/por_dosbox_game.py IMAGE OUTDIR --trace` patches the test copy (checks the 6 bytes
  first; v1.3 only).
- `tools/dosbox/ecltrace.s` is a small resident program (our own code) that hooks INT 66h. The handler does the
  removed copy, then writes one line to port E9 per instruction about to run: `E pppp oo ff` (PC, opcode byte at
  PC, the six compare flags as bits 0–5 = DS:6F78–6F7D, i.e. =, ≠, <, >, ≤, ≥). Build with GNU binutils:
  `as -o ecltrace.o ecltrace.s && ld -Ttext=0x100 --oformat=binary -e _start -o ECLTRACE.COM ecltrace.o`; copy
  `ECLTRACE.COM` into the test copy and start the emulator with the command `ECLTRACE` then `START` (a two-line
  COMMAND argument to `dosbox_xvfb.sh start`), with `bochs debug port e9 = true` and `[log] logfile=…`.
- Strip the log with `grep -a 'E9h' LOG | sed 's/.*E9h: //'`.
- Checked 2026-11-17: loading save A logs vector 4 of ECL2 block 20 ($9A5E … EXIT at $9AA4, 13 instructions); one
  step west logs vector 0 to its EXIT, then vector 1; Encamp logs vector 2; an interrupted rest logs vector 3.
- The trace slows the game only slightly at `cycles=max`. Without `ECLTRACE` loaded the patched game crashes at the
  first script run (INT 66h points nowhere), so never use a traced copy for anything else.
- The flags printed are the state *before* the instruction. To also see operand values, take save states around
  the instruction of interest, or extend the handler (e.g. dump DS:7086/7046 operand bytes after the loader).

## Comparing a headless VM against a capture

`goldbox-tools ecl-compare GAMEDIR [FIXTURES] [--capture NAME] [--trace-out DIR]` (code:
`src/GoldBox.Data/Por/Dosbox/`; the replay of each capture is in `PorGoldenCaptures.cs`). The same comparison runs
as skip-if-absent tests (`tests/GoldBox.Core.Tests/EclCaptureCompareTests.cs`).

1. **Start state.** A state capture starts from its `before` dump: the four areas, the script, the vectors (read
   from the script and checked against DS), the flags, PC and opcodes, the disk byte, the party position and
   **RandSeed**. The game seeds RandSeed from the DOS clock at start-up, so it differs on every boot and is taken
   from the capture. A save-load capture starts like the main menu's Load: memory from the save, the script init
   with DS:49F8 set, then vector 4. The party comes from the character files the save names.
2. **Inputs.** The replay does what the program does for the key, using the vector roles in `ecl-format.md`:
   - a forward step sets $6DD5, runs vector 0, writes $49F0/$49F1, moves the party and runs vector 1. The new
     position comes from the `after` capture, because the step itself is program code;
   - camp runs vector 2; an interrupted rest runs vector 3.
   - Menus with no recorded answer end the run where the game waits (`AwaitingInput`). PC is then after the menu's
     operands, as in the game.
3. **Program draws.** Random draws made by program code the VM does not model (door pick and bash, the rest loop)
   are a count, searched in a small range. The tool reports the count that reproduces the `after` state *and*
   RandSeed. Every RANDOM and SURPRISE roll comes from the generator, so RandSeed after the run is a strict check.
4. **State compare.** The four areas cell by cell, PC, opcode, previous opcode, flags, GOSUB stack (empty or
   not: its links are far pointers), disk, script, position (x, y, facing; since s10 also the wall ahead and event byte $C04E/$C04F) and RandSeed.
   - Cells the program changes outside the VM are listed as host-owned, with the reason, not counted: the clock
     $49C6–$49CC and the wall-set slot $4AFF. The replay runs the encounter opcodes in the game-side
     `EncounterHost` (map walls from the capture's `geo_planes.bin`, or the `GEO` block after a save load), so the
     encounter distance $6DC0/$6DC1 is compared like any other cell.
   - For each differing cell the report names the VM instruction that last wrote it.
5. **Trace compare.** The VM's instruction stream (PC, opcode, flags before each step) is aligned with the E9
   lines.
   - The traces come from a boot whose seed was not captured, so each roll is searched: the replay is repeated
     with each value until one stays on the trace up to the next roll.
   - On a mismatch the report gives the first differing instruction with the lines around it, from both sides.

Results (2026-11-17): every capture matches (`ecl-format.md` "Verification" 9). The first run diverged in two
places, both fixed: the VM now leaves a skipped instruction's opcode as the previous opcode (seen after a skipped
GOTO), and the replay sets $6DD5 on the step key as the program does (`geo-format.md` "Edges").

## Golden captures

`tests/fixtures-private/por/dosbox-v1.3/` (private: holds game data; excluded from the public export). Its
`README.md` lists each capture, the exact inputs and what it shows: load save A, one step, one turn, encamp,
interrupted rest, door pick/bash, a won fight, load of an in-game save, edge wrap, and two traces (load + step,
encamp + rest). Session 9 added a patched save whose script area holds test code (CLOCK, special reads, COMPARE
residue, PRINT of a number), with a state taken at its menu and a trace that ends in the 0x1F hang. Patching a
save's script area this way (save offset 5121 + A − $9900; vector 4 runs on load) is the quickest way to try one
instruction in the real game. Session 10 used the same trick to put the party on another map (vector 4 =
LOADFILES + LOADPIECES of a map of the same disk, party position at save offset 12801) with vectors that only count
their runs, for the one-way wall steps, and added a real round trip out of the Slums and back (`door_relock`).
