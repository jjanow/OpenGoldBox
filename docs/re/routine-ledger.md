# Routine coverage ledger (Pool of Radiance, DOS v1.3)

Every routine of the v1.3 program (`START.EXE` and `GAME.OVR`) that the game runs from the title to the ending, with how far the engine follows it. Built in session 47 (2026-11-22) to make the remaining [play] work finite: **P** and **U** rows are the list of known gaps; a **W** row was either run against the program (oracle, golden captures) or read and ported per the spec and C# comments, which is not the same as checked branch by branch (the two counts are split below). Addresses are GAME.OVR file offsets unless marked START (offsets in the EXEPACK-expanded `START.EXE` image), as in the other `docs/re` specs; far calls resolve through the overlay stubs (`ecl-format.md` "Program references").

## Statuses

| Status | Meaning | Count |
|---|---|---|
| **W** ported whole | the Core / Client code follows the routine | 778 |
| … W by execution | an oracle runs it (or runs its caller, for state routines), the ECL golden captures cover it, or it is empty | 624 |
| … W by citation | the C# names its entry and the spec reads it with no open `[?]` on those lines: read and ported per the comments, not verified branch by branch | 154 |
| **P** patched by symptom | partly built: the C# names only inside addresses, or the spec lines carry `[?]`, or the behaviour was built from emulator observations | 1 |
| **U** unchecked | no evidence that the engine matches it (not cited, or read in a spec but not ported) | 1 |
| N not ported by design | platform work the module format replaces: DAX reading (the importer), disk numbers, memory release, the code wheel | 32 |
| L library | Turbo Pascal run-time library, overlay manager, Crt and DOS units (one line per segment below) | 143 |

Total: 955 routines (280 START, 675 GAME.OVR). W + P + U = 780 game routines.

Evidence column: `oracle` = a `tools/scripts/por_*_oracle.py` runs the routine under unicorn and a Core test compares (`oracle (caller)`: run inside a called routine, not trapped; drawing, text and sound callees excluded since the oracles compare state, not pixels); `golden` = an ECL handler or VM helper covered by the golden DOSBox captures EclVm matches; `C#` / `C# (inside)` = a C# comment names the entry / only addresses inside it; `spec` / `spec (inside)` = the same for `docs/*.md`; `[?]`×n = spec lines on the entry with an open question; `seen only` = every spec line on the entry is a DOSBox-X / FS-UAE observation. Port = the C# type.member whose comment names the entry. Kind: **play** = rules, state or branching; **pres** = drawing, text, sound, input timing.

## How it was built (to rebuild after a session)

The generator scripts are in `tools/scripts/ledger/` (s47). Set `GOLDBOX_POR_DIR` to a v1.3 copy, `LEDGER_WORK` to a scratch folder (with a `porgame` link to the copy and a `tools/` copy of the scripts: `render.py` imports `overrides.py` and runs `gaps.py` from there) and `PYTHONPATH` to `tools/scripts`; run them from the repo path with the unicorn + capstone venv in this order: `cg.py`, `disp.py`, `ecldisp.py`, `cite.py`, `build.py`, `render.py` (`tables.py`, `peek.py` and the rest are probes).

1. Load the program with `tools/scripts/por_emu.py` `load_image` (EXEPACK expanded); the overlay stubs are the paragraphs starting `CD 3F 00 00` (dword GAME.OVR offset at +4, word code size at +8, entry count at +0x0C, entries of 5 bytes from +0x20: `CD 3F` + the routine offset in the unit).
2. Recursive descent with capstone (16-bit) from the entry point (START 0x0006), every stub entry and the sound timer interrupt (START 0x9ECA, installed through INT 21h 25h): near calls stay in the segment / unit, far calls resolve through the stubs, and decoding stops at stub paragraphs. Then a sweep for routine prologues (`55 89 E5`, `55 8B EC`) not yet covered. What stays uncovered is string constants kept in the code segments.
3. Indirect calls: the program has three, all through tables it fills at run time: the spell routines (DS:6B84, built by 0x2A42B, called from 0x27C38; its entry 0 is a procedure variable, the target pick 0x148BE, stored by the fight at 0x988C and by the builders at 0x11EB6 / 0x2A479 and called at 0x27D88), the effect handlers (DS:6828, built by 0x11DFD, called from 0x2AEEA) and the TP run-time. The tables were read by running the two builders (`por_spell_oracle.py` set-up). The ECL dispatcher (0x3C73) is a compare chain of near calls; its opcode → handler map is in the ECL section below.
4. Citations: every `0x` number in `docs/*.md`, `docs/re/*.md` (surveys and prior art left out) and in C# comments under `src/`: after `START` / `START.EXE` (and numbers chained to it, unless no START routine holds the number) a START address, `SSSS:OOOO` resolved by segment or stub, a range `0xA–0xB` counts for every entry inside it, otherwise a GAME.OVR offset (4–5 digits, or an exact routine entry); numbers after `+`, `$`, `DS:`, `hunk n` or `Amiga` are skipped. A citation counts for the routine whose decoded instructions hold it; "entry" means the routine's first address. The oracles were read by hand (their `e.call` targets count, trapped routines do not).
5. Rows marked with a note in the gap lists were checked by hand; the rest follow the rule in "Statuses". The count is 209 START and 2266 GAME.OVR addresses cited, on 758 routines.

## Title to ending: screens and paths

Each path's root routines and everything they call, without entering another path's roots (library and N rows not followed). Shared helpers (menus, text, messages, effects) count in every path that reaches them.

| Code | Path | Roots | Routines | W | P | U | Play gaps (P + U, kind play) |
|---|---|---|---|---|---|---|---|
| Su | Start-up, title, introduction | START 0x0006, START 0x4354, 0x06FB, 0x0AA9, 0x2EA4C, 0xE600 | 86 | 85 | 0 | 1 | — |
| Pm | Party menu, creation, Modify, icons, roster | 0x19AE0, 0x19EEB, 0x1BA1E, 0x1D1AC, 0x1E684, 0x1EAD2 | 455 | 454 | 1 | 0 | — |
| Ex | Area loop, 3D view, area map, overland, doors | 0x403E, 0x17054, 0x17438, 0x2DCF4, 0x2EEF4, 0x2DE38 | 465 | 464 | 1 | 0 | — |
| Ecl | ECL VM and host opcodes | 0x3C73, 0x3EB7, 0x7689, 0x7814 | 386 | 385 | 0 | 1 | — |
| Enc | Encounters (monsters, close-up, menu, parley, surprise) | 0x2C3E, 0x3311, 0x155D, 0x240F, 0x236F, 0x1196, 0x10FA, 0x1EEAA, 0x7A21 | 85 | 84 | 0 | 1 | — |
| Cb | Combat (map, menu, moves, AI, spells, effects, items, after combat) | 0x24E1, 0x9881, 0x6AA5 | 518 | 517 | 1 | 0 | — |
| Cp | Camp (all menus, rest, Memorize, Scribe, Cast, Alter) | 0x196A0, 0x24A66 | 462 | 461 | 1 | 0 | — |
| Sh | Shops, temples, venues (PROGRAM, CALL) | 0x7212, 0x5280, 0x3B56, 0x3A15 | 469 | 468 | 1 | 0 | — |
| Tl | Treasure and loot | 0x26F6 | 32 | 32 | 0 | 0 | — |
| Lv | Training and level up | 0x1C32D, 0x2AA87 | 66 | 66 | 0 | 0 | — |
| Sv | Save and load | 0x1FD3E, 0x1F5E8 | 46 | 45 | 0 | 1 | — |
| End | End game | 0x20941, 0x206A3 | 52 | 52 | 0 | 0 | — |
| Kd | Party killed / destroyed | 0x34BB, 0x5A47 | 286 | 286 | 0 | 0 | — |

The Paths column of the unit tables below uses these codes (the paths whose closure holds the routine).

## Gaps on the title-to-ending path

Ranked by how much of play they can change (rules, state, branching), then by size and callers. Each line is a candidate [play] item: read the routine whole, port every branch, add an oracle check where the `por_*_oracle.py` pattern fits, then confirm in DOSBox-X.

### Play (rules, state, branching)

None since s52: every play row is W or N (the W-by-citation rows are listed in the unit tables).

### Presentation only (drawing, text, sound, input timing)

| # | Routine | Role | St | Why it is open |
|---|---|---|---|---|
| 1 | 0x2581C | [?] number / money text builder | P | number / money text builder: the C# names only inside addresses |
| 2 | 0x32270 | [?] picture block load with disk check (wall sets, start-up) | U | picture block load with disk check (wall sets, start-up): spec only; the module replaces the disk check |

### Closed in s60

Rows that left the ranked lists this session, with the lane notes (their status stays in the unit tables). The lanes' reads are in `s52-lane-A.md` … `s52-lane-F.md`, `s60-lane-T.md`, `-V`, `-A`, `-C`, `-M` and under "Statuses set by hand".

| Routine | Role | St | Note |
|---|---|---|---|
| 0x2D669 | vertical list / item list window | W | done s60 lane T: run whole with its helpers and the menu routine under emulation (`por_listwin_oracle.py`, 1468 cases); digit-row keys fixed |
| START 0x3379 | screen redraw by case (hall, camp, loot, shop pictures and boxes) | W | read whole s60 lane T (the open `[?]` is about indirect callers) |
| 0x239D6 | spell list window (type, action) | W | read whole s60 lane T |
| 0x2CC8C | menu routine (horizontal words) | W | key layer under the list oracle, drawing read whole s60 lane T; `[?]` settled ($4AFF) |
| START 0x1307 | party list redraw | W | read whole s60 lane T with its helpers; seen in DOSBox-X |
| START 0x2B8B | effect message (text and pause) | W | read whole s60 lane T |
| 0x2E8F7 | area map draw | W | read s60 lane V (it is the area map draw); internals read; seen 0 px |
| START 0x4751 | picture block read (DAX block → buffer) | W | segment 0464 read branch by branch s60 lane V |
| 0x1E468 | combat icon: builds the record's icon in slot 0xBF through scratch slot 11 | W | icon builders read whole s60 lane V; head ORed into the body fixed; icon editor 0 px in DOSBox-X |
| 0x32123 | debug display (DS:49F7: `START STING` + Ctrl-D only) | N | s60 lane T: a debug display (START STING + Ctrl-D), not ported by design |
| 0x2E0CE | picture frame draw | W | read whole s60 lane V; the sprite renderer's nil frame fixed |
| START 0x8C85 | sound: tune cue | W | game-side sound calls read s60 lane V (= sound.md §2.1) |
| START 0x4354 | start-up configuration: command line, video mode, sound device, installer flags (only the `F` line read) | W | read whole s60 lane V (five POOL.CFG lines) |
| START 0x7D3B | [?] text / box helper (Items page, View) | N | s60 lane T: machine-level text helpers, not ported by design |

Not gaps: the N rows (DAX reader START segment 0802, disk numbers, memory release, the code wheel) and the library segments, which the module format and .NET replace.

### Statuses set by hand

| Routine | St | Why |
|---|---|---|
| 0x004A | N | HeapError handler (out of memory), installed by 0x00B4 through DS:4414; .NET has no fixed heap (start-menu.md §1, s49) |
| 0x00B4 | N | installs the HeapError handler 0x004A (start-menu.md §1, s49) |
| 0x00D4 | W | read whole s52 (s52-lane-F.md); before s52: Intro.cs waits read from the program (start-menu.md §1a) |
| 0x02DA | W | read whole s52 (s52-lane-F.md) |
| 0x06FB | W | read whole s52 (s52-lane-F.md) |
| 0x0AA9 | N | skipped by design (Program.cs: straight to the party menu) |
| 0x211D | W | read whole s52: CHKPARTY's four 0x814A writes (s52-lane-D.md); before s52: part of CHKPARTY (golden captures) |
| 0x3B19 | W | read whole s60 lane A: the camp after a vector-2 wipe fixed, seen in DOSBox-X and the Client; before: read whole s52, vectors 2/3 without the switch follow fixed (s52-lane-D.md) |
| 0x3C73 | W | read whole s52: compare chain = the ECL handler table (s52-lane-D.md); before s52: every opcode handler is ported; the dispatch is EclVm.Execute (golden captures) |
| 0x3EB7 | W | read whole s52 (s52-lane-D.md) |
| 0x3EFA | W | read whole s52; the 0x20D50 call hooked s58 (EclVm.FollowScriptChanges) |
| 0x403E | W | read whole s52, $49F2 after camp / Look fixed (s52-lane-D.md) |
| 0x458A | W | read whole s52: matches VenueScreens.Heal |
| 0x4636 | W | read whole s52: matches Money.Pay (narrow) and the answer rule |
| 0x4F4E | W | read whole s52: matches VenueScreens.HealList |
| 0x5280 | W | read whole s52: matches VenueScreens.Temple (extended keys: note E) |
| 0x557E | W | read whole s52: matches AfterCombat.Share; pool experience made signed longints |
| 0x5A47 | W | read whole s52: matches AfterCombat.Results |
| 0x5E94 | W | read whole s52: matches VenueScreens.Summary |
| 0x6184 | W | read whole s52; run under emulation (por_venue_oracle.py lootTake): empty list ends at once, screen redrawn |
| 0x62B6 | W | read whole s52; run under emulation (por_venue_oracle.py lootTake); exit test, redraws and Home / End fixed |
| 0x672A | W | read whole s52: matches AfterCombat.Cleanup |
| 0x6866 | W | read whole s52: matches FightRules.NpcTreasureShare |
| 0x6AA5 | W | read whole s52: matches AfterCombat.Run / Finish |
| 0x6D04 | W | read whole s52: matches VenueScreens.Buy / PriceRow |
| 0x7035 | W | read whole s52: matches ShopRules.Buy / Money.Pay |
| 0x7212 | W | read whole s52: matches VenueScreens.Shop (extended keys: note E) |
| 0x7A21 | W | read whole s54: every branch as ecl-format.md "The encounter draw 0x7A21" and EncounterHost.Draw |
| 0x8A40 | W | read whole s54: matches AmigaView.TildeCase for every v1.3 line (digits in its set, "~~" unused) |
| 0x9807 | N | memory release (no port needed; FightPlacement.Start empties the list) |
| 0x9881 | W | read whole s52 (s52-lane-D.md) |
| 0x99FD | W | read whole s52; s60 lane C: the view and panel before trigger 0x0F ported; before: read whole s52 (s52-lane-D.md) |
| 0x9B21 | W | read whole s52 (s52-lane-D.md) |
| 0x9E6D | W | read whole s52 (s52-lane-D.md) |
| 0xA087 | W | read whole s52 (s52-lane-D.md) |
| 0xA1E6 | W | read whole s52, helpless test after the step fixed (s52-lane-D.md) |
| 0xA54C | W | read whole s52 (s52-lane-D.md) |
| 0xA64C | W | read whole s52 (s52-lane-D.md) |
| 0xA806 | W | read whole s52 (s52-lane-D.md) |
| 0xA8FD | W | read whole s52 (s52-lane-D.md) |
| 0xAA32 | W | read whole s52 (s52-lane-D.md) |
| 0xABCF | W | read whole s52 (s52-lane-D.md) |
| 0xADC3 | W | read whole s52 (s52-lane-D.md) |
| 0xB3B2 | W | read whole s52, the flee result fixed (s52-lane-D.md) |
| 0xB6FC | W | read whole s52 (s52-lane-D.md) |
| 0xBB81 | W | read whole s52 (s52-lane-D.md) |
| 0xBF8A | W | read whole s52; s60 lane C: 0x2250A's "already using" shown, seen in DOSBox-X and the Client; before: read whole s52; the nil-pointer unready is skipped (original bug, fixed s53/s54) |
| 0xD815 | W | read whole s52 (arena header and builder choice; ground pictures pres) |
| 0xD8B2 | W | read whole s52 (FightPlacement.InitRecords, EncounterHost.StartFight) |
| 0xE402 | W | read whole s52 (FightPlacement.Start, EncounterHost.StartFight) |
| 0xE600 | W | read whole s52 (s52-lane-D.md) |
| 0xF2A2 | W | read whole s52 lane B: handler 16, empty (push bp / pop bp / retf 0xA) |
| 0xFFB0 | W | read whole s52 lane B: handler 54, empty |
| 0xFFB7 | W | read whole s52 lane B: handler 55, empty |
| 0x11570 | W | C# cites the entry (EffectHandlers.MagicResistance) |
| 0x11DF6 | W | read whole s52 lane B: the shared empty handler (5, 13, 19, 24, 27, 60, 63, 92, 127) |
| 0x12504 | W | read whole s52 lane B: unit init, empty |
| 0x161CD | W | read whole s52 (handler 73 rake; nil target not tested by the program) |
| 0x16281 | W | read whole s52 (handler 74 hold) |
| 0x166CB | W | read whole s52 (DoorMap.Seal) |
| 0x16901 | W | geo-format.md door menu; DoorRules builds the same test |
| 0x16CB9 | W | geo-format.md door menu; DoorRules.KnockSpell |
| 0x16D58 | W | read whole s52 (AreaLoop.ForwardKey) |
| 0x16E06 | W | read whole s52 (AreaLoop.Move) |
| 0x16EE3 | W | read whole s52 (Overland.Step) |
| 0x17054 | W | read whole s52 (AreaLoop.Execute / MenuCommand) |
| 0x17438 | W | read whole s52; Knock sets no face (Core sets both: original bug, fixed s53/s54), $6DC9 cleared only in modes 1-4 |
| 0x17CD2 | W | read whole s52 (CampScreens.CastMenu) |
| 0x17FAB | W | read whole s52 (CampRest.RestForPreparation) |
| 0x18668 | W | read whole s51 / s52 |
| 0x187ED | W | read whole s52 (CampSpells.Display) |
| 0x18C22 | W | read whole s52 (CampScreens.Magic) |
| 0x18FB1 | W | read whole s60 lane A, seen in DOSBox-X and the Client (Select / Place extended keys); before: read whole s52 (CampScreens.Order) |
| 0x19116 | W | read whole s52 (CampScreens.Drop, CampParty.Drop) |
| 0x192C8 | W | read whole s52 (CampScreens.Speed) |
| 0x1948A | W | read whole s52 (CampScreens.Alter) |
| 0x196A0 | W | read whole s52 (CampScreens.Camp) |
| 0x1997F | N | memory release (no port needed) |
| 0x19AE0 | W | read whole s52 (PartyMenuFlow.Run, PartyMenu.Enabled) |
| 0x1B8B6 | W | read whole s52 (PartyMenuFlow.Drop) |
| 0x1C9EC | W | read whole s52 (PartyMenu.TryAdd); the unset first-pick count is recounted (original bug, fixed s53/s54) |
| 0x1CD66 | W | read whole s52 (PartyMenuFlow.Portrait); Left arrow leaves in DOS, not followed (Client menu keys) |
| 0x1CE5E | W | read whole s60 lane V, seen (editor frames from COMSPR 25 / 153, 0 px) |
| 0x1CF3D | W | read whole s60 lane V, seen (editor screen 0 px) |
| 0x1DFF6 | W | read whole s52 (PorRoster.Entries) |
| 0x1E19E | W | read whole s52 (PorRoster.WriteEntries) |
| 0x1E294 | N | save directory MkDir and save-disk prompt (start-menu.md §5, s49) |
| 0x1E315 | W | read whole s60 lane V, seen (pixel 46,113 = 4 | 2) |
| 0x1E468 | W | read whole s60 lane V, seen (icon editor, DOSBox-X 0 px); head ORed (CombatArt.Party) |
| 0x1E684 | W | read whole s52 (PorRoster.Save; JSON files by design) |
| 0x1EAD2 | W | read whole s52 (PorRoster.Load) |
| 0x1EEAA | W | read whole s52 (EncounterHost.LoadMonsterRecord) |
| 0x1F248 | W | read whole s52 (EncounterHost.CreateNpc, EclVm.DoAddNpc) |
| 0x1F42C | W | read whole s52 (PartyMenu.Join, EclVm.DoAddNpc) |
| 0x1F5E8 | W | read whole s52; the roster-file deletes read s60, built s61 as an option off by default (PorRoster.RemoveLoaded); before: read whole s52 (PorGame.Loaded, PorParty.Load) |
| 0x1FD3E | W | read whole s52; the roster-file deletes read s60, built s61 as an option off by default (PorRoster.RemoveSaved); before: read whole s52 (PorSaveGame.Build, PorParty.Save) |
| 0x202BB | N | NEWECL's missing-file disk prompt; a module holds every script (ecl-format.md NEWECL, s49) |
| 0x206A3 | W | read whole s52 (EndGame.FrameTime, EndGameAnimation) |
| 0x20941 | W | read whole s52 (EndGame.Steps) |
| 0x20D50 | W | read whole s58, DOSBox-X both callers (OverlandRecovery, geo-format.md) |
| 0x21AED | W | read whole s52: matches FightItemScreens.ViewEntries / CharacterView.Run; before s52: read whole s49 (combat-menu.md "View / Use"); s51: the party menu's S shows the memorised list as every other caller (SpellLists.ShowMemorised; camp.md "Which View commands run where") |
| 0x21F32 | W | read whole s51 with Ready 0x2250A, Drop 0x22398 (after 0x21DAA), Halve 0x22827, Join 0x228D7; oracle `por_items_oracle.py` runs the loop for DS:49F3 0-6 (ItemsPageProgramTests); every caller outside a fight runs every command (camp.md "Which View commands run where") |
| 0x23190 | W | read whole s52: matches CharacterView.TradeMoney |
| 0x234E1 | W | read whole s52: matches FightItemScreens.DropMoney |
| 0x239D6 | W | read whole s60 lane T (combat-menu.md) |
| 0x243BC | W | read whole s52 (drawing only, s52-lane-D.md) |
| 0x24F1D | W | read whole s52: matches TextRequest.Maximum (6- vs 5-character buffer noted) |
| 0x2568A | W | read whole s52: matches FightItemScreens.DropCoins |
| 0x25974 | W | read whole s52: coin kinds above 0 as signed longints fixed in MoneyScreens.Take |
| 0x25C07 | W | read whole s52; runs inside 0x62B6 under por_venue_oracle.py lootTake |
| 0x27AA9 | W | read whole s52: member pick START 0x37E8 with DS:6DB5 (s51 decision) |
| 0x29558 | W | s60 lane C: read whole, messages compared under the venue oracle (cure 7) |
| 0x2A332 | W | read whole s52: the cast message in and out of a fight |
| 0x2BB39 | W | read whole s52 (EclPartyRules.Hits) |
| 0x2C936 | W | s60: run inside 0x2D669 under the list oracle |
| 0x2CC8C | W | s60: key layer run under the list oracle, drawing read whole; the `[?]` settled ($4AFF); the timeout DS:6B2E–6B32 dead in v1.3 |
| 0x2CFCA | W | read whole s52 (s52-lane-F.md) |
| 0x2CFE0 | W | s60: run inside 0x2D669 under the list oracle |
| 0x2D073 | W | s60: run inside 0x2D669, draw log compared |
| 0x2D1A6 | W | s60: run inside 0x2D669 under the list oracle |
| 0x2D1F2 | W | s60: run inside 0x2D669 under the list oracle |
| 0x2D238 | W | s60: run inside 0x2D669, draw log compared |
| 0x2D2D7 | W | s60: run inside 0x2D669, draw log compared |
| 0x2D669 | W | s60: run whole under emulation with the menu routine (por_listwin_oracle.py, ListWindowProgramTests, 1468 cases) and seen in DOSBox-X (s60-lane-T.md) |
| 0x2D9A0 | W | read whole s52 (s52-lane-F.md) |
| 0x2DCF4 | W | read whole s52 (s52-lane-F.md) |
| 0x2E03F | W | read whole s52 (s52-lane-F.md) |
| 0x2E0CE | W | read whole s60 lane V |
| 0x2E170 | W | read whole s52 (s52-lane-F.md) |
| 0x2E55D | W | read whole s60 lane V |
| 0x2E7D6 | W | read whole s60 lane V; a nil entry draws nothing (fixed for DOS two-frame sprites) |
| 0x2E8F7 | W | read s60 lane V, seen (save A 15,4 W, 0 px) |
| 0x2EA4C | W | read whole s52 (s52-lane-F.md) |
| 0x2EA6B | W | read s60 lane V (backdrop bands as view3d.md) |
| 0x2EB08 | W | read s60 lane V (as view3d.md) |
| 0x2EEEA | W | read s60 lane V |
| 0x2EEF4 | W | read whole s52 (s52-lane-F.md) |
| 0x2F6FF | W | read whole s52 (s52-lane-F.md) |
| 0x2F9BE | W | read whole s52 (s52-lane-F.md) |
| 0x3083F | W | read whole s52: palette restore when DS:4A1E = 1 |
| 0x31A8E | W | read whole s52 (s52-lane-F.md) |
| 0x31B5D | W | read whole s52 (s52-lane-F.md) |
| 0x31C34 | W | read whole s52 (s52-lane-F.md) |
| 0x32123 | N | s60 lane T: debug only, the Client has no debug mode |
| 0x32392 | W | read s60 lane V (symbol sets, error halt, buffer / direct modes) |
| START 0x0006 | W | read whole s52 (s52-lane-F.md) |
| START 0x1307 | W | read whole s60 lane T and seen in DOSBox-X (hall party list) |
| START 0x148F | W | read whole s60 lane T (AC / HP number draw) |
| START 0x1513 | W | read whole s60 lane T (AC / HP number draw) |
| START 0x1583 | W | read whole s60 lane T (the status line is built) |
| START 0x1C13 | W | read whole s52 (s52-lane-F.md) |
| START 0x1C4D | W | read s60 lane T |
| START 0x2233 | W | read whole s52 (s52-lane-F.md) |
| START 0x228C | W | read whole s52 (s52-lane-F.md) |
| START 0x2383 | W | the `[?]` on its camp.md line is the placing of START 0xFE2's text, not this routine (s49 rebuild) |
| START 0x23B6 | W | read whole s52 (s52-lane-F.md) |
| START 0x2432 | W | read whole s52 (s52-lane-F.md) |
| START 0x257E | W | read s60 lane T (frame builder, no delay of its own); before: read whole s52 (s52-lane-F.md) |
| START 0x25C9 | W | read whole s52 (s52-lane-F.md) |
| START 0x2B8B | W | read whole s60 lane T |
| START 0x2EEA | W | read whole s52 (s52-lane-F.md) |
| START 0x3226 | W | read whole s52 (s52-lane-F.md) |
| START 0x32CA | W | read whole s52 (s52-lane-F.md) |
| START 0x3379 | W | read whole s60 lane T (every case; the open `[?]` is about indirect callers) |
| START 0x34AB | W | read whole s52 (s52-lane-F.md) |
| START 0x36FE | W | read whole s52 (s52-lane-F.md) |
| START 0x3728 | W | read whole s52 (s52-lane-F.md) |
| START 0x37E8 | W | read whole s52 (s52-lane-F.md) |
| START 0x3F10 | W | read whole s52 (s52-lane-F.md) |
| START 0x4047 | W | read whole s52 (s52-lane-F.md) |
| START 0x41F8 | W | read whole s52 (s52-lane-F.md) |
| START 0x4354 | W | read whole s60 lane V (five POOL.CFG lines; the Client: EGA, --sound, full introduction) |
| START 0x4640 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x46BB | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x470F | W | read s60 lane V (video mode set / restore) |
| START 0x473F | W | read s60 lane V (video mode set / restore) |
| START 0x4751 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x48E5 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x4F14 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x53B1 | W | read whole s52 (s52-lane-F.md) |
| START 0x5AC4 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x5B49 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x5BD6 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x5C07 | W | read whole s52 (s52-lane-F.md) |
| START 0x5F91 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x6192 | W | read whole s52 (s52-lane-F.md) |
| START 0x649C | W | EGA mode run s58 (por_recolour_oracle.py, OverlandRecovery.Recolour); CGA/Tandy modes not modelled |
| START 0x6ACE | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x6BD5 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x6C72 | W | read branch by branch s60 lane V; the visible effect matches the importer and renderer |
| START 0x704D | W | read whole s52 (s52-lane-F.md) |
| START 0x7090 | N | s60 lane T: machine level (EGA registers, video pages, cell fill, clear) |
| START 0x7101 | N | s60 lane T: machine level (EGA registers, video pages, cell fill, clear) |
| START 0x7130 | N | s60 lane T: machine level (EGA registers, video pages, cell fill, clear) |
| START 0x7147 | N | s60 lane T: machine level (EGA registers, video pages, cell fill, clear) |
| START 0x720C | N | s60 lane T: font load (the importer) |
| START 0x726F | W | read whole s52 (s52-lane-F.md) |
| START 0x767B | W | read whole s52 (s52-lane-F.md) |
| START 0x76B6 | W | read whole s52 (s52-lane-F.md) |
| START 0x7737 | W | s52: run whole inside 0x77F8 by por_print_oracle.py; every glyph compared (PrintProgramTests) |
| START 0x77A4 | W | s52: as 0x7737 |
| START 0x7C85 | W | read whole s52 (s52-lane-F.md) |
| START 0x7CCF | W | read whole s52 (s52-lane-F.md) |
| START 0x7D3B | N | s60 lane T: machine level (full clear) |
| START 0x7DF8 | W | read whole s52 (s52-lane-F.md) |
| START 0x7E4E | W | read whole s52 (s52-lane-F.md); before s52: the `[?]` on its spec line (ecl-format.md DELAY) is the Amiga side, not this routine (s49 rebuild) |
| START 0x7E63 | W | read whole s52 (s52-lane-F.md) |
| START 0x896A | N | disk swap is platform; the module has no disks |
| START 0x897D | N | disk swap is platform |
| START 0x8990 | W | read whole s52 (s52-lane-F.md) |
| START 0x89E9 | W | read whole s52 (s52-lane-F.md) |
| START 0x8A94 | W | read whole s52 (s52-lane-F.md) |
| START 0x8AB2 | W | read whole s52 (s52-lane-F.md) |
| START 0x8AE0 | W | read s60 lane V = sound.md §2.1; Core brackets every auto-stop caller |
| START 0x8B4C | W | read s60 lane V = sound.md §2.1; Core brackets every auto-stop caller |
| START 0x8BBC | W | read whole s52 (s52-lane-F.md) |
| START 0x8C5B | W | read s60 lane V = sound.md §2.1; Core brackets every auto-stop caller |
| START 0x8C85 | W | read s60 lane V = sound.md §2.1; Core brackets every auto-stop caller |
| START 0x9E91 | W | read s60 lane V = sound.md §2.1; Core brackets every auto-stop caller |
| START 0x9F57 | W | s52: run by por_sound_oracle.py through the timer interrupt; port writes compared (SoundDriverProgramTests) |
| START 0x9FCC | W | s52: as 0x9F57 |
| START 0xA05A | W | s52: as 0x9F57 |
| START 0xA0EB | W | s52: as 0x9F57 |
| START 0xA2FA | W | read s60 lane V = sound.md §2.1; Core brackets every auto-stop caller |

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

#### Segment 0000: main program (1 routines: W 1 (0 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x0006 | main program: unit inits, configuration, introduction, code wheel, party menu, view colours, area loop, party-destroyed exit | W | play | Su | C#, spec | Program, GameClock.BackdropRule |

#### Segment 00BA: game core helpers (stats, messages, distance, lists, screen redraws) (52 routines: W 52 (48 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x0BA0 | stat recalculation: weapon (THAC0, Dex missile adjustment) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.Weapon |
| START 0x0D9B | stat recalculation: armour movement | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.ArmourMove |
| START 0x0E22 | stat recalculation: AC from readied items | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.AcItem |
| START 0x0F40 | stat recalculation: encumbrance | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.Encumbrance |
| START 0x0FE2 | item name (cached name build) | W | play | Pm Ex Ecl Cb Cp Sh Tl Kd | oracle (caller), C#, spec, `[?]`×2 | CharacterView.Ready, CharacterView.DropOnPage, TradeContext.StoredItemName |
| START 0x1307 | party list redraw | W | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | oracle (caller), C#, spec, `[?]`×2 | HallPicture, HallScreens.Run, ClientPlayer.DrawInterface |
| START 0x148F | party list: name / AC column colour | W | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | oracle (caller), C# | CombatScreen.DrawPanel, FramePiece.PartyList |
| START 0x1513 | party list: HP number colour | W | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | oracle (caller), C# | CombatScreen.DrawPanel, FramePiece.PartyList |
| START 0x1583 | creature icon and status panel redraw | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorTextCatalog.Image, CombatView.ShownLines, CombatView.ShowLines |
| START 0x1713 | creature is helpless (status / effects) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatScreen.DrawPanel, FightRules.HelplessEffects, FightRules.IsHelpless |
| START 0x1758 | stat recalculation | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | ClientResources.ItemTypes, CharacterView.Run, CharacterView.Items |
| START 0x1BD7 | Str of a byte | W | play | Pm Ex Ecl Enc Cb Cp Sh Tl Lv End Kd | oracle (caller), C#, spec | CampTime.TwoDigits |
| START 0x1C13 | number to text (Str of an unsigned word) | W | pres | Pm Ex Ecl Cb Cp Sh End | oracle (caller), C#, spec | EclVm.Execute |
| START 0x1C4D | Str of a long integer (no drawing) | W | pres | Pm Ex Cb Cp Sh | oracle (caller) | — |
| START 0x1C86 | AC from Dexterity | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.DexAc |
| START 0x1D08 | Dexterity adjustment (initiative, missile) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.DexAdjust, StatRecalc, StatRecalc.Weapon |
| START 0x1D8A | strength code for the carrying allowance | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | Carrying, Carrying.StrengthCode |
| START 0x1E22 | Strength to-hit bonus | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.StrHit |
| START 0x1EC5 | Strength damage bonus | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | StatRecalc, StatRecalc.StrDamage |
| START 0x1F4B | carrying allowance by strength code | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | Carrying, Carrying.AllowanceForCode, Carrying.StrengthAllowance |
| START 0x202B | remove a memorised spell | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.ApplyAttackDamage, FightSpells, FightSpells.RemoveSpell |
| START 0x20A9 | unlink and free an item | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CharacterView.DropOnPage, DropSpentWeapon, FightSpells.UseItemNow |
| START 0x2197 | append an item copy to a character | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | TradeRules.GiveItem, EffectHandlers.SpiritualHammer |
| START 0x2233 | row-24 message | W | pres | Pm Ex Ecl Cb Cp Sh Lv Kd | C#, spec | HallPicture.HallState, HallScreens.Password, CombatView.KeptList |
| START 0x228C | combat / status message line | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightSpells.CombatCastStep |
| START 0x2383 | clear the message window | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatChooser.Attach, CombatChooser.PrintCreatureMessage, CombatChooser.ClearMessageWindow |
| START 0x23B6 | character name in its colour (in play, hostile) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | oracle (caller), C#, spec | PorAmigaText, CombatChooser.NameColour, CharacterView.LetGoOnPage |
| START 0x2432 | draw an icon at a cell (picture, slot, mirror) | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FlightAnimation, FlightAnimation.SpellBlock, FlightAnimation.AimCursor |
| START 0x257E | hit / effect icon flash with delays | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.ShowHit, FightSpells.CastNow, FightSpells.HoldPerson |
| START 0x25C9 | moving icon (flight path) | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.Flying, CombatChooser.FlightPathMax, CombatChooser.Flight |
| START 0x2B8B | effect message (text and pause) | W | pres | Pm Ex Ecl Cb Cp Sh Kd | C#, spec, `[?]`×1 | PorTextCatalog, CombatChooser.EffectMessage, CombatChooser.EffectTextMessage |
| START 0x2D39 | find a creature's effect record by id | W | play | Pm Ex Ecl Enc Cb Cp Sh Tl Lv Kd | oracle (caller), C#, spec | MonsterAi.CrossingEffects, EffectHandlers.CharmGaze, EffectHandlers.TriggerEffect |
| START 0x2DC8 | what a hit does (HP, status) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | ClientPlayer.DamageApplied, FightRules.InflictDamage, EffectHandlers.NonWeaponDamage |
| START 0x2EEA | healed-amount message | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorTextCatalog.Image, CombatChooser.HealedWords, CombatChooser.EffectMessage |
| START 0x2F57 | opposite side test (attack-ally check) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightSpells.AimAuto, FightSpells.EnemiesBeside, FightSpells.Routine |
| START 0x2F7B | count the teams (DS:6814 / 6815) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CountTeams, TurnUndead.Run, EffectHandlers.Revive |
| START 0x2FD3 | count enemies within range | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.CastOneSide, FightSpells.EnemiesBeside, MonsterAi.EnemiesWithin |
| START 0x30FB | distance between two creatures | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | NoFightEffects.IFightGeometry, IFightGeometry, FightRules.RangePenalty |
| START 0x31D7 | end a creature's actions | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.EndActions, FightSpells.ContinueCastingAuto, FightSpells.UseItemNow |
| START 0x3226 | Guard | W | play | Cb | C#, spec | PorTextCatalog.Image, CombatChooser.Attach, MonsterAi.FinishRound |
| START 0x3262 | spell table entry (DS:31FA + 16·id) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorSpellData.Spells, FightSpells, FightSpells.CasterLevel |
| START 0x32CA | 3D layout frame (inner boxes) | W | pres | Pm Ex Ecl Cb Cp Sh End | oracle (caller), C#, spec | ClientPlayer.DrawInterface, ClientPlayer.WindowsDrawn, Draw |
| START 0x3379 | screen redraw by case (hall, camp, loot, shop pictures and boxes) | W | pres | Pm Ex Ecl Cb Cp Sh End | oracle (caller), C#, spec, `[?]`×2 | ClientPlayer, ExploreScreen |
| START 0x34AB | position line | W | pres | Pm Ex Ecl Cb Cp Sh End | oracle (caller), C#, spec | ClientPlayer.DrawInterface, PositionLineDrawn, ShownStatusLine |
| START 0x36FE | overland interface redraw | W | pres | Ex | C#, spec | PorGame.RunLoadedEntry, ClientPlayer.OtherLayout, AreaLoop.OverlandInterfacePending |
| START 0x3728 | screen clear | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Notice, FightSpells.CombatCastStep, FightMenu.Cast |
| START 0x37E8 | ask whom (member pick prompt) | W | pres | Pm Ex Ecl Cb Cp Sh | oracle (caller), C#, spec | PorTextCatalog.Image, MemberPick, MemberPick.Run |
| START 0x39A3 | has a ranged weapon | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.HasRangedWeapon |
| START 0x39E6 | has a throwable weapon | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.HasThrowableWeapon, DropSpentWeapon |
| START 0x3A2B | find ranged ammunition | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.FindRangedAmmo, FightRules.RecalcAttacksLeft, FightMenu.TargetOffered |
| START 0x3AF7 | race level limit | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightKit.WireLevels, EffectHandlers.RaceLevelLimit, LevelStatsTables.LevelStats |
| START 0x3C3E | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Segment 03F1: windows and frames (4 routines: W 4 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x3F10 | window frame draw | W | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | C#, spec | OutsideEffectMessages.Message, TradeContext.FrameTextBox, RestUi.CastMenu |
| START 0x4047 | window inside blank | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | C#, spec | PartyMenuFlow.Add, CharacterView.ProgramItems, CharacterView.DropOnPage |
| START 0x41F8 | window (frame and cleared inside) | W | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | C#, spec | PorAmigaFrame, KeyRequest.WindowLine, ListRequest.SpellPageLayout |
| START 0x4277 | empty routine (unit init or empty handler) | W | pres | Su | — | — |

#### Segment 042B: start-up configuration (1 routines: W 1 (0 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x4354 | start-up configuration: command line, video mode, sound device, installer flags (only the `F` line read) | W | pres | Su | C#, spec | Program |

#### Segment 0464: pictures: read, decode, draw (20 routines: W 20 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x4640 | [?] picture decode helper | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | C# | CombatArt.Draw |
| START 0x46BB | [?] picture buffer helper | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | C# | OverlandRecovery.Recolour |
| START 0x470F | [?] picture helper (start-up) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | — | — |
| START 0x473F | [?] picture block free / release | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | — | — |
| START 0x4751 | picture block read (DAX block → buffer) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | C#, spec | CombatArt.Draw |
| START 0x48E5 | [?] picture decode (block unpack into the off-screen buffer) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | — | — |
| START 0x4F14 | [?] masked piece draw (tiles, wall pieces) | W | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | spec | — |
| START 0x53B1 | picture block draw (FINAL, frames) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | C#, spec | CombatChooser.DrawFlying, CombatScreen.DrawMap, EndGameLine.EndGamePicture |
| START 0x5AC4 | [?] picture draw helper | W | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| START 0x5B49 | [?] picture draw helper | W | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| START 0x5BD6 | [?] picture draw helper | W | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| START 0x5C07 | [?] picture draw variant (with 0x5AC4 / 0x5B49 / 0x5BD6) | W | pres | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | CombatScreen.Mirrored |
| START 0x5F91 | picture block draw at a screen cell | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | spec | — |
| START 0x6192 | off-screen buffer to EGA memory | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv End Kd | C#, spec | OverlandRecovery, EffectHandlers.PutOutOfPlay |
| START 0x649C | picture block recolour (from/to maps, random dissolve) | W | play | Su Pm Ex Ecl Enc Cb Cp Sh Sv End | C#, spec | OverlandRecovery, OverlandRecovery.Colours, OverlandRecovery.Recolour |
| START 0x6ACE | [?] palette flash / picture show | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End Kd | C#, spec | OverlandRecovery.Recolour |
| START 0x6BD5 | picture memory free | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End Kd | spec | — |
| START 0x6C72 | [?] recolour (icon palette, backdrop) | W | pres | Pm Ex | C# | DrawIcons |
| START 0x704D | palette register flash (INT 10h) | W | pres | Su Pm Ex Ecl Cb Cp Sh Kd | C#, spec | Intro.FlashRegister |
| START 0x708A | empty routine (unit init or empty handler) | W | pres | Su | — | — |

#### Segment 0709: text, input, time (20 routines: W 14 (4 by execution), N 6)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x7090 | [?] text cell helper (under 0x7147) | N | pres | — | — | — |
| START 0x7101 | [?] text set-up at start-up | N | pres | Su | — | — |
| START 0x7130 | [?] text set-up helper | N | pres | Su | — | — |
| START 0x7147 | [?] window / box fill helper | N | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv End Kd | — | — |
| START 0x720C | [?] palette / colour set-up (start-up) | N | pres | Su | — | — |
| START 0x726F | glyph draw | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | C#, spec | PorAmigaRunes, TextWindow, TextWindow.ScreenRight |
| START 0x767B | see `combat-map.md` Start messages | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv Kd | C#, spec | CombatChooser.BattleBegins, ClientPlayer.Flash |
| START 0x76B6 | string draw (column, row, colour) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | C#, spec | CombatChooser.BattleBegins, SpellPageLayout.WholeTitle, TradeContext.ShowPageBare |
| START 0x7737 | see `combat-menu.md` The combat screen (Client) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Kd | C#, spec | TextWindow.Typed, TextWindow.OriginalWordEndHang, TextWindow.PrintDos |
| START 0x77A4 | text window helper (typing state) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Kd | C#, spec | TextWindow.PrintDos |
| START 0x77F8 | text window (typing, paging) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Kd | oracle, C#, spec | Calls.KeyPagePen, PorProgram.PagePrompt, CombatView.Panel, TextWindow.PrintDos (s48: the 255-character hang fixed, the original behind TextWindow.OriginalWordEndHang) |
| START 0x7A73 | line input (INPUTSTR, names) | W | pres | Su Pm Ecl | oracle, C#, spec | TextRequest.Takes, ClientPlayer.InputNumber, Answer |
| START 0x7BF6 | number input (INPUTNUM) | W | pres | Ecl | oracle, C#, spec | AmigaView.Number, ClientPlayer.InputNumber, DosLineInput |
| START 0x7C85 | row-24 key prompt | W | pres | Pm Ex Ecl Enc Cb Cp Sh Tl Sv Kd | C#, spec | PorAmigaText, Setup.PenSites, Calls.KeyPagePen |
| START 0x7CCF | GetTime (hundredths) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Lv Sv End | C#, spec | PictureAnimation, EndGamePicture.EndGameAnimation, DosClock |
| START 0x7D3B | [?] text / box helper (Items page, View) | N | pres | Pm Ex Cb Cp Sh | — | — |
| START 0x7DF8 | bottom-line message for game speed × 225 ms | W | pres | Pm Ex Lv | C#, spec | Calls.TimedWarnPen, HallPicture.HallState, HallScreens.Message |
| START 0x7E4E | game-speed delay | W | pres | Pm Ex Ecl Cb Cp Sh Lv Sv End Kd | C#, spec | CombatChooser.MessageMs, AmigaView.MessagePause, CharacterView.PauseOnly |
| START 0x7E63 | box blank after a pick | W | pres | Pm Ex Ecl Enc Cb Cp Sh End Kd | C#, spec | TextWindow.Clear, TextWindow.ClearRect, TextWindow.PrintDos |
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
| START 0x830B | see `ecl-format.md` Other handlers read in the program (2026-11-17) | N | pres | Pm Sv | spec | — |
| START 0x8376 | [?] not read; called by 0x1E684, 0x1F5E8 | N | pres | Pm Sv | C# | PorRoster.DosStem |
| START 0x8445 | see `start-menu.md` 7. Open `[?]` | N | pres | Pm Sv | C#, spec | PorRoster, PorRoster.DosStem |
| START 0x852F | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x85ED | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x8707 | [?] not read; called by START 0x8736 | N | pres | — | — | — |
| START 0x8736 | [?] not read; called by 0x1EEAA, 0x26F6 | N | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Sv End | — | — |
| START 0x896A | disk number set (DS:5376 / 5377) | N | play | Pm Ecl Enc Sv | spec | — |
| START 0x897D | [?] disk helper | N | play | Pm Ecl Sv | spec | — |
| START 0x8988 | empty routine (unit init or empty handler) | N | pres | Su | — | — |

#### Segment 0899: disk number, keys while busy, sound stop (5 routines: W 5 (4 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x8990 | sound stop command | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle (caller), C#, spec | Program, PartyMenuFlow.Run |
| START 0x89E9 | keys while busy (toggles) | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle (caller), C#, spec | Waiting.ReadingKeys |
| START 0x8A94 | key flush | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Sv End Kd | oracle (caller), C#, spec | TextWindow.PrintDos, IntroLine.Intro, Intro.Key |
| START 0x8AB2 | key poll (no action) | W | pres | Cb | C#, spec | Waiting.Discard, FightKit.AiKey, FightKit.Create |
| START 0x8ACC | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Segment 08AE: sound driver (PC speaker / Tandy, timer interrupt) (16 routines: W 16 (9 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| START 0x8AE0 | sound: driver entry | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | spec | — |
| START 0x8B4C | sound: driver helper | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |
| START 0x8BBC | sound: driver start | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | C#, spec | ISoundPorts.PcSoundDevice |
| START 0x8C5B | sound: device set-up | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |
| START 0x8C85 | sound: tune cue | W | pres | Su Pm Ex Ecl Cb Cp Sh Kd | spec | — |
| START 0x8CB2 | empty routine (unit init or empty handler) | W | pres | Su | — | — |
| START 0x9E5C | sound: timer install | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle | — |
| START 0x9E91 | sound: timer restore | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |
| START 0x9ECA | sound: timer interrupt (tick) | W | pres | — | oracle | — |
| START 0x9F57 | sound: per-tick voice step (from the timer interrupt) | W | pres | — | oracle (caller) | — |
| START 0x9FCC | sound: per-tick voice step (from the timer interrupt) | W | pres | — | oracle (caller) | — |
| START 0xA05A | sound: note start helper | W | pres | — | oracle (caller) | — |
| START 0xA0EB | sound: port write helper | W | pres | — | oracle (caller) | — |
| START 0xA222 | sound: PC-speaker tune programmer | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle | — |
| START 0xA28E | sound: Tandy tune programmer | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | oracle | — |
| START 0xA2FA | sound: driver helper | W | pres | Su Pm Ex Ecl Enc Cb Cp Sh Tl Lv Sv End Kd | — | — |

### GAME.OVR

#### Unit 0x0008–0x00C5 (stub 16): start-up odds and ends (2 routines: N 2)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x004A | HeapError handler (installed by 0x00B4): out of memory → warning, the game save while DS:49D0 ≠ 0 until it succeeds, then quit | N | play | — | spec | — |
| 0x00B4 | unit 0x16 init (START 0xF1): installs 0x004A as Turbo Pascal's HeapError (DS:4414 / 4416) | N | play | Su | C#, spec | Program |

#### Unit 0x00D4–0x08E1 (stub 19): introduction (4 routines: W 4 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x00D4 | introduction wait (time or key, key flush) | W | pres | Su | spec | — |
| 0x02DA | introduction credits page | W | pres | Su | C#, spec | PorTextCatalog, IntroScreen.DrawCredits, IntroStep |
| 0x06FB | the introduction: logo, title, credits | W | pres | Su | C#, spec | Program, IntroScreen, IntroLine.Intro |
| 0x08DD | empty routine (unit init or empty handler) | W | play | Su | C# (inside) | PorProgram |

#### Unit 0x09AC–0x0D09 (stub 1C): code wheel (2 routines: W 1 (1 by execution), N 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x0AA9 | code wheel (copy protection) | N | pres | Su | C#, spec | Program |
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
| 0x211D | CHKPARTY helper: writes a VM cell (0x814A) | W | play | Ecl | C# (inside), spec (inside) | PartyMenuFlow, AmigaView |
| 0x217B | ECL CHKPARTY handler | W | play | Ecl | golden, C#, spec | EclVm, EclPartyRules, EclPartyRules.CheckParty |
| 0x236F | ECL PSURPRISE handler | W | play | Enc | golden, C#, spec (inside) | OutsideEffectMessages.Message |
| 0x240F | ECL SURPRISE handler | W | play | Enc | golden, C#, spec | EclVm.Execute |
| 0x24E1 | ECL COMBAT handler | W | play | Cb | golden, C#, spec | EncounterHost.Combat, IEncounterUi.ICombatRunner |
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
| 0x3B19 | see `camp.md` The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3) | W | play | Ex Sh | C#, spec | AreaCommand, AreaLoop.Encamp, EclVm.ProgramCamp |
| 0x3B56 | ECL PROGRAM handler | W | play | Sh | golden, C#, spec | ClientPlayer.ProgramCamp, EclVm, IEclHost |
| 0x3BC3 | ECL PROTECT handler | W | play | Ecl | golden, C#, spec | ClientPlayer.DrawGlyphs, EclVm, IEclHost |
| 0x3C73 | ECL opcode dispatcher (compare chain on DS:6F7F) | W | play | Ecl | C# (inside), spec (inside) | EffectHandlers.Holds, PorOverland |
| 0x3EB7 | ECL run loop | W | play | Ecl | C#, spec | AreaLoop.Encamp, EncounterHost.Vm, EclVm.RunVector |
| 0x3EFA | after a script switch: vector 4, then 0 and 1 | W | play | Ex | C#, spec | AreaLoop.Run, AreaLoop.Encamp, OverlandRecovery |
| 0x403E | area loop (vectors 0/1, steps, party-defeated exit) | W | play | Ex | C#, spec | PorGame.LoadInHall, PorGame.RunLoadedEntry, PorGame.NewGameScript |
| 0x424E | empty routine (unit init or empty handler) | W | play | Su | C# (inside) | EclVm, EclOpcodes |

#### Unit 0x4577–0x54AD (stub 22): temple (12 routines: W 12 (8 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x458A | see `shop-format.md` Temple | W | play | Sh | C#, spec | VenueScreens.HealList, VenueScreens.Heal, TempleRules |
| 0x4636 | see `shop-format.md` Price checks | W | play | Sh | C#, spec | VenueScreens.HealList, VenueScreens.PayColours, Payer.Pay |
| 0x47C0 | temple cure blindness: 33 removed through 0x2AF10 | W | play | Sh | oracle, C#, spec | TempleRules.Apply |
| 0x4864 | temple cure 1: six effects (DS:0114) removed through 0x2AF10 under the lock DS:681F | W | play | Sh | oracle, C#, spec | TempleRules.CureDisease |
| 0x4978 | temple cures 2–4: heal 1d8 / 2d8+1 / 3d8+3 through 0x2C647 (flag 0; handler 0x4E revives) | W | play | Sh | oracle, C#, spec | TempleRules.Apply, EffectHandlers.RestoreHitPoints |
| 0x4A87 | temple raise dead: 32 and 55 removed through 0x2AF10 under DS:681F, HP / Con / max-HP rule (max HP fixed s61: the hall's Con bonus difference per level) | W | play | Sh | oracle, C#, spec | TempleRules.RaiseDead, TempleRules.RaiseDeadLoss |
| 0x4CC8 | temple cure poison: 55, 22, 15 removed through 0x2AF10 under DS:681F (s51 decision: every record, lock held throughout; handler 22's lock clearing and kill fixed) | W | play | Sh | oracle, C#, spec | TempleRules.CurePoison |
| 0x4DB1 | temple remove curse (DS:6C95 := member, spell routine 0x29558) | W | play | Sh | oracle, C#, spec | TempleRules.RemoveCurse |
| 0x4E9F | temple stone to flesh | W | play | Sh | oracle, C#, spec | TempleRules.Apply |
| 0x4F4E | temple heal list | W | play | Sh | C#, spec | PorProgram.TempleNames, SpellPageLayout.WholeTitle, VenueScreens.Temple |
| 0x5280 | temple | W | play | Sh | C#, spec | VenueScreens, VenueScreens.PriceField, EncounterHost.Combat, MoneyScreens.StayForMoney (s48: the go-back question no longer clears the priest's message) |
| 0x54A9 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x557E–0x6BF3 (stub 25): after combat (share, award, removal, party defeated) (12 routines: W 12 (6 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x557E | after combat: experience share | W | play | Kd | C#, spec | AfterCombat.Share |
| 0x58D3 | after combat: award | W | play | Kd | oracle, C#, spec | CombatRules.ExperienceFor, CombatRules.AwardExperience |
| 0x5A47 | see `start-menu.md` After combat | W | play | Kd | C#, spec | ClientPlayer.KeepsCombatRow24, EncounterHost.Vm, AfterCombat |
| 0x5E94 | see `combat-format.md` After combat | W | play | Cb | C#, spec | VenueScreens, VenueScreens.MemberMessage, VenueScreens.Loot |
| 0x6092 | see `combat-format.md` After combat | W | play | Cb | oracle, C#, spec | VenueScreens.ItemPage, RestUi.LootDetect, TakeSteps.TakeItems |
| 0x6184 | see `combat-format.md` After combat | W | play | Cb | oracle, C#, spec | VenueScreens.TakeItems, LootRules.ExtendedKey, TakeSteps.Items |
| 0x62B6 | see `combat-format.md` Loot menu | W | play | Cb | oracle, C#, spec | PorTextCatalog, VenueScreens.LootTake, LootRules.ExtendedKey |
| 0x644B | loot menu (View, Take, Pool, Share, Detect, Exit; D casts 0x27C38) | W | play | Cb | oracle, C#, spec | LootRules, LootRules.ExtendedKey (s51 decision: Del / Shift-F1 / Shift-F3 as the program, F10 only with Detect on the line), VenueScreens.LootMenu, CampScreens.LootDetect |
| 0x672A | after combat: removal of the monsters | W | play | Cb | C#, spec | ClientPlayer.KeepsCombatRow24, ClientPlayer.Trade, EncounterState.Monsters |
| 0x6866 | see `combat-format.md` After combat | W | play | Cb | C#, spec | VenueScreens.MemberMessage, ClientPlayer.Trade, OverlandRecovery.Recolour |
| 0x6AA5 | after combat | W | play | Cb | C#, spec | ClientPlayer.KeepsCombatRow24, Draw, EncounterHost.Combat |
| 0x6BEF | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x6CD4–0x7470 (stub 28): shop (5 routines: W 5 (2 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x6D04 | see `por-art-formats.md` Amiga program reads (s19; hunks numbered from 0 in file order) | W | play | Sh | C#, spec | TradeContext.StoredItemName, VenueScreens.Buy, VenueScreens.ItemPage |
| 0x6F0D | see `combat-format.md` After combat | W | play | Cb Sh | oracle, C#, spec | VenueScreens.Buy, VenueScreens.TakeItems, TakeSteps.TakeItems |
| 0x7035 | see `combat-format.md` Shop | W | play | Sh | C#, spec | VenueScreens.Buy, ShopRules.GiveCopy, BuyResult.Buy |
| 0x7212 | shop | W | play | Sh | C#, spec | VenueScreens, VenueScreens.VenueWord, EncounterHost.Combat |
| 0x746C | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x74D3–0x96EC (stub 2B): ECL VM helpers (operands, VM memory, text), script loader, encounter draw (33 routines: W 33 (33 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x74D3 | operand loader | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclVm.NormaliseInput, EclVm.LoadVariadic |
| 0x7609 | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclVm.NormaliseInput |
| 0x7689 | script init | W | play | Ecl | golden, C#, spec | GameClock.BackdropRule, EclVm.InitScript, EclCells.InitWrites |
| 0x7814 | script file loader (ECL<d>) | W | play | Ecl | golden, C#, spec | PorGame.RunNewGameEntry, TextWindow.PrintDos, ClientPlayer.ScriptChanging |
| 0x78E4 | [?] not read; called by 0x1196 | W | play | Enc | golden, spec (inside) | — |
| 0x7955 | see `ecl-format.md` View distance | W | play | Enc Cb | golden, C#, spec | EncounterHost.ViewDistance, EncounterHost.Combat, EncounterRules.ViewDistance |
| 0x79E4 | see `combat-format.md` Shop | W | play | Ecl Enc | golden, C#, spec | ViewPictures.DrawVenuePortrait, ClientPlayer.ShowEncounter, ClientPlayer.ShowPicture |
| 0x7A21 | encounter draw | W | pres | Enc | golden, C#, spec | EncounterHost.Draw, IEncounterUi, EclMemory.EncounterSpriteUp |
| 0x7B82 | lo/hi → word | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | DosLineInput.Ends |
| 0x7BA6 | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | DosLineInput.Finish |
| 0x7BCE | see `ecl-format.md` VM memory map | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | DosLineInput.Finish |
| 0x7C29 | [?] not read; called by 0x7C8F | W | play | Ecl Enc Sh Tl Kd | golden | — |
| 0x7C8F | see `ecl-format.md` Where the scripts live | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclPartyPosition.EclMemory, EclMemory.StoreCell, EclCharacterView |
| 0x7F47 | see `ecl-format.md` Where the scripts live | W | play | Ecl Enc | golden, C#, spec | EclPartyPosition.EclMemory, EclMemory.StoreCell, EclCharacterView |
| 0x814A | see `ecl-format.md` Other handlers read in the program (2026-11-17) | W | play | Ecl Enc | golden, C#, spec | IEclHost, EclMemory.SpecialWrites, EclMemory.SpecialNoOpWrites |
| 0x82DF | see `ecl-format.md` VM memory map | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclMemory.SpecialReads, EclMemory.Read |
| 0x8426 | see `ecl-format.md` SAVE of text | W | play | Ecl | golden, C#, spec | EclMemory.WriteText |
| 0x8626 | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclText.Slot |
| 0x87CE | see `ecl-format.md` Operand encoding | W | play | Ecl Enc Sh Tl Kd | golden, C#, spec | EclMemory.ReadText |
| 0x8A40 | see `por-art-formats.md` Amiga program reads (s19; hunks numbered from 0 in file order) | W | play | Ecl Enc Cb Sh | golden, C#, spec | AmigaView.TildeCase |
| 0x8B57 | see `combat-format.md` After combat | W | play | Ecl Enc Cb Sh | golden, C#, spec | DosKeys.MemberPick, ChoiceRequest.ExtendedKeysPickFirst, MoneyScreens.StayAtExit, Dialog.MenuHelper (s48: Return answers the lit word) |
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

#### Unit 0x9807–0xAA85 (stub 35): the fight (top level, clean-up, round loop) (14 routines: W 13 (2 by execution), N 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x9807 | fight clean-up: frees the creature list and the arena | N | play | Cb | C# (inside), spec | EclMemory, EclProfile |
| 0x9881 | the fight (top level) | W | play | Cb | C#, spec | CombatChooser.BattleBegins, EncounterHost.Combat, ICombatRunner |
| 0x9938 | next creature to act | W | play | Cb | oracle, C#, spec, `[?]`×1 | CombatRules.NextCreature, EndActionsController.Fight, Fight.NextCreature |
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

#### Unit 0xABC0–0xC4E6 (stub 38): monster AI (15 routines: W 15 (9 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0xABCF | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatChooser.Attach, CombatChooser.TurnStarted, CombatMsg |
| 0xADC3 | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xAE15 | see `combat-format.md` The fight | W | play | Cb | oracle (caller), C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xAEAB | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | CombatMsg, CombatChooser.AiSays, FightSpells.FindCureTarget |
| 0xAFA4 | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | CombatMsg, CombatChooser.AiSays, FightSpells.UseItem |
| 0xB105 | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | CombatMsg, CombatChooser.AiSays, FightSpells.CastAuto |
| 0xB235 | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | PorProgram.AiStepOffsetsBase, CombatMsg, CombatChooser.AiSays |
| 0xB3B2 | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xB6FC | see `combat-format.md` The fight | W | play | Cb | C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xBB0F | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | CombatMsg, CombatChooser.AiSays, AiMessage.MonsterAi |
| 0xBB81 | see `combat-format.md` The fight | W | play | Cb | C#, spec | Waiting.TakeAiKey, GameAction, CombatMsg |
| 0xBC97 | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | AiMessage.MonsterAi, MonsterAi.FinishRound, MonsterAi.CheckMorale |
| 0xBE4D | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | AiMessage.MonsterAi, MonsterAi.WeaponPriority |
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
| 0xE600 | game start-up: VM zones, start values, tables | W | play | Su | C#, spec | PorGame.NewGameScript, PorGame.NewGameState, Program |
| 0xEBE8 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0xEC5B–0x12508 (stub 41): effect handlers (127 routines: W 127 (127 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0xEC5B | CS:1C8D of unit | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | PorTextCatalog, EffectHandlers.More, EffectHandlers.Holds |
| 0xEC7A | see `spells-effects.md` Data-segment cells | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.AddEffectIfNoLock, Cure.CureDisease |
| 0xECB3 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | PorTextCatalog, CombatChooser.AttackEnd, CombatChooser.EffectMessage |
| 0xED68 | effect handler 1 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xED78 | effect handler 2 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xED96 | effect handler 3 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xEDDA | effect handler 4 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0xEE1E | effect handler 6 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xEE8E | effect handler 7 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.TrainingManual |
| 0xEFD2 | effect handlers 8, 45 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF007 | effect handlers 9, 46 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF03C | effect handler 10 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF05B | effect handler 11 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatScreen.Icon, EffectHandlers.CallHandler, EffectHandlers.Charm |
| 0xF115 | effect handlers 12, 38 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.StrengthEffect |
| 0xF231 | effect handler 14 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF246 | effect handler 15 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler, EffectEvent, Cure.CurePoison |
| 0xF2A2 | effect handler 16; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0xF2A9 | effect handler 17 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF2D4 | effect handler 18 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF33F | effect handler 20 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF36A | effect handler 21 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF3D0 | effect handler 22 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler, Cure.CurePoison |
| 0xF43B | effect handler 23 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler, EffectHandlers.SpiritualHammer |
| 0xF573 | effect handler 25 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF5A2 | effect handler 26 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF61B | effect handler 28 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF698 | effect handler 29 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF6C0 | effect handler 30 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler, EffectHandlers.TerrainCheck |
| 0xF776 | effect handler 32 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler, Cure.RaiseDead |
| 0xF807 | effect handler 33 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF82A | effect handler 34 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF861 | effect handler 35 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF870 | effect handler 36 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF881 | effect handler 37 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF8A6 | effect handler 39 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xF91E | effect handler 40 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.CloudGone |
| 0xFC07 | see `spells-effects.md` Missile dodge | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.DodgeMissile |
| 0xFC78 | see `spells-effects.md` Handlers 1–72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.MissileOf |
| 0xFCE1 | effect handler 41 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFD1C | effect handler 42 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFD39 | effect handler 43 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFDC3 | effect handler 44 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler, EffectEvent |
| 0xFE54 | effect handler 47 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFED1 | effect handler 48 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFF0D | effect handler 49 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFF56 | effect handler 50 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFF9A | effect handlers 31, 51, 52, 53 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler, EffectHandlers.TerrainCheck |
| 0xFFB0 | effect handler 54; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0xFFB7 | effect handler 55; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0xFFBE | effect handler 56 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0xFFF4 | effect handler 57 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0x100AD | effect handler 58 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0x100CD | effect handler 59 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0x100EB | effect handler 61 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0x10146 | effect handler 62 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHandler |
| 0x101A0 | see `spells-effects.md` Handlers 1–72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | PorTextCatalog, EffectHandlers.PoisonAttack, EffectEvent |
| 0x10246 | see `spells-effects.md` Handlers 1–72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers, EffectHandlers.ParalysingHit |
| 0x102B8 | effect handler 64 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers |
| 0x102CC | effect handler 65 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers |
| 0x102E0 | effect handler 66 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers |
| 0x102F4 | effect handler 67 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers |
| 0x10316 | effect handler 68 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers |
| 0x10341 | effect handler 69 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers |
| 0x10366 | effect handler 70 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers |
| 0x1037A | effect handler 71 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers |
| 0x1038B | effect handler 72 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers |
| 0x103CA | effect handler 75 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.BloodDrain |
| 0x10510 | effect handler 76 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers |
| 0x10590 | effect handler 77 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers |
| 0x10634 | effect handler 78 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x10674 | effect handler 79 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x106A7 | effect handler 80 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x10705 | effect handler 81 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.FearAura |
| 0x10803 | effect handler 82 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallHoldHandlers, EffectHandlers.ParalysingAura |
| 0x10908 | effect handler 83 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatMsg, EffectHandlers.CallHoldHandlers, EffectHandlers.Gaze |
| 0x10AD2 | effect handler 84 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatMsg, EffectHandlers.CallHoldHandlers, EffectHandlers.CharmGaze |
| 0x10C5D | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | EffectHandlers.LevelDrain, EffectHandlers.TopClass |
| 0x10D7D | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | PorTextCatalog, EffectHandlers.CallHoldHandlers, EffectHandlers.LevelDrain |
| 0x10FFF | effect handler 85 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | EffectHandlers.CallHoldHandlers |
| 0x1101D | effect handler 86 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | EffectHandlers.CallHoldHandlers |
| 0x11047 | effect handler 87 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x110EB | effect handler 89 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11134 | effect handler 90 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.ConSaveBonus |
| 0x111B1 | effect handler 91 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x111FD | effect handler 93 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11217 | effect handler 94 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1126B | effect handler 95 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x112A6 | effect handler 96 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x112E5 | effect handler 97 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.ConSaveBonus |
| 0x1135B | effect handler 98 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1138B | effect handler 99 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11434 | effect handler 100 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1146F | effect handler 101 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x114C1 | effect handler 102 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1151A | effect handler 103 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers, EffectEvent |
| 0x1155C | effect handler 104 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11570 | magic resistance / caster-level check (handlers 105, 106) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CasterLevel, EffectHandlers.MagicResistance |
| 0x115D1 | effect handler 105 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x115DF | effect handler 106 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x115ED | effect handler 107 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11611 | effect handler 108 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11626 | effect handler 109 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers.CallMoreHandlers |
| 0x11634 | effect handler 110 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers.CallMoreHandlers |
| 0x1164B | effect handler 111 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers.CallMoreHandlers |
| 0x1166C | effect handler 112 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers.CallMoreHandlers |
| 0x11683 | effect handler 113 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x116C8 | effect handler 114 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x116E2 | effect handler 115 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11741 | effect handler 116 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x1177B | effect handler 117 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x117BE | effect handler 118 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x117D8 | effect handler 119 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11823 | effect handler 120 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11877 | effect handler 121 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatMsg, EffectHandlers.CallHoldHandlers, EffectHandlers.AcidBreath |
| 0x11993 | effect handler 122 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x119E3 | effect handler 123 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11A3F | effect handler 124 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers.CallMoreHandlers |
| 0x11A63 | effect handler 125 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C# | EffectHandlers.CallMoreHandlers |
| 0x11A92 | effect handler 126 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11B35 | effect handlers 128, 129, 130, 133, 134, 136, 138, 139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11BB2 | effect handler 131 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers, EffectHandlers.Gauntlets |
| 0x11D00 | effect handler 132 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11D8F | effect handler 135 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.CallMoreHandlers |
| 0x11DD5 | effect handler 137 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers |
| 0x11DF6 | effect handlers 5, 13, 19, 24, 27, 60, 63, 92, 127; empty routine (unit init or empty handler) | W | play | Pm Ex Ecl Cb Cp Sh Kd | C#, spec | EffectHandlers.EmptyHandlers |
| 0x11DFD | see `por-art-formats.md` Amiga program reads (s19; hunks numbered from 0 in file order) | W | play | Su | oracle, C#, spec | EffectHandlers.Holds, EffectHandlers.LightningBreath, EffectHandlers |
| 0x12504 | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x127FB–0x1646C (stub 68): combat rules: round start, attacks, moves, turn undead, AI casts (39 routines: W 39 (37 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x127FB | start of round (per creature) | W | play | Cb | oracle, C#, spec | PorTextCatalog, FightRules.FindRangedAmmo, FightRules.BeginRound |
| 0x1291E | see `combat-format.md` Start of round | W | play | Cb | oracle (caller), C#, spec | FightRules.CreatureMovement, MonsterAi.HighestEnemyMovement, FightMoves.Flee |
| 0x1298C | see `combat-format.md` Damage | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.WeaponDamage |
| 0x12AF4 | see `combat-format.md` What a hit does | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Say, CombatChooser.AttackMessage, CombatChooser.AttackEnd |
| 0x12E22 | see `combat-format.md` Attacks | W | play | Cb | oracle (caller), C#, spec | CombatChooser.GuardAttacking, FightSeams, Fight.GuardAttacking |
| 0x12F0D | one-square move (cost, step) | W | play | Cb | oracle, C#, spec | CombatChooser.Stepped, FightMoves.MoveOne, FightSeams |
| 0x130AD | see `combat-format.md` Open `[?]` | W | play | Cb | oracle, C#, spec | FightMenu.Move, FightSeams, IFightMoves.IFightMap |
| 0x13465 | see `combat-format.md` Open `[?]` | W | play | Cb | oracle, C#, spec | CombatChooser.FleeMessage, CombatMsg, MonsterAi.MoveStep |
| 0x1351E | see `combat-format.md` Start of round | W | play | Cb | oracle (caller), C#, spec | FightRules.RecalcAttacksLeft |
| 0x1364D | see `combat-format.md` Start of round | W | play | Cb | oracle (caller), C#, spec | FightRules.AttacksFromRate |
| 0x1367D | see `combat-menu.md` The player's combat menu, sweep attacks and turn undead (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatMsg, MonsterAi.MoveAttack, IFightMoves.IFightMap |
| 0x13880 | see `combat-menu.md` Combat record and DS cells used here | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | MonsterAi.CanTarget, FightMenu.TriggerTargetDefender, FightMenu.Targetable |
| 0x13965 | see `combat-format.md` Attacks | W | play | Cb | oracle, C#, spec | CombatMsg, MonsterAi.TryTurnUndead, TurnUndead |
| 0x13B4C | see `combat-menu.md` Turn undead , cleric) | W | play | Cb | oracle, C#, spec | MonsterAi.TryTurnUndead, TurnUndead.NextTarget |
| 0x13C01 | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | RollAttacks, Fight.MakeAttack, EffectHandlers.Recalculate |
| 0x13FF1 | see `combat-format.md` Attacks | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | MonsterAi.MoveAttack, Fight.NoteAttackDirection, FightMoves.GuardsAttack |
| 0x1407F | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Say, CombatChooser.AttackStarting, MonsterAi.MoveAttack |
| 0x14319 | see `combat-format.md` Range penalty | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.RangePenalty |
| 0x143F3 | see `combat-ai.md` Items, spells, turning | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Ai, FightSpells.MaxHp, FightSpells.CureTarget |
| 0x14611 | see `combat-menu.md` The player's aim for a spell | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatChooser.AimItem, FightSpells.Ai, FightSpells.FindCureTarget |
| 0x148BE | see `combat-menu.md` The player's aim for a spell | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Ai, FightSpells, FightSpells.SquareOf |
| 0x14C0F | AI: spell choice and cast | W | play | Cb | oracle, C#, spec, `[?]`×1 (ids above 67) | FightSpells.CombatCastStep, FightMenu.Cast, FightSpells.CastAuto, FightSpells.Castable, CombatChooser.ChooseSpell |
| 0x14D6F | see `combat-format.md` Backstab | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.IsBackstab |
| 0x14E38 | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec, `[?]`×1 | NoFightEffects.IFightGeometry, IFightGeometry, FightRules.IsBackstab |
| 0x14EAB | see `combat-menu.md` A missile | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FlightAnimation.Missile, Fight.MissileFlight, Fight.MakeAttack |
| 0x15074 | see `combat-format.md` Enemy hit points | W | play | Cb | oracle, C#, spec | UpdateHostileHpPercent, Fight.EndOfRound, FightState.HostileHpPercent |
| 0x15103 | see `combat-map.md` Moves (stub | W | play | Cb | oracle, C#, spec | MonsterAi.HighestEnemyMovement, FightMoves.Flee, FightMoves.HighestEnemyMovement |
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
| 0x1638E | Alt-X debugging switch | W | play | Cb | oracle, C#, spec | PorTextCatalog.Overlay, GameAction, CombatChooser.Notice, CombatMap.ClearIconSize (s51 decision: original bug fixed, a hostile with no slot is skipped instead of zeroing the slot count DS:5F2A) |
| 0x16468 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x166CB–0x17776 (stub 72): explore: doors, steps, commands (14 routines: W 14 (8 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x166CB | see `geo-format.md` Barriers (plane 3) | W | play | Ex | C#, spec | PorTextCatalog.Overlay, AreaMessage, AreaLoop.DoorMenu |
| 0x167D4 | see `geo-format.md` Barriers (plane 3) | W | play | Ex | oracle (caller), C#, spec | AreaLoop.DoorMenu, DoorMap.SetFace, DoorRules.Knock |
| 0x16901 | some party member has a level in class k (door menu) | W | play | Ex | oracle, spec | — |
| 0x1695F | door Bash | W | play | Ex | oracle, C#, spec | DoorRules, DoorRules.Bash |
| 0x16BAA | door Pick | W | play | Ex | oracle, C#, spec | DoorRules, DoorRules.Pick |
| 0x16C76 | see `geo-format.md` Door menu | W | play | Ex | oracle (caller), C#, spec | DoorMember.SlotOf |
| 0x16CB9 | some party member has spell n memorised (door menu, via 0x16C76) | W | play | Ex | oracle, spec | — |
| 0x16D13 | door Knock | W | play | Ex | oracle, C#, spec | AreaLoop.DoorMenu, DoorRules, DoorRules.KnockSpell |
| 0x16D58 | see `ecl-format.md` Forward key | W | play | Ex | C#, spec | StepAction.ForwardKeyAction, Attempt.PorEncounterData, AreaCommand |
| 0x16E06 | see `ecl-format.md` Steps, doors and script switches (program read s11) | W | play | Ex | C#, spec | GameClock, IAreaUi.AreaLoop, AreaLoop.Move |
| 0x16EE3 | see `geo-format.md` Wilderness (overland) | W | play | Ex | C#, spec | AreaLoop.Step, Overland, Overland.Step |
| 0x17054 | explore command routine (menu keys) | W | play | Ex | C#, spec | ExploreScreen.RunMenu, AreaLoop, IAreaUi.AreaLoop |
| 0x17438 | party step / door menu / turn | W | play | Ex | C#, spec | RunVectorAction.StepAction, Attempt.PorEncounterData, ExploreScreen.Run |
| 0x17772 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x17831–0x1980E (stub 75): camp: Memorize, Scribe, preparation time, party order (23 routines: W 23 (13 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x17831 | see `camp.md` The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3) | W | play | Cp | oracle, C#, spec | CampSpells, CampSpells.Check, CampSpells.PrepMinutes |
| 0x1798A | see `camp.md` Magic menu | W | play | Cp | oracle, C#, spec | CampSpells, CampSpells.Check, CampSpells.ResetMemorise |
| 0x179CD | see `camp.md` Magic menu | W | play | Cp | oracle, C#, spec | CampSpells, CampSpells.Check, CampSpells.ResetScribe |
| 0x17A45 | see `camp.md` Camp menu | W | play | Cp | oracle, C#, spec | CampSpells, CampSpells.Check, CampSpells.ResetParty |
| 0x17A8D | see `camp.md` Magic menu | W | play | Cp | oracle, C#, spec | SpellLists.SlotBox, CampSpells, CampSpells.ResetParty |
| 0x17B9D | see `camp.md` Check | W | play | Ex Cp | oracle, C#, spec | RestUi.Allowed, RestUi.CastMenu, RestUi.Memorise |
| 0x17CD2 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Ex Cp | C#, spec | GameAction, SpellLists.CampScreens, RestUi.CastMenu |
| 0x17DD5 | see `camp.md` Magic menu | W | play | Cp | oracle (caller), C#, spec | ListRequest.SpellPageLayout, SpellLists.SlotBox, RestUi.SlotMessage |
| 0x17FAB | see `camp.md` Camp menu | W | play | Cp | C#, spec | SpellLists.CampScreens, CampScreens.Camp, CampScreens.Save |
| 0x18055 | see `camp.md` Magic menu | W | play | Cp | oracle, C#, spec | CampSpells, CampSpells.ResetParty, CampSpells.Sort |
| 0x1813B | see `camp.md` Memorize | W | play | Cp | oracle, C#, spec | SpellLists.CampScreens, RestUi.Memorise, CampSpells |
| 0x1838C | see `camp.md` Scribe | W | play | Cp | oracle, C#, spec | SpellLists.CampScreens, RestUi.Scribe, CampSpells |
| 0x18668 | Display list (0x187ED): append an entry (the text's first 40 bytes, flag +0x29) at the tail | W | play | Cp | C#, spec | RestUi.Display, CampSpells |
| 0x187ED | see `camp.md` Display | W | play | Cp | C#, spec | SpellPageLayout.CentredTitle, GameText._effectLabels, SpellLists.CampScreens |
| 0x18C22 | see `camp.md` Camp menu | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.ClearMessages, RestUi.Magic |
| 0x18CDA | see `camp.md` Alter | W | play | Cp | oracle, C#, spec | RestUi.Order, CampParty, CampParty.MoveUp |
| 0x18E32 | see `camp.md` Alter | W | play | Cp | oracle, C#, spec | RestUi.Order, CampParty, CampParty.MoveDown |
| 0x18FB1 | see `camp.md` Order | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Order |
| 0x19116 | see `camp.md` Drop | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Drop, CampParty |
| 0x192C8 | see `camp.md` Speed | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.Speed, CampParty |
| 0x1948A | see `camp.md` Camp menu | W | play | Cp | C#, spec | SpellLists.CampScreens, RestUi.PicsLine, RestUi.Alter |
| 0x196A0 | camp menu | W | play | Cp | C#, spec | PorProgram.CampMenu, VenueScreens, VenueScreens.TakeItems |
| 0x1980A | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x1997F–0x1DDD7 (stub 78): party menu, creation, Modify, training hall, icon editor (19 routines: W 18 (12 by execution), N 1)

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
| 0x1CE5E | icon editor: preview draw (recoloured icon) | W | pres | Pm | C#, spec, seen only | DrawIcons |
| 0x1CF3D | icon recolour into slot 12 from the record | W | pres | Pm | C# (inside), spec | CombatArt |
| 0x1D1AC | icon editor | W | pres | Pm | oracle, C#, spec | Program, CharacterPageRequest.IconRequest, PartyMenuFlow.EditIcon |
| 0x1D8B0 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | CharacterModify.HpMinimum, CharacterCreation.Roll, CharacterCreation.ConHpBonus |
| 0x1D932 | see `start-menu.md` Modify | W | play | Pm | oracle, C#, spec | CharacterModify.HpMinimum |
| 0x1DA0B | see `start-menu.md` Modify | W | play | Pm | oracle, C#, spec | CharacterModify.HpMaximum |
| 0x1DBB4 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | CharacterCreation.Roll, CharacterCreation.RollFirstLevelHp, Entry.HitDice |
| 0x1DC6C | see `training.md` Train Character | W | play | Lv | oracle (caller), C#, spec | Entry.Train, Entry.Pay |
| 0x1DDD3 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x1DFE6–0x203CA (stub 7C): roster and save files, monster loader (14 routines: W 12 (1 by execution), N 2)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x1DFF6 | see `start-menu.md` Add | W | play | Pm | C#, spec | PorRoster, PorRoster.Mark, PorRoster.Entries |
| 0x1E19E | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm | C#, spec | PorRoster.WriteEntries |
| 0x1E294 | save directory check: MkDir of the save path DS:537A, IOResult other than 0 / 5 → the save-disk prompt (drive DS:537B), again | N | play | Pm Sv | spec | — |
| 0x1E315 | icon picture build: head ORed into the body (EGA OR / AND) | W | pres | Pm Ecl Sv | C#, spec | CombatArt.Party, CombatArt.Or |
| 0x1E468 | combat icon: builds the record's icon in slot 0xBF through scratch slot 11 | W | pres | Pm Ecl Sv | C#, spec | CombatArt.Party, CombatScreen.Icon |
| 0x1E684 | character save (roster file) | W | play | Pm | C#, spec | PorRoster, PorRoster.Save, HallTrainer.HallHooks |
| 0x1EAD2 | character load (roster file) | W | play | Pm | C#, spec | PorRoster, PorRoster.Load |
| 0x1EEAA | monster loader (MON<d>CHA / ITM / SPC) | W | play | Enc | C#, spec | EncounterHost.LoadMonsterRecord |
| 0x1F248 | see `ecl-format.md` ADDNPC v1, v2 | W | play | Ecl | C#, spec | PorProgram.NpcPortraitBases, EncounterHost.CreateNpc, IEncounterData |
| 0x1F42C | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ecl Sv | C#, spec | PorParty.RefreshOnLoad, HallScreens.Run, CombatScreen.Icon |
| 0x1F5E8 | game load (from the party menu) | W | play | Sv | C#, spec | PorRoster.RemoveLoaded (option, s61), PorTextCatalog, PorGame.LoadAtPartyMenu, PorGame.Loaded, PorGame.ReloadSavedMap (s48: a hall Load reloads the marker slots) |
| 0x1FD3E | game save | W | play | Sv | C#, spec | PorRoster.RemoveSaved (option, s61), PorSaveGame.Build, PorParty.Save, PorGame.UnstartedSave |
| 0x202BB | NEWECL helper: when ECL<disk>.dax is missing, the save-and-exit / insert-disk menu; S steps the party back one square, saves and quits | N | play | Ecl | spec | — |
| 0x203C6 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2069D–0x20F97 (stub 82): end game (4 routines: W 4 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x206A3 | end-game animation | W | pres | End | C#, spec | DelayRequest.Chained, EndGamePicture.EndGameAnimation, EndGame.FrameTime |
| 0x20941 | end game | W | play | End | C#, spec | PorTextCatalog, HallScreens.Learn, ClientPlayer.ProgramCamp |
| 0x20D50 | the land recovers after Yarash: river tiles recoloured, animated pan (script switch, Load) | W | play | Ex Sv | C#, spec | OverlandDocument.Recovery, PorGame.LoadRecovery, PorOverlandRecovery |
| 0x20F93 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2103A–0x23B4B (stub 85): items: View, Items page, Trade, use, halve / join (21 routines: W 21 (18 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x21084 | see `start-menu.md` 3. Create | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | PickRequest.SheetRequest, PartyMenuFlow.PortraitShown, PartyMenuFlow.View |
| 0x21547 | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | ClientResources.Gender, CharacterView.Run, CharacterView.DropMoney |
| 0x21642 | see `start-menu.md` After every key but Exit | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CharacterView.Run, CharacterView.TradeMoney, CombatChooser.ChooseView |
| 0x2193B | see `start-menu.md` Modify | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatSheet, CombatSheet.Build, DrawSheet |
| 0x21AED | View menu (character page) | W | play | Pm Ex Cb Cp Sh | C#, spec | Program, GameAction, HallScreens.Run, SpellLists.ShowMemorised, PartyMenuFlow.View |
| 0x21DAA | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CharacterView, CharacterView.ProgramItems, CharacterView.SellOnPage |
| 0x21F32 | View's Items page (Ready, Use, Trade, Drop, Halve, Join, Sell, Id) | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | Program, CombatChooser.Choose, SheetRequest.CharacterPageRequest, ItemCommands.Ready, ItemCommands.Drop, CharacterView.ProgramItems, TradeContext.ItemHandler, TradeContext.ItemEffects |
| 0x2250A | Ready / unready an item (refusals: cursed, class, slot, hands; item handler 0x2AEEA) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CombatChooser.Attach, PartyMenuFlow.View, CharacterView, ItemCommands.Ready (s51: the slot-taken words name the item in the slot; hands full quiet for a computer-controlled creature in a fight) |
| 0x2276E | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | PorTextCatalog.Overlay, CharacterView, CharacterView.TradeItem |
| 0x22827 | see `camp.md` Halve | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CharacterView, CharacterView.Halve, CombatMsg |
| 0x228D7 | see `camp.md` Halve | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CharacterView, CharacterView.ProgramItems, FightItemScreens.Use |
| 0x22AC4 | see `camp.md` Magic menu | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CombatMsg, TradeContext.UseItem, CombatChooser.ChooseScrollSpell |
| 0x22D28 | see `camp.md` Sell and Id over the Items page | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CharacterView.SellOnPage, TradeContext.TalkOverItemsPage, Draw |
| 0x22F8C | shop Id box | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CharacterView.IdentifyOnPage, CharacterView.Identify, TradeContext.TalkOverItemsPage |
| 0x23190 | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ex Cb Cp Sh | C#, spec | PorTextCatalog.Overlay, PartyMenuFlow.Coins, CharacterView |
| 0x234E1 | see `start-menu.md` 4. Add, Remove, Drop, View | W | play | Pm Ex Cb Cp Sh | C#, spec | Program, SheetRequest.CharacterPageRequest, CharacterPageRequest.PickRows |
| 0x23795 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | TradeRules.GiveItem, ShopRules.CanCarry, ShopRules.GiveCopy |
| 0x23819 | party member selection (Home / End; any other key → the first member) | W | play | Pm Ex Ecl Enc Cb Cp Sh | oracle, C#, spec | PartySelection.Select |
| 0x238F8 | see `shop-format.md` Gold total | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | Money, Money.CharacterGold |
| 0x239D6 | spell list window (type, action) | W | pres | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | PorTextCatalog, HallScreens.Learn, CombatChooser.ChooseSpell |
| 0x23B47 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x23DCC–0x24C30 (stub 8C): effect timeouts, camp time, rest (15 routines: W 15 (14 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x23DCC | see `camp.md` The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3) | W | play | Ex Ecl Cb Cp | oracle, C#, spec | PorGame, GameClock.Advance, AreaLoop.EffectTimeout |
| 0x240BE | see `camp.md` Rest-time arithmetic | W | play | Ex Ecl Cb Cp | oracle, C#, spec | GameClock.Advance, GameClock.Normalise, CampState.CampTime |
| 0x2416F | see `camp.md` Rest → | W | play | Cp | oracle, C#, spec | CampTime, CampState.CampTime, CampTime.Validate |
| 0x241A1 | see `camp.md` Rest → | W | play | Ex Ecl Cb Cp | oracle, C#, spec | PorGame, GameClock, GameClock.Advance |
| 0x24246 | see `camp.md` Rest → | W | play | Cp | oracle, C#, spec | CampTime, CampState.CampTime, CampTime.Decrement |
| 0x24331 | rest-time two-digit number text | W | pres | Cp | oracle, C#, spec | CampTime.TwoDigits, CampScreens.RestUi.Two |
| 0x243BC | see `camp.md` Rest → | W | play | Cp | C#, spec | RestUi.Draw, CampTime.TwoDigits, ICampRestUi |
| 0x244ED | see `camp.md` Rest-time menu | W | play | Cp | oracle, C#, spec | ChoiceRequest.LeftAnswer, RestUi.ChooseRestTime, ICampRestUi |
| 0x2463D | see `camp.md` Rest → | W | play | Cp | oracle, C#, spec | RestUi.PartyHealed, CampState.HealTicks, ICampRestUi |
| 0x246F1 | see `camp.md` Rest → | W | play | Cp | oracle, C#, spec | ICampRestUi, CampRest.PrepTick, CampSpells |
| 0x247B3 | see `camp.md` Rest → | W | play | Cp | oracle, C#, spec | ICampRestUi, CampRest.PrepTick, CampSpells |
| 0x248D6 | see `camp.md` Rest → | W | play | Cp | oracle, C#, spec | CampState.PrepTimers, CampRest.Run, CampRest.PrepTick |
| 0x2497E | see `camp.md` Rest → | W | play | Cp | oracle, C#, spec | CampRest.Run, CampRest.PrepBaseTick, CampSpells |
| 0x24A66 | rest loop | W | play | Cp | oracle, C#, spec | TradeContext.HoldPositionLine, SpellLists.CampScreens, CampScreens.Save |
| 0x24C2C | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x24C89–0x26EE3 (stub 8F): money, treasure and loot (22 routines: W 21 (18 by execution), P 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x24C89 | see `character-format.md` Strength code and carrying allowance (program read s12) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | TradeRules.GiveCoins, Money, Money.CarryLimit |
| 0x24CA9 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightItemScreens.DropCoins, TradeRules.GiveCoins, Money |
| 0x24CC3 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | TradeRules.GiveCoins, Money |
| 0x24CDD | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | CharacterView.SellOnPage, Money, Money.Overloaded |
| 0x24D31 | see `shop-format.md` Gold total | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | Money, Money.PoolGoldTotal |
| 0x24DB9 | see `shop-format.md` Paying | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | Money, Money.SetCharacterGold |
| 0x24E0E | see `shop-format.md` Money | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | Money, Money.SetPoolGold |
| 0x24E8C | see `shop-format.md` Add platinum | W | play | Sh | oracle, C#, spec | Money, Money.AddPlatinum |
| 0x24F1D | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | C#, spec | PartyMenuFlow.Coins, AmigaView.Number, AmigaView.AmountLength |
| 0x250D5 | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | CharacterView.TradeMoney, PartyMenuScreen.HandOver, TradeRules.GiveCoins |
| 0x25191 | see `combat-format.md` Loot menu | W | play | Cb Sh | oracle, C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Pool, IdentifyResult.PoolMoney |
| 0x25269 | see `shop-format.md` Shop menu | W | play | Cb Sh | oracle, C#, spec | IdentifyResult.Share |
| 0x252C3 | see `combat-format.md` Loot menu | W | play | Cb Sh | oracle, C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Share, LootRules.ExtendedKey |
| 0x2568A | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh | C#, spec | CharacterView.DropMoney, FightItemScreens.DropMoney, FightItemScreens.DropCoins |
| 0x25711 | see `shop-format.md` Take | W | play | Cb Sh | oracle, C#, spec | MoneyScreens.Take, IdentifyResult.TakeMaximum, IdentifyResult.Take |
| 0x2581C | [?] number / money text builder | P | pres | Pm Ex Cb Cp Sh | C# | GameText |
| 0x25974 | see `combat-format.md` After combat | W | play | Cb Sh | C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Take, TakeSteps.Coins, MoneyScreens.TakeMaximum (s48: the maximum is the pool up to 65535) |
| 0x25C07 | see `combat-format.md` After combat | W | play | Cb Sh | oracle, C#, spec | TradeContext.PoolHasMoney, MoneyScreens.StayForMoney, VenueScreens.Shop |
| 0x25C5B | see `item-format.md` Types 1–59, 73, 77, 93 | W | play | Tl | oracle, C#, spec | TreasureMaker.PlusRoll |
| 0x25C99 | see `ecl-format.md` Encounters (program read 2026-11-17) | W | play | Tl | oracle, C#, spec | EncounterHost.ItemMaker, TreasureMaker |
| 0x2663B | see `combat-format.md` Shop | W | play | Sh | oracle, C#, spec | CoinTexts.MoneyScreens, MoneyScreens.Appraise, VenueScreens.Appraise |
| 0x26EDF | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x27018–0x2A7CC (stub 96): spells: cast, spell lists, spell routines 1–67 (74 routines: W 74 (72 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x270A2 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | HallScreens.Learn, ListRequest.Spell, ListRequest.SpellPageLayout |
| 0x272A5 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | FightSpells.Castable, ListKind.ScrollList |
| 0x27466 | scroll spell list (readable items) | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | CampSpells.ScrollList, CampSpells.List |
| 0x27534 | scroll lists of modes 3 / 6: clears DS:477F / DS:4843, 0x27466 per readable item | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), spec | CampSpells.List |
| 0x275B4 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh Lv | oracle, C#, spec | FightSpells.Castable, FightSpells.MemorisedList, MonsterAi.CastSpell |
| 0x27741 | see `combat-menu.md` Cast , from the menu with no spell) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells.AimAuto, FightSpells, FightSpells.Range |
| 0x277EE | see `spells-effects.md` Duration | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells, FightSpells.Duration |
| 0x278DB | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | PorTextCatalog, FightSpells, FightSpells.Around |
| 0x27AA9 | spell routine for spell 0 | W | play | Pm Ex Cb Cp Sh | C#, spec | MemberPick, RestUi.PickTarget, RestUi.UseItem, SpellGlobals.Ds6DB5 (s51: the remembered target on the game memory, kept across View / Use; cleared when its member leaves the party, an original bug fixed by decision: SpellGlobals.MemberLeft / PartyReplaced) |
| 0x27C38 | cast a spell (pick, aim, routine call) | W | play | Pm Ex Cb Cp Sh | oracle (out of a fight; the fight branch against the program's rules, Core by citation), C#, spec | CampCast.Run, FightSpells.CastNow, FightMenu.CastNow, LootRules.Detect (s51: the loot menu's D 0x6605) |
| 0x27F67 | see `spells-effects.md` Spells | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.CastOneSide, EffectTexts, EffectTexts.BySpell |
| 0x28027 | spell routine for spell 1 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x28058 | spell routine for spell 2 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x28083 | spell routine for spell 3 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | OutsideEffectMessages.Show, CombatChooser.SpellEvent, RestUi.WithCastMessages |
| 0x280BF | spell routine for spell 4 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x28101 | spell routine for spells 5, 11, 18, 22, 29 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | RestUi.LootDetect, TakeSteps.Detect, FightSpells.Routine |
| 0x2813B | spell routine for spells 6, 7, 16, 17, 52, 53, 54 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x2817A | spell routine for spell 8 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x281A8 | spell routine for spell 9 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x281F5 | spell routine for spell 10 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, FightSpells.CharmPerson, EffectTexts.BySpell |
| 0x282BD | spell routine for spell 12 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, FightSpells.Enlarge, FightSpells.Sleep |
| 0x28386 | spell routine for spell 13 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x283F0 | spell routine for spell 14 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28451 | spell routine for spell 15 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x284B1 | spell routine for spell 19 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x284DF | spell routine for spell 20 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x28533 | spell routine for spell 21 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, FightSpells.Sleep, EffectTexts.BySpell |
| 0x28670 | spell routine for spells 23, 49 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell |
| 0x28801 | spell routine for spell 24 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x2883A | spell routine for spell 25 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28873 | spell routine for spell 26 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, FightSpells.SlowPoison, EffectTexts.BySpell |
| 0x28927 | spell routine for spell 27 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, FightSpells.SnakeCharm, EffectTexts.BySpell |
| 0x289DB | spell routine for spell 28 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine |
| 0x28A2E | spell routine for spells 30, 50 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28A67 | spell routine for spell 31 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28AA2 | spell routine for spell 32 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28AE4 | spell routine for spell 33 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.Routine, EffectTexts.BySpell |
| 0x28B29 | spell routine for spell 34 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.StinkingCloud |
| 0x28F59 | spell routine for spell 35 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec, `[?]`×1 | FightSpells.MoreRoutine, FightSpells.Strength |
| 0x29087 | spell routine for spell 36 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine, FightSpells.AnimateDead |
| 0x2922F | spell routine for spell 37 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29275 | spell routine for spell 38 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x292A2 | see `spells-effects.md` Spells | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells.MoreRoutine, FightSpells.CureDisease, FightSpells.Spell58 |
| 0x29347 | spell routine for spell 39 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29364 | spell routine for spell 40 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x2939D | spell routine for spells 41, 46 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.DispelMagic |
| 0x294EE | spell routine for spell 42 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29558 | spell routine for spell 43 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | VenueScreens.Heal, FightSpells.MoreRoutine, FightSpells.RemoveCurse |
| 0x2961C | spell routine for spell 44 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29655 | spell routine for spell 45 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29683 | spell routine for spells 47, 64 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.FireballRangeCell, FightSpells.MoreRoutine, FightSpells.Fireball |
| 0x2977C | see `spells-effects.md` Spells | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | EffectTexts, EffectTexts.BySpell, FightSpells.OneSideRate |
| 0x298AA | spell routine for spell 48 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x298D4 | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightSpells.BoltHit |
| 0x29973 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | Body.LineWalk, FlightAnimation.Bolt, FightSpells.Bolt |
| 0x29BCF | spell routine for spell 51 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29C21 | spell routine for spell 55 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29C5B | spell routine for spell 56 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.RestorationResidueIndex |
| 0x29E1A | spell routine for spell 57 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29E65 | spell routine for spell 58 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine, FightSpells.Spell58 |
| 0x29EFD | spell routine for spell 59 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29F65 | spell routine for spell 60 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x29FB1 | spell routine for spell 61 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x29FE8 | spell routine for spell 62 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x2A042 | spell routine for spell 63 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x2A070 | spell routine for spell 65 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x2A0AA | spell routine for spell 66; empty routine (unit init or empty handler) | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | FightSpells.MoreRoutine |
| 0x2A0BA | spell routine for spell 67 | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | EffectTexts.BySpell, FightSpells.MoreRoutine |
| 0x2A0F1 | effect handler 88 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | PorTextCatalog, CombatMsg, EffectHandlers.CallHoldHandlers |
| 0x2A255 | see `combat-menu.md` View / Use | W | play | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | TradeContext.IsScroll, FightSpells.IsScroll, FightSpells.UseItemNow |
| 0x2A2A2 | see `camp.md` Rest → | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | FightSpells.UseItemNow, FightSpells.SpendItem, FightSpells.RemoveScrollSpell |
| 0x2A332 | see `combat-menu.md` The combat screen (Client) | W | pres | Pm Ex Cb Cp Sh | C#, spec | CombatMsg, CombatChooser.SayCast, RestUi.CastOne |
| 0x2A42B | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Su | oracle, C#, spec | RestUi.UseItem, FightSpells, SpellGlobals.Ds6DB5 |
| 0x2A7C8 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2AA87–0x2AEE9 (stub AC): level stats (5 routines: W 5 (5 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2AA87 | level stats (hit dice, saves, slots) | W | play | Lv | oracle, C#, spec, `[?]`×1 | ExperienceClass.LevelStatsDocument, PorParty.RefreshOnLoad, PorGame |
| 0x2AC36 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | CharacterModify.Lower, CharacterCreation.Roll, LevelStats.Apply |
| 0x2ACDC | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | PorSpellData.LevelTables, CharacterCreation.PickClass, LevelStats.Apply |
| 0x2ADB0 | see `start-menu.md` 3. Create | W | play | Pm Lv | oracle (caller), C#, spec | PorSpellData.LevelTables, CharacterCreation.PickClass, LevelStats.Apply |
| 0x2AEE5 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2AEEA–0x2C820 (stub B0): effect list: call handler, add / remove effect, dice, heal (24 routines: W 24 (23 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2AEEA | see `camp.md` View from the camp, shop, temple and loot menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | PorTextCatalog, PartyMenuFlow.View, PartyMenuFlow.TextOverPageFramed |
| 0x2AF10 | see `camp.md` Rest → | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | PorGame.OutsideEffects, CampScreens.Rest, IFightEffects |
| 0x2B04A | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | InAuraRange, EffectHandlers.CharmGazeRangeResidue, EffectHandlers |
| 0x2B1E4 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | BaseItemTable.IFightEffects, IFightEffects, FightRules.TriggerDamageAttacker |
| 0x2B9D1 | terrain effects (stinking cloud) after a step and at the end of round | W | play | Pm Ex Cb Cp Sh | oracle, C#, spec | IFightEffects, EndOfRoundCreature, FightMenu.Move |
| 0x2BB39 | see `combat-format.md` The fight | W | play | Kd | C#, spec | EclPartyRules.Hits |
| 0x2BBA1 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.AttackRoll, FightSpells.Resolve |
| 0x2BC4D | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers, EffectCells.SaveRoll, EffectHandlers.SavingThrow |
| 0x2BCCD | see `camp.md` Rest → | W | play | Pm Ex Ecl Enc Cb Cp Sh Tl Lv Kd | oracle, C#, spec | IFightGeometry.FightRules, FightRules.Dice, AiMessage.MonsterAi |
| 0x2BD18 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | FightRules.WeaponDamage, EffectHandlers.CallMoreHandlers, EffectHandlers.DamageDiceRoll |
| 0x2BD3C | see `start-menu.md` 3. Create | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.PoisonAttack, EffectHandlers, EffectHandlers.FindEffect |
| 0x2BDF0 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatChooser.FleeMessage, CombatMsg, CombatChooser.AiSays |
| 0x2BEBC | see `camp.md` Magic menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightRules.InvisibilityEffect, FightRules.AttackRoll, FightRules.RemoveInvisibility |
| 0x2BEF4 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×2 | CombatChooser.AttackEnd, CombatChooser.EffectMessage, FightRules.DroppedEffects |
| 0x2BF2B | see `combat-format.md` The fight | W | play | Cb | oracle, C#, spec | MonsterAi.MoraleEffects |
| 0x2BF6B | see `combat-format.md` The fight | W | play | Pm Ex Cb Cp Sh | oracle (caller), C#, spec | VenueScreens.Heal, FightSpells.Routine, FightSpells.RemoveWithMessage |
| 0x2BFCE | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.StrengthCode |
| 0x2BFF7 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.StrengthFromCode |
| 0x2C046 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | FightSpells.Enlarge, EffectHandlers.RaiseStrength, EffectHandlers.Gauntlets |
| 0x2C22E | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.AttackMade, CombatChooser.EffectMessage, CombatMsg |
| 0x2C540 | see `camp.md` Magic menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorTextCatalog, CombatChooser.AttackMade, CombatChooser.EffectMessage |
| 0x2C647 | see `camp.md` Heal | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CampScreens.Rest, FightSpells.Routine, EffectHandlers.CallHandler |
| 0x2C753 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | PorTextCatalog, OutsideEffectMessages.Show, CombatMsg |
| 0x2C81C | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x2C8CF–0x2DB18 (stub 3C5): menus and lists (menu routine, list window, yes / no) (20 routines: W 20 (12 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2C8CF | list window: entry k of a list | W | play | Pm Ex Ecl Cb Cp Sh Lv | oracle (caller), C# | ListWindowRoutine |
| 0x2C936 | list window: the list's length | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C# | ListWindowRoutine |
| 0x2C99A | menu routine: parse the menu words (digits and capitals start words) | W | play | Pm Ex Ecl Enc Cb Cp Sh Lv Sv | oracle (caller), C#, spec | KeyRequest.MenuEntries, Draw.WordCount, DrawLine |
| 0x2CAA9 | menu routine: draw the menu line | W | play | Pm Ex Ecl Enc Cb Cp Sh Lv Sv | oracle (caller), C#, spec | DrawLine |
| 0x2CC8C | menu routine (horizontal words) | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv | oracle (caller), C#, spec | PorAmigaText, Setup.PenSites, DosKeys.KeypadMenuScan |
| 0x2CFCA | see `start-menu.md` 1a. The introduction | W | play | Su Pm Ecl Cb Cp Sv | oracle (caller), C#, spec | CombatView.Row24Writes, CombatChooser.Attach, CombatChooser.BattleBegins |
| 0x2CFE0 | list window: last label at or above k (result unused by 0x2D073) | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C# | ListWindowRoutine |
| 0x2D073 | list window: the rows from the top | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C# | ListWindowRoutine, Draw.DrawRows |
| 0x2D1A6 | list window: first non-space index | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C# | ListWindowRoutine, Draw.Trimmed |
| 0x2D1F2 | list window: last non-space index | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C# | ListWindowRoutine, Draw.Trimmed |
| 0x2D238 | list window: the highlighted row | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C# | ListWindowRoutine, Draw.DrawLit |
| 0x2D2D7 | list window: the row unlit | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C# | ListWindowRoutine, Draw.DrawLit |
| 0x2D3D2 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Lv | oracle, C#, spec | SpellPageLayout.ListPaging, ListWindowRoutine, ListPage.Step |
| 0x2D502 | see `combat-menu.md` The combat screen (Client) | W | pres | Pm Ex Ecl Cb Cp Sh Lv | oracle, C#, spec | MenuHighlight.ListEntered, MenuHighlight.ListPaged, ListWindowRoutine |
| 0x2D594 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Lv | oracle, C#, spec | SpellPageLayout.ListPaging, ListWindowRoutine, ExploreScreen.ListAnswer |
| 0x2D669 | vertical list / item list window | W | pres | Pm Ex Ecl Cb Cp Sh Lv | C#, spec, `[?]`×1 | PartyMenuRequest.PickRequest, SheetRequest.CharacterPageRequest, PartyMenuFlow.Pick |
| 0x2D9A0 | yes / no question | W | pres | Pm Ex Cb Cp Sh Lv | oracle (caller), C#, spec | Calls.YesNoWarnPen, HallTrainer.YesNo, CombatChooser.YesNo |
| 0x2DA0F | [?] not read; called by 0x19EEB, 0x1C4E | W | play | Pm Ecl Sh | oracle (caller) | — |
| 0x2DAC2 | see `camp.md` Magic menu | W | play | Pm Ex Ecl Cb Cp Sh Lv | oracle (caller), spec | — |
| 0x2DB14 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2DB87–0x2DE2B (stub 3CA): 3D view / overland redraw (6 routines: W 6 (6 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2DB87 | see `geo-format.md` Marker | W | play | Ex | oracle (caller), C#, spec | AreaLoop.Redraw, AreaLoop.EnterCommandRoutine, AreaLoop.InterfaceMarker |
| 0x2DC4E | see `view3d.md` Backdrop | W | play | Ex | oracle (caller), C#, spec | BackdropRule.Horizon1 |
| 0x2DC82 | see `view3d.md` Backdrop | W | play | Ex | oracle (caller), C#, spec | BackdropRule.Horizon2 |
| 0x2DCB6 | see `view3d.md` Backdrop | W | play | Ex | oracle (caller), C#, spec | BackdropRule.Ground |
| 0x2DCF4 | 3D view / overland redraw (window, backdrop, marker) | W | pres | Ex | oracle (caller), C#, spec | ClientPlayer.ViewBoxBlank, ClientPlayer.DrawInterface, ClientPlayer.SwitchRedrawn |
| 0x2DE27 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2DE38–0x2E0BF (stub 3CD): overland view (3 routines: W 3 (1 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2DE38 | overland view draw | W | pres | Ex | oracle (caller), C#, spec | PorOverlandArt.DrawTiles, ClientPlayer.RecoveryView, DrawView |
| 0x2E03F | see `geo-format.md` Overland view, move and entry (program read s19; Core `Overland`, Data `PorOverlandArt`) | W | play | Ex | C#, spec | Draw, AreaLoop.OverlandLayout |
| 0x2E0BB | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2E0CE–0x2E892 (stub 3D0): pictures and sprites (loader, frames, free) (10 routines: W 10 (8 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2E0CE | picture frame draw | W | pres | Pm Ex Ecl Enc Cb Cp Sh Lv Sv End | spec | — |
| 0x2E11E | [?] not read; called by 0x2E765 | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller) | — |
| 0x2E13E | [?] not read; called by 0x2E765 | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller) | — |
| 0x2E170 | picture / sprite loader (frame table DS:6ABE) | W | pres | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller), C#, spec | PictureAnimation, ViewPicture.ViewPictures, ViewPictures.Find |
| 0x2E55D | picture free (frame table) | W | pres | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller), C#, spec | ViewPictures.DrawSprite |
| 0x2E5C7 | see `start-menu.md` Modify | W | play | Pm Ex Cb Cp Sh | oracle (caller), spec | — |
| 0x2E6A7 | see `combat-format.md` Shop | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller), spec | — |
| 0x2E765 | see `combat-format.md` Shop | W | play | Pm Ex Ecl Enc Cb Cp Sh End | oracle (caller), spec | — |
| 0x2E7D6 | sprite renderer (encounter close-up frame) | W | pres | Enc | C#, spec | ViewPictures.DrawSprite |
| 0x2E88E | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2E8F7–0x2FB01 (stub 3D5): 3D view drawing, wall sets (13 routines: W 13 (11 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2E8F7 | area map draw | W | pres | Ex | oracle (caller) | — |
| 0x2EA4C | view colours: backdrop bytes DS:6A60–6A63 | W | pres | Su | oracle (caller), C#, spec | Program, GameClock.BackdropRule |
| 0x2EA6B | 3D view: backdrop (sky / ground colours DS:6A60–6A63) | W | pres | Ex | oracle (caller), spec | — |
| 0x2EB08 | 3D view: DrawWallPiece | W | pres | Ex | oracle (caller) | — |
| 0x2EC16 | [?] not read; called by 0x2EC47, 0x2ED72 | W | play | Ex Enc Cb Sh | oracle (caller), C# | EncounterRules.ViewDistance |
| 0x2EC47 | see `combat-map.md` The wall between two squares | W | play | Ex Cb | oracle (caller), C#, spec | AreaMap, ArenaBuilder.WallBetween, DoorMap.FaceBarrier |
| 0x2ED72 | see `combat-format.md` CALL ids (handler | W | play | Ex Enc Cb Sh | oracle (caller), C#, spec | AreaLoop.ViewDrawn, AreaMap, EncounterHost.Call |
| 0x2EE71 | see `combat-format.md` CALL ids (handler | W | play | Ex Sh | oracle (caller), C#, spec | AreaLoop.EventHere, AreaMap, EncounterHost.StepForward |
| 0x2EEEA | 3D view: flush the screen buffer | W | pres | Ex | oracle (caller) | — |
| 0x2EEF4 | 3D view draw (Draw3DView) | W | pres | Ex | oracle (caller), C#, spec | ExploreScreen, AmigaMenuPens.DrawRoster, AreaLoop.ViewDrawn |
| 0x2F6FF | wall-set loader (WALLDEF<d>) | W | pres | Ecl Enc Sv | C#, spec | EclMemory.WallSetRequested |
| 0x2F9BE | see `ecl-format.md` Where the scripts live | W | play | Ecl Sv | C#, spec | Attempt.PorEncounterData, AreaLoop.LoadMap |
| 0x2FAFD | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x2FB52–0x3072F (stub 3DB): combat screen: aim, map draw (8 routines: W 8 (8 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x2FB52 | see `spells-effects.md` Handlers 73–139 | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatMap, FightSpells.LightningBreath |
| 0x2FB80 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | BuildTargetList, SortTargets |
| 0x2FCF2 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Flight, Body.LineWalk, LineWalk.Start |
| 0x2FE1E | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.Flight, Body.LineWalk, LineWalk.Next |
| 0x2FF74 | see `combat-menu.md` Aim | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | ManualView, ManualView.LineReached, FightMenu.Manual |
| 0x300CD | see `combat-menu.md` Cast , from the menu with no spell) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | FightSpells.CastNow, Body.IsDirToward |
| 0x3045B | see `combat-menu.md` Candidates | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | FightSpells.AimAuto, CombatCastOutcome.IFightTargetList, MonsterAi.AreaAround |
| 0x3072B | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x3073A–0x31A39 (stub 3E0): combat arena: cells, targets, icons, distance (23 routines: W 23 (22 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x3073A | see `combat-menu.md` The downed picture | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.HitMarkerSize, CombatScreen.DrawMap, CombatMap |
| 0x307A2 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), spec | — |
| 0x3081D | see `start-menu.md` Icon editor | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatPage.Grey, CombatScreen, DrawIcons |
| 0x3083F | see `combat-format.md` The fight | W | pres | Pm Ex Cb Cp Sh | C#, spec | CombatPage.Grey |
| 0x30861 | see `combat-map.md` Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C# (inside), spec | CombatArt |
| 0x30A0F | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.Scroll, CombatChooser.Downed, Fight.TurnEnded |
| 0x30ADA | see `combat-menu.md` Menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | EffectHandlers.PutOutOfPlay, CombatMap, CombatMap.Rebuild |
| 0x30BF8 | see `combat-menu.md` Aim | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | FightSpells.CureTarget, CombatMap.Contents, FightSpells.StinkingCloud |
| 0x30C65 | see `spells-effects.md` Spells | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.StepDrawing, EffectHandlers.PutOutOfPlay, Fight.StepDrawing |
| 0x30E23 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.InView |
| 0x30E54 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatChooser.DefenderTurned, CombatChooser.FacingDrawn, CombatChooser.AnyCellInView |
| 0x30F0E | see `combat-menu.md` View scrolling | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.Rebuild |
| 0x310D0 | see `combat-menu.md` Menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.Cursor, CombatView.Scroll, FightMenu.GodsIntervene |
| 0x31232 | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatView.Scroll, CombatChooser.Posed, CombatChooser.DefenderTurned |
| 0x31321 | see `combat-menu.md` Callers | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.PutBackOnSquare, CombatMap.Position, EffectHandlers.Fly |
| 0x31349 | see `combat-menu.md` Callers | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | EffectHandlers.PutBackOnSquare, CombatMap.Position, EffectHandlers.Fly |
| 0x31371 | see `spells-effects.md` Triggers | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | IFightTargetList, CombatMap.IconSize, AroundCreature |
| 0x31399 | see `combat-menu.md` Menu | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatMap.Slot, Body.ClearIconSize, EffectHandlers.SlotOf |
| 0x313F3 | see `combat-menu.md` Move | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | CombatMap.Probe, IsOffMap, TerrainUnder |
| 0x3154B | see `combat-format.md` Attacks | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec, `[?]`×1 | CombatView.HitMarker, CombatChooser.AttackMessage, CombatChooser.AttackEnd |
| 0x317E1 | see `combat-menu.md` Move | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle, C#, spec | FightMenu.Move, EffectHandlers.PutBackOnSquare, EffectHandlers.Revive |
| 0x319D2 | see `combat-format.md` The fight | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatView.CursorSize, CombatChooser.Centered, CombatChooser.TurnStarted |
| 0x31A35 | empty routine (unit init or empty handler) | W | play | Su | spec | — |

#### Unit 0x31A6C–0x320B0 (stub 3E9): tiles and icons (close-ups, area map) (6 routines: W 6 (4 by execution))

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x31A8E | see `combat-menu.md` The combat screen (Client) | W | play | Ecl Cb | C#, spec | OverlandPicture.PorOverlandArt, CombatPicture.CombatArt, CombatArt.RareFirstTile |
| 0x31B5D | overland / area-map tile draw | W | pres | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | OverlandPicture.PorOverlandArt, CombatArt.Ground |
| 0x31BBA | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ecl Cb Cp Sv Kd | oracle (caller), C#, spec | CombatArt.Party |
| 0x31C34 | see `start-menu.md` 3. Create | W | play | Su Pm Ex Ecl Enc Cb Sh Sv | C#, spec | CombatArt.Monster, CombatArt.Party, CombatArt.Draw |
| 0x31F68 | see `combat-menu.md` The combat screen (Client) | W | play | Pm Ex Ecl Cb Cp Sh Kd | oracle (caller), C#, spec | CombatScreen.Mirrored |
| 0x320AC | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x3211F–0x3221A (stub 3EE): text page (2 routines: W 1 (1 by execution), N 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x32123 | debug display (DS:49F7: `START STING` + Ctrl-D only) | N | pres | Pm Ex Ecl Cb Cp Sh Kd | — | — |
| 0x32216 | empty routine (unit init or empty handler) | W | play | Su | — | — |

#### Unit 0x32235–0x324E6 (stub 428): picture load with disk check (3 routines: W 2 (2 by execution), U 1)

| Address | Role | St | Kind | Paths | Evidence | Port (C#) |
|---|---|---|---|---|---|---|
| 0x32270 | [?] picture block load with disk check (wall sets, start-up) | U | pres | Su Ecl Enc Sv | spec | — |
| 0x32392 | wall / tile piece draw | W | pres | Su Pm Ex Ecl Cb Cp Sh Lv End Kd | oracle (caller), spec (inside) | — |
| 0x324E2 | empty routine (unit init or empty handler) | W | play | Su | — | — |

