# s60 lane A: area loop, camp, party menu, save / load, overland leftovers

Units: START segment 0000, GAME.OVR stub 1F (ECL handlers / area loop), 3E (game start-up), 72 (explore), 75 (camp),
78 (party menu, creation, hall, icon editor; not the icon builders), 7C (roster and save files), 8C (effect timeouts,
rest). Statuses: `seen` (matched on screen), `exec` (oracle + test), `read` (branch by branch, port matches), `fixed`
(difference fixed), `bug?` (question for the user), `open` (not finished).

## 1. The camp after a vector 2 that destroyed the party (0x3B19, 0x196A0) — fixed, seen

Read whole: 0x3B19 runs vector 2 through the run loop 0x3EB7, then calls the camp menu 0x196A0 with no test at all;
with the "rest interrupted" flag set it draws START 0x3379 (0x3B3B) and runs vector 3, then the view redraw 0x2DCF4 and
DS:557D := 0. 0x196A0 has no DS:49FF test either (its loop ends on the interrupted flag or a key in its exit set).
0x3EB7 tests the stop flag DS:442E and DS:49FF **before** the first opcode, so with DS:49FF set vector 3 runs nothing.
The area loop reaches 0x3B19 only with DS:49FF = 0 at the command (0x4118); after it, 0x4172 tests DS:49FF and skips
the command read, and the loop ends.

So a DAMAGE in vector 2 that kills the whole party shows its lines, the party-killed window, and then **the camp menu
over the dead party**; its Exit ends the area loop (DOS prompt). Core skipped the menu when DS:49FF was set.

- DOSBox-X (`por_test_saves.py ... B=camp-wipe`, new: vector 2 = slot B's DAMAGE 0xE0, 1, 1, 200, 0; Load B, E): five
  death lines and the page prompt, the sixth, the party-killed window, then the camp's opening message, the camp
  picture, the position line with the camping word, every name red with 0 HP, the camp menu line. Exit → the DOS
  prompt.
- Fixed: `AreaLoop.Encamp` opens the camp after Exit, NewScript and ExternalStop (DS:49FF) runs; only the engine's own
  stops (fault, step budget, missing input answer) still skip it. Test
  `AreaLoopTests.Encamp_opens_the_camp_menu_after_a_vector_2_that_destroyed_the_party` (vector 3: 0 steps, loop ended).
- Client on Xvfb, the same save: same screens; Exit closes the Client cleanly (log has no fault), as DOS quits.
- PROGRAM 9's nested camp (`EclVm.ProgramCamp`) never had the gate: nothing to change.

Two Client differences found on the way, both fixed:
- **DAMAGE's party list one line late.** 0x959F prints the line (0x96B0), applies the hit (START 0x2DC8, 0x96BF), blanks
  (17, 1)–(38, 15) (0x96D0) and draws the party list (0x96DD). The Client took the list in `DamageReport`, before the
  hit: at the sixth line's page prompt DOSBox-X shows the fifth member red with 0 HP, the Client cyan with 5. New
  `EclPartyRules.DamageApplied` event → `IEclHost.DamageApplied` (not a traced host call) → `ClientPlayer.DamageApplied`
  (position line erased, list drawn). Test `DamageScreenTests.The_party_list_is_drawn_after_the_hit_not_before`.
- **The position line after a DAMAGE stayed blank in the camp.** 0x196C7 draws START 0x3379, whose camp case draws the
  position line; the Client kept DAMAGE's erase. `TradeContext.FrameTextBox(false)` (the Client's START 0x27D9 / 0x3379
  redraw) now clears `PositionLineErased`. Seen: the position line with the camping word, as DOSBox-X.

Noted, not a question: a dead party at this camp can Save (the program writes it; a Load of it starts with every member
dead). It needs a vector 2 with a party-killing DAMAGE, which the shipped scripts would have to contain; not checked
which, if any, do.

## 2. The game Save / Load roster-file deletes (0x1FD3E, 0x1F5E8) — decided s61, built (an option, off by default)

Read whole (the delete parts):
- **Save 0x1FD3E**, for each member in list order (DS:5D96, next at +0x104), after its `CHRDAT<l><n>` files are written
  (0x1E684, 0x20124): the path DS:537A + the name through START 0x8376 / 0x8445 (`PorRoster.DosStem`: '.' and the
  other DS:0D1A characters removed, 8 characters, upper case) + `.cha`; if the file exists (START 0x830B) it is
  assigned and erased (START 0xC362, 0xC59F). Then the same for `.itm` and `.spc`, each tested on its own
  (0x20192, 0x201E7, 0x2023C). Then DS:557D := 1.
- **Load 0x1F5E8**, after every stem's `.sav` is loaded (0x1EAD2), joined (0x1F42C) and the save directory checked
  (0x1E294: MkDir, the save-disk prompt), it walks the party list again (0x1F9DD): `.cha` exists → erase it, then
  `.itm` and `.spc` each if it exists; **no `.cha` → none of the three is touched** (0x1FA6B jumps past them).

Effect in the original: a character in a saved game is no longer in the roster, so it cannot be Added to another party
(no copies). Only Remove (0x1E684 with the name) writes a member's roster files back; the party menu's Save and Exit
(Exit saves until DS:557D) are this same game Save 0x1FD3E, so they erase them again. Edge: two
names with the same DosStem share one file, so a Save erases the other character's roster file too.

The engine keeps its roster in its own folder (`PorRoster`, JSON per character, DOS files read when no JSON exists) and
never writes into the player's game folder; the deletes are not done, so a saved member can be Added again from the
roster (a copy). **Decided (user s61): option 3**, built: an option, **off by default** (the separate roster as
before); on, the program's deletes run on the engine's roster folder only (`PorRoster.RemoveSaved` after every game
Save, `PorRoster.RemoveLoaded` on Load: only when the member's character file, JSON or `.cha`, exists), never in the
game folder. `start-menu.md` §5 "Roster deletes"; Client setting `RosterDeletes`, `--roster-deletes`, controls screen
ROSTER ON SAVE.

## 3. Order's Select / Place extended keys (0x18FB1) — seen, read

Read whole: the menu runs with the keypad flag. Select mode: an extended key goes to 0x23819 (Home / End: previous /
next; any other picks the first member, the s50 rule) and the list is drawn (0x1902B). Place mode: Home (0x47) →
0x18CDA, End (0x4F) → 0x18E32, **any other extended key only draws the list again** (0x19032–0x1904F; this settles the
Client's `[?]`). Enter / P / S switch the mode (0x1905A: the key in the set at CS:174E).

DOSBox-X and the Client (save A, E, A, O), the same keys: End End (the third member lit), F5 → the first member lit;
End Insert → the first member (Client); End, Enter (Place, the second member's selected message), F5 → nothing
moves, End → the second member swaps down below the third. Frames match (the Client's grey key hints aside, its own addition).

## 4. The land's recovery tables into the module — fixed (moved), seen

Core held 0x20D50's tile lists, colour maps and view squares as constants (s58). They are now the module's:
`rules/overland.json` gains an optional `recovery` object (`listA`, `listB`, `from`, `toA`, `toB`, `first`, `last`,
`pan`, `source`; importer 0.7.40, schema regenerated, `module-format.md` §6, `geo-format.md` "The land recovers").
- Importer: `PorOverlandRecovery.Locate` finds them through 0x20D50's code, as `PorOverland.Locate` does for the
  overland: the four list tests `80 BD imm 7F / 76 2C / BF from / 1E 57 / BF to / 1E 57` (the random and whole passes
  must agree), the loop ends `80 7E FF n 75` (counts n + 1), the pan pair `8B F8 D1 E7 FF B5 imm` and the frame loop
  `80 7E FE n 74`, and the `B8 x 00 50 B8 y 00 50 9A` squares before the first list test and after the last;
  `PorOverlandRecovery.Read` takes the bytes from the player's data segment. `PorProgramData` puts them on
  `OverlandMap.Recovery`; the codec writes / reads them (a wrong-shaped table fails the load).
- Core: `OverlandRecoveryTables` (record) and `OverlandRecovery.Run / Pass / ApplyFinal / AnimatedDraws(tables)`; only
  the callers' cells ($4AB3, $4AA2), 0xFE and block 26 stay in Core. `EclVm.OverlandRecovery` holds the module's
  tables (set by `PorGame`); `IEclHost.OverlandRecovery(tables, animated, random)`. A module without them: the switch
  into block 26 skips the recovery with a diagnostic (no recolour, no RandSeed draws); Load's still run is skipped.
- Tests: `OverlandRecoveryTests` now use tables of their own (two short lists, three frames: draws counted from the
  formula) plus a real-data test that the program's tables are found (11, 18, 9 frames, first / last = the pan's
  ends, 51,840 draws); the switch test without tables draws 0. The round trip compares the seven tables with the DS
  bytes, the squares, and the tables through `PorModule` (OVERLAND 2 → 10, OVERLAND (engine) 3 → 4).
- Seen: the Client (DOS art) on a `save-overland` save of block 26 at 9,24 N with $4AB3 = 254, $4AA2 = 255 shows the
  blue river and green ground (s58's recovered state), from the module's tables.
- Kept as notes: the CGA / Tandy random modes of 0x649C are not modelled (the engine draws EGA); the Amiga program's
  own colour map and routine are not read (Amiga tiles not recoloured).

## 5. W-by-citation rows

Every GAME.OVR row on the list (St W, evidence C# / spec only) in units 1F, 3E, 72, 75, 78, 7C and 8C was already
read branch by branch in s52 (`s52-lane-*.md`: 0x211D, 0x3C73, 0x3EFA, 0xE600, 0x166CB, 0x16D58, 0x16E06, 0x16EE3,
0x17054, 0x17CD2, 0x17FAB, 0x18668, 0x187ED, 0x18C22, 0x19116, 0x192C8, 0x1948A, 0x196A0, 0x19AE0, 0x1B8B6, 0x1C9EC,
0x1CD66, 0x1DFF6, 0x1E19E, 0x1E684, 0x1EAD2, 0x1EEAA, 0x1F248, 0x1F42C, 0x243BC read; 0x403E, 0x17438 fixed); this
lane added:
- START 0x0006 (main program), read whole: the unit inits (START 0xAF80 … OVR 0xB4), 0xE600, ParamStr(1) against
  START CS:0000 (equal: the introduction 0x6FB and the code wheel 0xAA9 skipped), the party menu 0x19AE0, the view
  colours 0x2EA4C(11, 0, 7, 6), the area loop 0x403E, the quit START 0x8990, Halt(0). Matches `start-menu.md` and
  `Program` (the code wheel not ported, by design).
- Empty routines (`push bp / mov bp,sp / pop bp / retf`), read: 0x424E, 0xEBE8, 0x17772, 0x1980A, 0x1DDD3, 0x203C6,
  0x24C2C.
- On screen, DOSBox-X vs the Client (save A, camp): Alter → Speed (the speed line with its range, the
  speed menu), Alter → Pics (the two toggles and Exit), Rest (the rest-time line at zero and its menu), the
  camp menu, Order (§3), the camp over a dead party (§1): the same text, colours and places (the Client's grey key
  hints aside). Seen for 0x192C8, 0x1948A, 0x243BC, 0x196A0, 0x18FB1.
- Not done: the drawing rows of the party menu / creation (0x19AE0, 0x1CD66) and the camp's Display / Cast (0x187ED,
  0x17CD2) were not compared on screen this session (s33–s35 / s38 compared them before the ledger); open if the main
  session wants a fresh frame compare.

## Ledger evidence

- 0x3B19 → read whole, fixed (Core opened no camp after a wipe in vector 2), seen in DOSBox-X and the Client (s60 lane A §1)
- 0x196A0 → read whole for the entry, exit and DS:49FF (none), seen over a dead party (§1)
- 0x3EB7 → read whole: tests DS:442E and DS:49FF before each opcode; vector 3 after a wipe runs nothing (§1, test)
- 0x403E → the camp / Look path 0x4118–0x418F read: E only with DS:49FF = 0, 0x4172 ends the loop after it (§1)
- 0x959F → line, hit, blank, list order read; Client list timing fixed, seen (§1)
- 0x34BB → its party-wipe path seen in DOSBox-X and the Client with the camp after it (§1)
- 0x18FB1 → read whole, seen in DOSBox-X and the Client (Select / Place extended keys) (§3)
- 0x1FD3E → the roster deletes read; built as an option, off by default (user s61, §2)
- 0x1F5E8 → the roster deletes read; built as an option, off by default (user s61, §2)
- 0x1E294 → read (MkDir + save-disk prompt; N as before)
- 0x424E, 0xEBE8, 0x17772, 0x1980A, 0x1DDD3, 0x203C6, 0x24C2C → empty routines, read
- START 0x0006 → read whole (§5)
- 0x20D50 → its tables now come from the module (`rules/overland.json` "recovery", `PorOverlandRecovery`); W kept,
  evidence unchanged (oracle for 0x649C, DOSBox-X s58), the recovered overland seen from the module's tables (§4)
- 0x192C8, 0x1948A, 0x243BC → seen in DOSBox-X and the Client (Speed, Pics, rest-time screen) (§5)
