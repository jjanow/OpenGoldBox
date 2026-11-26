# s52 lane C: W-by-citation rows of the spell, combat and effect units

Units: stub 96 (spells, 0x27018–0x2A7CC), AC (level stats), B0 (effect list), 68 (combat rules), 3B (combat map),
3E0 (combat arena), 3DB (combat screen aim). Every routine below was disassembled whole (capstone on the EXEPACK-expanded
image; GAME.OVR offsets) and compared branch by branch with the C# it names. "exec" rows are now run by an oracle and
compared by a Core test; the instruction coverage of each oracle run was measured with a unicorn code hook (every
instruction of the routine executed unless listed).

Empty routines (0xE55E, 0x16468, 0x2A0AA (spell 66), 0x2A7C8, 0x2AEE5, 0x2C81C, 0x3072B, 0x31A35) already count as by execution and are
left out of the counts. Unit AC and 3DB have no citation rows.

## Rows

| Routine | Status | Notes |
|---|---|---|
| 0x27F67 (Bless / Curse on one side) | exec | `por_spell_oracle.py`, spells 1 / 2 outside a fight and Bless in a fight (DS:49F3 = 5, the enemy-beside test START 0x2FD3) |
| 0x28027, 0x28058 (1, 2) | exec | spell oracle, 12+ cases each |
| 0x28083 (3) | exec | incl. no target |
| 0x280BF (4), 0x28101 (5, 11, 18, 22, 29), 0x2813B (6, 7, 16, 17, 52–54), 0x2817A (8), 0x281A8 (9) | exec | spell oracle; DS:4844 / 4845 / 6819 / 681C / 6820 compared |
| 0x281F5 (10 Charm Person) | exec | type / size unaffected branch, an existing charm's handler |
| 0x282BD (12 Enlarge) | exec | casting levels 1–7 and 9 (DS:4845 residue path) |
| 0x28386 (13 Reduce) | exec | with / without Enlarge, no target |
| 0x283F0 (14 Friends) | exec | Charisma cap 25 |
| 0x28451 (15), 0x284B1 (19), 0x284DF (20) | exec | |
| 0x28533 (21 Sleep) | exec | hit-dice costs, race, status, 0x35, no target |
| 0x28670 (23, 49 Hold Person) | exec | 0–4 targets; only the nil-slot skip 0x286CD not run (no caller passes a nil slot) |
| 0x28801 (24), 0x2883A (25) | exec | |
| 0x28873 (26 Slow Poison) | exec | animated, poisoned, HP 0, no target |
| 0x28927 (27 Snake Charm) | **fixed** | the budget is DS:4844 = the *current character's* HP, paid from the global (Core used a local from the caster): `FightSpells.SnakeCharm` |
| 0x289DB (28), 0x28A2E (30, 50), 0x28A67 (31), 0x28AA2 (32), 0x28AE4 (33) | exec | |
| 0x29275 (38), 0x29364 (40), 0x29655 (45), 0x29FB1 (61), 0x2A042 (63) | exec | (0x2A0AA, 66, is empty; run too) |
| 0x29FE8 (62) (already exec) | fixed, bug? | the count DS:6C94 is not tested: the 2d4 is always drawn (Core now draws), DS:6C95 healed even with no target (question 1) |
| 0x27AA9 (spell 0, the target pick outside a fight) | read | member pick through START 0x37E8 with DS:6DB5 (s51 decision); UI, no oracle |
| 0x2A332 (cast message) | read | pres: in a fight START 0x16EC (row 10, pause 1), row 23 cleared, "Spell:" + DS:28E9 name in colour 10 (`CombatChooser.SayCast`); outside one the window (1, 18)–(38, 22) cleared, name (1, 19), the word at length + 2, the spell name (1, 20) colour 10, PauseOnly, START 0x2383 (`RestUi.CastOne`) |
| 0x13965 (turn undead) | exec | spell oracle "turn" (90 cases on a combat map; 0x3154B stubbed to zero the slot size): every instruction |
| 0x13B4C (turn-undead target search) | exec | same cases |
| 0x130AD (free attacks before a step) | exec | `por_round_oracle.py` "free" (200 cases, MakeAttack trapped and logged with phase and attacks; Core `Free_attacks_match_0x130AD`); the start > end facing wrap (0x132E4) not hit |
| 0x13465 (flee) | **fixed** | the routine returns START 0x31D7's 1 whether the creature got away or not; the AI loop 0xB4EB ends on it, so a fleeing monster at the edge tries once per move. Core's `MonsterAi.MoveStep` retried until it got away (up to 50 times, a hack): now one try. Round oracle "flee" (200 cases) runs it whole with 0x2BDF0, every instruction |
| 0x15074 (enemy HP percent) | exec | round oracle after the picks (DS:6E2F preset 0xEE, max 0 keeps it) |
| 0x15103 (fastest enemy) | exec | round oracle, both Core ports (FightMoves, MonsterAi) |
| 0x161CD (handler 73 rake) | read | matches; the attack 0x1407F needs the round oracle's MakeAttack set-up (not done). Program reads the target through a nil +0x0A without a test; Core returns |
| 0x16281 (handler 74 hold) | read | matches; the held slot creature nil is read as a record by the program, Core treats it as out of play |
| 0x2BB39 (to-hit without an attacker, ECL) | read | `EclPartyRules.Hits` matches; `FightRules.AttackRollNoAttacker` has no caller (dead code) |
| 0x2BDF0 (put out of play) | exec (caller) | run by the flee cases (status 3); the status ≠ 3 HP-0 line is the AI surrender, read |
| 0x2BF2B (morale removals 0x4A, 0x4B) | exec | spell oracle "morale" (DS:0C24 / 0C25 = 0x4A, 0x4B) |
| 0xD815 (arena set-up) | read | ground pictures (pres), header 4/5/6 = 0/1/0, builder by DS:49FA |
| 0xD8B2 (records at fight start) | read | stats, zeroed record, +0x13 past $6E3E, facing DS:02F2, the NPC morale $6DC6 + 0x80 (`FightPlacement.InitRecords` + `EncounterHost.StartFight`) |
| 0xE402 (fight start) | read | every state write matched (`FightPlacement.Start`, `EncounterHost.StartFight`); picture frees, START 0x3728 and 0x6ACE are drawing |
| 0x3083F (palette restore) | read | pres (DS:4A1E = 1: START 0x704D (0,0), (8,8)) |
| 0x317E1 (put on a square) | exec | `por_handler_oracle.py` "place" (80 cases: fight or not, bodies, moving, not-party), every instruction |

Counts (50 citation rows: 3B 3, 68 8, 96 34, B0 3, 3E0 2): exec 39, read 9, fixed 2 (0x28927, 0x13465), open 0;
bug? 1 (spell 62's 0x29FE8, not a citation row, also changed: see question 1).

## Code fixes

- `FightSpells.SnakeCharm` (0x28927): budget in DS:4844 (`SpellGlobals.Ds4844`) from the current character's HP.
- `FightSpells` spell 62 (0x29FE8): the 2d4 is drawn with no target.
- `MonsterAi.MoveStep`: the flee at the edge is one try (0x13465 returns 1); test
  `MonsterAiTests.A_failed_flee_at_the_edge_ends_the_move_after_one_try`, spec `combat-ai.md`.
- `tools/scripts/por_emu.py`: a second trap on one address replaces the first (the spell oracle's new sound traps
  otherwise ran twice in the round oracle).

## Questions for the user (bug?)

1. **Spell 62 with no target (0x29FE8).** The routine never tests the target count DS:6C94: it draws 2d4 and heals
   whatever record DS:6C95 still points at (the previous cast's first target). Several other single-target routines
   read DS:6C95 without the count too (10, 12, 14, 26, 28, 37, 43, 56–59). Reaching it needs a cast whose target pick
   succeeds with an empty list; not established that any path does (the cast 0x27C38 calls the routine whenever the
   pick returns true). Only spell 62 was changed in Core (it now draws the dice and heals nothing); the other routines
   still return early with no target, and Friends (14) draws its 2d4 before reading the target, so the same RandSeed
   difference applies there. Leave as is, give them all the program's draws, or treat the stale target as a bug?

## Proposed ledger entries

`tools/scripts/ledger/cite.py`, the spell id list:

```
for sid in [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
            30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56,
            57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67]:
```

and in `ORACLE_O`: `0x27F67: 'spell', 0x13965: 'spell', 0x13B4C: 'spell', 0x2BF2B: 'spell', 0x15074: 'round',
0x15103: 'round', 0x130AD: 'round', 0x13465: 'round', 0x317E1: 'handler'`.

`overrides.py` (hand-checked reads):

```
'O:27aa9': dict(status='W', why='read whole s52: member pick START 0x37E8 with DS:6DB5 (s51 decision)'),
'O:161cd': dict(status='W', why='read whole s52 (handler 73 rake; nil target not tested by the program)'),
'O:16281': dict(status='W', why='read whole s52 (handler 74 hold)'),
'O:2bb39': dict(status='W', why='read whole s52 (EclPartyRules.Hits)'),
'O:d815': dict(status='W', why='read whole s52 (arena header and builder choice; ground pictures pres)'),
'O:d8b2': dict(status='W', why='read whole s52 (FightPlacement.InitRecords, EncounterHost.StartFight)'),
'O:e402': dict(status='W', why='read whole s52 (FightPlacement.Start, EncounterHost.StartFight)'),
'O:3083f': dict(status='W', kind='pres', why='read whole s52: palette restore when DS:4A1E = 1'),
'O:2a332': dict(status='W', kind='pres', why='read whole s52: the cast message in and out of a fight'),
```

## Other notes

- `FightRules.AttackRollNoAttacker` (0x2BB39) has no caller; the ECL uses `EclPartyRules.Hits`.
- Every oracle fixture was regenerated after the `por_emu` trap change and the new traps: all byte-identical except
  the ones extended here (`spells.json`, `round.json`, `handlers.json`); `cast.json` differs only in its source label.
  On a merge conflict, regenerate those three from the merged scripts rather than merging the JSON.
