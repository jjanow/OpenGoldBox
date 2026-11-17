# The player's combat menu, sweep attacks and turn undead (Pool of Radiance, DOS v1.3)

What a player-controlled creature can do on its turn (GAME.OVR 0x9B21, called by the turn 0x99FD when record 0x10F
= 0; `combat-format.md` "A creature's turn"), the sweep attack (0x1367D) and turn undead (0x13965). Read from the
program in session 12 (2026-11-17); addresses are GAME.OVR file offsets unless marked START (offsets in the unpacked
`START.EXE` image); far calls go through the overlay stubs (`ecl-format.md` "Program references").

Status marks: **P** read in the program, **D** checked against DOSBox-X, `[?]` not settled. talonius' source was
used only to find routines. Code: `src/GoldBox.Core/Game/FightMenu.cs` (menu, aim, move, done menu, attack ally),
`SweepAttack.cs`, `TurnUndead.cs`, `MenuController.cs` (an `IFightController` that asks an `IFightMenuChooser`).

DOSBox (s12): states of the Slums goblin fight from save A at player menus (`fight_moves`, one continuous round 0,
in the private fixtures; `fight_ranged` as scratch states). Checked facts are marked **D** below;
`FightMenuCaptureTests` replays the Done-menu entries, Delay, Quit, the main menu's entries and the Turn.

## Combat record and DS cells used here

| Where | What |
|---|---|
| combat record +0 | spell begun earlier (cast when the creature's next turn comes) |
| +1 | cast allowed (start of round 1; damage clears it) |
| +3 | initiative (signed) |
| +5 | sweep budget (start of round = record 0x6B) |
| +6 | movement left, in half squares |
| +9 | facing |
| +0x0A | creature attacked last (far pointer) |
| +0x0E | rounds dying |
| **+0x10** | **turned** (turn undead succeeded on it; the AI reads it at 0xAC9F, 0xBCC1) |
| **+0x11** | **has tried turning** this fight (the menu's Turn entry needs 0) |
| +0x13 | not a party member |
| DS:6E30 | quick fighters use magic (Alt-M toggles; fight start 0xE457 clears it; the AI reads it at 0xB187) |
| DS:681E | "not targetable", set by effect handlers during the target check 0x13880 |
| DS:49E2 | game speed (Speed in the Done menu) |
| VM $49E5 | no magic here: Cast is not offered while it is non-zero (word [DS:49D2]+0x1CA) |
| VM $6E33 | the party attacked an ally (word [DS:49D6]+0x666; fight start clears it) |

## Menu (0x9B21) — P

Argument: the creature (also the current creature DS:5D92, set by the turn).

1. Out of play (0x10D = 0): actions end (START 0x31D7) and the menu returns.
2. A spell begun earlier (+0 > 0): +0 = 0, the spell is cast now (0x27C38 with the spell; it picks its own targets),
   actions end, return.
3. Otherwise loop until an action reports "done": build and show the menu (0x9E6D, below), run the chosen entry; when
   not done, the creature's icon and status panel are redrawn (0x319D2, START 0x1583; no state).

**Building the menu** (0x9E6D), every time it is shown: effects with trigger 0x0F on the creature (0x2B1E4; the turn
0x99FD already ran trigger 0x0F once, so the first display runs it a second time), then the entries in this order:

| Entry | Shown when |
|---|---|
| Move | movement left +6 > 0 |
| View, Aim | always |
| Use | item count 0xC7 > 0 |
| Cast | any byte of the memorised spells 0x17–0x2B is non-zero (unsigned, so a spell still being memorised, bit 7, counts), +1 ≠ 0 and VM $49E5 = 0 |
| Turn | cleric level 0x96 > 0 (signed) and +0x11 = 0 |
| Quick, Done | always |

The menu routine (0x2CC8C) accepts a letter only when it is the hotkey of a shown entry, so a hidden entry can't be
chosen. Besides the entries it accepts space and the extended keys Alt-Q (scan 0x10), Alt-R (0x13), Alt-X (0x2D),
Alt-M (0x32); other extended keys become 0 and are ignored (0x9FF3–0xA057, Pascal sets at 0x9E2D / 0x9E4D).
**D**: `fight_moves` shows Move View Aim Use Cast Quick Done for a magic-user and no Turn after a cleric's turn.

**Entries:**

| Key | Routine | Effect |
|---|---|---|
| M | 0xA1E6 | Move (below) |
| V | 0x21AED, then 0x1351E | the character view screen (items, ready, trade …; not read `[?]`); then attacks left are recomputed (`combat-format.md` "Attacks left"); "done" is what the screen reports |
| A | 0x15D53 | Aim (below) with range 0xFF ("from the weapon"), attack mode |
| U | 0x21F32, then 0x1351E | use an item (not read `[?]`), then attacks left recomputed |
| C | 0x14C0F | Cast (below) |
| T | 0x13965, then START 0x31D7 | turn undead (below); actions end |
| Q | 0xAA32, then the AI 0xABCF | the creature becomes computer-controlled (0x10F = 1; a remembered target +0x0A on its own side is forgotten), short pause, done, and the AI acts for it at once |
| D | 0xA64C | Done menu (below) |
| Alt-M | — | DS:6E30 toggled, with a message saying which way it was set |
| space | — | every creature in the list with 0x84 < 0x80 (not an NPC) gets 0x10F = 0; not done |
| Alt-Q | 0xAA32 for all | this creature's initiative = 20; every creature in the list becomes computer-controlled as by Q; done. The creature is picked again at once (initiative 20), the turn makes it 19 and the AI acts |
| Alt-R | — | accepted, does nothing |
| Alt-X | 0x1638E | a debugging switch: only when the program was started with a particular command-line word, every hostile creature (0x10E = 1) gets in play 0 and status 6 and every creature's actions end; done. Otherwise a refusal message, not done |

**Done menu** (0xA64C), loop until Exit / Escape or done:

| Entry | Shown when | Effect |
|---|---|---|
| Guard | no ranged weapon, or a throwable one (START 0x39A3 / 0x39E6) | START 0x3226: actions end, +7 = 1 (guarding), message |
| Delay | always | initiative 1, done: the creature acts again after the others this round |
| Quit | always | actions end (START 0x31D7) |
| Bandage | a party member (+0x13 = 0, hostile 0) is dying (status 5) | the **first** dying party member in list order (anywhere on the map, no distance test) becomes unconscious (status 4), +0x0E = 0, message; then actions end |
| Speed | always | game speed DS:49E2: Slower (shown when < 9) adds 1, Faster (shown when > 0) subtracts 1 |
| Exit | always | back to the main menu |

**D** (`fight_moves`): Guard is shown for a fighter with a two-handed sword and not for a thief with darts (ranged,
flags 0x10, not throwable), as the rule says; Delay and Quit cost no draws (the next pick follows at once).

The bandage test (0xA806 with 0) is also what the end of round uses for its message about a dying party member.

### Move (0xA1E6) — P, D

Saves movement +6, facing +9 and the square. Loops while +6 > 1 and the last key was not Escape or Return, showing
the movement left (+6 shr 1):

- A direction key (keypad: Up 0, PgUp 1, Right 2, PgDn 3, Down 4, End 5, Left 6, Home 7; the digits 1–9 map to
  them): the creature faces that direction (+9 = direction, 0x31232); the square in that direction is looked up
  (0x313F3: creature on it, ground byte):
  - A creature: **attack it** (0xA54C, below).
  - Ground 0 (off the map): a yes/no flee question; yes → flee (0x13465: always ends the actions, returns done);
    no → not done.
  - The ground's cost (DS:2744, 4-byte entries) > +6: a message that the square can't be entered.
  - Else a step (0x130AD), then 0x12F0D; after each, a creature out of play ends its actions; then the terrain
    effects (0x2B9D1), and a helpless creature (START 0x1713) ends its actions. The AI's move (0xB29A–0xB6E2) makes
    the same calls.
- **Escape undoes the whole move**: +6 back to the saved value, the creature is put back on its starting square
  (0x317E1; done = not put back), facing restored. **D** (`m05_after_escape`: back on its square with +6 and facing
  as before).
- Return ends the move with what was walked; back to the menu (**D** `m06`).
- At the end, +6 < 2 becomes 0.

**Attack by moving into a creature** (0xA54C): a ranged weapon that is not throwable can't be used this way (a
message, nothing else). Else the attack-ally check (0x15193, below); when it allows: +0x0A = the defender; the
**sweep** (0x1367D) when it applies → done; otherwise note the attack direction (0x13FF1) and MakeAttack (0x1407F,
no ammunition, `combat-format.md` "Attacks"), done = its result.

**Attack-ally check** (0x15193): a defender on the other side (0x10E = the opposite of the attacker's, START 0x2F57)
or a computer-controlled one (0x10F ≠ 0) is attacked without a question. A player-controlled ally brings a yes/no
question; yes: DS:6DE2 = 1 (a byte nothing else reads), VM $6E33 = 1, every creature with status 0 and 0x84 > 0x7F
(the NPCs) becomes hostile (0x10E = 1) and forgets its target (+0x0A), the teams are counted again (START 0x2F7B).

### Aim (0x15D53) — P, D partly

Arguments: creature, range (0xFF = from the weapon), attack mode (1 from the menu), square mode, done, result.

- **Range**: 0xFF → with a weapon in the weapon slot, its base range (byte 12) − 1; no weapon → 1; a result of 0 or
  0xFF becomes 1.
- **Candidates** (0x15AE1): the area search (0x3045B) from the creature's square with range 0x7F, both sides; the list
  is walked from its first entry (**D**: the first is the creature itself, range 0) with Next (+1) and Prev (−1),
  wrapping at both ends (0x15B84). A candidate that fails the target check (0x13880, below) is skipped in the current
  direction.
- For each shown candidate (0x1529B): distance (START 0x30FB) and the entries Target (when allowed), Next, Prev,
  Manual, Center, Exit. **Target is offered** when distance ≤ range and, in attack mode, the candidate is not the
  creature itself and either it has no ranged weapon, or it has one with ammunition (START 0x3A2B) and no enemy
  stands next to it (START 0x2FD3 range 1) unless the weapon is throwable. Outside attack mode only distance ≤
  range counts.
- **Target** (0x15438, attack mode): the attack-ally check; when it allows: the result holds the defender and its
  square; the sweep when it applies → actions end (START 0x31D7; the move path does not do this); otherwise note the
  direction (0x13FF1), ammunition = the ranged weapon's ammunition, except a throwable weapon at distance 1 uses none;
  MakeAttack (0x1407F) with it. Aim keeps running while not done and the key was not Exit / Escape.
- **Manual** (0x155F5): a cursor on squares (0..49 × 0..24), path distance from the creature (0x2FF74) shown; Target
  / Return accept a square when reachable, distance ≤ range, its ground cost is not 0xFF, the creature there passes
  the target check, and in attack mode it is not the creature itself, not a downed creature's square (ground 0x1F
  with no live occupant; the bodies list DS:66D6, 7-byte entries, count DS:6715), plus the ranged-weapon rule above;
  with no creature on it only in square mode. The Core model takes targets from the candidate list only; manual
  squares are left to the Client `[?]`.
- **D**: `m09`/`m10` (aim starts on the creature itself at range 0, Next goes to a goblin at range 1 with Target
  offered, Target makes the melee attack); `fight_ranged` (darts, range 6 − 1 = 5: a goblin at range 3 is offered;
  three darts thrown, count 11 → 8).

**Target check** (0x13880, attacker, defender): no defender → no; the attacker itself → yes; else DS:681E = 0,
effects with trigger 1 on the defender; if still 0, the attacker's +0x0A is set to the defender for effects with
trigger 0 on the attacker and restored; yes when DS:681E is still 0.

### Cast (0x14C0F, from the menu with no spell) — P

The spell list (0x239D6) picks a spell. A spell whose table entry (16 bytes at DS:3200 + 16 × spell) has byte 5 = 0
can't be cast in a fight (a message that the spell is for camping only, nothing else). Casting time = byte 6 div 3:

- 0: the spell is cast now (0x27C38; it picks its own targets) and the actions end (START 0x31D7).
- Else: done, a message that casting has started, +0 = the spell, initiative − casting time when the initiative is larger,
  else 1. The creature is picked again later in the round and step 2 of the menu casts the spell.

## Sweep attacks (0x1367D, attacker, defender) — P

Called by the menu's move-attack (0xA5BD), Aim's Target (0x154F2) and the AI (0xBA3C). Applies (returns true) only
when all of:

1. Phase-1 attacks left 0x113 < sweep budget +5 (set from record 0x6B at the start of round).
2. The defender's hit dice / level 0x73 = 0 (less than one hit die).
3. Distance (START 0x30FB) = 1.
4. The enemies of the attacker next to it (START 0x2FD3 range 1: the area search's creatures whose 0x10E is the
   opposite side, in the search's order) with 0x73 = 0 number more than 0x113.

Then: count = min(that number, +5); a message that the attacker sweeps; the defender is put first in the list (its
place and the first entry swap; `[?]` a defender that is not in the list, e.g. an ally, writes the old first entry
to an index the program never set); then for each list entry in order, while count > 0 and the entry's 0x73 = 0:
note the attack direction (0x13FF1), 0x113 = 1, MakeAttack (0x1407F, no ammunition), count − 1. So each swept
creature gets one phase-1 attack (and the phase-2 attacks left 0x114 go to the first one, as the attack routine
runs phase 2 first). No draws besides the attacks'.

## Turn undead (0x13965, cleric) — P; D partly

Called by the menu's Turn and by the AI (0xAE01).

1. Message that the cleric turns undead; +0x11 = 1; `left` = 6.
2. **Draws**: `count` = 1d12, then `roll` = 1d20 (dice 0x2BCCD), always both, before any target is looked at.
3. Column: cleric level 0x96 (signed) 1–8 → that level; 9–13 → 9; anything else (14 and up, or 0 and below) → 10.
4. Loop: the target (0x13B4C) is, among the cleric's enemies the area search finds at any range (START 0x2FD3
   range 0xFF), the first with combat record +0x10 = 0 and 0x76 > 0 (signed; the turn-undead row, `character-format.md`)
   and the lowest 0x76 (strictly lower than the best so far, starting at 13, so rows 1–12). Stop when there is none,
   when `count` = 0, or after a failure.
   - v = the signed byte at DS:0447 + 10 × row + column (a table in START.EXE, read from the player's file, not
     copied here). Rows 1–10 × columns 1–10 are the 100 bytes DS:0452–DS:04B5; a Pascal string follows. The
     monsters of PoR use rows 1, 2, 3, 5, 7, 8, 9 and 10 (0x76 surveyed over all MON*CHA records), so rows 11 and
     12, which would read the string, never occur.
   - `roll` < |v|: failure; the loop stops.
   - Else the creature is affected: v > 0 → turned (+0x10 = 1, message); v ≤ 0 → destroyed (message, taken off the
     map 0x3154B, status 8, in play 0). `left` − 1 when > 0; `count` − 1; when `count` reaches 0 while `left` > 0
     and v < 0, `count` = 1 again.
5. Nothing affected: a message that it had no effect. Teams counted (START 0x2F7B); actions end (DS:6E31 = 1).

The status 8 of a destroyed undead is not one of the dying / dead values the damage code uses (6 dead); the
after-combat step treats it as "not okay" `[?]` (not checked in a capture).

**D** (`fight_moves` t01, a cleric's Turn with no undead on the map): +0x11 = 1 and the actions ended (DS:6E31 = 1);
the pick that followed (a sleeping goblin guard before another guard of the same initiative) matches two draws for
the turn and not none. A capture with undead is still missing.

## Random draws, in order

- Menu, Done menu, Aim, Move, View: none of their own. The step, flee (0x13465: 1d2 on a speed tie), attacks,
  spells and items draw in their routines.
- Sweep: only the attacks' draws, target by target in list order.
- Turn undead: 1d12, 1d20, nothing else.
- Quick / Alt-Q: the AI's draws.

## Open `[?]`

- View (0x21AED) and Use (0x21F32) screens; the area search's order (0x3045B) and the path distance; the step
  routines (0x130AD, 0x12F0D) and flee (0x13465) in detail (combat-map work).
- Manual aim in the Core model.
- Sweep with a defender missing from the adjacent-enemy list.
- Turn undead against real undead (no capture yet).
- Status 8 after turn undead in the after-combat step.
