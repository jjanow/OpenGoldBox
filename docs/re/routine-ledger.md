# Routine coverage ledger (Pool of Radiance, DOS v1.3)

Every routine of the v1.3 program (`START.EXE` and `GAME.OVR`) that the game runs from the title to the ending, with how far the engine follows it. Built in session 47 (2026-11-22) to make the remaining [play] work finite: **P** and **U** rows are the list of known gaps; a **W** row was either run against the program (oracle, golden captures) or read and ported per the spec and C# comments, which is not the same as checked branch by branch (the two counts are split below). Addresses are GAME.OVR file offsets unless marked START (offsets in the EXEPACK-expanded `START.EXE` image), as in the other `docs/re` specs; far calls resolve through the overlay stubs (`ecl-format.md` "Program references").

## Statuses

| Status | Meaning | Count |
|---|---|---|
| **W** ported whole | the Core / Client code follows the routine | 697 |
| … W by execution | an oracle runs it (or runs its caller, for state routines), the ECL golden captures cover it, or it is empty | 330 |
| … W by citation | the C# names its entry and the spec reads it with no open `[?]` on those lines: read and ported per the comments, not verified branch by branch | 367 |
| **P** patched by symptom | partly built: the C# names only inside addresses, or the spec lines carry `[?]`, or the behaviour was built from emulator observations | 40 |
| **U** unchecked | no evidence that the engine matches it (not cited, or read in a spec but not ported) | 54 |
| N not ported by design | platform work the module format replaces: DAX reading (the importer), disk numbers, memory release, the code wheel | 21 |
| L library | Turbo Pascal run-time library, overlay manager, Crt and DOS units (one line per segment below) | 143 |

Total: 955 routines (280 START, 675 GAME.OVR). W + P + U = 791 game routines.

Evidence column: `oracle` = a `tools/scripts/por_*_oracle.py` runs the routine under unicorn and a Core test compares (`oracle (caller)`: run inside a called routine, not trapped; drawing, text and sound callees excluded since the oracles compare state, not pixels); `golden` = an ECL handler or VM helper covered by the golden DOSBox captures EclVm matches; `C#` / `C# (inside)` = a C# comment names the entry / only addresses inside it; `spec` / `spec (inside)` = the same for `docs/*.md`; `[?]`×n = spec lines on the entry with an open question; `seen only` = every spec line on the entry is a DOSBox-X / FS-UAE observation. Port = the C# type.member whose comment names the entry. Kind: **play** = rules, state or branching; **pres** = drawing, text, sound, input timing.

## How it was built (to rebuild after a session)

The generator scripts are in `tools/scripts/ledger/` (copied as used in s47; set `GOLDBOX_POR_DIR` to a v1.3 copy and `LEDGER_WORK` to a scratch folder, run them with the unicorn + capstone venv; `cg.py` first, then the citation, table and dispatcher passes, then `build.py` and `render.py`; check each script's inputs at its top).

1. Load the program with `tools/scripts/por_emu.py` `load_image` (EXEPACK expanded); the overlay stubs are the paragraphs starting `CD 3F 00 00` (dword GAME.OVR offset at +4, word code size at +8, entry count at +0x0C, entries of 5 bytes from +0x20: `CD 3F` + the routine offset in the unit).
2. Recursive descent with capstone (16-bit) from the entry point (START 0x0006), every stub entry and the sound timer interrupt (START 0x9ECA, installed through INT 21h 25h): near calls stay in the segment / unit, far calls resolve through the stubs, and decoding stops at stub paragraphs. Then a sweep for routine prologues (`55 89 E5`, `55 8B EC`) not yet covered. What stays uncovered is string constants kept in the code segments.
3. Indirect calls: the program has three, all through tables it fills at run time: the spell routines (DS:6B84, built by 0x2A42B, called from 0x27C38; its entry 0 is a procedure variable, the target pick 0x148BE, stored by the fight at 0x988C and by the builders at 0x11EB6 / 0x2A479 and called at 0x27D88), the effect handlers (DS:6828, built by 0x11DFD, called from 0x2AEEA) and the TP run-time. The tables were read by running the two builders (`por_spell_oracle.py` set-up). The ECL dispatcher (0x3C73) is a compare chain of near calls; its opcode → handler map is in the ECL section below.
4. Citations: every `0x` number in `docs/*.md`, `docs/re/*.md` (surveys and prior art left out) and in C# comments under `src/`: after `START` / `START.EXE` (and numbers chained to it, unless no START routine holds the number) a START address, `SSSS:OOOO` resolved by segment or stub, a range `0xA–0xB` counts for every entry inside it, otherwise a GAME.OVR offset (4–5 digits, or an exact routine entry); numbers after `+`, `$`, `DS:`, `hunk n` or `Amiga` are skipped. A citation counts for the routine whose decoded instructions hold it; "entry" means the routine's first address. The oracles were read by hand (their `e.call` targets count, trapped routines do not).
5. Rows marked with a note in the gap lists were checked by hand; the rest follow the rule in "Statuses". The count is 181 START and about 1990 GAME.OVR addresses cited, on 731 routines.

## Title to ending: screens and paths

Each path's root routines and everything they call, without entering another path's roots (library and N rows not followed). Shared helpers (menus, text, messages, effects) count in every path that reaches them.

| Code | Path | Roots | Routines | W | P | U | Play gaps (P + U, kind play) |
|---|---|---|---|---|---|---|---|
| Su | Start-up, title, introduction | START 0x0006, START 0x4354, 0x06FB, 0x0AA9, 0x2EA4C, 0xE600 | 92 | 64 | 3 | 25 | START 0x0006, 0x00B4 |
| Pm | Party menu, creation, Modify, icons, roster | 0x19AE0, 0x19EEB, 0x1BA1E, 0x1D1AC, 0x1E684, 0x1EAD2 | 461 | 393 | 26 | 42 | 0xF91E, 0x10AD2, 0x11134, 0x112E5, 0x1E294, 0x21AED, 0x22F8C, 0x23819, 0x27466, 0x27534, 0x27C38, 0x282BD, 0x2A0F1 |
| Ex | Area loop, 3D view, area map, overland, doors | 0x403E, 0x17054, 0x17438, 0x2DCF4, 0x2EEF4, 0x2DE38 | 470 | 400 | 26 | 44 | 0xF91E, 0x10AD2, 0x11134, 0x112E5, 0x21AED, 0x22F8C, 0x23819, 0x27466, 0x27534, 0x27C38, 0x282BD, 0x2A0F1 |
| Ecl | ECL VM and host opcodes | 0x3C73, 0x3EB7, 0x7689, 0x7814 | 391 | 334 | 19 | 38 | 0xF91E, 0x10AD2, 0x11134, 0x112E5, 0x202BB, 0x23819, 0x2A0F1 |
| Enc | Encounters (monsters, close-up, menu, parley, surprise) | 0x2C3E, 0x3311, 0x155D, 0x240F, 0x236F, 0x1196, 0x10FA, 0x1EEAA, 0x7A21 | 88 | 59 | 6 | 23 | 0x23819 |
| Cb | Combat (map, menu, moves, AI, spells, effects, items, after combat) | 0x24E1, 0x9881, 0x6AA5 | 523 | 456 | 29 | 38 | 0x9938, 0xF91E, 0x10AD2, 0x11134, 0x112E5, 0x127FB, 0x12F0D, 0x14C0F, 0x1638E, 0x21AED, 0x22F8C, 0x23819, 0x27466, 0x27534, 0x27C38, 0x282BD, 0x2A0F1 |
| Cp | Camp (all menus, rest, Memorize, Scribe, Cast, Alter) | 0x196A0, 0x24A66 | 467 | 403 | 26 | 38 | 0xF91E, 0x10AD2, 0x11134, 0x112E5, 0x18668, 0x21AED, 0x22F8C, 0x23819, 0x24331, 0x27466, 0x27534, 0x27C38, 0x282BD, 0x2A0F1 |
| Sh | Shops, temples, venues (PROGRAM, CALL) | 0x7212, 0x5280, 0x3B56, 0x3A15 | 474 | 411 | 25 | 38 | 0x4864, 0xF91E, 0x10AD2, 0x11134, 0x112E5, 0x21AED, 0x22F8C, 0x23819, 0x27466, 0x27534, 0x27C38, 0x282BD, 0x2A0F1 |
| Tl | Treasure and loot | 0x26F6 | 32 | 27 | 0 | 5 | — |
| Lv | Training and level up | 0x1C32D, 0x2AA87 | 69 | 43 | 4 | 22 | 0x27466, 0x27534 |
| Sv | Save and load | 0x1FD3E, 0x1F5E8 | 47 | 25 | 2 | 20 | 0x1E294 |
| End | End game | 0x20941, 0x206A3 | 55 | 29 | 4 | 22 | — |
| Kd | Party killed / destroyed | 0x34BB, 0x5A47 | 290 | 256 | 13 | 21 | 0xF91E, 0x10AD2, 0x11134, 0x112E5, 0x2A0F1 |

The Paths column of the unit tables below uses these codes (the paths whose closure holds the routine).

## Gaps on the title-to-ending path

Ranked by how much of play they can change (rules, state, branching), then by size and callers. Each line is a candidate [play] item: read the routine whole, port every branch, add an oracle check where the `por_*_oracle.py` pattern fits, then confirm in DOSBox-X.

### Play (rules, state, branching)

| # | Routine | Role | St | Why it is open |
|---|---|---|---|---|
| 1 | 0x27C38 | cast a spell (pick, aim, routine call) | P | every spell cast, fight and camp; open: whether bit 7 of a scroll pick is kept (`combat-ai.md`). Read the routine whole, oracle it (scroll and memorised casts, aim abort) |
| 2 | 0x127FB | start of round (per creature) | P | start of round per creature, marked "not read" in `combat-format.md`; it sets what each creature may do this round |
| 3 | 0x9938 | next creature to act | P | turn order picks; the RNG contract of a round's start depends on it (`combat-format.md` "Start of round") |
| 4 | 0x12F0D | one-square move (cost, step) | P | one-square move: cost, attacks of opportunity hand-off, terrain effects 0x2B9D1 `[?]` (`combat-map.md`) |
| 5 | 0x14C0F | AI: spell choice and cast | P | AI and menu combat cast: spell 0 → the spell list 0x239D6 `[?]` (`spells-effects.md` "Combat cast") |
| 6 | 0xF91E | effect handler 40 | P | handler 40: stinking cloud cells of the arena `[?]` (map side) |
| 7 | 0x10AD2 | effect handler 84 | P | handler 84 charm gaze: its range reads an uninitialised stack word `[?]` |
| 8 | 0x2A0F1 | effect handler 88 | P | handler 88 lightning breath: built from a DOSBox-X run plus a read; recheck the whole routine |
| 9 | 0x11134 | effect handler 90 | P | handler 90: Constitution save bonus `[?]` on the spec line |
| 10 | 0x112E5 | effect handler 97 | P | handler 97: as 90 |
| 11 | 0x282BD | spell routine for spell 12 | P | spell 12 Enlarge: levels outside 1–6 keep the previous value `[?]` |
| 12 | 0x27466 | scroll spell list (readable items) | P | reading a scroll: the scroll list `[?]` (`item-format.md`) |
| 13 | 0x27534 | [?] scroll read helper: sets DS:6B80 / 6B82 and DS:4843 around 0x27466 | U | scroll read helper, never cited: sets DS:6B80 / 6B82 and DS:4843 around 0x27466 |
| 14 | 0x4864 | temple cure 1: six effects (DS:0114) | P | temple cure 1: DS:681F = 1 meanwhile `[?]` (what the flag changes in 0x2AF10) |
| 15 | 0x21AED | View menu (character page) | P | View's menu: which entries it offers per caller (`combat-format.md`) |
| 16 | 0x22F8C | shop Sell / Id box | P | shop Id / Sell box `[?]` (`combat-menu.md`) |
| 17 | 0x23819 | party member selection (Next / Prev member) | P | party member selection (Home / End, camp and party menu): wrapping `[?]`, "not read" in `view3d.md` |
| 18 | 0x202BB | [?] NEWECL helper: disk number DS:5376 check, writes DS:6AAD / 6AAE, may ask through the menu 0x2CC8C and save 0x1FD3E | U | NEWECL helper, never cited: disk number check, DS:6AAD / 6AAE, a menu and the save routine; may decide what a script change does on a disk swap |
| 19 | 0x24331 | camp time: time text / clock advance helper | P | camp time helper: C# only (CampTime), no spec line |
| 20 | 0x1E294 | [?] file-error report (DS:537A / 537B) with a key prompt, for the roster and save routines | U | roster / save file-error report, never cited (error path only) |
| 21 | 0x00B4 | [?] start-up: sets DS:4414 / DS:4416 (unit 0x16) | U | start-up: sets DS:4414 / DS:4416, never cited |
| 22 | 0x004A | [?] no caller found (unit 0x16): disk prompt then the game save 0x1FD3E; reached through a procedure variable or dead | U | no direct caller (unit 0x16): a disk prompt, then the game save; find how it is reached |
| 23 | 0x18668 | [?] list buffer allocation for Memorize / Scribe pages (0x187ED) | P | Memorize / Scribe page buffer allocation (memory only; likely nothing to port) |
| 24 | 0x1638E | Alt-X debugging switch | P | Alt-X debugging switch (needs the start word; no effect in normal play) |
| 25 | START 0x0006 | main program: unit inits, configuration, introduction, code wheel, party menu, view colours, area loop, party-destroyed exit | P | main program: the order is in `start-menu.md` §1 and Program.cs, cited only inside |

### Presentation only (drawing, text, sound, input timing)

| # | Routine | Role | St | Why it is open |
|---|---|---|---|---|
| 1 | START 0x77F8 | text window (typing, paging) | P | text window: typing and paging; open `[?]` on the 255-character case (PLAN "Next" (1)) |
| 2 | 0x2D669 | vertical list / item list window | P | list window and its never-cited helpers 0x2C936, 0x2CFE0, 0x2D073, 0x2D1A6, 0x2D1F2, 0x2D238, 0x2D2D7: the Client rebuilt the pages from DOSBox-X (s45–s46) |
| 3 | START 0x3379 | screen redraw by case (hall, camp, loot, shop pictures and boxes) | P | screen redraw by case (hall, camp, loot, shop): two `[?]` and three observed-only lines |
| 4 | 0x2DCF4 | 3D view / overland redraw (window, backdrop, marker) | P | 3D / overland redraw: marker and backdrop seen in DOSBox-X; the Amiga marker after a same-area Load is open (PLAN "Next" (2)) |
| 5 | 0x239D6 | spell list window (type, action) | P | spell list window: four `[?]` lines; the Client draws its own page |
| 6 | 0x2CC8C | menu routine (horizontal words) | P | menu routine (every horizontal menu): one `[?]` line; its key handling is run by the oracles but its drawing is not compared |
| 7 | START 0x1307 | party list redraw | P | party list redraw and its colour helpers 0x148F, 0x1513, the creature panel 0x1583, the icon flash 0x257E, the View text helper 0x1C4D: run under the oracles, drawing compared only in DOSBox-X |
| 8 | START 0x228C | combat / status message line | P | combat / status message line: C# cites only inside |
| 9 | START 0x2B8B | effect message (text and pause) | P | effect message and pause |
| 10 | 0x2EEF4 | 3D view draw (Draw3DView) | W | 3D view internals 0x2E8F7, 0x2EA6B, 0x2EB08, 0x2EEEA never cited, the piece draw 0x32392 cited only inside: the view is pixel-equal to DOSBox-X from the formats (`view3d.md`), so only a check |
| 11 | 0x20D50 | [?] after a load or a script switch: overland / area picture set-up (calls 0x2DE38, picture draw) | U | after a load or script switch, never cited: overland / picture set-up (calls 0x2DE38 and the picture draw) |
| 12 | START 0x4751 | picture block read (DAX block → buffer) | U | pictures, START segment 0464 (0x4640–0x6C72): read, decode, draw, recolour, free; the importer decodes the formats and the Client draws them (`por-art-formats.md`); only some draw entries are cited |
| 13 | 0x1E468 | combat icon slot: picks a free slot 0..7 and builds its picture | P | combat icon slots and their builders 0x1E315, 0x1CE5E, 0x1CF3D (icon editor preview, recolour) |
| 14 | 0x32123 | [?] text page with a key prompt (level drain path; trapped by the spell oracle) | U | text page with a key prompt on the level-drain path, never cited (trapped by the spell oracle) |
| 15 | 0x2E0CE | picture frame draw | U | picture frame draw / free (0x2E0CE, 0x2E55D) and the sprite renderer 0x2E7D6: spec lines only |
| 16 | START 0x8C85 | sound: tune cue | U | sound API (cue 0x8C85, device set-up 0x8C5B, driver start 0x8BBC and helpers): the driver itself is oracle-checked, the game-side calls are spec only |
| 17 | START 0x4354 | start-up configuration: command line, video mode, sound device, installer flags (only the `F` line read) | P | start-up configuration (video, sound, installer lines), read in part |
| 18 | START 0x7D3B | [?] text / box helper (Items page, View) | U | text helpers 0x7090, 0x7101, 0x7130, 0x7147, 0x720C, 0x7D3B never cited |

Not gaps: the N rows (DAX reader START segment 0802, disk numbers, memory release, the code wheel) and the library segments, which the module format and .NET replace.

### Statuses set by hand

| Routine | St | Why |
|---|---|---|
| 0x00D4 | W | Intro.cs waits read from the program (start-menu.md §1a) |
| 0x0AA9 | N | skipped by design (Program.cs: straight to the party menu) |
| 0x211D | W | part of CHKPARTY (golden captures) |
| 0x3C73 | W | every opcode handler is ported; the dispatch is EclVm.Execute (golden captures) |
| 0x9807 | N | memory release (no port needed; FightPlacement.Start empties the list) |
| 0x11570 | W | C# cites the entry (EffectHandlers.MagicResistance) |
| 0x16901 | W | geo-format.md door menu; DoorRules builds the same test |
| 0x16CB9 | W | geo-format.md door menu; DoorRules.KnockSpell |
| 0x1997F | N | memory release (no port needed) |
| 0x2EA4C | P | spec cites it; the Client sets the same colours from the redraw, routine not cited |
| START 0x4354 | P | the controls screen copies the choices; the routine itself read in part (start-menu.md) |
| START 0x896A | N | disk swap is platform; the module has no disks |
| START 0x897D | N | disk swap is platform |

## ECL opcode handlers (dispatcher 0x3C73)

| Handler | Opcodes | Status |
|---|---|---|
| 0x0D47 | 0x00 EXIT | W |
| 0x0E3A | 0x01 GOTO | W |
| 0x0E57 | 0x02 GOSUB | W |
| 0x0E6C | 0x03 COMPARE | W |
| 0x0EE9 | 0x04 ADD, 0x05 SUB, 0x06 DIV, 0x07 MUL | W |
| 0x0F7A | 0x08 RANDOM | W |
| 0x0FD8 | 0x09 SAVE | W |
| 0x1026 | 0x0A LOADCHAR | W |
| 0x10FA | 0x0C SETUPMON | W |
| 0x1196 | 0x0B LOADMON | W |
| 0x155D | 0x0D APPROACH | W |
| 0x159C | 0x0E PICTURE | W |
| 0x1697 | 0x0F INPUTNUM | W |
| 0x16DA | 0x10 INPUTSTR | W |
| 0x174F | 0x11 PRINT, 0x12 PRINTCLR | W |
| 0x17E5 | 0x13 RETURN | W |
| 0x1833 | 0x14 CMPAND | W |
| 0x1898 | 0x16 IFEQ, 0x17 IFNE, 0x18 IFLT, 0x19 IFGT, 0x1A IFLE, 0x1B IFGE | W |
| 0x1917 | 0x20 NEWECL | W |
| 0x19BA | 0x21 LOADFILES, 0x37 LOADPIECES | W |
| 0x1B35 | 0x2F AND, 0x30 OR | W |
| 0x1BA4 | 0x2A GETTABLE | W |
| 0x1C02 | 0x35 SAVETABLE | W |
| 0x1C4E | 0x15 VMENU | W |
| 0x1E17 | 0x2B HMENU | W |
| 0x1FA3 | 0x1C CLRMON | W |
| 0x200C | 0x1D PARTYSTR | W |
| 0x217B | 0x1E CHKPARTY | W |
| 0x236F | 0x22 PSURPRISE | W |
| 0x240F | 0x23 SURPRISE | W |
| 0x24E1 | 0x24 COMBAT | W |
| 0x263E | 0x25 ONGOTO, 0x26 ONGOSUB | W |
| 0x26F6 | 0x27 TREASURE | W |
| 0x2AF7 | 0x28 ROB | W |
| 0x2C3E | 0x29 ENCMENU | W |
| 0x3311 | 0x2C PARLEY | W |
| 0x33B0 | 0x32 FINDITEM | W |
| 0x3465 | 0x3A DELAY | W |
| 0x34BB | 0x2E DAMAGE | W |
| 0x37E8 | 0x31 SPRITEOFF | W |
| 0x3807 | 0x34 CLOCK | W |
| 0x3831 | 0x33 PRINTRET | W |
| 0x385A | 0x3D CLEARBOX | W |
| 0x3879 | 0x39 WHO | W |
| 0x38C4 | 0x36 ADDNPC | W |
| 0x3949 | 0x3B SPELL | W |
| 0x3A15 | 0x2D CALL | W |
| 0x3B56 | 0x38 PROGRAM | W |
| 0x3BC3 | 0x3C PROTECT | W |

All 62 PoR opcodes have an `EclVm` case; the handlers that call into the host (COMBAT, TREASURE, ENCMENU, PARLEY, shops through PROGRAM / CALL) are W for the handler itself; the host routines they reach have their own rows below.

## Ledger by program unit

### START.EXE

Library segments (status L, not listed one by one):

| Segment | Routines | Content |
|---|---|---|
| 03DA (START 0x3DA0…) | 1 | unit initialisation |
| 03ED (START 0x3ED0…) | 1 | unit initialisation |
| 0A31 (START 0xA310…) | 1 | unit initialisation |
| 0A32 (START 0xA32B…) | 6 | DOS unit helpers (INT 21h) |
| 0A40 (START 0xA400…) | 17 | Crt-style unit: BIOS keyboard (KeyPressed / ReadKey), Delay, video mode, cursor |
| 0AA2 (START 0xAA20…) | 22 | Turbo Pascal overlay manager (INT 3Fh handler, overlay buffer, reads from GAME.OVR) |
| 0AF8 (START 0xAF80…) | 95 | Turbo Pascal System unit: heap (GetMem / FreeMem), strings, sets, longint and real arithmetic, file I/O, Halt |

#### Segment 0000: main program (1 routines: P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x0006 | main program: unit inits, configuration, introduction, code wheel, party menu, view colours, area loop, party-destroyed exit | P | play | Su | C# (inside), spec (inside) | Program, ExploreScreen |

#### Segment 00BA: game core helpers (stats, messages, distance, lists, screen redraws) (52 routines: W 43 (30 by execution), P 8, U 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x0BA0 | stat recalculation: weapon (THAC0, Dex missile adjustment) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.Weapon |
| START 0x0D9B | stat recalculation: armour movement | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.ArmourMove |
| START 0x0E22 | stat recalculation: AC from readied items | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.AcItem |
| START 0x0F40 | stat recalculation: encumbrance | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.Encumbrance |
| START 0x0FE2 | item name (cached name build) | W | play | Pm Ex Ecl Cb Cp Sh Tl Kd | oracle (caller), C#, spec, `[?]`×1 | TradeContext.StoredItemName, EncounterHost.Treasure, ItemNames |
| START 0x1307 | party list redraw | P | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | oracle (caller), C#, spec, `[?]`×2 | HallPicture, HallScreens.Run, ClientPlayer.DrawInterface |
| START 0x148F | party list: name / AC column colour | P | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | oracle (caller), C# | FramePiece.PartyList |
| START 0x1513 | party list: HP number colour | P | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | oracle (caller), C# | CombatScreen.DrawPanel, FramePiece.PartyList |
| START 0x1583 | creature icon and status panel redraw | P | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | PorTextCatalog.Image, CombatView.ShownLines, CombatView.ShowLines |
| START 0x1713 | creature is helpless (status / effects) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatScreen.DrawPanel, FightRules.HelplessEffects, FightRules.IsHelpless |
| START 0x1758 | stat recalculation | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | ClientResources.ItemTypes, CharacterView.Run, CharacterView.Items |
| START 0x1BD7 | [?] small arithmetic helper (no spec line; run under the oracles) | W | play | Pm Ex Ecl Enc Cb Cp Sh Tl Lv End Kd | oracle (caller) | — |
| START 0x1C13 | number to text (Str of an unsigned word) | W | pres | Pm Ex Ecl Cb Cp Sh End | oracle (caller), C#, spec | EclVm.Execute |
| START 0x1C4D | [?] text helper used by View (no spec line) | U | pres | Pm Ex Cb Cp Sh | oracle (caller) | — |
| START 0x1C86 | AC from Dexterity | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.DexAc |
| START 0x1D08 | Dexterity adjustment (initiative, missile) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.DexAdjust, StatRecalc, StatRecalc.Weapon |
| START 0x1D8A | strength code for the carrying allowance | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | Carrying, Carrying.StrengthCode |
| START 0x1E22 | Strength to-hit bonus | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.StrHit |
| START 0x1EC5 | Strength damage bonus | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.StrDamage |
| START 0x1F4B | carrying allowance by strength code | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | Carrying, Carrying.AllowanceForCode, Carrying.StrengthAllowance |
| START 0x202B | remove a memorised spell | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.ApplyAttackDamage, FightSpells, FightSpells.RemoveSpell |
| START 0x20A9 | unlink and free an item | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | DropSpentWeapon, FightSpells.UseItemNow, FightSpells.RemoveScrollSpell |
| START 0x2197 | append an item copy to a character | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | TradeRules.GiveItem, EffectHandlers.SpiritualHammer |
| START 0x2233 | row-24 message | W | pres | Pm Ex Ecl Cb Cp Sh Lv Kd | C#, spec | HallPicture.HallState, HallScreens.Password, CharacterView.RowMessage |
| START 0x228C | combat / status message line | P | pres | Pm Ex Ecl Cb Cp Sh Kd | C# (inside), spec | CampScreens |
| START 0x2383 | clear the message window | W | pres | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatChooser.TurnStarted, CombatChooser.PrintCreatureMessage, CombatChooser.ClearMessageWindow |
| START 0x23B6 | character name in its colour (in play, hostile) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | oracle (caller), C#, spec | PorAmigaText, CombatChooser.NameColour, CharacterView.LetGoOnPage |
| START 0x2432 | draw an icon at a cell (picture, slot, mirror) | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FlightAnimation, FlightAnimation.SpellBlock, FlightAnimation.AimCursor |
| START 0x257E | hit / effect icon flash with delays | P | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatChooser.ShowHit, FightSpells.CastNow, FightSpells.HoldPerson |
| START 0x25C9 | moving icon (flight path) | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.Flying, CombatChooser.FlightPathMax, CombatChooser.Flight |
| START 0x2B8B | effect message (text and pause) | P | pres | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, `[?]`×1 | PorTextCatalog, CombatChooser.EffectMessage, CombatChooser.EffectTextMessage |
| START 0x2D39 | find a creature's effect record by id | W | play | Pm Ex Ecl Enc Cb Cp Sh Tl Lv Kd | oracle (caller), C#, spec | MonsterAi.CrossingEffects, EffectHandlers.TriggerEffect, EffectHandlers.FindEffect |
| START 0x2DC8 | what a hit does (HP, status) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.InflictDamage, EffectHandlers.NonWeaponDamage, EclPartyRules.Apply |
| START 0x2EEA | healed-amount message | W | pres | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | PorTextCatalog.Image, CombatChooser.HealedWords, CombatChooser.EffectMessage |
| START 0x2F57 | opposite side test (attack-ally check) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightSpells.AimAuto, FightSpells.EnemiesBeside, FightSpells.Routine |
| START 0x2F7B | count the teams (DS:6814 / 6815) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CountTeams, TurnUndead.Run, EffectHandlers.Revive |
| START 0x2FD3 | count enemies within range | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.CastOneSide, FightSpells.EnemiesBeside, MonsterAi.EnemiesWithin |
| START 0x30FB | distance between two creatures | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | NoFightEffects.IFightGeometry, IFightGeometry, FightRules.RangePenalty |
| START 0x31D7 | end a creature's actions | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.EndActions, FightSpells.ContinueCastingAuto, FightSpells.UseItemNow |
| START 0x3226 | Guard | W | play | Cb | C#, spec | PorTextCatalog.Image, CombatChooser.Attach, MonsterAi.FinishRound |
| START 0x3262 | spell table entry (DS:31FA + 16·id) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorSpellData.Spells, FightSpells, FightSpells.CasterLevel |
| START 0x32CA | 3D layout frame (inner boxes) | W | pres | Pm Ex Ecl Cb Cp Sh End | C#, spec | ClientPlayer.DrawInterface, Draw, OverlandBoxed |
| START 0x3379 | screen redraw by case (hall, camp, loot, shop pictures and boxes) | P | pres | Pm Ex Ecl Cb Cp Sh End | C#, spec, `[?]`×2 | ClientPlayer, ExploreScreen |
| START 0x34AB | position line | W | pres | Pm Ex Ecl Cb Cp Sh End | C#, spec | ClientPlayer.DrawInterface, PositionLineDrawn, ShownStatusLine |
| START 0x36FE | overland interface redraw | W | pres | Ex | C#, spec | PorGame.RunLoadedEntry, ClientPlayer.OtherLayout, AreaLoop.OverlandInterfacePending |
| START 0x3728 | screen clear | W | pres | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| START 0x37E8 | ask whom (member pick prompt) | W | pres | Pm Ex Ecl Cb Cp Sh | C#, spec | PorTextCatalog.Image, MemberPick, RestUi.PickTarget |
| START 0x39A3 | has a ranged weapon | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.HasRangedWeapon |
| START 0x39E6 | has a throwable weapon | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.HasThrowableWeapon, DropSpentWeapon |
| START 0x3A2B | find ranged ammunition | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.FindRangedAmmo, FightRules.RecalcAttacksLeft, FightMenu.TargetOffered |
| START 0x3AF7 | race level limit | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightKit.WireLevels, EffectHandlers.RaceLevelLimit, LevelStatsTables.LevelStats |
| START 0x3C3E | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Segment 03F1: windows and frames (4 routines: W 4 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x3F10 | window frame draw | W | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | C#, spec | OutsideEffectMessages.Message, TradeContext.FrameTextBox, RestUi.CastMenu |
| START 0x4047 | window inside blank | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | C#, spec | PartyMenuFlow.Add, CharacterView.ProgramItems, TextWindow.ClearRect |
| START 0x41F8 | window (frame and cleared inside) | W | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | C#, spec | PorAmigaFrame, KeyRequest.WindowLine, ListRequest.SpellPageLayout |
| START 0x4277 | empty routine (unit init or empty handler) | W | pres | Su | — | — |

#### Segment 042B: start-up configuration (1 routines: P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x4354 | start-up configuration: command line, video mode, sound device, installer flags (only the `F` line read) | P | pres | Su | spec (inside) | — |

#### Segment 0464: pictures: read, decode, draw (20 routines: W 5 (1 by execution), U 15)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x4640 | [?] picture decode helper | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | — | — |
| START 0x46BB | [?] picture buffer helper | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | — | — |
| START 0x470F | [?] picture helper (start-up) | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | — | — |
| START 0x473F | [?] picture block free / release | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | — | — |
| START 0x4751 | picture block read (DAX block → buffer) | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | spec | — |
| START 0x48E5 | [?] picture decode (block unpack into the off-screen buffer) | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | — | — |
| START 0x4F14 | [?] masked piece draw (tiles, wall pieces) | U | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | spec | — |
| START 0x53B1 | picture block draw (FINAL, frames) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | C#, spec | CombatChooser.DrawFlying, CombatScreen.DrawMap, EndGameLine.EndGamePicture |
| START 0x5AC4 | [?] picture draw helper | U | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| START 0x5B49 | [?] picture draw helper | U | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| START 0x5BD6 | [?] picture draw helper | U | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| START 0x5C07 | [?] picture draw variant (with 0x5AC4 / 0x5B49 / 0x5BD6) | W | pres | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatScreen.Mirrored |
| START 0x5F91 | picture block draw at a screen cell | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | spec | — |
| START 0x6192 | off-screen buffer to EGA memory | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | C#, spec | EffectHandlers.PutOutOfPlay |
| START 0x649C | [?] picture block copy / recolour into a slot | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | spec (inside) | — |
| START 0x6ACE | [?] palette flash / picture show | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End Kd | spec | — |
| START 0x6BD5 | picture memory free | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End Kd | spec | — |
| START 0x6C72 | [?] recolour (icon palette, backdrop) | U | pres | Pm Ex | — | — |
| START 0x704D | palette register flash (INT 10h) | W | pres | Su Pm Ex Ecl Cb Cp Sh Kd | C#, spec | Intro.FlashRegister |
| START 0x708A | empty routine (unit init or empty handler) | W | pres | Su | — | — |

#### Segment 0709: text, input, time (20 routines: W 11 (1 by execution), P 2, U 7)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x7090 | [?] text cell helper (under 0x7147) | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | — | — |
| START 0x7101 | [?] text set-up at start-up | U | pres | Su | — | — |
| START 0x7130 | [?] text set-up helper | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | — | — |
| START 0x7147 | [?] window / box fill helper | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | — | — |
| START 0x720C | [?] palette / colour set-up (start-up) | U | pres | Su | — | — |
| START 0x726F | glyph draw | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | C#, spec | PorAmigaRunes, TextWindow, ClientPlayer.DrawGlyphs |
| START 0x767B | see `combat-map.md` Start messages | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv Kd | C#, spec | CombatChooser.BattleBegins, ClientPlayer.Flash |
| START 0x76B6 | string draw (column, row, colour) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | C#, spec | CombatChooser.BattleBegins, SpellPageLayout.WholeTitle, VenueScreens.Summary |
| START 0x7737 | see `combat-menu.md` The combat screen (Client) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Kd | C#, spec | TextWindow.Typed, TextWindow.Put, TextWindow.Glyph |
| START 0x77A4 | text window helper (typing state) | U | pres | Pm Ex Ecl Enc Cb Cp Sh Kd | spec | — |
| START 0x77F8 | text window (typing, paging) | P | pres | Pm Ex Ecl Enc Cb Cp Sh Kd | C#, spec, `[?]`×1 | Calls.KeyPagePen, PorProgram.PagePrompt, CombatView.Panel |
| START 0x7A73 | line input (INPUTSTR, names) | W | pres | Su Pm Ecl | C#, spec | AmigaMenuPens.DrawTypedLine |
| START 0x7BF6 | number input (INPUTNUM) | P | pres | Ecl | C# (inside), spec | ClientPlayer |
| START 0x7C85 | row-24 key prompt | W | pres | Pm Ex Ecl Enc Cb Cp Sh Tl Sv Kd | C#, spec | PorAmigaText, Setup.PenSites, Calls.KeyPagePen |
| START 0x7CCF | GetTime (hundredths) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End | C#, spec | PictureAnimation, EndGamePicture.EndGameAnimation, DosClock |
| START 0x7D3B | [?] text / box helper (Items page, View) | U | pres | Pm Ex Cb Cp Sh | — | — |
| START 0x7DF8 | bottom-line message for game speed × 225 ms | W | pres | Pm Ex Lv | C#, spec | HallPicture.HallState, HallScreens.Message, HallContext.HallTrainer |
| START 0x7E4E | game-speed delay | W | pres | Pm Ex Ecl Cb Cp Sh Lv Sv End Kd | C#, spec | CombatChooser.MessageMs, CharacterView.PauseOnly, OutsideEffectMessages.Pause |
| START 0x7E63 | box blank after a pick | W | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | C#, spec | TextWindow.Clear, ClientPlayer.ClearBox, ClientPlayer.VerticalMenu |
| START 0x8012 | empty routine (unit init or empty handler) | W | pres | Su | — | — |

#### Segment 0802: DAX archive reader (open, index, block read, unpack) (18 routines: N 18)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x8020 | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x8040 | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x8060 | [?] not read; called by START 0x8125, START 0x8195 | N | pres | — | — | — |
| START 0x8125 | [?] not read; called by START 0x81B3 | N | pres | — | — | — |
| START 0x8195 | [?] not read; called by START 0x81B3 | N | pres | — | — | — |
| START 0x81B3 | [?] not read; called by START 0x827D, START 0x8736 | N | pres | — | spec (inside) | — |
| START 0x8232 | [?] not read; called by START 0x4354 | N | pres | Su | spec (inside) | — |
| START 0x827D | [?] not read; called by 0x1EAD2, 0x1F5E8 | N | pres | Su Pm Sv | — | — |
| START 0x830B | [?] not read; called by 0x1DFF6, 0x1E684 | N | pres | Pm Ecl Sv | — | — |
| START 0x8376 | [?] not read; called by 0x1E684, 0x1F5E8 | N | pres | Pm Sv | — | — |
| START 0x8445 | [?] not read; called by 0x1E684, 0x1EAD2 | N | pres | Pm Sv | — | — |
| START 0x852F | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x85ED | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x8707 | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x8736 | [?] not read; called by 0x1EEAA, 0x26F6 | N | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Sv End | — | — |
| START 0x896A | disk number set (DS:5376 / 5377) | N | play | Pm Ecl Enc Sv | spec | — |
| START 0x897D | [?] disk helper | N | play | Pm Ecl Sv | — | — |
| START 0x8988 | empty routine (unit init or empty handler) | N | pres | Su | — | — |

#### Segment 0899: disk number, keys while busy, sound stop (5 routines: W 5 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x8990 | sound stop command | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle (caller), C#, spec | PartyMenuFlow.Run |
| START 0x89E9 | keys while busy (toggles) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle (caller), C#, spec | Waiting.ReadingKeys |
| START 0x8A94 | key flush | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End Kd | oracle (caller), C#, spec | IntroLine.Intro, Intro.Key |
| START 0x8AB2 | key poll (no action) | W | pres | Cb | C#, spec | Waiting.Discard, FightKit.AiKey, FightKit.Create |
| START 0x8ACC | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Segment 08AE: sound driver (PC speaker / Tandy, timer interrupt) (16 routines: W 10 (5 by execution), U 6)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x8AE0 | sound: driver entry | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | spec | — |
| START 0x8B4C | sound: driver helper | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |
| START 0x8BBC | sound: driver start | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | C#, spec | ISoundPorts.PcSoundDevice |
| START 0x8C5B | sound: device set-up | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |
| START 0x8C85 | sound: tune cue | U | pres | Su Pm Ex Ecl Cb Cp Sh Kd | spec | — |
| START 0x8CB2 | empty routine (unit init or empty handler) | W | pres | Su | — | — |
| START 0x9E5C | sound: timer install | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle | — |
| START 0x9E91 | sound: timer restore | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |
| START 0x9ECA | sound: timer interrupt (tick) | W | pres | — | oracle | — |
| START 0x9F57 | sound: per-tick voice step (from the timer interrupt) | W | pres | — | oracle (caller) | — |
| START 0x9FCC | sound: per-tick voice step (from the timer interrupt) | W | pres | — | oracle (caller) | — |
| START 0xA05A | sound: note start helper | W | pres | — | oracle (caller) | — |
| START 0xA0EB | sound: port write helper | W | pres | — | oracle (caller) | — |
| START 0xA222 | sound: PC-speaker tune programmer | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle | — |
| START 0xA28E | sound: Tandy tune programmer | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle | — |
| START 0xA2FA | sound: driver helper | U | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |

### GAME.OVR

#### Unit 0x0008–0x00C5 (stub 16): start-up odds and ends (2 routines: U 2)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x004A | [?] no caller found (unit 0x16): disk prompt then the game save 0x1FD3E; reached through a procedure variable or dead | U | play | — | spec (inside) | — |
| 0x00B4 | [?] start-up: sets DS:4414 / DS:4416 (unit 0x16) | U | play | Su | spec (inside) | — |

#### Unit 0x00D4–0x08E1 (stub 19): introduction (4 routines: W 4 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x00D4 | introduction wait (time or key, key flush) | W | pres | Su | spec (inside) | — |
| 0x02DA | introduction credits page | W | pres | Su | C#, spec | PorTextCatalog, IntroScreen.DrawCredits, IntroStep |
| 0x06FB | the introduction: logo, title, credits | W | pres | Su | C#, spec | Program, IntroScreen, IntroLine.Intro |
| 0x08DD | empty routine (unit init or empty handler) | W | play | Su | C# (inside) | PorProgram |

#### Unit 0x09AC–0x0D09 (stub 1C): code wheel (2 routines: W 1 (1 by execution), N 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x0AA9 | code wheel (copy protection) | N | pres | Su | spec | — |
| 0x0D05 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x0D46–0x4252 (stub 1F): ECL opcode handlers, dispatcher, run loop, area loop (56 routines: W 56 (50 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x0D47 | ECL EXIT handler | W | play | Ecl Sh | golden, C# (inside), spec | ClientPlayer, EclVm |
| 0x0E3A | ECL GOTO handler | W | play | Ecl | golden | — |
| 0x0E57 | ECL GOSUB handler | W | play | Ecl | golden, spec | — |
| 0x0E6C | ECL COMPARE handler | W | play | Ecl | golden, C#, spec | EclVm.DoCompare |
| 0x0EE9 | ECL ADD/SUB/DIV/MUL handler | W | play | Ecl | golden, C# (inside), spec | AmigaDiskFont, EclPartyRules |
| 0x0F7A | ECL RANDOM handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x0FD8 | ECL SAVE handler | W | play | Ecl | golden, C#, spec (inside) | EclVm.Execute |
| 0x1026 | ECL LOADCHAR handler | W | play | Ecl | golden, C#, spec | EclVm, EclPartyRules, EclPartyRules.LoadCharacter |
| 0x10FA | ECL SETUPMON handler | W | play | Enc | golden, C#, spec | EncounterHost.StartEncounter |
| 0x1196 | ECL LOADMON handler | W | play | Enc | golden, C# (inside), spec | ModuleDocuments, HallView |
| 0x155D | ECL APPROACH handler | W | play | Enc | golden, C# (inside), spec | AmigaView, EclMemory |
| 0x159C | ECL PICTURE handler | W | play | Ecl | golden, C#, spec | ViewPictures.Find |
| 0x1697 | ECL INPUTNUM handler | W | play | Ecl | golden, C#, spec | ClientPlayer.InputNumber, EclVm.Execute |
| 0x16DA | ECL INPUTSTR handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x174F | ECL PRINT/PRINTCLR handler | W | play | Ecl | golden, C#, spec | TextWindow.Left, ClientPlayer.Print, EclVm.Execute |
| 0x17E5 | ECL RETURN handler | W | play | Ecl | golden, spec (inside) | — |
| 0x1833 | ECL CMPAND handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x1898 | ECL IFEQ/IFNE/IFLT/IFGT/IFLE/IFGE handler | W | play | Ecl | golden, C# (inside) | Carrying, EclVm |
| 0x1917 | ECL NEWECL handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x19BA | ECL LOADFILES/LOADPIECES handler | W | play | Ecl | golden, C#, spec | EclVm.DoLoadFiles, EclVm.DoLoadPieces, EclMemory.FilesLoadedSinceSwitch |
| 0x1B35 | ECL AND/OR handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x1BA4 | ECL GETTABLE handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x1C02 | ECL SAVETABLE handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x1C4E | ECL VMENU handler | W | play | Ecl | golden, C#, spec | ChoiceRequest.ListRow, ClientPlayer.VerticalMenu, EclVm.Execute |
| 0x1E17 | ECL HMENU handler | W | play | Ecl | golden, C#, spec | PorTextCatalog.Overlay, ClientPlayer.OneWordSwap, EclVm.Execute |
| 0x1FA3 | ECL CLRMON handler | W | play | Ecl | golden, spec | — |
| 0x200C | ECL PARTYSTR handler | W | play | Ecl | golden, C#, spec | EclVm.PartyStrength |
| 0x211D | CHKPARTY helper: writes a VM cell (0x814A) | W | play | Ecl | C# (inside), spec (inside) | AmigaView |
| 0x217B | ECL CHKPARTY handler | W | play | Ecl | golden, C#, spec | EclVm, EclPartyRules, EclPartyRules.CheckParty |
| 0x236F | ECL PSURPRISE handler | W | play | Enc | golden, C#, spec (inside) | OutsideEffectMessages.Message |
| 0x240F | ECL SURPRISE handler | W | play | Enc | golden, C#, spec | EclVm.Execute |
| 0x24E1 | ECL COMBAT handler | W | play | Cb | golden, C#, spec | IEncounterUi.ICombatRunner |
| 0x263E | ECL ONGOTO/ONGOSUB handler | W | play | Ecl | golden, C#, spec | CombatChooser.Flight, EclVm.Execute, EclVm.LoadVariadic |
| 0x26F6 | ECL TREASURE handler | W | play | Tl | golden, C#, spec | CombatChooser.Flight |
| 0x2AF7 | ECL ROB handler | W | play | Ecl | golden, C#, spec | CombatChooser.Flight, EclVm, EclPartyRules |
| 0x2C3E | ECL ENCMENU handler | W | play | Enc | golden, C#, spec | EclVm, IEclHost |
| 0x3311 | ECL PARLEY handler | W | play | Enc | golden, spec | — |
| 0x33B0 | ECL FINDITEM handler | W | play | Ecl | golden, C#, spec | EclVm.Execute |
| 0x3465 | ECL DELAY handler | W | play | Ecl | golden | — |
| 0x34BB | ECL DAMAGE handler | W | play | Kd | golden, C#, spec | Calls.KeyDamagePen, ClientPlayer.PartyDestroyed, EclVm |
| 0x37E8 | ECL SPRITEOFF handler | W | play | Ecl | golden, C#, spec | PorTextCatalog.Image, MemberPick, RestUi.PickTarget |
| 0x3807 | ECL CLOCK handler | W | play | Ecl | golden, C#, spec | EclVm |
| 0x3831 | ECL PRINTRET handler | W | play | Ecl | golden, C#, spec | TextWindow.NewLine, IEclHost |
| 0x385A | ECL CLEARBOX handler | W | play | Ecl | golden, C#, spec | ClientPlayer.ClearBox, IEclHost |
| 0x3879 | ECL WHO handler | W | play | Ecl | golden, C#, spec | ClientPlayer.PickCharacter, EclVm, IEclHost.NoCharacter |
| 0x38C4 | ECL ADDNPC handler | W | play | Ecl | golden, C#, spec | EclVm.DoAddNpc |
| 0x3949 | ECL SPELL handler | W | play | Ecl | golden, C#, spec | EclVm, EclPartyRules, EclPartyRules.Spell |
| 0x3A15 | ECL CALL handler | W | play | Sh | golden, C#, spec | EncounterHost.AfterCombatStep, EclProfiles.Por13 |
| 0x3B19 | see `camp.md` The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3) | W | play | Ex Sh | C#, spec | AreaCommand, AreaLoop.Encamp |
| 0x3B56 | ECL PROGRAM handler | W | play | Sh | golden, C#, spec | ClientPlayer.Program, EclVm, EclProfiles.Por13 |
| 0x3BC3 | ECL PROTECT handler | W | play | Ecl | golden, C#, spec | ClientPlayer.DrawGlyphs, EclVm, IEclHost |
| 0x3C73 | ECL opcode dispatcher (compare chain on DS:6F7F) | W | play | Ecl | C# (inside) | PorOverland, IScreen |
| 0x3EB7 | ECL run loop | W | play | Ecl | C#, spec | EncounterHost.Vm, EclVm.RunVector, UnhandledOpcodeMode |
| 0x3EFA | after a script switch: vector 4, then 0 and 1 | W | play | Ex | C#, spec | AreaLoop.Run, OverlandStep.Overland, EclVm.FollowScriptChanges |
| 0x403E | area loop (vectors 0/1, steps, party-defeated exit) | W | play | Ex | C#, spec | PorGame.LoadInHall, PorGame.RunLoadedEntry, PorGame.NewGameScript |
| 0x424E | empty routine (unit init or empty handler) | W | play | Su | C# (inside) | EclVm, EclOpcodes |

#### Unit 0x4577–0x54AD (stub 22): temple (12 routines: W 11 (1 by execution), P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x458A | see `shop-format.md` Temple | W | play | Sh | C#, spec | VenueScreens.HealList, VenueScreens.Heal, TempleRules |
| 0x4636 | see `shop-format.md` Price checks | W | play | Sh | C#, spec | VenueScreens.HealList, VenueScreens.PayColours, Payer.Pay |
| 0x47C0 | 1000 | W | play | Sh | C#, spec | TempleRules |
| 0x4864 | temple cure 1: six effects (DS:0114) | P | play | Sh | C#, spec, `[?]`×1 | TempleRules, Cure.DiseaseEffects |
| 0x4978 | temple cures 2–4: heal 1d8 / 2d8+1 / 3d8+3 | W | play | Sh | C#, spec | TempleRules |
| 0x4A87 | temple raise dead | W | play | Sh | C#, spec | TempleRules |
| 0x4CC8 | 1000 | W | play | Sh | C#, spec | TempleRules, Cure.PoisonEffects |
| 0x4DB1 | temple remove curse | W | play | Sh | C#, spec | TempleRules |
| 0x4E9F | 2000 | W | play | Sh | C#, spec | TempleRules |
| 0x4F4E | temple heal list | W | play | Sh | C#, spec | PorProgram.TempleNames, SpellPageLayout.WholeTitle, VenueScreens.Temple |
| 0x5280 | temple | W | play | Sh | C#, spec | VenueScreens, VenueScreens.PriceField, ICombatRunner |
| 0x54A9 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x557E–0x6BF3 (stub 25): after combat (share, award, removal, party defeated) (12 routines: W 12 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x557E | after combat: experience share | W | play | Kd | C#, spec | AfterCombat.Share |
| 0x58D3 | after combat: award | W | play | Kd | C#, spec | CombatRules.ExperienceFor, CombatRules.AwardExperience |
| 0x5A47 | see `start-menu.md` After combat | W | play | Kd | C#, spec | ClientPlayer.KeepsCombatRow24, EncounterHost.Vm, AfterCombat |
| 0x5E94 | see `combat-format.md` After combat | W | play | Cb | C#, spec | VenueScreens, VenueScreens.Heal, VenueScreens.Loot |
| 0x6092 | see `por-art-formats.md` Amiga program reads (s19; hunks numbered from 0 in file order) | W | pres | Cb | C#, spec | VenueScreens.ItemPage |
| 0x6184 | see `combat-format.md` After combat | W | play | Cb | C#, spec | VenueScreens.TakeItems, ShopRules.GiveCopy |
| 0x62B6 | see `combat-format.md` Loot menu | W | play | Cb | C#, spec | PorTextCatalog, VenueScreens.LootTake |
| 0x644B | see `combat-format.md` After combat | W | play | Cb | C#, spec | PorTextCatalog, TradeContext.VenueRedrawn, VenueScreens |
| 0x672A | after combat: removal of the monsters | W | play | Cb | C#, spec | ClientPlayer.KeepsCombatRow24, ClientPlayer.Trade, EncounterState.Monsters |
| 0x6866 | see `combat-format.md` After combat | W | play | Cb | C#, spec | VenueScreens.Heal, ClientPlayer.Trade, EndOfRoundCreature |
| 0x6AA5 | after combat | W | play | Cb | C#, spec | ClientPlayer.KeepsCombatRow24, Draw, EncounterHost.AfterCombatStep |
| 0x6BEF | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x6CD4–0x7470 (stub 28): shop (5 routines: W 5 (2 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x6D04 | see `por-art-formats.md` Amiga program reads (s19; hunks numbered from 0 in file order) | W | play | Sh | C#, spec | TradeContext.StoredItemName, VenueScreens.Buy, VenueScreens.ItemPage |
| 0x6F0D | see `shop-format.md` Shop menu | W | play | Cb Sh | oracle, C#, spec | VenueScreens.Buy, VenueScreens.TakeItems, ShopRules.GiveCopy |
| 0x7035 | see `combat-format.md` Shop | W | play | Sh | C#, spec | VenueScreens.Buy, ShopRules.GiveCopy, BuyResult.Buy |
| 0x7212 | shop | W | play | Sh | C#, spec | VenueScreens, VenueScreens.MemberLetters, ShopRules |
| 0x746C | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x74D3–0x96EC (stub 2B): ECL VM helpers (operands, VM memory, text), script loader, encounter draw (33 routines: W 33 (31 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x74D3 | operand loader | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclVm.NormaliseInput, EclVm.LoadVariadic |
| 0x7609 | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclVm.NormaliseInput |
| 0x7689 | script init | W | play | Ecl | golden, C#, spec | EclVm.InitScript, EclCells.InitWrites |
| 0x7814 | script file loader (ECL<d>) | W | play | Ecl | golden, C#, spec | PorGame.RunNewGameEntry, ClientPlayer.ScriptChanging, Draw |
| 0x78E4 | [?] not read; called by 0x1196 | W | play | Enc | golden, spec (inside) | — |
| 0x7955 | see `ecl-format.md` View distance | W | play | Enc Cb | golden, C#, spec | EncounterHost.ViewDistance, EncounterRules.ViewDistance |
| 0x79E4 | see `combat-format.md` Shop | W | play | Ecl Enc | golden, C#, spec | ViewPictures.DrawVenuePortrait, ClientPlayer.ShowEncounter, ClientPlayer.ShowPicture |
| 0x7A21 | encounter draw | W | pres | Enc | golden, C#, spec | EncounterHost.Draw, IEncounterUi, EclMemory.EncounterSpriteUp |
| 0x7B82 | lo/hi → word | W | play | Ecl Enc Sh Tl Kd | golden, spec | — |
| 0x7BA6 | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, spec | — |
| 0x7BCE | see `ecl-format.md` VM memory map | W | play | Ecl Enc Sh Tl Kd | golden, spec | — |
| 0x7C29 | [?] not read; called by 0x7C8F | W | play | Ecl Enc Sh Tl Kd | golden | — |
| 0x7C8F | see `ecl-format.md` Where the scripts live | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclPartyPosition.EclMemory, EclMemory.StoreCell, EclCharacterView |
| 0x7F47 | see `ecl-format.md` Where the scripts live | W | play | Ecl Enc | golden, C#, spec | EclPartyPosition.EclMemory, EclMemory.StoreCell, EclCharacterView |
| 0x814A | see `ecl-format.md` Other handlers read in the program (2026-11-17) | W | play | Ecl Enc | golden, C#, spec | IEclHost, EclMemory.SpecialWrites, EclMemory.SpecialNoOpWrites |
| 0x82DF | see `ecl-format.md` VM memory map | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclMemory.SpecialReads, EclMemory.Read |
| 0x8426 | see `ecl-format.md` SAVE of text | W | play | Ecl | golden, C#, spec | EclMemory.WriteText |
| 0x8626 | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, spec | — |
| 0x87CE | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclMemory.ReadText |
| 0x8A40 | see `por-art-formats.md` Amiga program reads (s19; hunks numbered from 0 in file order) | W | pres | Ecl Enc Cb Sh | golden, C#, spec | AmigaView.TildeCase |
| 0x8B57 | see `view3d.md` Text window ; program read s12) | W | play | Ecl Enc Cb Sh | golden, C#, spec | VenueScreens.LootMenu |
| 0x8C5B | see `ecl-format.md` Other handlers read in the program (2026-11-17) | W | play | Ecl | golden, C#, spec | ChoiceRequest.ListRow, ClientPlayer.VerticalMenu |
| 0x8CE8 | see `ecl-format.md` Instructions and control flow | W | play | Ecl | golden, C#, spec | EclVm.DoCompare |
| 0x8DBE | see `ecl-format.md` Instructions and control flow | W | play | Ecl | golden, C#, spec | EclVm.DoCompare |
| 0x8E24 | GOSUB push | W | play | Ecl | golden, spec | — |
| 0x8EAE | see `combat-format.md` CALL ids (handler | W | play | Sh | golden, C#, spec | EncounterHost.StepForward |
| 0x8F42 | 1 / 0) | W | play | Sh | golden, C#, spec | EncounterState.Duel, EncounterHost.SetUpDuel |
| 0x9196 | [?] not read; called by 0x2AF7 | W | play | Ecl | golden, C# (inside) | EclVm, EclProfiles |
| 0x92C3 | see `ecl-format.md` Party, damage and text-box opcodes (program read s11, 2026-11-17) | W | play | Ecl | golden, C#, spec | EclPartyRules.Rob |
| 0x9379 | see `ecl-format.md` Instructions and control flow | W | play | Ecl | golden, C#, spec | EclVm.SkipNextInstruction, EclOpcodes.Por13, EclOpcodes.SkipCount |
| 0x9484 | see `ecl-format.md` Encounters (program read 2026-11-17) | W | play | Enc | golden, C#, spec | EncounterRules.EffectDoubleMove, EncounterRules.PartySpeed, EclCharacter.Effects |
| 0x959F | see `ecl-format.md` Party, damage and text-box opcodes (program read s11, 2026-11-17) | W | play | Kd | golden, C#, spec | Calls.KeyDamagePen, Calls.DamageLinePen, PorTextCatalog.Overlay |
| 0x96E8 | empty routine (unit init or empty handler) | W | play | Su | golden | — |

#### Unit 0x9807–0xAA85 (stub 35): the fight (top level, clean-up, round loop) (14 routines: W 12 (1 by execution), P 1, N 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x9807 | fight clean-up: frees the creature list and the arena | N | play | Cb | C# (inside), spec | EclMemory, EclProfile |
| 0x9881 | the fight (top level) | W | play | Cb | C#, spec | CombatChooser.BattleBegins, ICombatRunner, EndActionsController.Fight |
| 0x9938 | next creature to act | P | play | Cb | C#, spec, `[?]`×1 | CombatRules.NextCreature, EndActionsController.Fight, Fight.NextCreature |
| 0x99FD | see `combat-format.md` Data the combat code uses | W | play | Cb | C#, spec | CombatChooser.TurnStarted, CombatChooser.TurnEnded, EndActionsController.Fight |
| 0x9B21 | see `combat-format.md` The fight | W | play | Cb | C#, spec | GameAction, CombatView.CombatChooser, CombatChooser.TurnStarted |
| 0x9E6D | see `combat-format.md` Program units of the fight | W | play | Cb | C#, spec | CombatMsg, FightCommand, IFightMenuChooser |
| 0xA087 | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, GameClock, EndOfRoundCreature |
| 0xA1E6 | see `combat-format.md` Program units of the fight | W | play | Cb | C#, spec | CombatPlayer, CombatChooser.ChooseMove, CombatMsg |
| 0xA54C | see `combat-format.md` Program units of the fight | W | play | Cb | C#, spec | CombatMsg, FightMenu.AttackByMove |
| 0xA64C | see `combat-format.md` Program units of the fight | W | play | Cb | C#, spec | CombatMsg, FightCommand.DoneCommand, IFightMenuChooser |
| 0xA806 | see `combat-format.md` Program units of the fight | W | play | Cb | C#, spec | CombatMsg, FightMenu.Bandage, Fight.PartyMemberDying |
| 0xA8FD | see `combat-format.md` Program units of the fight | W | play | Cb | C#, spec | CombatMsg, DoneCommand.SpeedCommand, IFightMenuChooser |
| 0xAA32 | see `combat-format.md` Program units of the fight | W | play | Cb | C#, spec | FightMenu.MakeQuick |
| 0xAA81 | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0xABC0–0xC4E6 (stub 38): monster AI (15 routines: W 15 (3 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0xABCF | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatChooser.Attach, CombatChooser.TurnStarted, CombatMsg |
| 0xADC3 | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xAE15 | see `combat-format.md` The fight | W | play | Cb | oracle (caller), C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xAEAB | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | CombatMsg, CombatChooser.AiSays, FightSpells.FindCureTarget |
| 0xAFA4 | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, FightSpells.UseItem |
| 0xB105 | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, FightSpells.CastAuto |
| 0xB235 | see `combat-format.md` The fight | W | play | Cb | C#, spec | PorProgram.AiStepOffsetsBase, CombatMsg, CombatChooser.AiSays |
| 0xB3B2 | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xB6FC | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xBB0F | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xBB81 | see `combat-format.md` The fight | W | play | Cb | C#, spec | Waiting.TakeAiKey, GameAction, CombatMsg |
| 0xBC97 | see `combat-format.md` The fight | W | play | Cb | C#, spec | AiMessage.MonsterAi, MonsterAi.FinishRound, MonsterAi.CheckMorale |
| 0xBE4D | see `combat-format.md` The fight | W | play | Cb | C#, spec | AiMessage.MonsterAi, MonsterAi.WeaponPriority |
| 0xBF8A | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatChooser.Attach, AiMessage.MonsterAi, MonsterAi.PanelRedrawn |
| 0xC4E2 | empty routine (unit init or empty handler) | W | play | Su | C#, spec | AiMessage.MonsterAi |

#### Unit 0xC5D1–0xE562 (stub 3B): combat map: build, terrain, placement (23 routines: W 23 (20 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0xC5D1 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | CombatArena.ArenaBuilder, ArenaBuilder.SprinkleRareFeatures, ArenaBuilder.BuildWilderness |
| 0xC6FC | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.WallBetween |
| 0xC77E | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.WallBetween |
| 0xC7F2 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), spec | — |
| 0xC88B | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.BuildDungeon |
| 0xC948 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.BuildDungeon |
| 0xC9E0 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.BuildDungeon, ArenaBuilder.CornerNorthWest |
| 0xCBE6 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.BuildDungeon, ArenaBuilder.CornerNorthEast |
| 0xCDE1 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle, C#, spec | CombatArena, CombatArena.ArenaBuilder, ArenaBuilder.BuildDungeon |
| 0xCE75 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle, C#, spec | ArenaBuilder.TerrainClass |
| 0xD168 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), spec | — |
| 0xD1AF | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.BuildWilderness, ArenaBuilder.PlaceRiver |
| 0xD2E4 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.BuildWilderness, ArenaBuilder.PlaceTrees |
| 0xD46F | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | ArenaBuilder.BuildWilderness, ArenaBuilder.ScatterFeatures |
| 0xD785 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle, C#, spec | CombatArena, CombatArena.ArenaBuilder, ArenaBuilder.BuildWilderness |
| 0xD815 | see `combat-menu.md` Ground, program read (P, s26). | W | play | Cb | C#, spec | CombatArt.Ground, FightStartArea.FightPlacement, FightPlacement.Start |
| 0xD8B2 | see `combat-format.md` Data the combat code uses | W | play | Cb | C#, spec | EncounterState.NotParty, EncounterHost.StartFight, AfterCombat.NotParty |
| 0xD9D4 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | TeamGrid.FullyOff |
| 0xDA05 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | FightPlacement.InitRecords, TeamGrid.TryPlaceAt |
| 0xDB48 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle (caller), C#, spec | FightPlacement.InitRecords, TeamGrid.FindPlacement |
| 0xDFC6 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Cb | oracle, C#, spec | Waiting.Discard, FightKit.Create, FightStartArea.FightPlacement |
| 0xE402 | see `combat-format.md` The fight | W | play | Cb | C#, spec | PorTextCatalog, Waiting.Discard, CombatChooser.BattleBegins |
| 0xE55E | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0xE5D3–0xEBEC (stub 3E): game start-up (VM zones, start values) (2 routines: W 2 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0xE600 | game start-up: VM zones, start values, tables | W | play | Su | C#, spec | PorGame.NewGameScript, PorGame.NewGameState, MenuHighlight.ListTop |
| 0xEBE8 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0xEC5B–0x12508 (stub 41): effect handlers (127 routines: W 123 (14 by execution), P 4)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0xEC5B | CS:1C8D of unit | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | PorTextCatalog, EffectHandlers.More, EffectHandlers.Holds |
| 0xEC7A | see `spells-effects.md` Spells | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.AddEffectIfNoLock |
| 0xECB3 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | PorTextCatalog, CombatChooser.AttackEnd, CombatChooser.EffectMessage |
| 0xED68 | effect handler 1 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xED78 | effect handler 2 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xED96 | effect handler 3 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xEDDA | effect handler 4 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0xEE1E | effect handler 6 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xEE8E | effect handler 7 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.TrainingManual |
| 0xEFD2 | effect handlers 8, 45 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF007 | effect handlers 9, 46 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF03C | effect handler 10 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF05B | effect handler 11 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatScreen.Icon, EffectHandlers.CallHandler, EffectHandlers.Charm |
| 0xF115 | effect handlers 12, 38 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.StrengthEffect |
| 0xF231 | effect handler 14 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF246 | effect handler 15 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler, EffectEvent |
| 0xF2A2 | effect handler 16; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0xF2A9 | effect handler 17 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF2D4 | effect handler 18 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF33F | effect handler 20 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF36A | effect handler 21 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF3D0 | effect handler 22 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF43B | effect handler 23 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler, EffectHandlers.SpiritualHammer |
| 0xF573 | effect handler 25 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF5A2 | effect handler 26 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF61B | effect handler 28 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, seen only | EffectHandlers.CallHandler |
| 0xF698 | effect handler 29 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF6C0 | effect handler 30 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF776 | effect handler 32 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF807 | effect handler 33 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF82A | effect handler 34 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF861 | effect handler 35 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF870 | effect handler 36 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF881 | effect handler 37 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF8A6 | effect handler 39 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xF91E | effect handler 40 | P | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, `[?]`×1 | EffectHandlers.CallHoldHandlers, EffectHandlers.CloudGone |
| 0xFC07 | see `spells-effects.md` Missile dodge | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.DodgeMissile |
| 0xFC78 | see `spells-effects.md` Handlers 1–72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.MissileOf |
| 0xFCE1 | effect handler 41 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFD1C | effect handler 42 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFD39 | effect handler 43 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFDC3 | effect handler 44 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler, EffectEvent |
| 0xFE54 | effect handler 47 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFED1 | effect handler 48 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFF0D | effect handler 49 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFF56 | effect handler 50 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFF9A | effect handlers 31, 51, 52, 53 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFFB0 | effect handler 54; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0xFFB7 | effect handler 55; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0xFFBE | effect handler 56 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0xFFF4 | effect handler 57 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0x100AD | effect handler 58 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0x100CD | effect handler 59 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0x100EB | effect handler 61 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0x10146 | effect handler 62 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHandler |
| 0x101A0 | see `spells-effects.md` Handlers 1–72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | PorTextCatalog, EffectHandlers.PoisonAttack, EffectEvent |
| 0x10246 | see `spells-effects.md` Handlers 1–72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers, EffectHandlers.ParalysingHit |
| 0x102B8 | effect handler 64 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CallHandler |
| 0x102CC | effect handler 65 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers |
| 0x102E0 | effect handler 66 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers |
| 0x102F4 | effect handler 67 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers |
| 0x10316 | effect handler 68 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers |
| 0x10341 | effect handler 69 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers |
| 0x10366 | effect handler 70 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers |
| 0x1037A | effect handler 71 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers |
| 0x1038B | effect handler 72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers |
| 0x103CA | effect handler 75 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.BloodDrain |
| 0x10510 | effect handler 76 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHoldHandlers |
| 0x10590 | effect handler 77 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHoldHandlers |
| 0x10634 | effect handler 78 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x10674 | effect handler 79 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x106A7 | effect handler 80 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x10705 | effect handler 81 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.FearAura |
| 0x10803 | effect handler 82 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.ParalysingAura |
| 0x10908 | effect handler 83 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatMsg, EffectHandlers.CallHoldHandlers, EffectHandlers.Gaze |
| 0x10AD2 | effect handler 84 | P | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, `[?]`×1 | CombatMsg, EffectHandlers.CallHoldHandlers, EffectHandlers.CharmGaze |
| 0x10C5D | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | EffectHandlers.LevelDrain, EffectHandlers.TopClass |
| 0x10D7D | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | PorTextCatalog, EffectHandlers.CallHoldHandlers, EffectHandlers.LevelDrain |
| 0x10FFF | effect handler 85 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | EffectHandlers.CallHoldHandlers |
| 0x1101D | effect handler 86 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | EffectHandlers.CallHoldHandlers |
| 0x11047 | effect handler 87 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x110EB | effect handler 89 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11134 | effect handler 90 | P | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, `[?]`×1 | EffectHandlers.CallMoreHandlers, EffectHandlers.ConSaveBonus |
| 0x111B1 | effect handler 91 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x111FD | effect handler 93 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11217 | effect handler 94 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1126B | effect handler 95 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x112A6 | effect handler 96 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x112E5 | effect handler 97 | P | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, `[?]`×1 | EffectHandlers.CallMoreHandlers, EffectHandlers.ConSaveBonus |
| 0x1135B | effect handler 98 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1138B | effect handler 99 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11434 | effect handler 100 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1146F | effect handler 101 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x114C1 | effect handler 102 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1151A | effect handler 103 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers, EffectEvent |
| 0x1155C | effect handler 104 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11570 | magic resistance / caster-level check (handlers 105, 106) | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CasterLevel, EffectHandlers.MagicResistance |
| 0x115D1 | effect handler 105 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x115DF | effect handler 106 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x115ED | effect handler 107 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11611 | effect handler 108 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11626 | effect handler 109 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CallMoreHandlers |
| 0x11634 | effect handler 110 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CallMoreHandlers |
| 0x1164B | effect handler 111 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CallMoreHandlers |
| 0x1166C | effect handler 112 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CallMoreHandlers |
| 0x11683 | effect handler 113 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x116C8 | effect handler 114 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x116E2 | effect handler 115 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11741 | effect handler 116 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1177B | effect handler 117 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x117BE | effect handler 118 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x117D8 | effect handler 119 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11823 | effect handler 120 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11877 | effect handler 121 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatMsg, EffectHandlers.CallHoldHandlers, EffectHandlers.AcidBreath |
| 0x11993 | effect handler 122 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x119E3 | effect handler 123 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11A3F | effect handler 124 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CallMoreHandlers |
| 0x11A63 | effect handler 125 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C# | EffectHandlers.CallMoreHandlers |
| 0x11A92 | effect handler 126 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11B35 | effect handlers 128, 129, 130, 133, 134, 136, 138, 139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11BB2 | effect handler 131 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.Gauntlets |
| 0x11D00 | effect handler 132 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11D8F | effect handler 135 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11DD5 | effect handler 137 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11DF6 | effect handlers 5, 13, 19, 24, 27, 60, 63, 92, 127; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.EmptyHandlers |
| 0x11DFD | see `por-art-formats.md` Amiga program reads (s19; hunks numbered from 0 in file order) | W | play | Su | oracle, C#, spec, `[?]`×1 | EffectHandlers.Holds, EffectHandlers.LightningBreath, EffectHandlers |
| 0x12504 | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x127FB–0x1646C (stub 68): combat rules: round start, attacks, moves, turn undead, AI casts (39 routines: W 35 (23 by execution), P 4)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x127FB | start of round (per creature) | P | play | Cb | C#, spec, `[?]`×1 | PorTextCatalog, FightRules.FindRangedAmmo, FightRules.BeginRound |
| 0x1291E | see `combat-format.md` Start of round | W | play | Cb | C#, spec | FightRules.CreatureMovement, MonsterAi.HighestEnemyMovement, FightMoves.Flee |
| 0x1298C | see `combat-format.md` Damage | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.WeaponDamage |
| 0x12AF4 | see `combat-format.md` What a hit does | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Say, CombatChooser.AttackMessage, CombatChooser.AttackEnd |
| 0x12E22 | see `combat-format.md` Attacks | W | play | Cb | C#, spec | FightSeams, FightMoves, FightMoves.GuardsAttack |
| 0x12F0D | one-square move (cost, step) | P | play | Cb | C#, spec, `[?]`×1 | CombatChooser.Stepped, FightMenu.Move, FightSeams |
| 0x130AD | see `combat-format.md` Open `[?]` | W | play | Cb | C#, spec | FightMenu.Move, FightSeams, IFightMoves.IFightMap |
| 0x13465 | see `combat-format.md` Open `[?]` | W | play | Cb | C#, spec | CombatChooser.FleeMessage, CombatMsg, MonsterAi.MoveStep |
| 0x1351E | see `combat-format.md` Start of round | W | play | Cb | C#, spec | FightRules.RecalcAttacksLeft |
| 0x1364D | see `combat-format.md` Start of round | W | play | Cb | C#, spec | FightRules.AttacksFromRate |
| 0x1367D | see `combat-menu.md` The player's combat menu, sweep attacks and turn undead (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatMsg, MonsterAi.MoveAttack, IFightMoves.IFightMap |
| 0x13880 | see `combat-menu.md` Combat record and DS cells used here | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | MonsterAi.CanTarget, FightMenu.TriggerTargetDefender, FightMenu.Targetable |
| 0x13965 | see `combat-format.md` Attacks | W | play | Cb | C#, spec | CombatMsg, MonsterAi.TryTurnUndead, TurnUndead |
| 0x13B4C | see `combat-menu.md` Turn undead , cleric) | W | play | Cb | C#, spec | MonsterAi.TryTurnUndead, TurnUndead.NextTarget |
| 0x13C01 | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | RollAttacks, Fight.MakeAttack, EffectHandlers.Recalculate |
| 0x13FF1 | see `combat-format.md` Attacks | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | MonsterAi.MoveAttack, Fight.NoteAttackDirection, FightMoves.GuardsAttack |
| 0x1407F | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Say, CombatChooser.AttackStarting, MonsterAi.MoveAttack |
| 0x14319 | see `combat-format.md` Range penalty | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.RangePenalty |
| 0x143F3 | see `combat-ai.md` Items, spells, turning | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Ai, FightSpells.MaxHp, FightSpells.CureTarget |
| 0x14611 | see `combat-menu.md` The player's aim for a spell | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatChooser.AimItem, FightSpells.Ai, FightSpells.FindCureTarget |
| 0x148BE | see `combat-menu.md` The player's aim for a spell | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec, `[?]`×1 | FightSpells.Ai, FightSpells, FightSpells.SquareOf |
| 0x14C0F | AI: spell choice and cast | P | play | Cb | C#, spec, `[?]`×1 | CombatChooser.ChooseSpell, ListRequest.Grey, CombatMsg |
| 0x14D6F | see `combat-format.md` Backstab | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.IsBackstab |
| 0x14E38 | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | NoFightEffects.IFightGeometry, IFightGeometry, FightRules.IsBackstab |
| 0x14EAB | see `combat-menu.md` A missile | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FlightAnimation.Missile, Fight.MissileFlight, Fight.MakeAttack |
| 0x15074 | see `combat-format.md` Enemy hit points | W | play | Cb | C#, spec | UpdateHostileHpPercent, Fight.EndOfRound, FightState.HostileHpPercent |
| 0x15103 | see `combat-map.md` Moves (stub | W | play | Cb | C#, spec | MonsterAi.HighestEnemyMovement, FightMoves.Flee, FightMoves.HighestEnemyMovement |
| 0x15193 | see `combat-menu.md` Move | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatMsg, IFightMenuChooser, FightMenu.CanAttack |
| 0x1529B | see `combat-menu.md` Aim | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatMsg, SpeedCommand.AimKey, AimView |
| 0x15438 | see `combat-menu.md` Target | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightMenu.AimAt, FightMenu.Manual, FightMenu.TargetAttack |
| 0x155F5 | see `combat-menu.md` Manual | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatMoveRequest.CombatCursorRequest, CombatChooser.ChooseManual, CombatMsg |
| 0x15AE1 | see `combat-menu.md` Candidates | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightMenu.AimAt, IFightMap |
| 0x15B84 | see `combat-menu.md` Aim | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightMenu.Aim, FightMenu.AimAt, FightMenu.AimFlight |
| 0x15D53 | see `combat-format.md` Attacks | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatChooser.AimItem, FightSpells.UseItemNow, FightMenu.CanAttack |
| 0x15FDF | see `combat-format.md` Attacks | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells.AimAuto, MonsterAi.AcquireTarget, FightState.IgnoreObstacles |
| 0x161CD | effect handler 73 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.Holds, EffectHandlers.CallHoldHandlers |
| 0x16281 | effect handler 74 | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.Hold |
| 0x1638E | Alt-X debugging switch | P | play | Cb | C#, spec, `[?]`×1 | PorTextCatalog.Overlay, GameAction, CombatChooser.Notice |
| 0x16468 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x166CB–0x17776 (stub 72): explore: doors, steps, commands (14 routines: W 14 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x166CB | see `geo-format.md` Barriers (plane 3) | W | play | Ex | C#, spec | PorTextCatalog.Overlay, AreaMessage, AreaLoop.DoorMenu |
| 0x167D4 | see `geo-format.md` Barriers (plane 3) | W | play | Ex | C#, spec | DoorMap.SetFace |
| 0x16901 | some party member has a level in class k (door menu) | W | play | Ex | spec | — |
| 0x1695F | door Bash | W | play | Ex | C#, spec | DoorRules, DoorRules.Bash |
| 0x16BAA | door Pick | W | play | Ex | C#, spec | DoorRules, DoorRules.Pick |
| 0x16C76 | see `geo-format.md` Door menu | W | play | Ex | C#, spec | DoorMember.SlotOf |
| 0x16CB9 | some party member has spell n memorised (door menu, via 0x16C76) | W | play | Ex | spec | — |
| 0x16D13 | door Knock | W | play | Ex | C#, spec | DoorRules, DoorRules.KnockSpell, DoorRules.Knock |
| 0x16D58 | see `ecl-format.md` Forward key | W | play | Ex | C#, spec | StepAction.ForwardKeyAction, Attempt.PorEncounterData, AreaCommand |
| 0x16E06 | see `ecl-format.md` Steps, doors and script switches (program read s11) | W | play | Ex | C#, spec | GameClock, IAreaUi.AreaLoop, AreaLoop.Move |
| 0x16EE3 | see `geo-format.md` Wilderness (overland) | W | play | Ex | C#, spec | AreaLoop.Step, Overland, Overland.Step |
| 0x17054 | explore command routine (menu keys) | W | play | Ex | C#, spec | ExploreScreen.RunMenu, AreaLoop, IAreaUi.AreaLoop |
| 0x17438 | party step / door menu / turn | W | play | Ex | C#, spec | RunVectorAction.StepAction, Attempt.PorEncounterData, ExploreScreen.Run |
| 0x17772 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x17831–0x1980E (stub 75): camp: Memorize, Scribe, preparation time, party order (23 routines: W 22 (1 by execution), P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x17831 | see `camp.md` The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3) | W | play | Cp | C#, spec | CampSpells, CampSpells.Check, CampSpells.PrepMinutes |
| 0x1798A | see `camp.md` Magic menu | W | play | Cp | C#, spec | CampSpells, CampSpells.Check, CampSpells.ResetMemorise |
| 0x179CD | see `camp.md` Magic menu | W | play | Cp | C#, spec | CampSpells, CampSpells.Check, CampSpells.ResetScribe |
| 0x17A45 | see `camp.md` Camp menu | W | play | Cp | C#, spec | CampSpells, CampSpells.Check, CampSpells.ResetParty |
| 0x17A8D | see `camp.md` Magic menu | W | play | Cp | C#, spec | SpellLists.SlotBox, CampSpells, CampSpells.ResetParty |
| 0x17B9D | see `camp.md` Check | W | play | Ex Cp | C#, spec | RestUi.Allowed, RestUi.CastMenu, RestUi.Memorise |
| 0x17CD2 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Ex Cp | C#, spec | GameAction, SpellLists.CampScreens, RestUi.CastMenu |
| 0x17DD5 | see `camp.md` Magic menu | W | play | Cp | C#, spec | ListRequest.SpellPageLayout, SpellLists.SlotBox, RestUi.SlotMessage |
| 0x17FAB | see `camp.md` Camp menu | W | play | Cp | C#, spec | SpellLists.CampScreens, CampScreens.Camp, CampScreens.Save |
| 0x18055 | see `camp.md` Magic menu | W | play | Cp | C#, spec | CampSpells, CampSpells.ResetParty, CampSpells.Sort |
| 0x1813B | see `camp.md` Memorize | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Memorise, CampSpells |
| 0x1838C | see `camp.md` Scribe | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Scribe, CampSpells |
| 0x18668 | [?] list buffer allocation for Memorize / Scribe pages (0x187ED) | P | play | Cp | C# | CampSpells |
| 0x187ED | see `camp.md` Display | W | play | Cp | C#, spec | SpellPageLayout.CentredTitle, GameText._effectLabels, SpellLists.CampScreens |
| 0x18C22 | see `camp.md` Camp menu | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.ClearMessages, RestUi.Magic |
| 0x18CDA | see `camp.md` Alter | W | play | Cp | C#, spec | RestUi.Order, CampParty, CampParty.MoveUp |
| 0x18E32 | see `camp.md` Alter | W | play | Cp | C#, spec | RestUi.Order, CampParty, CampParty.MoveDown |
| 0x18FB1 | see `camp.md` Order | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Order |
| 0x19116 | see `camp.md` Drop | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Drop, CampParty |
| 0x192C8 | see `camp.md` Speed | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Speed, CampParty |
| 0x1948A | see `camp.md` Camp menu | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.PicsLine, RestUi.Alter |
| 0x196A0 | camp menu | W | play | Cp | C#, spec | PorProgram.CampMenu, VenueScreens, VenueScreens.TakeItems |
| 0x1980A | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x1997F–0x1DDD7 (stub 78): party menu, creation, Modify, training hall, icon editor (19 routines: W 16 (12 by execution), P 1, U 1, N 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x1997F | free a creature record | N | play | Pm Ecl Cb Cp Kd | C# | PorTextCatalog |
| 0x19AE0 | party menu / hall menu | W | play | Pm | C#, spec | PorTextCatalog, PorGame.LoadInMenu, PorGame.LoadInHall |
| 0x19EEB | Create: character creation | W | play | Pm | oracle, C#, spec | HallTrainer.HallHooks, ModifyRequest.PartyMenuFlow, PartyMenuFlow.Create |
| 0x1B8B6 | see `start-menu.md` Drop | W | play | Pm | C#, spec | HallTrainer.HallHooks, PartyMenuFlow.Drop, PartyMenuItem.PartyMenu |
| 0x1B9B2 | see `start-menu.md` Modify | W | play | Pm | oracle (caller), spec | — |
| 0x1BA1E | Modify | W | play | Pm | oracle, C#, spec | HallTrainer.HallHooks, IconRequest.ModifyRequest, ModifyRequest.PartyMenuFlow |
| 0x1C32D | Train Character (level up) | W | play | Lv | oracle, C#, spec | PorTextCatalog, HallText.HallScreens, HallScreens.Train |
| 0x1C9EC | see `start-menu.md` Add | W | play | Pm | C#, spec | HallTrainer.HallHooks, PartyMenuFlow.Add, PartyMenuItem.PartyMenu |
| 0x1CC10 | see `start-menu.md` Remove | W | play | Pm Ecl Cb Cp Kd | oracle (caller), C#, spec | HallScreens.Run, HallTrainer.HallHooks, PartyMenuFlow.Remove |
| 0x1CD66 | see `start-menu.md` 3. Create | W | play | Pm | C#, spec | SheetRequest.IsPortraitMenu, PartyMenuFlow.Portrait |
| 0x1CE5E | icon editor: preview draw (recoloured icon) | U | pres | Pm | — | — |
| 0x1CF3D | icon recolour into slot 12 from the record | P | pres | Pm | C# (inside), spec | CombatArt |
| 0x1D1AC | icon editor | W | pres | Pm | oracle, C#, spec | Program, CharacterPageRequest.IconRequest, PartyMenuFlow.EditIcon |
| 0x1D8B0 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | CharacterModify.HpMinimum, CharacterCreation.Roll, CharacterCreation.ConHpBonus |
| 0x1D932 | see `start-menu.md` Modify | W | play | Pm | oracle, C#, spec | CharacterModify.HpMinimum |
| 0x1DA0B | see `start-menu.md` Modify | W | play | Pm | oracle, C#, spec | CharacterModify.HpMaximum |
| 0x1DBB4 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | CharacterCreation.Roll, CharacterCreation.RollFirstLevelHp, Entry.HitDice |
| 0x1DC6C | see `training.md` Train Character | W | play | Lv | oracle (caller), C#, spec | Entry.Train, Entry.Pay |
| 0x1DDD3 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x1DFE6–0x203CA (stub 7C): roster and save files, monster loader (14 routines: W 10 (1 by execution), P 1, U 3)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x1DFF6 | see `start-menu.md` Add | W | play | Pm | C#, spec | PorRoster, PorRoster.Mark, PorRoster.Entries |
| 0x1E19E | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm | C#, spec | PorRoster.WriteEntries |
| 0x1E294 | [?] file-error report (DS:537A / 537B) with a key prompt, for the roster and save routines | U | play | Pm Sv | — | — |
| 0x1E315 | [?] icon picture build per video mode (DS:4A1E) | U | pres | Pm Ecl Sv | — | — |
| 0x1E468 | combat icon slot: picks a free slot 0..7 and builds its picture | P | pres | Pm Ecl Sv | C#, spec, `[?]`×1 | CombatScreen.Icon |
| 0x1E684 | character save (roster file) | W | play | Pm | C#, spec | PorRoster, PorRoster.Save, HallTrainer.HallHooks |
| 0x1EAD2 | character load (roster file) | W | play | Pm | C#, spec | PorRoster, PorRoster.Load |
| 0x1EEAA | monster loader (MON<d>CHA / ITM / SPC) | W | play | Enc | C#, spec | EncounterHost.LoadMonsterRecord |
| 0x1F248 | see `ecl-format.md` ADDNPC v1, v2 | W | play | Ecl | C#, spec | PorProgram.NpcPortraitBases, EncounterHost.CreateNpc, IEncounterData |
| 0x1F42C | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ecl Sv | C#, spec | PorParty.RefreshOnLoad, HallScreens.Run, CombatScreen.Icon |
| 0x1F5E8 | game load (from the party menu) | W | play | Sv | C#, spec | PorTextCatalog, PorGame.LoadAtPartyMenu, PorGame.Loaded |
| 0x1FD3E | game save | W | play | Sv | C#, spec | PorSaveGame.Build, PorParty.Save, PorGame.UnstartedSave |
| 0x202BB | [?] NEWECL helper: disk number DS:5376 check, writes DS:6AAD / 6AAE, may ask through the menu 0x2CC8C and save 0x1FD3E | U | play | Ecl | — | — |
| 0x203C6 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2069D–0x20F97 (stub 82): end game (4 routines: W 3 (1 by execution), U 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x206A3 | end-game animation | W | pres | End | C#, spec | DelayRequest.Chained, EndGamePicture.EndGameAnimation, EndGame.FrameTime |
| 0x20941 | end game | W | play | End | C#, spec | PorTextCatalog, HallScreens.Learn, ClientPlayer.Program |
| 0x20D50 | [?] after a load or a script switch: overland / area picture set-up (calls 0x2DE38, picture draw) | U | pres | Ex Sv | — | — |
| 0x20F93 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2103A–0x23B4B (stub 85): items: View, Items page, Trade, use, halve / join (21 routines: W 17 (11 by execution), P 4)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x21084 | see `start-menu.md` 3. Create | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | PickRequest.SheetRequest, PartyMenuFlow.PortraitShown, PartyMenuFlow.View |
| 0x21547 | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | ClientResources.Gender, CharacterView.Run, CharacterView.DropMoney |
| 0x21642 | see `start-menu.md` After every key but Exit | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CharacterView.Run, CharacterView.TradeMoney, CombatChooser.ChooseView |
| 0x2193B | see `start-menu.md` Modify | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatSheet, CombatSheet.Build, DrawSheet |
| 0x21AED | View menu (character page) | P | play | Pm Ex Cb Cp Sh | C#, spec, `[?]`×1 | Program, GameAction, HallScreens.Run |
| 0x21DAA | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | C#, spec | CharacterView.ProgramItems, CharacterView.SellOnPage, CharacterView.LetGoOnPage |
| 0x21F32 | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ex Cb Cp Sh | C#, spec | Program, CombatChooser.Choose, SheetRequest.CharacterPageRequest |
| 0x2250A | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh | C#, spec | CombatMsg, FightKit.Create, MonsterAi.EquipWeapon |
| 0x2276E | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | PorTextCatalog.Overlay, CharacterView, CharacterView.TradeItem |
| 0x22827 | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CombatMsg, FightItemScreens.Use, FightItemScreens.Halve |
| 0x228D7 | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightItemScreens.Use, FightItemScreens.Join |
| 0x22AC4 | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CombatMsg, CombatChooser.ChooseScrollSpell, FightSpells.Ai |
| 0x22D28 | see `camp.md` Sell and Id over the Items page | W | play | Pm Ex Cb Cp Sh | C#, spec | CharacterView.SellOnPage, TradeContext.TalkOverItemsPage, Draw |
| 0x22F8C | shop Sell / Id box | P | play | Pm Ex Cb Cp Sh | C#, spec, `[?]`×1 | CharacterView.IdentifyOnPage, CharacterView.Identify, TradeContext.TalkOverItemsPage |
| 0x23190 | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ex Cb Cp Sh | C#, spec | PorTextCatalog.Overlay, PartyMenuFlow.Coins, CharacterView |
| 0x234E1 | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ex Cb Cp Sh | C#, spec | Program, SheetRequest.CharacterPageRequest, CharacterPageRequest.PickRows |
| 0x23795 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | TradeRules.GiveItem, ShopRules.CanCarry, ShopRules.GiveCopy |
| 0x23819 | party member selection (Next / Prev member) | P | play | Pm Ex Ecl Enc Cb Cp Sh | C#, spec, `[?]`×2 | PartyMenuFlow.ViewPage, ChoiceRequest.PreviousMember, TradeContext.SelectMember |
| 0x238F8 | see `shop-format.md` Gold total | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | Money, Money.CharacterGold |
| 0x239D6 | spell list window (type, action) | P | pres | Pm Ex Cb Cp Sh Lv | C#, spec, `[?]`×4 | PorTextCatalog, HallScreens.Learn, CombatChooser.ChooseSpell |
| 0x23B47 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x23DCC–0x24C30 (stub 8C): effect timeouts, camp time, rest (15 routines: W 14 (1 by execution), P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x23DCC | see `camp.md` The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3) | W | play | Ex Ecl Cb Cp | C#, spec | PorGame, GameClock.Advance, AreaLoop.EffectTimeout |
| 0x240BE | see `camp.md` Rest-time arithmetic | W | play | Ex Ecl Cb Cp | C#, spec | GameClock.Advance, GameClock.Normalise, CampState.CampTime |
| 0x2416F | see `camp.md` Rest → | W | play | Cp | C#, spec | CampTime, CampState.CampTime, CampTime.Validate |
| 0x241A1 | see `camp.md` Rest → | W | play | Ex Ecl Cb Cp | C#, spec | PorGame, GameClock, GameClock.Advance |
| 0x24246 | see `camp.md` Rest → | W | play | Cp | C#, spec | CampTime, CampState.CampTime, CampTime.Decrement |
| 0x24331 | camp time: time text / clock advance helper | P | play | Cp | C# | CampState.CampTime |
| 0x243BC | see `camp.md` Rest → | W | play | Cp | C#, spec | RestUi.Draw, ICampRestUi, Key.Shown |
| 0x244ED | see `camp.md` Rest-time menu | W | play | Cp | C#, spec | ChoiceRequest.LeftAnswer, RestUi.ChooseRestTime, ICampRestUi |
| 0x2463D | see `camp.md` Rest → | W | play | Cp | C#, spec | RestUi.PartyHealed, CampState.HealTicks, ICampRestUi |
| 0x246F1 | see `camp.md` Rest → | W | play | Cp | C#, spec | ICampRestUi, CampRest.PrepTick, CampSpells |
| 0x247B3 | see `camp.md` Rest → | W | play | Cp | C#, spec | ICampRestUi, CampRest.PrepTick, CampSpells |
| 0x248D6 | see `camp.md` Rest → | W | play | Cp | C#, spec | CampState.PrepTimers, CampRest.Run, CampRest.PrepTick |
| 0x2497E | see `camp.md` Rest → | W | play | Cp | C#, spec | CampRest.Run, CampRest.PrepBaseTick, CampSpells |
| 0x24A66 | rest loop | W | play | Cp | C#, spec | TradeContext.HoldPositionLine, SpellLists.CampScreens, CampScreens.Save |
| 0x24C2C | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x24C89–0x26EE3 (stub 8F): money, treasure and loot (22 routines: W 21 (5 by execution), P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x24C89 | see `character-format.md` Strength code and carrying allowance (program read s12) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | TradeRules.GiveCoins, Money, Money.CarryLimit |
| 0x24CA9 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightItemScreens.DropCoins, TradeRules.GiveCoins, Money |
| 0x24CC3 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | TradeRules.GiveCoins, Money |
| 0x24CDD | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | C#, spec | CharacterView.SellOnPage, Money, Money.Overloaded |
| 0x24D31 | see `shop-format.md` Gold total | W | play | Pm Ex Cb Cp Sh | C#, spec | Money, Money.PoolGoldTotal |
| 0x24DB9 | see `shop-format.md` Paying | W | play | Pm Ex Cb Cp Sh | C#, spec | Money, Money.SetCharacterGold |
| 0x24E0E | see `shop-format.md` Money | W | play | Pm Ex Cb Cp Sh | C#, spec | Money, Money.SetPoolGold |
| 0x24E8C | see `shop-format.md` Add platinum | W | play | Sh | C#, spec | Money, Money.AddPlatinum |
| 0x24F1D | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | C#, spec | PartyMenuFlow.Coins, TextRequest.Maximum, CharacterView.TradeMoney |
| 0x250D5 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CharacterView.TradeMoney, PartyMenuScreen.HandOver, TradeRules.GiveCoins |
| 0x25191 | see `combat-format.md` Loot menu | W | play | Cb Sh | C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Pool, IdentifyResult.PoolMoney |
| 0x25269 | see `shop-format.md` Shop menu | W | play | Cb Sh | C#, spec | IdentifyResult.Share |
| 0x252C3 | see `combat-format.md` Loot menu | W | play | Cb Sh | C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Share, IdentifyResult.Share |
| 0x2568A | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh | C#, spec | CharacterView.DropMoney, FightItemScreens.DropMoney, FightItemScreens.DropCoins |
| 0x25711 | see `shop-format.md` Take | W | play | Cb Sh | C#, spec | MoneyScreens.Take, IdentifyResult.Take |
| 0x2581C | [?] number / money text builder | P | pres | Pm Ex Cb Cp Sh | C# | GameText |
| 0x25974 | see `combat-format.md` After combat | W | play | Cb Sh | C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Take |
| 0x25C07 | see `combat-format.md` After combat | W | play | Cb Sh | C#, spec | TradeContext.PoolHasMoney, VenueScreens.Shop |
| 0x25C5B | see `item-format.md` Types 1–59, 73, 77, 93 | W | play | Tl | C#, spec | TreasureMaker.PlusRoll |
| 0x25C99 | see `ecl-format.md` Encounters (program read 2026-11-17) | W | play | Tl | C#, spec | ClientPlayer.MakeRandomItem, EncounterHost.ItemMaker, TreasureMaker |
| 0x2663B | see `combat-format.md` Shop | W | play | Sh | C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Appraise, VenueScreens.Appraise |
| 0x26EDF | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x27018–0x2A7CC (stub 96): spells: cast, spell lists, spell routines 1–67 (74 routines: W 69 (33 by execution), P 4, U 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x270A2 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh Lv | C#, spec | HallScreens.Learn, ListRequest.Spell, ListRequest.SpellPageLayout |
| 0x272A5 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh Lv | C#, spec | ListKind.ScrollList |
| 0x27466 | scroll spell list (readable items) | P | play | Pm Ex Cb Cp Sh Lv | C#, spec, `[?]`×1 | ListKind.ScrollList |
| 0x27534 | [?] scroll read helper: sets DS:6B80 / 6B82 and DS:4843 around 0x27466 | U | play | Pm Ex Cb Cp Sh Lv | — | — |
| 0x275B4 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh Lv | C#, spec | ListKind.ScrollList, Entry.Learnable |
| 0x27741 | see `combat-menu.md` Cast , from the menu with no spell) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells.AimAuto, FightSpells, FightSpells.Range |
| 0x277EE | see `spells-effects.md` Duration | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells, FightSpells.Duration |
| 0x278DB | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | PorTextCatalog, FightSpells, FightSpells.Around |
| 0x27AA9 | spell routine for spell 0 | W | play | Pm Ex Cb Cp Sh | C#, spec | MemberPick, RestUi.PickTarget, CampCast |
| 0x27C38 | cast a spell (pick, aim, routine call) | P | play | Pm Ex Cb Cp Sh | C#, spec, `[?]`×1 | CombatChooser.ChooseAim, CombatChooser.ChooseSpell, CombatMsg |
| 0x27F67 | see `spells-effects.md` Spells | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.CastOneSide, EffectTexts, EffectTexts.BySpell |
| 0x28027 | spell routine for spell 1 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine |
| 0x28058 | spell routine for spell 2 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine |
| 0x28083 | spell routine for spell 3 | W | play | Pm Ex Cb Cp Sh | C#, spec | OutsideEffectMessages.Show, CombatChooser.SpellEvent, RestUi.CastOne |
| 0x280BF | spell routine for spell 4 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine |
| 0x28101 | spell routine for spells 5, 11, 18, 22, 29 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x2813B | spell routine for spells 6, 7, 16, 17, 52, 53, 54 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x2817A | spell routine for spell 8 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x281A8 | spell routine for spell 9 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine |
| 0x281F5 | spell routine for spell 10 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine, FightSpells.CharmPerson, EffectTexts.BySpell |
| 0x282BD | spell routine for spell 12 | P | play | Pm Ex Cb Cp Sh | C#, spec, `[?]`×1 | FightSpells.Routine, FightSpells.Enlarge |
| 0x28386 | spell routine for spell 13 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine |
| 0x283F0 | spell routine for spell 14 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28451 | spell routine for spell 15 | W | play | Pm Ex Cb Cp Sh | C#, spec, seen only | FightSpells.Routine |
| 0x284B1 | spell routine for spell 19 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x284DF | spell routine for spell 20 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine |
| 0x28533 | spell routine for spell 21 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine, FightSpells.Sleep, EffectTexts.BySpell |
| 0x28670 | spell routine for spells 23, 49 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | EffectTexts.BySpell |
| 0x28801 | spell routine for spell 24 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x2883A | spell routine for spell 25 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x28873 | spell routine for spell 26 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine, FightSpells.SlowPoison, EffectTexts.BySpell |
| 0x28927 | spell routine for spell 27 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine, FightSpells.SnakeCharm, EffectTexts.BySpell |
| 0x289DB | spell routine for spell 28 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine |
| 0x28A2E | spell routine for spells 30, 50 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x28A67 | spell routine for spell 31 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x28AA2 | spell routine for spell 32 | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28AE4 | spell routine for spell 33 | W | play | Pm Ex Cb Cp Sh | C#, spec (inside) | FightSpells.Routine, EffectTexts.BySpell |
| 0x28B29 | spell routine for spell 34 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.StinkingCloud |
| 0x28F59 | spell routine for spell 35 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec, `[?]`×1 | FightSpells.MoreRoutine, FightSpells.Strength |
| 0x29087 | spell routine for spell 36 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine, FightSpells.AnimateDead |
| 0x2922F | spell routine for spell 37 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29275 | spell routine for spell 38 | W | play | Pm Ex Cb Cp Sh | C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x292A2 | see `spells-effects.md` Spells | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells.MoreRoutine, FightSpells.CureDisease, FightSpells.Spell58 |
| 0x29347 | spell routine for spell 39 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29364 | spell routine for spell 40 | W | play | Pm Ex Cb Cp Sh | C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x2939D | spell routine for spells 41, 46 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.DispelMagic |
| 0x294EE | spell routine for spell 42 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29558 | spell routine for spell 43 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.RemoveCurse, Cure.RemoveCurse |
| 0x2961C | spell routine for spell 44 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29655 | spell routine for spell 45 | W | play | Pm Ex Cb Cp Sh | C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29683 | spell routine for spells 47, 64 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.FireballRangeCell, FightSpells.MoreRoutine, FightSpells.Fireball |
| 0x2977C | see `spells-effects.md` Spells | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | EffectTexts, EffectTexts.BySpell, FightSpells.OneSideRate |
| 0x298AA | spell routine for spell 48 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x298D4 | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×2 | FightSpells.BoltHit |
| 0x29973 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | Body.LineWalk, FlightAnimation.Bolt, FightSpells.Bolt |
| 0x29BCF | spell routine for spell 51 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29C21 | spell routine for spell 55 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29C5B | spell routine for spell 56 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.RestorationResidueIndex |
| 0x29E1A | spell routine for spell 57 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29E65 | spell routine for spell 58 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.Spell58 |
| 0x29EFD | spell routine for spell 59 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29F65 | spell routine for spell 60 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29FB1 | spell routine for spell 61 | W | play | Pm Ex Cb Cp Sh | C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29FE8 | spell routine for spell 62 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x2A042 | spell routine for spell 63 | W | play | Pm Ex Cb Cp Sh | C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x2A070 | spell routine for spell 65 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x2A0AA | spell routine for spell 66; empty routine (unit init or empty handler) | W | play | Pm Ex Cb Cp Sh | C#, spec | FightSpells.MoreRoutine |
| 0x2A0BA | spell routine for spell 67 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x2A0F1 | effect handler 88 | P | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, `[?]`×1 | PorTextCatalog, CombatMsg, EffectHandlers.CallHoldHandlers |
| 0x2A255 | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | TradeContext.IsScroll, FightSpells.IsScroll, FightSpells.UseItemNow |
| 0x2A2A2 | see `camp.md` Rest → | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec, `[?]`×1 | FightSpells.UseItemNow, FightSpells.RemoveScrollSpell, ListKind.RemoveFromScroll |
| 0x2A332 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh | C#, spec | CombatMsg, CombatChooser.SayCast |
| 0x2A42B | see `spells-effects.md` Data-segment cells | W | play | Su | oracle, C#, spec, `[?]`×1 | FightSpells |
| 0x2A7C8 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2AA87–0x2AEE9 (stub AC): level stats (5 routines: W 5 (5 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2AA87 | level stats (hit dice, saves, slots) | W | play | Lv | oracle, C#, spec, `[?]`×2 | ExperienceClass.LevelStatsDocument, PorParty.RefreshOnLoad, PorGame |
| 0x2AC36 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | CharacterModify.Lower, CharacterCreation.Roll, LevelStats.Apply |
| 0x2ACDC | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | PorSpellData.LevelTables, CharacterCreation.PickClass, LevelStats.Apply |
| 0x2ADB0 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | PorSpellData.LevelTables, CharacterCreation.PickClass, LevelStats.Apply |
| 0x2AEE5 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2AEEA–0x2C820 (stub B0): effect list: call handler, add / remove effect, dice, heal (24 routines: W 24 (21 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2AEEA | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | PorTextCatalog, FightKit.Create, MonsterAi.ItemHandler |
| 0x2AF10 | see `camp.md` Rest → | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | PorGame.OutsideEffects, CampScreens.Rest, IFightEffects |
| 0x2B04A | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | InAuraRange, EffectHandlers, EffectHandlers.AuraEffects |
| 0x2B1E4 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | BaseItemTable.IFightEffects, IFightEffects, FightRules.TriggerDamageAttacker |
| 0x2B9D1 | see `combat-format.md` The fight | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec, `[?]`×2 | IFightEffects, EndOfRoundCreature, FightMenu.Move |
| 0x2BB39 | see `combat-format.md` The fight | W | play | Kd | C#, spec | FightRules.AttackRollNoAttacker, EclPartyRules.Hits |
| 0x2BBA1 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.AttackRoll, FightSpells.Resolve |
| 0x2BC4D | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers, EffectCells.SaveRoll, EffectHandlers.SavingThrow |
| 0x2BCCD | see `camp.md` Rest → | W | play | Pm Ex Ecl Enc Cb Cp Sh Tl Lv Kd | oracle (caller), C#, spec | IFightGeometry.FightRules, FightRules.Dice, AiMessage.MonsterAi |
| 0x2BD18 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.WeaponDamage, EffectHandlers.CallMoreHandlers, EffectHandlers.DamageDiceRoll |
| 0x2BD3C | see `start-menu.md` 3. Create | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.PoisonAttack, EffectHandlers, EffectHandlers.FindEffect |
| 0x2BDF0 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatChooser.FleeMessage, CombatMsg, CombatChooser.AiSays |
| 0x2BEBC | see `camp.md` Magic menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | FightRules.InvisibilityEffect, FightRules.AttackRoll, FightRules.RemoveInvisibility |
| 0x2BEF4 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×2 | CombatChooser.AttackEnd, CombatChooser.EffectMessage, FightRules.DroppedEffects |
| 0x2BF2B | see `combat-format.md` The fight | W | play | Cb | C#, spec | MonsterAi.MoraleEffects |
| 0x2BF6B | see `combat-format.md` The fight | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells.Routine, FightSpells.RemoveWithMessage |
| 0x2BFCE | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.StrengthCode |
| 0x2BFF7 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.StrengthFromCode |
| 0x2C046 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×2 | FightSpells.Enlarge, EffectHandlers.RaiseStrength, EffectHandlers.Gauntlets |
| 0x2C22E | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.AttackMade, CombatChooser.EffectMessage, CombatMsg |
| 0x2C540 | see `camp.md` Magic menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorTextCatalog, CombatChooser.AttackMade, CombatChooser.EffectMessage |
| 0x2C647 | see `camp.md` Heal | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CampScreens.Rest, FightSpells.Routine, EffectHandlers.CallHandler |
| 0x2C753 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorTextCatalog, OutsideEffectMessages.Show, CombatMsg |
| 0x2C81C | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x2C8CF–0x2DB18 (stub 3C5): menus and lists (menu routine, list window, yes / no) (20 routines: W 11 (6 by execution), P 2, U 7)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2C8CF | [?] not read; called by 0x19EEB, 0x2D073 | W | play | Pm Ex Ecl Cb Cp Sh Lv | oracle (caller) | — |
| 0x2C936 | list window: [?] internal helper | U | pres | Pm Ex Ecl Cb Cp Sh Lv | — | — |
| 0x2C99A | see `camp.md` Rest → | W | play | Pm Ex Ecl Enc Cb Cp Sh Lv Sv | oracle (caller), C#, spec | KeyRequest.MenuEntries, DrawLine |
| 0x2CAA9 | see `camp.md` Rest → | W | play | Pm Ex Ecl Enc Cb Cp Sh Lv Sv | oracle (caller), C#, spec | DrawLine |
| 0x2CC8C | menu routine (horizontal words) | P | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv | oracle (caller), C#, spec, `[?]`×1 | PorAmigaText, Setup.PenSites, SheetRequest.IsView |
| 0x2CFCA | see `start-menu.md` 1a. The introduction | W | play | Su Pm Ecl Cb Cp Sv | C#, spec | CombatView.Row24Writes, CombatChooser.Attach, CombatChooser.BattleBegins |
| 0x2CFE0 | list window: [?] row helper | U | pres | Pm Ex Ecl Cb Cp Sh Lv | — | — |
| 0x2D073 | list window: draw the rows | U | pres | Pm Ex Ecl Cb Cp Sh Lv | — | — |
| 0x2D1A6 | list window: [?] paging helper | U | pres | Pm Ex Ecl Cb Cp Sh Lv | — | — |
| 0x2D1F2 | list window: [?] paging helper | U | pres | Pm Ex Ecl Cb Cp Sh Lv | — | — |
| 0x2D238 | list window: [?] page up / previous | U | pres | Pm Ex Ecl Cb Cp Sh Lv | — | — |
| 0x2D2D7 | list window: [?] page down / next | U | pres | Pm Ex Ecl Cb Cp Sh Lv | — | — |
| 0x2D3D2 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Lv | C#, spec | SpellPageLayout.ListPaging, ListPage.Step, ListPage.SkipWindowLabels |
| 0x2D502 | see `combat-menu.md` The combat screen (Client) | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C#, spec | MenuHighlight.ListEntered, MenuHighlight.ListPaged, ExploreScreen.PageList |
| 0x2D594 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Lv | C#, spec | SpellPageLayout.ListPaging, ExploreScreen.ListAnswer, ListPage.Step |
| 0x2D669 | vertical list / item list window | P | pres | Pm Ex Ecl Cb Cp Sh Lv | C#, spec, `[?]`×1 | PartyMenuRequest.PickRequest, SheetRequest.CharacterPageRequest, PartyMenuFlow.Pick |
| 0x2D9A0 | yes / no question | W | pres | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | Calls.YesNoWarnPen, HallTrainer.YesNo, CombatChooser.YesNo |
| 0x2DA0F | [?] not read; called by 0x19EEB, 0x1C4E | W | play | Pm Ecl Sh | oracle (caller) | — |
| 0x2DAC2 | see `camp.md` Magic menu | W | play | Pm Ex Ecl Cb Cp Sh Lv | oracle (caller), spec | — |
| 0x2DB14 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2DB87–0x2DE2B (stub 3CA): 3D view / overland redraw (6 routines: W 5 (1 by execution), P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2DB87 | see `geo-format.md` Marker | W | play | Ex | C#, spec | AreaLoop.Redraw, AreaLoop.EnterCommandRoutine, AreaLoop.InterfaceMarker |
| 0x2DC4E | see `view3d.md` Backdrop | W | play | Ex | C#, spec | BackdropRule.Horizon1 |
| 0x2DC82 | see `view3d.md` Backdrop | W | play | Ex | C#, spec | BackdropRule.Horizon2 |
| 0x2DCB6 | see `view3d.md` Backdrop | W | play | Ex | C#, spec | BackdropRule.Ground |
| 0x2DCF4 | 3D view / overland redraw (window, backdrop, marker) | P | pres | Ex | C#, spec, `[?]`×1 | ClientPlayer.ViewBoxBlank, ClientPlayer.DrawInterface, ClientPlayer.SwitchRedrawn |
| 0x2DE27 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2DE38–0x2E0BF (stub 3CD): overland view (3 routines: W 2 (1 by execution), P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2DE38 | overland view draw | P | pres | Ex | C# (inside), spec (inside) | PorOverland, OverlandMap |
| 0x2E03F | see `geo-format.md` Overland view, move and entry (program read s19; Core `Overland`, Data `PorOverlandArt`) | W | play | Ex | C#, spec | Draw, AreaLoop.OverlandLayout |
| 0x2E0BB | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2E0CE–0x2E892 (stub 3D0): pictures and sprites (loader, frames, free) (10 routines: W 7 (6 by execution), U 3)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2E0CE | picture frame draw | U | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv End | spec | — |
| 0x2E11E | [?] not read; called by 0x2E765 | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller) | — |
| 0x2E13E | [?] not read; called by 0x2E765 | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller) | — |
| 0x2E170 | picture / sprite loader (frame table DS:6ABE) | W | pres | Pm Ex Ecl Enc Cb Cp Sh End | C#, spec | PictureAnimation, ViewPicture.ViewPictures, ViewPictures.Find |
| 0x2E55D | picture free (frame table) | U | pres | Pm Ex Ecl Enc Cb Cp Sh End | spec | — |
| 0x2E5C7 | see `start-menu.md` Modify | W | play | Pm Ex Cb Cp Sh | oracle (caller), spec | — |
| 0x2E6A7 | see `combat-format.md` Shop | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller), spec | — |
| 0x2E765 | see `combat-format.md` Shop | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller), spec | — |
| 0x2E7D6 | sprite renderer (encounter close-up frame) | U | pres | Enc | spec | — |
| 0x2E88E | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2E8F7–0x2FB01 (stub 3D5): 3D view drawing, wall sets (13 routines: W 8 (4 by execution), P 1, U 4)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2E8F7 | 3D view: wall column draw | U | pres | Ex | — | — |
| 0x2EA4C | view colours: backdrop bytes DS:6A60–6A63 | P | pres | Su | spec | — |
| 0x2EA6B | 3D view: backdrop (sky / ground colours DS:6A60–6A63) | U | pres | Ex | — | — |
| 0x2EB08 | 3D view: far wall pieces | U | pres | Ex | — | — |
| 0x2EC16 | [?] not read; called by 0x2EC47, 0x2ED72 | W | play | Ex Enc Cb Sh | oracle (caller), C# | EncounterRules.ViewDistance |
| 0x2EC47 | see `combat-map.md` The wall between two squares | W | play | Ex Cb | oracle (caller), C#, spec | AreaMap, ArenaBuilder.WallBetween, DoorMap.FaceBarrier |
| 0x2ED72 | see `combat-format.md` CALL ids (handler | W | play | Ex Enc Cb Sh | oracle (caller), C#, spec | AreaLoop.ViewDrawn, AreaMap, EncounterHost.Call |
| 0x2EE71 | see `combat-format.md` CALL ids (handler | W | play | Ex Sh | C#, spec | AreaLoop.EventHere, AreaMap, EncounterHost.StepForward |
| 0x2EEEA | 3D view: small helper (calls START 0x6192) | U | pres | Ex | — | — |
| 0x2EEF4 | 3D view draw (Draw3DView) | W | pres | Ex | C#, spec | ExploreScreen, AmigaMenuPens.DrawRoster, AreaLoop.ViewDrawn |
| 0x2F6FF | wall-set loader (WALLDEF<d>) | W | pres | Ecl Enc Sv | C#, spec | EclMemory.WallSetRequested |
| 0x2F9BE | see `ecl-format.md` Where the scripts live | W | play | Ecl Sv | C#, spec | Attempt.PorEncounterData, AreaLoop.LoadMap |
| 0x2FAFD | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2FB52–0x3072F (stub 3DB): combat screen: aim, map draw (8 routines: W 8 (8 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2FB52 | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatMap, FightSpells.LightningBreath |
| 0x2FB80 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | BuildTargetList, SortTargets |
| 0x2FCF2 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Flight, Body.LineWalk, LineWalk.Start |
| 0x2FE1E | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Flight, Body.LineWalk, LineWalk.Next |
| 0x2FF74 | see `combat-menu.md` Aim | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×2 | ManualView, ManualView.LineReached, FightMenu.Manual |
| 0x300CD | see `combat-menu.md` Cast , from the menu with no spell) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | FightSpells.CastNow, Body.IsDirToward |
| 0x3045B | see `combat-menu.md` Candidates | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | FightSpells.AimAuto, SpellTable.IFightTargetList, MonsterAi.AreaAround |
| 0x3072B | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x3073A–0x31A39 (stub 3E0): combat arena: cells, targets, icons, distance (23 routines: W 23 (19 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x3073A | see `combat-menu.md` The downed picture | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.HitMarkerSize, CombatScreen.DrawMap, CombatMap |
| 0x307A2 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), spec | — |
| 0x3081D | see `start-menu.md` Icon editor | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatPage.Grey, CombatScreen, DrawIcons |
| 0x3083F | see `combat-format.md` The fight | W | play | Pm Ex Cb Cp Sh | C#, spec | CombatPage.Grey |
| 0x30861 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C# (inside), spec | CombatArt |
| 0x30A0F | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatView.Scroll, CombatChooser.Downed, Fight.TurnEnded |
| 0x30ADA | see `spells-effects.md` Spells | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.PutOutOfPlay, CombatMap, CombatMap.Rebuild |
| 0x30BF8 | see `combat-menu.md` Aim | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightSpells.CureTarget, CombatMap.Contents, FightSpells.StinkingCloud |
| 0x30C65 | see `spells-effects.md` Spells | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.PutOutOfPlay |
| 0x30E23 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.InView |
| 0x30E54 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.DefenderTurned, CombatChooser.FacingDrawn, CombatChooser.AnyCellInView |
| 0x30F0E | see `combat-menu.md` View scrolling | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.Rebuild |
| 0x310D0 | see `combat-menu.md` Menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.Cursor, CombatView.Scroll, FightSpells.Fireball |
| 0x31232 | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatView.Scroll, CombatChooser.Posed, CombatChooser.DefenderTurned |
| 0x31321 | see `combat-menu.md` Callers | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.PutBackOnSquare, CombatMap.Position, EffectHandlers.Fly |
| 0x31349 | see `combat-menu.md` Callers | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.PutBackOnSquare, CombatMap.Position, EffectHandlers.Fly |
| 0x31371 | see `spells-effects.md` Triggers | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | IFightTargetList, CombatMap.IconSize, AroundCreature |
| 0x31399 | see `spells-effects.md` Triggers | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatMap.Slot, EffectHandlers.SlotOf, EffectHandlers |
| 0x313F3 | see `combat-menu.md` Move | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatMap.Probe, IsOffMap, TerrainUnder |
| 0x3154B | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatView.HitMarker, CombatChooser.AttackMessage, CombatChooser.AttackEnd |
| 0x317E1 | see `combat-menu.md` Move | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | FightMenu.Move, EffectHandlers.PutBackOnSquare, EffectHandlers.Revive |
| 0x319D2 | see `combat-menu.md` Menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.CursorSize, CombatChooser.Centered, CombatChooser.TurnStarted |
| 0x31A35 | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x31A6C–0x320B0 (stub 3E9): tiles and icons (close-ups, area map) (6 routines: W 6 (3 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x31A8E | see `combat-menu.md` The combat screen (Client) | W | play | Ecl Cb | C#, spec | OverlandPicture.PorOverlandArt, CombatPicture.CombatArt, CombatArt.RareFirstTile |
| 0x31B5D | overland / area-map tile draw | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | OverlandPicture.PorOverlandArt, CombatArt.Ground |
| 0x31BBA | see `ecl-format.md` Party, damage and text-box opcodes (program read s11, 2026-11-17) | W | play | Pm Ecl Cb Cp Sv Kd | oracle (caller), spec | — |
| 0x31C34 | see `start-menu.md` 3. Create | W | play | Su Pm Ex Ecl Enc Cb Sh Sv | C#, spec | CombatArt.Monster |
| 0x31F68 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatScreen.Mirrored |
| 0x320AC | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x3211F–0x3221A (stub 3EE): text page (2 routines: W 1 (1 by execution), U 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x32123 | [?] text page with a key prompt (level drain path; trapped by the spell oracle) | U | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0x32216 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x32235–0x324E6 (stub 428): picture load with disk check (3 routines: W 1 (1 by execution), U 2)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x32270 | [?] picture block load with disk check (wall sets, start-up) | U | pres | Su Ecl Enc Sv | spec | — |
| 0x32392 | wall / tile piece draw | U | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | oracle (caller), spec (inside) | — |
| 0x324E2 | empty routine (unit init or empty handler) | W | play | Su | — | — |

