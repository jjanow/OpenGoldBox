# s52 lane D: fight top level and round loop, monster AI, ECL loops, game start-up, camp time and rest

The W-by-citation routines of five program units (`routine-ledger.md`): stub 35 (0x9807–0xAA85), stub 38
(0xABC0–0xC4E6), stub 1F (0x0D46–0x4252), stub 3E (0xE5D3–0xEBEC), stub 8C (0x23DCC–0x24C30). Empty routines (0xAA81,
0xC4E2, 0xEBE8, 0x24C2C, 0x424E) and 0x9807 (N by hand) are already counted and left out: 43 rows.

Status: `exec` oracle / test added; `read` branch-by-branch read, port matches; `fixed` difference found and fixed;
`bug?` original bug (user question); `open` not finished.

## Rows

| Routine | Unit | Role | Status | Notes |
|---|---|---|---|---|
| 0x211D | 1F | CHKPARTY's four writes | read | 0x814A on the four destinations with CHKPARTY's values (the fourth sign-extended, 0/1 only); `EclPartyRules.CheckParty` + the VM write (golden captures cover CHKPARTY) |
| 0x3B19 | 1F | encamp | fixed | vectors 2 and 3 run through 0x3EB7 alone: a NEWECL there is not followed (no 0x3EFA); the flag stays set for the next command's vector 0 or a Look. Core had followed it at once (vectors 4, 0, 1 before the camp menu). Also PROGRAM 9 (0x3B56) calls 0x3B19 inside the running script: Core had run the camp menu alone, so vector 2 never ran there and an interrupted rest never ran vector 3 (ECL3 block 0 $A1AD, ECL7 block 17 $A5EF use it); the VM now runs vector 2 nested, the camp (`IEclHost.ProgramCamp`, the Client's camp menu), vector 3 when interrupted, then EXIT (EclVmHostTests). The redraw and DS:557D = 0 after it are not modelled (drawing). `[?]` Core still skips the camp menu after a destroyed party (DS:49FF) or a faulted run; the program opens it |
| 0x3C73 | 1F | ECL opcode dispatcher | read | the compare chain on DS:6F7F maps the opcodes to the handlers of the ledger's ECL table; an opcode with no case returns and the run loop executes the byte again (`EclVmPolicy`) |
| 0x3EB7 | 1F | ECL run loop | read | PC DS:49ED = vector; stop flag DS:442E = 0; until it is set or DS:49FF: previous opcode DS:6F7E = DS:6F7F, opcode = the byte at PC, dispatch; DS:442E = 0 at the end. `EclVm.RunFrom` (stop flag, ExternalStop for 49FF, previous opcode) |
| 0x3EFA | 1F | after a script switch | read | every branch matches `FollowScriptChanges` (event byte, $6DD5, saved current, DS:49F9/49FA, vector 4, blocks 25–27 with $49E6 = 0, the redraw rule, vector 0, vector 1 unless the flag is set again, current restored). One call not ported: with block 26, $4AB3 = 0xFE and $4AA2 = 0 it calls 0x20D50(1) (0x3F86; the ledger's U row, overland / picture set-up, presentation, not this lane's unit) |
| 0x403E | 1F | area loop | fixed | the command read that follows a camp or a Look (0x4172–0x418F) goes straight back to 0x40FF without writing $49F2 = the block (0x40F1); Core wrote it after every command. Rest read: vector 0 / step / redraw / sound / DS:84E0–84E1 / vector 1, the Look path (0x4132), the DS:49FF end |
| 0x9881 | 35 | the fight (top level) | read | DS:49F3 = 5, DS:6B84 = the AI pick, 0xE402, then rounds while both teams: START 0x2F7B (teams), 0x127FB for each creature, $6DCB = 0, the picks 0x9938 / turns 0x99FD, 0xA087; 0x9807 at the end. `Fight.Run` / `Round` (the fight_quick captures replay draw for draw) |
| 0x99FD | 35 | a creature's turn | read | +0x0F/+0x12/+7 = 0, trigger 7, initiative ≤ 0 → out, 20 → 19, DS:5D92, START 0x1758, trigger 0x0F, AI or menu: `Fight.Turn` matches. Presentation only: the program puts the view on the creature (0x319D2) and draws its panel before trigger 0x0F and redraws its cell whenever it got past the first test; Core's `TurnStarted` / `TurnEnded` fire only when initiative is still above 0 after trigger 0x0F |
| 0x9B21 | 35 | the player's menu | read | begun spell (+0 cleared first), then the key dispatch M V A U C T Q D, Alt-M, space (0x84 < 0x80 only, no status test), Alt-Q (initiative 20, every creature Quick), Alt-X: `FightMenu.Run` / `Perform` match |
| 0x9E6D | 35 | the main menu | read | trigger 0x0F, the entries and their conditions (`Entries`); extended keys kept only for 0x10, 0x13, 0x2D, 0x32 (CS:0x626), the loop ends on a key of CS:0x646 (0x10, 0x13, space, '-', '2', A C D M Q T U V) |
| 0xA087 | 35 | end of round | read | 0x241A1(1, 1), DS:6DE3 + 1, 0x15074, per creature trigger 0x13 + 0x2B9D1 + dying count, 0xA806(0) message, teams, the continue question unless DS:49F4: `Fight.EndOfRound` |
| 0xA1E6 | 35 | Move | fixed | after a step is taken (0x130AD, 0x12F0D, 0x2B9D1) the helpless test (START 0x1713) runs whether or not 0x12F0D moved, and only the terrain-cost refusal before the step shows its message; Core showed the message also when 0x12F0D failed for want of movement and then skipped the helpless test (a creature paralysed by a free attack kept its turn) |
| 0xA54C | 35 | attack by moving | read | ranged and not throwable → message; 0x15193; +0x0A; sweep (done) else 0x13FF1 + 0x1407F with its out flag as done: `AttackByMove` |
| 0xA64C | 35 | Done menu | read | Guard (not ranged, or throwable), Delay (+3 = 1), Quit, Bandage (0xA806(1) then actions end), Speed, Exit: `DoneMenu` |
| 0xA806 | 35 | bandage / dying | read | every party creature (+0x13 = 0, hostile 0, status 5) counts; with the flag the first is bandaged (status 4, +0x0E = 0, message): `Bandage` |
| 0xA8FD | 35 | speed menu | read | Slower below 9, Faster above 0, DS:49E2 ± 1 until Exit: `SpeedMenu` |
| 0xAA32 | 35 | Quick | read | 0x10F = 1; +0x0A cleared when its 0x10E equals the creature's: `MakeQuick` |
| 0xABCF | 38 | AI turn | read | every branch lines up with `MonsterAi.Turn` (key check, out of play, the opening dice always, morale unless the key ended it, the panic message, item / begun spell / turn undead / spell, weapon, the search + move/attack loop with 15FDF(0xFF, relaxed, keep) and initiative > 0 signed) |
| 0xADC3 | 38 | turn undead | read | +0x11 ≠ 0, cleric level ≤ 0 signed, no undead (0x13B4C) → no; else 0x13965: `TryTurnUndead` matches |
| 0xAFA4 | 38 | item use | exec | `por_ai_oracle.py` choose (item): 0xAEAB run, 0x22AC4 trapped and logged; RandSeed compared (AiCastProgramTests) |
| 0xB105 | 38 | spell | exec | choose (spell): 0xAEAB run, 0x14C0F trapped (scripted result); the pending ids (+0x80) are left out of the cases, as Core leaves them out by decision (s50) |
| 0xB235 | 38 | can step | exec | step cases (400): 0x313F3 and START 0x2199 run |
| 0xB3B2 | 38 | one step | fixed | the flee at the arena edge: 0x13465 returns START 0x31D7's result (always 1), so the step ends whether or not the creature got away; Core retried the flee (another message and 1d2 each time) until a guard of its own. Rest read: base direction, attempts, DS:443C/443D, 0x31232 → 0x130AD → 0x12F0D → 0x2B9D1 (`FightMoves.MoveAfterChecks`) |
| 0xB6FC | 38 | move and attack | read | every branch matches `MoveAttack` (20-pass finish, range, kept target only with 0x10E ≠ 0, 1d(count) pick, ranged weapon with an enemy adjacent, sweep, 0x13FF1 + 0x1407F with its out flag as "done"). DS:443E is zeroed here and read nowhere in START or GAME.OVR (byte search), so it is not modelled |
| 0xBB0F | 38 | finish | exec | finish cases (150): START 0x1713 / 0x39A3 / 0x31D7 / 0x3226 run, the guard message trapped |
| 0xBB81 | 38 | key check | read | key-driven: one key (two reads for an extended one), 0x32 / 0x20 / 0x2D compared whether extended or not, START 0899:0104 after; the Client's `TakeAiKey` gives '2' and '-' the same meaning |
| 0xBC97 | 38 | morale | exec | morale cases (300): triggers 0x11, 0x2BF2B / 0x2AF10, 0x15103, 0x1291E run; the surrender's 0x2BDF0 trapped (logged) |
| 0xBE4D | 38 | weapon worth | exec | worth cases (300) |
| 0xBF8A | 38 | ready the best weapon | bug? | read whole, matches `EquipWeapon`, with one corner: see question 1 |
| 0xE600 | 3E | game start-up | read | the zones allocated and zeroed (VM memory starts at 0), the square 15,1 facing 6 (DS:6AAD–6AAF), the door flags DS:6DDE–6DE0 = 1 (`DoorMenuFlags`), the wall-set words DS:6AB2–6ABC (0, 1, 0xFFFF…; `UnstartedSave`), DS:49F5/49F6 = 1 (pictures), DS:4A00 = 1 (typing), DS:49E2 = 1 (speed), DS:49F4 = 0, DS:49FA = DS:49F9 = 1, $6E3E = 0, DS:6B29 = 1 (`MenuHighlight`), DS:49FB = 0 (`CampState.IntervalTicks`), DS:6DDC = 0, DS:6F68/6F69 = 0xDB/0xE8 (replaced by the script init's 0x41/9 before any redraw, `BackdropRule`), ITEMS into DS:5582, then the spell and handler tables (0x2A42B, 0x11DFD). The other bytes are drawing, sound and file state. No difference found; not run under emulation (it allocates and reads files) |
| 0x23DCC | 8C | effect timeout | exec | `por_clock_oracle.py` advance: inside 0x241A1, in camp (flags, $6E3E) and outside; 0x2AF10 trapped and logged (ClockProgramTests) |
| 0x240BE | 8C | carry pass | exec | every 0x241A1 / 0x2416F case (the year cell ages the list without wrapping) |
| 0x2416F | 8C | rest-time carry | exec | resttime cases (direct), inside 0x24246, the menu and the rest |
| 0x241A1 | 8C | clock advance | exec | advance cases (400) |
| 0x24246 | 8C | rest-time borrow | exec | resttime cases (direct, the clear-all branch included), the menu, the rest |
| 0x243BC | 8C | rest time drawn | read | drawing only (trapped): colour 10, the lit word 15, two digits of each word's low byte (0x24331), minutes = 10 × tens + ones; `RestUi.Draw` matches |
| 0x244ED | 8C | rest-time menu | exec | menu cases: 0x2CC8C scripted (letters, Enter, extended Up/Down/Left/Right and others); the exit set at CS:0x701 is {0, E, R}, any other extended key is a no-op 'X' |
| 0x2463D | 8C | heal count | exec | inside the rest cases (0x2C647 trapped with a +1 HP rule, logged) |
| 0x246F1 | 8C | memorise one | exec | inside the rest cases (memorised log compared) |
| 0x247B3 | 8C | scribe one | exec | inside the rest cases (0x2A2A2 run; START 0x1509 trapped, the item unlinked) |
| 0x248D6 | 8C | preparation timers | exec | inside the rest cases (DS:6DD0 compared) |
| 0x2497E | 8C | preparation hours | exec | inside the rest cases (record 0x2C compared) |
| 0x24A66 | 8C | rest loop | exec | rest cases: the loop whole, 150 cases (stop question, interruption roll, RandSeed compared) |

## Questions for the user (original bugs)

1. **0xBF8A, two hands over two with no weapon chosen** (0xC41A–0xC452). After readying, when the hands-used byte 0x100
   is above 2 and the off-hand item (+0xD0) is missing or cursed, the routine unreadies the chosen weapon through
   0x2250A, even when no weapon was chosen: it then calls 0x2250A with a nil pointer and reads the item fields from
   0000:0000 (the interrupt table). Reached by a computer-controlled creature (monster or Quick party member) that
   holds a cursed two-handed weapon (a cursed weapon is worth 0, so it is never the chosen one) and a cursed shield, with
   no other weapon beating its natural attack. Core skips the call (`EquipWeapon`: `else if (chosen is not null)`).
   Keep the skip as the fix, or something else?

## Other observations (not fixed here)

- Client, rest loop: `CampScreens.StopAsked` polled the keyboard every fifth tick (the program polls every tick,
  0x24B1D). Fixed s55: a poll before every tick, one Client update each (`RestUi.Poll` zero).
- 0x3EFA → 0x20D50(1) at a switch into block 26 with $4AB3 = 0xFE and $4AA2 = 0: hooked s58 (the land's recovery,
  `geo-format.md` "The land recovers"; it draws 51,840 randoms, so it is play, not presentation).
- 0x99FD's view / panel / cell redraw order around trigger 0x0F (row above): presentation only.

## Proposed `overrides.py` entries (hand-checked rows; the oracle rows count by themselves once the ledger is rebuilt)

```python
'O:211D': dict(status='W', why='read whole s52: CHKPARTY\'s four 0x814A writes (s52-lane-D.md)'),
'O:3B19': dict(status='W', why='read whole s52, vectors 2/3 without the switch follow fixed (s52-lane-D.md)'),
'O:3C73': dict(status='W', why='read whole s52: compare chain = the ECL handler table (s52-lane-D.md)'),
'O:3EB7': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:3EFA': dict(status='W', why='read whole s52; the 0x20D50 call is the U row 0x20D50 (s52-lane-D.md)'),
'O:403E': dict(status='W', why='read whole s52, $49F2 after camp / Look fixed (s52-lane-D.md)'),
'O:9881': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:99FD': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:9B21': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:9E6D': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:A087': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:A1E6': dict(status='W', why='read whole s52, helpless test after the step fixed (s52-lane-D.md)'),
'O:A54C': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:A64C': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:A806': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:A8FD': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:AA32': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:ABCF': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:ADC3': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:B3B2': dict(status='W', why='read whole s52, the flee result fixed (s52-lane-D.md)'),
'O:B6FC': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:BB81': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:BF8A': dict(status='W', why='read whole s52; nil-pointer corner is a user question (s52-lane-D.md)'),
'O:E600': dict(status='W', why='read whole s52 (s52-lane-D.md)'),
'O:243BC': dict(status='W', why='read whole s52 (drawing only, s52-lane-D.md)'),
```

The oracle rows (new `e.call` targets): `por_clock_oracle.py` 0x241A1, 0x2416F, 0x24246, 0x244ED, 0x24A66 (and their
untrapped callees 0x23DCC, 0x240BE, 0x2463D, 0x246F1, 0x247B3, 0x248D6, 0x2497E); `por_ai_oracle.py` 0xBE4D, 0xB235,
0xBC97, 0xBB0F, 0xAFA4, 0xB105. The ledger's oracle list needs `por_clock_oracle.py` added.

**Decided s53 (user, 2026-11-26):** the questions above are settled; see PLAN.md "Decided s53".
