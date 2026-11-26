# s52 lane E: explore, camp, party menu, roster / save, end game (W-by-citation rows)

Units: stub 72 explore (0x166CB–0x17776), stub 75 camp (0x17831–0x1980E), stub 78 party menu (0x1997F–0x1DDD7), stub
7C roster and save files (0x1DFE6–0x203CA), stub 82 end game (0x2069D–0x20F97). Each routine disassembled whole
(capstone on the EXEPACK-expanded image) and compared branch by branch with the C# it names and the spec.

Status: `exec` (oracle + Core test added), `read` (branch-by-branch read, port matches), `fixed` (difference fixed),
`bug?` (original bug, question below), `open` (not finished).

## Stub 72: explore (doors, steps, commands)

| Routine | Role | Status | Notes |
|---|---|---|---|
| 0x166CB | seal the party's face (barrier 0) | read | `DoorMap.Seal`: face bits cleared for facing 6/4/2/0, no bounds check (the party's own square) |
| 0x167D4 | set a face to 1 | exec | run inside Bash / Pick by `por_door_oracle.py` (`DoorMap.Open`: both faces, 0..15 bounds per square, no wrap) |
| 0x16901 | some member has a level in class k | exec | `por_door_oracle.py` "class" (signed byte 0x96 + k > 0) |
| 0x1695F | Bash | exec | `por_door_oracle.py` "bash" (barrier 2 / 3 tables, DS:6DDE, faces, RandSeed) |
| 0x16BAA | Pick | exec | `por_door_oracle.py` "pick" (1d100 per member, status 0, DS:6DDF := 0 after) |
| 0x16C76 | slot of spell n | exec | run inside 0x16CB9 / 0x16D13 by the door oracle |
| 0x16CB9 | first member with spell n memorised | exec | `por_door_oracle.py` "spell" (record pointer → party index) |
| 0x16D13 | Knock | exec + fixed + bug? | oracle "knock"; the routine only clears the slot, and its caller 0x17438 steps without setting a face: Core opened the map, now it does not (see question 1) |
| 0x16D58 | forward key (edge flag $6DD5) | read | `AreaLoop.ForwardKey`: $6DD5 := 0; face not 0 → step off 0..15 sets 1 (position clamped to the edge it is on) |
| 0x16E06 | the move | read | `AreaLoop.Move`: sound DS:2628, x / y + DS:2736 / 273F wrapped 0 ↔ 15, wall ahead 0x2ED72 → DS:6AB0, door flags reset (FillChar 3 × 1), event byte 0x2EE71 → DS:6AB1, clock 0x241A1(1, 2) with $6DCA bit 0 else (1, 1) |
| 0x16EE3 | overland step | read | `Overland.Step`: clamps 0..15 / 0..35, tile DS:3648 + y·44 + DS:3C76[mode] + x, impassable list, river with $4AA2 = 0 (message, colour 14), else $49C3 / $49C4 and 0x241A1(12, 3) |
| 0x17054 | command routine | read | `AreaLoop.Execute` / `MenuCommand` / `EnterCommandRoutine`: 3D keys A (toggle DS:6AAC unless $49FB), C (status 0x10C = 0), V, E, S ($6DCA ^ 1), L ($6DCA \| 2, 10 minutes, PC := vector 1), Up (0x16D58, ends), Down / Left / Right turn and loop (sound on Left / Right only), others 0x23819; overland: interface when DS:49F9 < 2 (ICON 0, 1, 2 → slots 9–11, START 0x36FE), C without the status test, arrows set the facing and end, other extended keys 0x23819; end: DS:49F9 := DS:49FA |
| 0x17438 | step / door menu | fixed | (a) Knock: no face set (above); (b) $6DC9 ≥ 255 is cleared only with DS:49FA 1–4 (Core cleared it in every mode); the rest matched (menu strings, seal when only Exit is left, barrier 3's Pick only clears DS:6DDF, bump sound when no step) |

## Stub 75: camp (Memorize, Scribe, preparation time, party order)

Oracle `por_camp_oracle.py` → `CampProgramTests` (600 cases; Memorize and Scribe whole with the spell list and the
questions scripted, Core's marks and resets replayed in the program's order; a deliberate change to `PrepMinutes` or
`MarkScribe` fails it).

| Routine | Role | Status | Notes |
|---|---|---|---|
| 0x17831 | preparation minutes | exec | `CampSpells.PrepMinutes` (byte sums, base hours 4 / 6 to 0x2C) |
| 0x1798A | pending memorised slots cleared | exec | `CampSpells.ResetMemorise` |
| 0x179CD | pending scroll bytes cleared | exec | `CampSpells.ResetScribe` (readable items only, 0x2A255) |
| 0x17A45 | both resets for the party | exec | `CampSpells.ResetParty` |
| 0x17A8D | slots left (level, class) | exec | `CampSpells.SlotsLeft` (a byte, wraps below 0) |
| 0x17B9D | activity check | exec | `CampSpells.Check`; messages 0x302 / 0x322 (+ the activity's word) |
| 0x17CD2 | camp Cast | read | `CampScreens.CastMenu`: DS:6DB5 cleared, check 1, list 0 action 1 until no pick; a pick: text box frame and inside, 0x27C38, position −1; empty list (flag 0) → message 0x489; START 0x3379 at the end when a list was drawn or a spell cast |
| 0x17DD5 | Memorize's slot lines | exec | run inside 0x1813B (its any-slot result decides the loop); lines per class with a slot, 2-character counts via 0x17A8D |
| 0x17FAB | Rest for preparation | read | `CampRest.RestForPreparation`: max of 0x17831 over the party, DS:6DC8 / 6DC6 / 6DC4 = hours, tens, minutes, 0x24A66, DS:6DC2..6DCF cleared, position line |
| 0x18055 | slot sort | exec | `CampSpells.Sort` |
| 0x1813B | Memorize | exec | pending list (5, 0) and question 0x8BF (N resets, Y ends); 0x17DD5 each pass (no slot → message 0x8D7, ends); the book (1, 2): a pick goes pending at the first 0 byte (unbounded walk) when 0x17A8D > 0, then sorted; at the end, when the book was shown, the pending list and question 0x8F2 |
| 0x1838C | Scribe | exec | as Memorize with lists 6 / 3 and questions 0xAB9 / 0xB45; a pick: known (0x32 + id) → row message 0xAE7; a pending byte for it on a readable item → 0xB03 (once per matching byte on that item; Core shows it once, presentation only); no slot byte → 0xB26; else the first byte equal to the spell on any item (readable or not) gets bit 7; empty list → member message 0xACF |
| 0x18668 | Display list append | read | (s51) 40-byte text, flag +0x29, at the tail |
| 0x187ED | Display | read | `CampSpells.Display` / `CampScreens.Display`: spell-effect and labelled ids match the program's compare chain; first spell 1..56 with table +10 equal; "none" when a member shows nothing |
| 0x18C22 | Magic menu | read | `CampScreens.Magic`: until the rest flag or a key in {0, 'E'}; extended keys 0x23819 + party list |
| 0x18CDA | Order: move up | exec | `CampParty.MoveUp` (the first goes to the end) |
| 0x18E32 | Order: move down | exec | `CampParty.MoveDown` (the last goes to the head) |
| 0x18FB1 | Order | read | `CampScreens.Order`: mode toggled by {Enter, P, S}; Select mode's extended keys → 0x23819; Place mode Home / End → 0x18CDA / 0x18E32; exits on {0, 'E'} |
| 0x19116 | Drop | read | `CampScreens.Drop` / `CampParty.Drop`: alone → question 0x1877, Y → 0x1CC10(0, 1) then START 0x8990 (quit); else message 0x1885, question 0x1892, 0x10D ≠ 0 → 0x18A4 else 0x18B6, then 0x1CC10(0, 1); N → 0x18CB |
| 0x192C8 | Speed | read | `CampScreens.Speed` / `CampSettings`: F / S unbounded but offered only inside 0..9; Down / Up bounded |
| 0x1948A | Alter | read | `CampScreens.Alter`: O, D, S, I (0x1D1AC + START 0x3379), P (Pics loop toggling DS:49F6 monsters / DS:49F5 portraits; Core keeps them as $49FF bits) |
| 0x196A0 | camp menu | read | `CampScreens.Camp`: DS:49F3 := 2 (restored at the end), DS:6DD8 := 0, rest time cleared, message 0x1E23, resets, menu until the rest flag or {0, 'E'}; S → 0x1FD3E then question 0x1E62 (Y quits); at the end resets, DS:6DB5 := nil |

## Stub 78: party menu (the four W-by-citation rows)

| Routine | Role | Status | Notes |
|---|---|---|---|
| 0x19AE0 | party menu / hall menu | read | `PartyMenuFlow.Run` / `PartyMenu.Enabled`: entry flags DS:642 + 0x2A·k (with a current member Drop, Modify, View, Remove, Save, Begin on, Load off, Train on when $6DA8 > 0 or DS:470C, never cleared in that branch; without one those off and Load on; Create / Add / Exit untouched); DS:5376 := 3, DS:470C := 0, DS:49F3 := 0 on entry; extended keys only Home / End ({'G', 'O'}) → 0x23819; every other key outside {'E', 'S'} clears DS:557D; Remove = 0x1E684 then 0x1CC10(0, 1); Load leaves when DS:49F3 ≠ 0 afterwards (DS:49F8 := 1); Begin sets DS:49F3 := 4; Exit's question, then Save until DS:557D, then quit; J compares ParamStr(1) |
| 0x1B8B6 | Drop | read | `PartyMenuFlow.Drop`: two questions, then 0x10D = 0 → the "dump" row message else the farewell, 0x1CC10(0, 1); any No → the relief message. The character file stays |
| 0x1C9EC | Add | read | `PartyMenuFlow.Add` / `PartyMenu.TryAdd` / `AddFull`: Return or A on an unmarked entry loads it (0x1EAD2), marks it "* ", empty party → $6E3E := 0 and join; else walk for name + 0xAB, counting PCs; PC joins below 6, NPC while $6E3E < 8; the loop ends on E / 0, more than 5 PCs or $6E3E > 7; list written back (0x1E19E). `[?]` A first pick of a marked entry (or another key) tests the PC count byte [bp−0x16] before anything set it (stack residue); Core recounts the party |
| 0x1CD66 | portrait pick | read | `PartyMenuFlow.Portrait`: H head 1..14, B body 1..12 (wrap to 1), K keeps; the loop leaves on key 0x4B, so the extended Left arrow (scan 0x4B) also leaves and puts the portrait back as it was on entry. The Client gives Left its menu meaning (moves the lit word; PLAN "(1) DOS": all menus left as is), so not followed |

## Stub 7C: roster, save files, monster loader

| Routine | Role | Status | Notes |
|---|---|---|---|
| 0x1DFF6 | LoadCharlist | read | `PorRoster.Entries`: `charlist.txt` lines (40 characters), a line equal to a member's name (whole string) gets "* " in front |
| 0x1E19E | SaveCharlist | read | `PorRoster.WriteEntries`: lines whose second character is not '*' written back; the save-disk prompt on an IO error (N by design) |
| 0x1E684 | SaveChar | read | `PorRoster.Save`: with no file name the list is re-read, the name appended and written, the file name from the name's clean-up (0x8376 / 0x8445, `[?]` in `PorRoster`) + `.cha`; with one (the game save) `.sav`; then `.itm` / `.spc` erased and written only when there are items / effects. The engine writes JSON per character by design (module format §15) |
| 0x1EAD2 | LoadChar | read | `PorRoster.Load`: the record (pointers 0x104, 0xC8, 0x7F, 0x108 cleared), then `.itm` (63-byte records) and `.spc` (9) when present. The program allocates the first item before testing EOF, so an empty `.itm` would give one record of stale bytes; the program never writes an empty one |
| 0x1EEAA | monster loader | read | `EncounterHost.LoadMonsterRecord`: icon size 1 for 0x2E ∈ {1, 3, 5}, 2 for {2, 4, 6, 7}; colours 0xC1–0xC3 and 0xC5–0xC6 = (d + 8)·16 + d for d = 1d6 each, 0xC4 = 0xC4; then the effects (SPC), then the items (ITM); a missing record quits to DOS (Core logs it) |
| 0x1F248 | ADDNPC's record | read | `EncounterHost.CreateNpc` + `EclVm.DoAddNpc`: $6E3E ≤ 7; Random(256) → 0xAB; classes 0..6 with a level (none → 2); 1d(count) picks one; body from DS:8D7 / 8EB / 8F5 / 8E1 + 5·0x9E + 1d5, head DS:8C7 + 8·0x9E + 1d8 (draw order as the program); join and icon |
| 0x1F42C | join | read | `PartyMenu.Join` / `DoAddNpc`: 0xBF := 0xFF, appended, current, the first free slot 0..7 (8 when all are taken), $6E3E + 1, level stats 0x2AA87. `PartyMenu.Join` leaves $6E3E to the game start (the party menu has no game memory) |
| 0x1F5E8 | game load | read | `PorGame.LoadAtPartyMenu` / `Loaded` / `ReloadSavedMap`, `PorParty.Load`: slot menu of the letters whose `savgam<l>.dat` exists (none → nothing; keys outside {0, A..J} or a missing file ask again; 0 leaves); the reads in `save-format.md` order; DS:49F6 / 49F5 := $49FF bit 1 / bit 0, DS:49E2 := $49FC; $6E3E := 0, then each stem's `.sav` that exists is loaded (0x1EAD2) and joined (0x1F42C, $6E3E + 1; a missing one is skipped); each member's roster files `<name>.cha/.itm/.spc` erased (not done, camp.md); icons for every member, the first current; DS:5376 := $6E12; DS:49FA < 2 → map $49C5 and the wall sets with id ≥ 0, else ICON 0 into slot 11 (fixed by decision s48); $4AA2 ≠ 0 → 0x20D50. Core takes $6E3E from the save rather than counting the files found (differs only when a `.sav` is missing) |
| 0x1FD3E | game save | read | `PorSaveGame.Build` / `PorParty.Save` / `CampScreens.Save`: slot menu A..J until a key in {0, A..J} (animated at the camp, DS:49F3 = 2), 0 leaves; mkdir, `savgam<l>.dat` (disk prompt on an IO error); "Saving..." on row 24; $4AFA–$4AFC / $4AFD–$4AFF := the wall-set cache, $49FC := DS:49E2, $49FF := 49F6·2 + 49F5, $6E12 := DS:5376; then the layout (stems `CHRDAT<l><n>` in a stack array: the bytes after each stem and the unused slots are stack residue, Core writes 0); each member through 0x1E684 with its stem (`.sav`, `.itm` / `.spc`), then its roster files `<name>.cha/.itm/.spc` erased (not done, the engine's roster is its own folder); DS:557D := 1 |

## Stub 82: end game

| Routine | Role | Status | Notes |
|---|---|---|---|
| 0x206A3 | end-game animation | read | `EndGame.FrameTime` / `EndGameAnimation`: frame k held until the clock moved more than (speed + 3) × delay (0x2076E jg / ja), frames 1..n wrap, n loops; block drawn at (column − 1, row − 1); frames freed at the end |
| 0x20941 | end game | read | `EndGame.Steps`: DS:49F3 := 7 (restored, START 0x3379 at the end); lines 17–19; FINAL block 1 at (1, 1); 0x206A3(3, 5, 2, ((10 − speed)·5) shr 1); block 3; blocks 4 / 5 / 6 at (1, 12) / (14, 10) / (1, 10) for $6E3E > 1 / > 2 / > 3; lines 17–22; spirit (3, 1, 7, loops); lines 17–20; (3, 1, 8, 1); three START 0x7E4E delays |

## Counts

exec 19 (stub 72: 7; stub 75: 12), read 30, fixed 1 (0x17438; 0x16D13 is exec with the fix in its caller), bug? 2
(0x16D13, 0x1C9EC, below; both rows also counted above), open 0. Of 50 W-by-citation rows: 50 settled.

## Code fixes

- `AreaLoop.DoorMenu`: Knock no longer sets the two faces (0x16D13 / 0x17438 set none; Bash and Pick do through 0x167D4).
- `AreaLoop.Step`: $6DC9 ≥ 255 is cleared only in modes 1–4, as 0x17438 (other modes leave it).
- `PorRoster.DosStem` (new): a DOS roster character's `.cha/.itm/.spc` is found by the program's name rule (START 0x8445 /
  0x8376: the ten characters " .*,?/\:;|" removed one by one, a match right after a removed one kept, 8 characters,
  a..z upper); before, names with a space or more than 8 characters were not found.

## Question for the user

1. **Knock leaves the door locked (0x16D13, caller 0x17438).** Bash and Pick open a locked door by setting both of its
   faces to passable (0x167D4) before the party steps through. Knock only clears the caster's Knock slot and the party
   steps through; the map keeps the lock, so the door menu comes back the next time the party walks into that face
   (and from the other side where that face is locked too), and another Knock (or Bash / Pick) is needed. To reach it:
   memorise Knock, walk into a locked door (barrier 2) or barred door (barrier 3, with a magic-user), choose Knock,
   then step back and walk into it again. Core now follows the program. Keep it, or fix it (Knock sets both faces as
   Bash and Pick do)?

2. **Add's first pick reads an unset count (0x1C9EC, 0x1CBE2).** The party-member count byte [bp−0x16] is set only
   when a name is loaded (0x1CAEB). If the first pick in Add is a name already marked "* " (a party member), or a
   key other than Return / A / E / Escape, the loop's end test reads the byte before anything wrote it: a stack
   residue above 5 ends Add at once, otherwise the list comes back. To reach it: with a party, choose Add and pick a
   marked name first. Core recounts the party's player characters there (so Add stays open unless 6 are present).
   Keep the recount (as a fix), or follow the program (the residue is not traced, so it would stay `[?]`)?

## Proposed ledger inputs (main thread)

`tools/scripts/ledger/cite.py` `ORACLE_O` (new oracles):

```python
            # s52 lane E: por_door_oracle.py, por_camp_oracle.py
            0x1695F: 'door', 0x16BAA: 'door', 0x16D13: 'door', 0x16901: 'door', 0x16CB9: 'door',
            0x17831: 'camp', 0x1798A: 'camp', 0x179CD: 'camp', 0x17A45: 'camp', 0x17A8D: 'camp', 0x17B9D: 'camp',
            0x18055: 'camp', 0x18CDA: 'camp', 0x18E32: 'camp', 0x1813B: 'camp', 0x1838C: 'camp',
```

`tools/scripts/ledger/build.py` `ORACLES`:

```python
    # s52 lane E: the barrier accessor answers the case's barrier
    'door': ([O_(x) for x in (0x1695F, 0x16BAA, 0x16D13, 0x16901, 0x16CB9)], [O_(0x2EC47)] + ALLOC),
    # s52 lane E: list, yes/no, messages, drawing, redraw trapped (por_items_oracle.py's set-up)
    'camp': ([O_(x) for x in (0x17831, 0x1798A, 0x179CD, 0x17A45, 0x17A8D, 0x17B9D, 0x18055, 0x18CDA, 0x18E32, 0x1813B, 0x1838C)],
             [O_(x) for x in (0x239D6, 0x2D9A0, 0x2D669, 0x2AEEA)]
             + [S_(x) for x in (0x228C, 0x2233, 0x3379, 0xFE2, 0x7D3B, 0x76B6, 0x77F8, 0x3F10, 0x4047, 0x41F8, 0x23B6)] + ALLOC),
```

`tools/scripts/ledger/overrides.py` (hand-checked rows, status W):

```python
    'O:166cb': dict(status='W', why='read whole s52 (DoorMap.Seal)'),
    'O:16d58': dict(status='W', why='read whole s52 (AreaLoop.ForwardKey)'),
    'O:16e06': dict(status='W', why='read whole s52 (AreaLoop.Move)'),
    'O:16ee3': dict(status='W', why='read whole s52 (Overland.Step)'),
    'O:17054': dict(status='W', why='read whole s52 (AreaLoop.Execute / MenuCommand)'),
    'O:17438': dict(status='W', why='read whole s52; Knock sets no face, $6DC9 cleared only in modes 1-4 (fixed)'),
    'O:17cd2': dict(status='W', why='read whole s52 (CampScreens.CastMenu)'),
    'O:17fab': dict(status='W', why='read whole s52 (CampRest.RestForPreparation)'),
    'O:18668': dict(status='W', why='read whole s51 / s52'),
    'O:187ed': dict(status='W', why='read whole s52 (CampSpells.Display)'),
    'O:18c22': dict(status='W', why='read whole s52 (CampScreens.Magic)'),
    'O:18fb1': dict(status='W', why='read whole s52 (CampScreens.Order)'),
    'O:19116': dict(status='W', why='read whole s52 (CampScreens.Drop, CampParty.Drop)'),
    'O:192c8': dict(status='W', why='read whole s52 (CampScreens.Speed)'),
    'O:1948a': dict(status='W', why='read whole s52 (CampScreens.Alter)'),
    'O:196a0': dict(status='W', why='read whole s52 (CampScreens.Camp)'),
    'O:19ae0': dict(status='W', why='read whole s52 (PartyMenuFlow.Run, PartyMenu.Enabled)'),
    'O:1b8b6': dict(status='W', why='read whole s52 (PartyMenuFlow.Drop)'),
    'O:1c9ec': dict(status='W', why='read whole s52 (PartyMenu.TryAdd); first-pick count byte [?]'),
    'O:1cd66': dict(status='W', why='read whole s52 (PartyMenuFlow.Portrait); Left arrow leaves in DOS, not followed (Client menu keys)'),
    'O:1dff6': dict(status='W', why='read whole s52 (PorRoster.Entries)'),
    'O:1e19e': dict(status='W', why='read whole s52 (PorRoster.WriteEntries)'),
    'O:1e684': dict(status='W', why='read whole s52 (PorRoster.Save; JSON files by design)'),
    'O:1ead2': dict(status='W', why='read whole s52 (PorRoster.Load)'),
    'O:1eeaa': dict(status='W', why='read whole s52 (EncounterHost.LoadMonsterRecord)'),
    'O:1f248': dict(status='W', why='read whole s52 (EncounterHost.CreateNpc, EclVm.DoAddNpc)'),
    'O:1f42c': dict(status='W', why='read whole s52 (PartyMenu.Join, EclVm.DoAddNpc)'),
    'O:1f5e8': dict(status='W', why='read whole s52 (PorGame.Loaded, PorParty.Load); roster-file erase not done'),
    'O:1fd3e': dict(status='W', why='read whole s52 (PorSaveGame.Build, PorParty.Save); roster-file erase not done'),
    'O:206a3': dict(status='W', why='read whole s52 (EndGame.FrameTime, EndGameAnimation)'),
    'O:20941': dict(status='W', why='read whole s52 (EndGame.Steps)'),
```
