# s52 lane B: the effect handlers (unit 0xEC5B–0x12508, stub 41)

Lane B of session 52: the W-by-citation routines of the effect-handler unit. Result: every handler of the unit is run
by the program under unicorn and compared with `EffectHandlers` in Core (`docs/re/spells-effects.md` "Every handler
under emulation").

## Method

- `tools/scripts/por_handler_oracle.py` got a **call** case kind: the handler through the dispatcher 0x2AEEA (id,
  creature, record, removed), 40 cases per id (4600 in all, fixture `tests/fixtures-private/por/program-v1.3/
  handler_calls.json.gz`, gzip: about 2 MB). Each case sets 2–4 generated creatures (effect lists, items, weapons for
  the current character, names from the handlers' name lists), the combat records and their +0x0A target, DS:6826,
  the DS cells 6816–682A, the round counter DS:6DE3, the team counts DS:6814/6815 and DS:6DBF; a third of the cases
  run on a combat map in a fight (DS:49F3 = 5); the holds (75–77), the dodges (41, 104, 120), the acid breath (121)
  and the revives (78, 99, 102, with a creature on the reviver's square half the time) always on a map. The record is
  the creature's first with the id, an aura carrier's (21, 45, 46, 49) or, for 128–139, the item.
- `tests/GoldBox.Core.Tests/HandlerCallProgramTests.cs` loads the same state into `EffectHandlers` / `FightState` /
  `FightSpells` and compares records (equipment slot pointers aside), effect lists, items (the name cache aside),
  combat records and their target, every cell, DS:6826 and RandSeed. **All 4600 equal.**
- Branch coverage: `HANDLER_COVERAGE=FILE` makes the oracle record every executed address of the unit. Both sides of
  every conditional jump are taken in every routine the call cases run (checked with a small report script over the
  capstone listing); the only untaken sides left in the unit are handler 40's cloud-list walk (another caster's
  cloud, no cloud found, unlinking past the head), which only the s49 cloud cases reach.
- Presentation: every display call site in the unit (START 0x16EC / 0x1693 / 0x2B8B / 0x2EEA / 0x0767 party list /
  0x25C9 and 0x257E flights / 0x7E4E pause / 0x2383 clear / 0x1583 panel) was matched to the `EffectEvent` C# raises
  at the same point (list in the table below under "Display call sites"). One unmodelled redraw: handler 30 redraws
  the status panel (START 0x1583, 0xF749–0xF761) when the coughing creature is the current one (see "Open").

## Code changes

- `TurboPascalRandom.Next(0)` now steps the seed and returns 0, as the program's Random (START 0xC140: the seed steps
  before the range test). Reached by handler 28 (Mirror Image, 1d(param + 1) with param 255). Before, Core drew
  nothing there and RandSeed fell one step behind.
- `EffectHandlers.SpiritualHammer`: the `[?]` on START 0x2197 settled (a copy appended at the list end; the count
  0xC7 is left), comment only.
- `EffectHandlers` handler 89 with a nil record (the cloaks' Ready, 0x11B35): Core threw a NullReferenceException;
  it now reads and writes `EffectHandlers.NilRecord`, a stand-in for 0000:0000 (question below).
- Oracle and tests as above; `HandlerProgramTests` unchanged (its fixture `handlers.json` is byte-identical).

No other difference between the program and Core was found in this unit.

## Notes (no play effect found; no user decision needed)

- **START 0x2F7B** (team count, called by handlers 11, 78, 99, 102 through Charm / 0x2C753) increments
  `DS:6814 + hostile` with the hostile byte sign-extended: a hostile byte other than 0 / 1 would step DS:6813,
  DS:6816 (the save roll) and so on. Core's `FightRules.CountTeams` counts only 0 / 1. Play sets only 0 / 1 (charm
  writes param bit 7, Animate Dead param ÷ 16 with the caster's side), so the cases keep to those.
- **0x11B35** (item codes 128–130, 133, 134, 136, 138, 139) runs the granted effect's handler with a **nil record**.
  In the data the codes grant 61 (ring, 129), 89 (cloaks, 133) and 3 (two-handed sword, 136) (`item-format.md`);
  61 and 3 do not read the record, 89 does: see the question below. The cases grant 89 (with 0000:0003 set per case)
  and otherwise only ids whose handlers do not read the record.
- **START 0x3262** (casting level, for 105 / 106) leaves its result unset for a class byte other than 0–2: only spell
  rows past the table; cases use spell ids 0–67.
- Handlers 3, 6, 18, 26, 47, 64–70, 76, 77, 79, 80, 85–87, 126 read the combat record's +0x0A target without a nil
  test; their classes run after MakeAttack (0x14118) has written it, so it is set in play. Core tests for null.
- Handler 75 / 74 read DS:65B9 + 4·param unchecked; the param is the slot 0x31399 gave when the hold started.

## Question for the user (bug?)

**Cloak of Displacement on Ready (0x11B35 → handler 89 with a nil record, 0x110EB).** Readying a cloak (item code 133,
effect 89 in byte 0x3D) adds effect 89 (param 12) and then runs handler 89 with a nil record instead of the record
just added, so the handler reads and writes byte 3 of 0000:0000, the interrupt table (the high byte of INT 0's
segment, Turbo Pascal's divide-error handler): at round 0 with attack roll 0 it writes that byte AND 0x0F; otherwise,
with its bit 4 clear, it sets the attack roll DS:6822 to −1 and sets bit 4. The handler's later runs (class 8 at the
fight start, class 0x10 on each attack roll against the wearer) get the real record, so the displacement itself works.
In play: every Ready of a cloak (camp Items page, or in a fight); the corrupted vector only matters if a divide error
happens later (it would then jump to a wrong segment instead of "Runtime error 200"), and the −1 lands on whatever
attack roll was in DS:6822. Core used to throw a NullReferenceException here (a crash on Ready); it now follows the
program on a stand-in byte (`EffectHandlers.NilRecord`, `[?]` its real value, 0 at start), checked against the
program in the call cases. Fix it (run 89 with the new record, or skip the handler call on Ready), or keep the
faithful stand-in?

## Open

- [release] Handler 30's panel redraw (START 0x1583 when the coughing creature is DS:5D92, 0xF749): no event in Core;
  the Client draws the panel's creature lines live except while `CombatPlayer.ShownLines` holds a frozen name colour /
  HP, which this redraw would refresh. Not seen in DOSBox-X.
- The question above (cloak Ready).

## Proposed ledger inputs

`tools/scripts/ledger/cite.py`: the call cases' handlers counted as oracle-run, like 40 / 84 / 88 / 90 / 97:

```python
for hid in [40, 84, 88, 90, 97,  # por_handler_oracle.py (90 / 97 inside the saving throw 0x2BC4D)
            # s52 lane B: the 'call' cases (HandlerCallProgramTests)
            1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 14, 15, 17, 18, 20, 21, 22, 23, 25, 26, 28, 29, 30, 31, 32, 33, 34,
            35, 36, 37, 38, 39, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 56, 57, 58, 59, 61, 62, 64, 65,
            66, 67, 68, 69, 70, 71, 72, 75, 76, 77, 78, 79, 80, 87, 89, 91, 93, 94, 95, 96, 98, 99, 100, 101, 102,
            103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123,
            124, 125, 126, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139]:
    ORACLE_O.setdefault(int(disp['handlers'][str(hid)][2:], 16), 'handler')
# the unit's helpers, run inside those handlers (not trapped)
for a in (0xEC5B, 0xEC7A, 0xECB3, 0xFC07, 0xFC78, 0x11570, 0x101A0, 0x10246, 0x2BCCD, 0x2BD18):
    ORACLE_O.setdefault(a, 'handler')
```

`tools/scripts/ledger/overrides.py`, for the rows read by hand (empty routines; they already count as W):

```python
'O:f2a2': dict(status='W', why='read whole s52 lane B: handler 16, empty (push bp / pop bp / retf 0xA)'),
'O:ffb0': dict(status='W', why='read whole s52 lane B: handler 54, empty'),
'O:ffb7': dict(status='W', why='read whole s52 lane B: handler 55, empty'),
'O:11df6': dict(status='W', why='read whole s52 lane B: the shared empty handler (5, 13, 19, 24, 27, 60, 63, 92, 127)'),
'O:12504': dict(status='W', why='read whole s52 lane B: unit init, empty'),
```

## Display call sites (pres read)

| Site | Program | Core event |
|---|---|---|
| 0xECB3 | message 0x16EC, pause 0x7E4E, clear 0x2383, outside a fight the party list 0x0767 | the caller's status event, `StatusDone` |
| 4 0xEDFB, 7 0xEEAF | 0x16EC row 10, pause | `StartsTraining`, `Hardier` (before Con + 1) |
| 15 0xF299, 44 0xFE19 | outside a fight the party list (for DS:5D92) after the 1-point damage | `HandlerDamageListed` |
| 21 0xF39A | 0x16EC when item use was allowed | `Silenced` |
| 23 0xF55D | after the hammer is added | `GainsItem` |
| 28 0xF668 | after the nullify, before the image count drops | `LostImage` |
| 30 0xF6F0 / 0xF761 | 0x16EC when item use was allowed; panel 0x1583 when current | `Coughing`; panel not modelled (Open) |
| 39 0xF8E7 | first time, before the age + 1 | `Ages` |
| 40 0xF9A4 | 0x1693 row 24 | `AirClears` |
| 0xFC5F | dodge, before damage 0 | `MissileDodged` |
| 43 0xFD81 | Str > 3, before Str − 1 | `Weakens` |
| 50 0xFF91, 62 0x10181 | 0x2EEA after the heal | `Healed` |
| 57 0x1001F | before Cha − 2 | `Rots` |
| 0x101F2 / 0x101F7 | poisoned, then PauseOnly | `Poisoned` |
| 0x10294 | 0x2B8B (effect frames) | `ParalysisFailed` |
| 75 0x10485, 77 0x10629 | 0x16EC row 10, pause | `SucksBlood`, `HeldFast` |
| 83, 84, 0x10D7D, 121 | messages and flights (s27–s49, seen in DOSBox-X) | `Gazes`, `GazeReflected`, `CharmGazes`, `Drained`, `SpitsAcid`, flights |
| 87 0x110E0 | on the target | `Diseased` |
| 91 0x111E0 | spell 15 | `Unaffected` |
| 131 0x11CB1 / 0x11CD8 | only when 0x2C046 raised; in a fight DS:49F3 = 2 around it | `Stronger` |
| 135 0x11DCA | 0x1693 row 24 | `NeedsGiantStrength` |

## Routines

Statuses: **exec** 121 (109 newly run by the call cases or as their helpers; 12 already by execution: 40, 81–86, 90, 97, the
level drain helpers 0x10C5D / 0x10D7D, the table builder 0x11DFD), **read** 5 (empty routines), **fixed** 1 (handler 89 0x110EB:
a nil record crashed Core, now the program's stand-in; the other Core fix is in `TurboPascalRandom`, outside the
unit), **bug?** 1 (the same handler 89 call, question above), **open** 0 routines (one release note above).

| Routine | Role | Status | Evidence |
|---|---|---|---|
| 0xEC5B | CS:1C8D of unit | exec | nullify (DS:6817 / 6818): run by 28, 61, 91, 105–112, 124, 125 |
| 0xEC7A | see `spells-effects.md` Data-segment cells | exec | add unless locked (DS:681F): run by 15, 43, 44, 50, 57, 62, 78, 102, lock 0 and 1 |
| 0xECB3 | see `combat-menu.md` The combat screen (Client) | exec | status change: run by 22, 32, 57, 64–66, 70, 95, in and out of a fight |
| 0xED68 | effect handler 1 | exec | call cases, both sides of every branch taken |
| 0xED78 | effect handler 2 | exec | call cases, both sides of every branch taken |
| 0xED96 | effect handler 3 | exec | call cases, both sides of every branch taken |
| 0xEDDA | effect handler 4 | exec | call cases, both sides of every branch taken |
| 0xEE1E | effect handler 6 | exec | call cases, both sides of every branch taken |
| 0xEE8E | effect handler 7 | exec | call cases, both sides of every branch taken |
| 0xEFD2 | effect handlers 8, 45 | exec | call cases, both sides of every branch taken |
| 0xF007 | effect handlers 9, 46 | exec | call cases, both sides of every branch taken |
| 0xF03C | effect handler 10 | exec | call cases, both sides of every branch taken |
| 0xF05B | effect handler 11 | exec | call cases, both sides of every branch taken |
| 0xF115 | effect handlers 12, 38 | exec | call cases, both sides of every branch taken |
| 0xF231 | effect handler 14 | exec | call cases, both sides of every branch taken |
| 0xF246 | effect handler 15 | exec | call cases, both sides of every branch taken |
| 0xF2A2 | effect handler 16 | read | empty |
| 0xF2A9 | effect handler 17 | exec | call cases, both sides of every branch taken |
| 0xF2D4 | effect handler 18 | exec | call cases, both sides of every branch taken |
| 0xF33F | effect handler 20 | exec | call cases, both sides of every branch taken |
| 0xF36A | effect handler 21 | exec | call cases, both sides of every branch taken |
| 0xF3D0 | effect handler 22 | exec | call cases, both sides of every branch taken |
| 0xF43B | effect handler 23 | exec | call cases, both sides of every branch taken |
| 0xF573 | effect handler 25 | exec | call cases, both sides of every branch taken |
| 0xF5A2 | effect handler 26 | exec | call cases, both sides of every branch taken |
| 0xF61B | effect handler 28 | exec | call cases, both sides of every branch taken |
| 0xF698 | effect handler 29 | exec | call cases, both sides of every branch taken |
| 0xF6C0 | effect handler 30 | exec | call cases, both sides of every branch taken |
| 0xF776 | effect handler 32 | exec | call cases, both sides of every branch taken |
| 0xF807 | effect handler 33 | exec | call cases, both sides of every branch taken |
| 0xF82A | effect handler 34 | exec | call cases, both sides of every branch taken |
| 0xF861 | effect handler 35 | exec | call cases, both sides of every branch taken |
| 0xF870 | effect handler 36 | exec | call cases, both sides of every branch taken |
| 0xF881 | effect handler 37 | exec | call cases, both sides of every branch taken |
| 0xF8A6 | effect handler 39 | exec | call cases, both sides of every branch taken |
| 0xF91E | effect handler 40 | exec | already W by execution (s49 oracles) |
| 0xFC07 | see `spells-effects.md` Missile dodge | exec | missile dodge: run by 41, 104, 120 on a map (distance START 0x30FB) |
| 0xFC78 | see `spells-effects.md` Handlers 1–72 | exec | the missile (START 0x3A2B, else the weapon): run by 41, 94, 96, 115, 116, 119, 123 |
| 0xFCE1 | effect handler 41 | exec | call cases, both sides of every branch taken |
| 0xFD1C | effect handler 42 | exec | call cases, both sides of every branch taken |
| 0xFD39 | effect handler 43 | exec | call cases, both sides of every branch taken |
| 0xFDC3 | effect handler 44 | exec | call cases, both sides of every branch taken |
| 0xFE54 | effect handler 47 | exec | call cases, both sides of every branch taken |
| 0xFED1 | effect handler 48 | exec | call cases, both sides of every branch taken |
| 0xFF0D | effect handler 49 | exec | call cases, both sides of every branch taken |
| 0xFF56 | effect handler 50 | exec | call cases, both sides of every branch taken |
| 0xFF9A | effect handlers 31, 51, 52, 53 | exec | call cases, both sides of every branch taken |
| 0xFFB0 | effect handler 54 | read | empty |
| 0xFFB7 | effect handler 55 | read | empty |
| 0xFFBE | effect handler 56 | exec | call cases, both sides of every branch taken |
| 0xFFF4 | effect handler 57 | exec | call cases, both sides of every branch taken |
| 0x100AD | effect handler 58 | exec | call cases, both sides of every branch taken |
| 0x100CD | effect handler 59 | exec | call cases, both sides of every branch taken |
| 0x100EB | effect handler 61 | exec | call cases, both sides of every branch taken |
| 0x10146 | effect handler 62 | exec | call cases, both sides of every branch taken |
| 0x101A0 | see `spells-effects.md` Handlers 1–72 | exec | poisoned hit: run by 64, 65, 66, 70 (save, 0x37, status 6) |
| 0x10246 | see `spells-effects.md` Handlers 1–72 | exec | paralysing hit: run by 67, 68, 69 (minutes rolled before the save) |
| 0x102B8 | effect handler 64 | exec | call cases, both sides of every branch taken |
| 0x102CC | effect handler 65 | exec | call cases, both sides of every branch taken |
| 0x102E0 | effect handler 66 | exec | call cases, both sides of every branch taken |
| 0x102F4 | effect handler 67 | exec | call cases, both sides of every branch taken |
| 0x10316 | effect handler 68 | exec | call cases, both sides of every branch taken |
| 0x10341 | effect handler 69 | exec | call cases, both sides of every branch taken |
| 0x10366 | effect handler 70 | exec | call cases, both sides of every branch taken |
| 0x1037A | effect handler 71 | exec | call cases, both sides of every branch taken |
| 0x1038B | effect handler 72 | exec | call cases, both sides of every branch taken |
| 0x103CA | effect handler 75 | exec | call cases, both sides of every branch taken |
| 0x10510 | effect handler 76 | exec | call cases, both sides of every branch taken |
| 0x10590 | effect handler 77 | exec | call cases, both sides of every branch taken |
| 0x10634 | effect handler 78 | exec | call cases, both sides of every branch taken |
| 0x10674 | effect handler 79 | exec | call cases, both sides of every branch taken |
| 0x106A7 | effect handler 80 | exec | call cases, both sides of every branch taken |
| 0x10705 | effect handler 81 | exec | already W by execution (s49 oracles) |
| 0x10803 | effect handler 82 | exec | already W by execution (s49 oracles) |
| 0x10908 | effect handler 83 | exec | already W by execution (s49 oracles) |
| 0x10AD2 | effect handler 84 | exec | already W by execution (s49 oracles) |
| 0x10C5D | see `spells-effects.md` Handlers 73–139 | exec | level drain: por_spell_oracle.py handlers 85 / 86 (already oracle (caller)) |
| 0x10D7D | see `spells-effects.md` Handlers 73–139 | exec | level drain: por_spell_oracle.py handlers 85 / 86 (already oracle (caller)) |
| 0x10FFF | effect handler 85 | exec | already W by execution (s49 oracles) |
| 0x1101D | effect handler 86 | exec | already W by execution (s49 oracles) |
| 0x11047 | effect handler 87 | exec | call cases, both sides of every branch taken |
| 0x110EB | effect handler 89 | fixed, bug? | call cases (with the cloaks' nil record); Core crashed on a nil record, now the stand-in; question above |
| 0x11134 | effect handler 90 | exec | already W by execution (s49 oracles) |
| 0x111B1 | effect handler 91 | exec | call cases, both sides of every branch taken |
| 0x111FD | effect handler 93 | exec | call cases, both sides of every branch taken |
| 0x11217 | effect handler 94 | exec | call cases, both sides of every branch taken |
| 0x1126B | effect handler 95 | exec | call cases, both sides of every branch taken |
| 0x112A6 | effect handler 96 | exec | call cases, both sides of every branch taken |
| 0x112E5 | effect handler 97 | exec | already W by execution (s49 oracles) |
| 0x1135B | effect handler 98 | exec | call cases, both sides of every branch taken |
| 0x1138B | effect handler 99 | exec | call cases, both sides of every branch taken |
| 0x11434 | effect handler 100 | exec | call cases, both sides of every branch taken |
| 0x1146F | effect handler 101 | exec | call cases, both sides of every branch taken |
| 0x114C1 | effect handler 102 | exec | call cases, both sides of every branch taken |
| 0x1151A | effect handler 103 | exec | call cases, both sides of every branch taken |
| 0x1155C | effect handler 104 | exec | call cases, both sides of every branch taken |
| 0x11570 | magic resistance / caster-level check (handlers  | exec | magic resistance: run by 105, 106 (casting level START 0x3262) |
| 0x115D1 | effect handler 105 | exec | call cases, both sides of every branch taken |
| 0x115DF | effect handler 106 | exec | call cases, both sides of every branch taken |
| 0x115ED | effect handler 107 | exec | call cases, both sides of every branch taken |
| 0x11611 | effect handler 108 | exec | call cases, both sides of every branch taken |
| 0x11626 | effect handler 109 | exec | call cases, both sides of every branch taken |
| 0x11634 | effect handler 110 | exec | call cases, both sides of every branch taken |
| 0x1164B | effect handler 111 | exec | call cases, both sides of every branch taken |
| 0x1166C | effect handler 112 | exec | call cases, both sides of every branch taken |
| 0x11683 | effect handler 113 | exec | call cases, both sides of every branch taken |
| 0x116C8 | effect handler 114 | exec | call cases, both sides of every branch taken |
| 0x116E2 | effect handler 115 | exec | call cases, both sides of every branch taken |
| 0x11741 | effect handler 116 | exec | call cases, both sides of every branch taken |
| 0x1177B | effect handler 117 | exec | call cases, both sides of every branch taken |
| 0x117BE | effect handler 118 | exec | call cases, both sides of every branch taken |
| 0x117D8 | effect handler 119 | exec | call cases, both sides of every branch taken |
| 0x11823 | effect handler 120 | exec | call cases, both sides of every branch taken |
| 0x11877 | effect handler 121 | exec | call cases, both sides of every branch taken |
| 0x11993 | effect handler 122 | exec | call cases, both sides of every branch taken |
| 0x119E3 | effect handler 123 | exec | call cases, both sides of every branch taken |
| 0x11A3F | effect handler 124 | exec | call cases, both sides of every branch taken |
| 0x11A63 | effect handler 125 | exec | call cases, both sides of every branch taken |
| 0x11A92 | effect handler 126 | exec | call cases, both sides of every branch taken |
| 0x11B35 | effect handlers 128, 129, 130, 133, 134, 136, 13 | exec | call cases, both sides of every branch taken |
| 0x11BB2 | effect handler 131 | exec | call cases, both sides of every branch taken |
| 0x11D00 | effect handler 132 | exec | call cases, both sides of every branch taken |
| 0x11D8F | effect handler 135 | exec | call cases, both sides of every branch taken |
| 0x11DD5 | effect handler 137 | exec | call cases, both sides of every branch taken |
| 0x11DF6 | effect handlers 5, 13, 19, 24, 27, 60, 63, 92, 1 | read | the shared empty handler (5, 13, 19, 24, 27, 60, 63, 92, 127): push bp / pop bp / retf 0xA |
| 0x11DFD | see `por-art-formats.md` Amiga program reads (s1 | exec | already W by execution (s49 oracles) |
| 0x12504 | empty routine (unit init or empty handler) | read | empty |
