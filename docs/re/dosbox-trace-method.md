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
python3 -I tools/scripts/dosbox_state.py fight STATE.sav OUTDIR
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
- `fight` (s12) writes raw fight data for a state taken in combat: `ds.bin` (the whole DS segment), `arena.bin`
  (0x4E9 bytes at the far pointer DS:6716), the 19-byte records of the list DS:6DBB (next pointer at +4 per Lee
  `[?]`), and in `fight.json` every creature of the combat list with its record, 22-byte combat record (+0x108),
  item records (+0xC8, next +0x2A) and effect records (+0x7F, next +5), plus far pointers, the pool and a few fight
  DS bytes. Nothing is decoded beyond Lee-labelled locations (DS:5F2B/65BD).
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

## Stack residue probe (s18)

Three routines read a local byte before writing it: TREASURE's item type ([bp−0x65], a second roll of 48/49),
SPELL's "found" byte ([bp−8]) and Restoration's class index ([bp−0xC]). With no debugger, a second resident hook
reads them:

- `python3 -I tools/scripts/por_dosbox_game.py IMAGE OUTDIR --residue [--residue-watch]` replaces, in each routine,
  a 4-byte `mov byte [bp−k], imm` that runs after the frame is set up and before the read (GAME.OVR 0x2705, 0x3982,
  0x29CC6) by `INT 66h`, a site id and a NOP. `--residue-watch` also hooks the ECL run loop (0x3ED3, as `--trace`).
  Not together with `--trace` (both use INT 66h).
- `tools/dosbox/residue.s` (RESIDUE.COM, built like ECLTRACE.COM, started before START) redoes the store and writes
  `T pppp bbbb rr` / `S …` / `R …` lines to port E9: ECL PC, BP, residue byte. With the watch it also writes
  `W pppp oo vv` whenever the TREASURE cell SS:3E7D changed since the last instruction (PC and opcode about to run,
  new value), which names the instruction that wrote it. The hooks inside the routines run below the probed cell;
  the watch's frame sits at the run loop's depth and covers the SPELL cell SS:3EDA, so S lines are void with it.
- Measured: every opcode handler runs with BP = SS:3EE2 (so the cells are fixed); Restoration's BP was 3CEE in a fight
  (scroll used from the Items screen) and 3BD0 outside.
- Test saves for the paths: patch a copy of save A's script area (vector 0 pointed at real event code of ECL2
  block 20, or code copied from another block to the same addresses). A save names its members' files by the stems
  stored in it (`CHRDATA1` …), so a save with its own member files needs those stems changed too.
- Results: `item-format.md` "Random treasure", `ecl-format.md` "SPELL", `spells-effects.md` "Restoration with no
  class to raise". Note: in this DOSBox-X build, loading a state (F12+L) after the probe was loaded crashed the
  emulator twice (malloc error); the runs were redone from the boot instead.

## Screen checks with test saves (s22)

For screens that only show at the end of a long play (the party destroyed, the end game, a shop), build a save whose
first step runs the code: `python3 -I tools/scripts/por_test_saves.py GAMEDIR` writes slots B–I into a scratch game
copy (the list is in the script). The same folder serves both sides: DOSBox-X loads the slot from the party menu (L,
letter), the Client gets `--game GAMEDIR --save X` (its module is imported from that folder), so both run the same
step. Compare screens as EGA indices: reduce the DOSBox-X shot (640×400 under the menu bar) and the Client's (3 ×
320×200) to 320×200 colour indices and count differing pixels per text row; the Client's help lines and, with these
saves, nothing else: vector 4 loads no wall sets, and both sides show the saved ones (the Client re-reads them since
s23, Load 0x1FBEE).

- **Timing.** Use the script's default 3000 cycles for anything timed: Turbo Pascal's Delay is calibrated by a loop
  at start-up, and at `cycles=max` Delay(3000) took about 0.3 s; at 3000 cycles it measured 3.0 s. The intro's
  lightning rate also scales with the cycles (start-menu.md §1a).
- **Fast sampling.** A window grab through python-xlib (`get_image` on the DOSBox-X window, about 175 a second) with
  a colour histogram per grab gives page and palette changes with their times; `xwd` + ffmpeg manages about 6 a second.
- **States.** In s22 a state loaded into a freshly started emulator (F12+L after a restart, `forceloadstate=true`)
  crashed DOSBox-X right after "Loaded" both times; booting and loading the slot from the party menu (about 15 s at
  3000 cycles: three Returns for the intro and the code wheel, L, the letter) was used instead.

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
   - the step is cancelled when the script left $6DC9 ≥ 255 ($6DC9 back to 0, position kept); a LOADFILES of a map
     re-reads GEO<disk> block $49C5 into the walls and event bytes the encounter host uses (s18, for the hedge-maze
     captures; `StepAction.To` gives the stepped-on square when vector 1 moves the party again).
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

## Fight captures for chosen encounters (s13)

To test a rule in a fight the game does not offer where you need it, build the fight into a test save:

- **Encounter.** Replace the save's script area (offset 5121) with vectors 1–4 → EXIT and vector 0 → `CLRMON;
  LOADMON m, n, icon; …; COMBAT; EXIT` (`0B 00 m 00 n 00 icon` per LOADMON, `1C`, `24`, `00`). One step forward
  runs vector 0 and the fight starts. `m` is a block of `MON<disk>CHA.DAX` (list them with `gbchar.py monsters`),
  `icon` a block of `CPIC<disk>.DAX` (`dax.py list`; the area scripts use the monster id when it exists).
- **Another disk.** Set save byte 0 and $6E12 to the disk, $49C5 to a map of that disk, the party position (offset
  12801: x, y, facing, wall ahead, event byte from `geo.py dump`), and wall sets $4AFA–$4AFF to blocks that exist in
  that disk's `WALLDEF`/`8X8D` files. The graveyard's own LOADPIECES 21/22/20 failed both from the saved cells and
  from vector 4 (the game reports it cannot load the wall set); 1/3/20 loads (the 3D view looks wrong, the fight does not care: the
  combat arena comes from the GEO map).
- **Spells.** Edit the copied `CHRDAT*.SAV` records: memorised spell ids at 0x17–0x2B (`character-format.md`). The
  cast list shows them in slot order, the highlight starts on the last; in DOSBox-X the arrow keys did not move it,
  **Home** (keypad 7) moves it up one.
- **Taking states.** With `savefile`, `compresssaveparts=false`, `forceloadstate=true` in `CONF_EXTRA`, a helper
  sends F12+S, waits for the file, copies it and runs `dosbox_state.py fight`. F12+L on a copied state replays from
  it: the game is deterministic from a state (a second run from the same state ended on the same RandSeed), so a
  wrong key is undone by reloading, and a run can be repeated with denser states. A state taken while the game waits
  at a party member's menu is exact; one taken during messages lies somewhere between two draws.
- **All-Quick runs.** Take a state every ~2 s; when two in a row have the same RandSeed and the current creature is
  a party member (list index from the DS:5D92 pointer), the game waits at a menu: press Q.
- **VM cells.** `fight.json` holds DS only; the cells a fight reads from the VM ($49E5, $6DC1, $6DC6, $6DCB, $6E33,
  $6E70–$6E72) go in `vm.json` from `dosbox_state.py dump --areas`.
- **Tests.** `tests/GoldBox.Core.Tests/Support/FightCapture.cs` loads a state into a fight wired as `FightKit.Create`
  on the captured arena and squares (no placement), with the remembered target +0x0A resolved from its far pointer;
  `RunMenu` plays the captured keys through `MenuController` (a `ScriptedChooser`), `ContinueToNextMenu` goes on as
  `Fight.Round` does until the next party menu, and `Fields`/`Diff` compare RandSeed, round, current creature,
  squares, HP, status, Quick, memorised spells, effects and combat records. `GOLDBOX_FIGHT_TRACE` logs every draw.

- **Monsters of another disk** (s27): copy `MON<n>CHA.DAX`, `MON<n>ITM.DAX`, **`MON<n>SPC.DAX`** and `CPIC<n>.DAX`
  over the current disk's names in the scratch copy (with CHA alone the monsters load with **no effects** on both
  sides). The s27 flight checks used save A on disk 2 with MON5/CPIC5 in place: vector 0 = CLRMON; LOADMON 66, 1, 66
  (Tyranithraxus) or 49, 1, 49 (medusa); COMBAT; every member's HP 200 (bytes 0x32, 0xB1, 0x11B of `CHRDAT*.SAV`),
  Phineas's dagger replaced by the sling (ITEM1.DAX block 53 record 12, readied), name word 118 (Mirror) at byte 0x2F
  of Thrender's and Bakshi's weapons and of Rhiannon's and Sean's armour and shields. Party turns: Done, Quit (no
  draws). Seeds (DS:4428 before the step, `--seed` for the Client): 424744245 (breath), 273946116 (medusa).
- **Frame sampling**: python-xlib `get_image` on the window in a loop (~500 grabs a second on Xvfb), each distinct
  frame kept with its time; per frame the bounding box of changed pixels in the map, panel and bottom row gives a
  flight's positions and timing, compared line by line with the Client's window sampled the same way (its window
  has no menu bar: DOSBox-X's y − 17).

The s13 captures (fixtures README: `fight_casts`, `fight_mirror`, `fight_undead`) and what they settled:

| Check | Capture | Original | Core |
|---|---|---|---|
| Level-1 Magic Missile | `fight_casts` b02 → b04 (Darkstar at a 5th-level MU) | 0 damage, **no draw**, copy removed; the caster faces the target (+9) and +0x0A points at it | same damage and draws (`FightCastCaptureTests`); facing / +0x0A not set (skipped test) |
| Mirror Image cast | b05 → b07 | 1d4 = 4 images (1C 02 00 04), then the elf's handler 107 rolls 1d100 | matches (31 draws to the next menu); facing on a self cast 0 in DOSBox, unchanged in Core |
| Effect minutes | b08 → q01 | the image's minutes 2 → 1 at the end of round 0 (0xA087 → 0x241A1 → 0x23DCC); gone at the end of round 2 | matches (`FightKit.WireClock`, s15) |
| Melee hit on a Mirror Image caster | `fight_mirror` c03 → c04, c02 → c05 | 1d20, damage, then the image handler's 1d(param + 1); never absorbed by a weapon hit | matches, including the whole c02 → c05 stretch with both Charm Person casts (`FightMirrorImageCaptureTests`) |
| Turn undead | `fight_undead` d04 → d06 | 1d12 = 11, 1d20 = 15: 4 skeletons and 3 zombies turned | matches (`TurnUndeadCaptureTests`) |
| Monsters casting | `fight_casts` q01 → q34 | Charm Person on Sean, Magic Missile (level 5) on Phineas, Charm Person on Darkstar | the whole fight replays to the party's fall (1060 draws; every state taken between turns matches, mid-turn states differ only in that window, s15); it needed the effect clock and an AI cast with a casting time picked again in the round (the charmed Sean's Bless, q29 → q30). The 3 draws after the fight to the end screen q34 are the after-combat step's: the charmed Sean and Darkstar, okay, make the fight count as won while the party is destroyed, so the share runs and rolls 1d10 for three items of value 0 (s18; `combat-format.md` "After combat"); the replay runs that step and q34 matches (`FightMonsterCastReplayTests`) |
| Item use from the menu | `fight_manual_use` p01 → p03 (s18; save K with a readied wand given to Phineas, `saves/CHRDATN6.*`) | U, U, Next × 5, Target: 2d4 + 2 = 7 on a magic-user, one charge off | matches to the next menu (`FightManualUseCaptureTests`) |
| Manual aim | `fight_manual_use` p03 → p04 (flail), p05 → p06 (darts) | cursor from the aimer (range 0) over a bare square (no Target), an ally and an enemy (Target); the melee hit and the three darts | same squares, ranges and Target offers; matches to the next menu (`FightManualUseCaptureTests`) |
