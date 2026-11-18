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
| V | 0x21AED, then 0x1351E | the character view screen ("View / Use" below); then attacks left are recomputed (`combat-format.md` "Attacks left"); "done" is what the screen reports |
| A | 0x15D53 | Aim (below) with range 0xFF ("from the weapon"), attack mode |
| U | 0x21F32, then 0x1351E | the Items screen ("View / Use" below), then attacks left recomputed |
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
- **Manual** (0x155F5; read in full s15): a cursor on squares, starting on the shown candidate's square. Each pass
  moves it one square (arrows, Home, PgUp, End, PgDn: directions 0–7 through DS:2736/273F; C centres the view and
  the cursor stays; any other key: no move), clamped to 0..49 × 0..24. Then the line 0x2FF74 from the aimer to the
  cursor (length argument 0xFF, so only blocking terrain stops it); the distance shown is the walked half squares ÷ 2
  ("Range = n" on row 23 in attack mode when the line got through). The creature under the cursor, only when the line
  got through: the one in the cell's slot (0x30BF8), else on ground 0x1F the last entry of the bodies list DS:66D6
  (7-byte entries, count DS:6715) lying there. Target is offered when the line got through, range ≥ distance and the
  ground's cost (DS:2744) is not 0xFF; and with a creature: it passes the target check (0x13880; run even when out of
  range), and in attack mode it is not the aimer, not a body square with no live occupant, plus the ranged-weapon rule
  above; with no creature: only in square mode. Menu: a cursor-keys hint + a target word (when offered) + "Center
  Exit". Target / Return: when offered, the result = the creature (or none) and the square; attack mode → the attack
  0x15438 (done as it reports), else done = 1; when not offered or not done the result is cleared and done = 0.
  Manual ends after Target, Return, E or Escape (set CS:0x2DA7); Aim then builds its list again (0x15AE1) and goes on
  from the same place unless done. The menu's Aim passes square mode 0 (0x9BEE). Core: `FightMenu.Manual`
  (`IFightMenuChooser.ChooseManual`, `ManualView`, `ManualInput`; needs the `CombatMap`).
- **D**: `m09`/`m10` (aim starts on the creature itself at range 0, Next goes to a goblin at range 1 with Target
  offered, Target makes the melee attack); `fight_ranged` (darts, range 6 − 1 = 5: a goblin at range 3 is offered;
  three darts thrown, count 11 → 8).

**Target check** (0x13880, attacker, defender): no defender → no; the attacker itself → yes; else DS:681E = 0,
effects with trigger 1 on the defender; if still 0, the attacker's +0x0A is set to the defender for effects with
trigger 0 on the attacker and restored; yes when DS:681E is still 0.

### Cast (0x14C0F, from the menu with no spell) — P

The spell list (0x239D6) picks a spell. A spell whose table entry (16 bytes at DS:3200 + 16 × spell) has byte 5 = 0
can't be cast in a fight (a message that the spell is for camping only, nothing else). Casting time = byte 6 div 3:

- 0: the spell is cast now (0x27C38, below) and the actions end (START 0x31D7).
- Else: done, a message that casting has started, +0 = the spell, initiative − casting time when the initiative is larger,
  else 1. The creature is picked again later in the round and step 2 of the menu casts the spell (0x27C38 again, aimed
  then, not when it was begun).

**The player's aim for a spell** (0x27C38 → target pick 0x148BE with ai = 0 → 0x14611) — P, D (Sleep):

- 0x27C38 in a fight loops on the pick: picked → the cast; not picked → for the player a yes/no question whether to
  abort (0x27EF2): yes → message, the memorised copy is removed (START 0x202B), no cast; no → the pick (and the aim) runs
  again. The AI (ai ≠ 0) loses the spell at once.
- Pick mode = spell table byte 6 AND 0x0F: 0 → the caster, no aim; 0x0F → one aim with square mode 0; 8–0x0E → one aim
  with square mode 1, the target list around DS:6DB9/6DBA; 1–7 → (byte 6 AND 3) + 1 aims (0x14AD1): an aim left without
  a target uses up a pick, a creature already taken gives a message and is aimed again (no pick used).
- 0x14611 for the player: Aim (0x15D53) with the creature, range = 0x27741 (the spell's range for the caster; 0 or 0xFF
  becomes 1 in Aim), **attack mode 0**, the square mode; afterwards the caster's +0x0A = the aim's result creature (the
  result is cleared at the start of Aim, so none after Exit). Done → DS:6DB9/6DBA = the result's square.
- Aim in attack mode 0: Target is offered when the distance ≤ range (the creature itself included); Target (0x15438)
  only stores the candidate and its square as the result and reports done — no attack-ally question, no attack.
- After a pick, in a fight (0x27D9C–0x27E00): the caster **faces the aimed square** (the first direction whose cone,
  0x300CD, holds DS:6DB9/6DBA; its own square gives 0) through 0x31232, so combat record +9 changes. **D**: Rhiannon's
  +9 6 → 0 casting Sleep at the goblin north of her, DS:6DB9/6DBA = 26,12 afterwards (`fight_moves` s03, r01).
- Square mode (8–0x0E only, 0x14A27; the other modes pass 0): Manual may take a bare square; the pick is made when the
  aim is done and the list is built around the aimed square (DS:6DB9/6DBA = the result's square), the caster's +0x0A
  = none. Core: `FightMenu.AimSpell` (`FightManualAimTests`: Sleep at an empty square).

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

## Replay of `fight_moves` — D

`FightMovesReplayTests` replays the whole player-driven capture through the wired fight (`FightKit.Create`,
`MenuController`, `FightMenu`) from `fight_quick/r0_start` (the arena seed found as `FightQuickReplayTests` does), with a
chooser that presses the recorded keys: Darkstar M S S N Escape, M S S Return, D Delay; Thrender A Next Target;
Bakshi D Quit; Phineas D Quit; Rhiannon C Sleep, aim Next Target; Sean T; Darkstar (delayed) A Next. Each key is asked
for by the creature it was pressed for, in order; the 20 states taken at a prompt are compared with the replay at that
prompt, `s03` and `t01` (taken in a message delay) with the snapshots between their draw and the next. All 22 states
match (RandSeed, round, current creature, squares, HP, status, in play, Quick, memorised spells, effects, every
combat record byte and remembered target), 240 draws.

What the replay needed: the player's spell aim above (the menu passed no target, so the Sleep was lost) and the
caster's facing after the pick; a spell begun earlier is now cast with `IFightSpells.ContinueCasting` (the menu used
`Cast`, which with a casting time would only begin it again). Which candidate Darkstar's aim showed after Next is not
dumped; a stack word at DS:C4FE that holds the aimed square in m09 (27,13, Thrender) and m10 (27,12, the goblin) holds
28,13 in r01, Bakshi's square, as the replay shows (a stack slot, so only a hint `[?]`).

## View / Use (0x21AED, 0x21F32) — P; checked under emulation in part

Read in session 13 (2026-11-17). Code: `src/GoldBox.Core/Game/FightItemScreens.cs` (`FightItemScreens :
IFightItemScreens`, asking an `IItemScreenChooser`; `FightKit` uses it when the menu's chooser also implements that
interface). Both screens work on the current creature (DS:5D92; View copies it to DS:471A and its redraw 0x21084 to
DS:4716). DS:49F3 tells where the screen is open: 1 shop, 5 fight (2, 3, 4, 6 other places `[?]`). "NPC in play"
below means 0x84 ≥ 0x80, in play (0x10D ≠ 0) and status ≠ 1.

**View** (0x21AED, var done): done = 0; loop until the key is Exit (or 0) or done: the menu (0x2CC8C) with

| Entry | Shown when | Does |
|---|---|---|
| Items | the item list (0xC8) is not empty | the Items screen 0x21F32 (it may set done) |
| Spells | a byte of 0x17–0x2B is non-zero | the memorised list (0x239D6 in show mode); nothing changes |
| Trade | not an NPC in play, the character has money (a word of 0x88–0x94 ≠ 0), not in a fight | money trade 0x23190 `[?]` |
| Drop | it has money | drop money 0x234E1 (below) |
| Exit | always | |

**Items** (0x21F32, var done; also the combat menu's U): loop until Exit, done, or the item count 0xC7 is 0: the
entries, then the item list (0x2D669: an entry key and the item under the cursor):

| Entry | Shown when | Does |
|---|---|---|
| Ready | always | ready / unready 0x2250A (`combat-ai.md` "Ready / unready") |
| Use | in play, VM $49E5 = 0, and DS:49F3 is 2–4, or a fight with combat record +2 (item use allowed) ≠ 0 | not readied → a "needs readying" message; a scroll (0x2A255) or power 0x3D > 0 with kind 0x3E < 0x80 → item use 0x22AC4 (`combat-ai.md` "Item use"), done as it reports (outside a fight done is reset to 0); else nothing. The player's pick is 0x27C38's (ai = 0): the same aim and abort loop as a cast from the menu (Core: `FightMenu.PickForItem`, through `IFightItemScreens.Use(fight, c, menu)`; `IItemScreenChooser.AimItem` only when the screens are opened without the menu) |
| Trade | not an NPC in play, not in a fight | 0x21DAA, then 0x2276E `[?]` |
| Drop | always | 0x21DAA, then "drop it?" → the item leaves the list (START 0x20A9) |
| Halve | 0xC7 < 16 | 0x22827 |
| Join | always | 0x228D7 |
| Sell | not an NPC in play, in a shop | 0x21DAA, then 0x22D28 (`shop-format.md`) |
| Id | in a shop | 0x22F8C `[?]` |

After every choice START 0x1758 recalculates the creature. So in a fight the Items screen offers Ready, Use (when
allowed), Drop, Halve, Join; only Use can end the turn.

- **Let go** (0x21DAA): a readied item → "must be unreadied", no. A scroll with a spell byte 0x3C–0x3E above 0x7F
  (flagged for scribing) → a yes/no question. Else yes.
- **Halve** (0x22827): half = count 0x39 shr 1; 0 → a message. Else a new copy (GetMem 0x3F, Move) with count =
  half and readied 0 is linked right after the item, which keeps count − half.
- **Join** (0x228D7): total = the item's count; walking the current creature's list, every other item with count >
  0 and the same 0x2F, 0x30, 0x31, 0x2E, 0x32, 0x33, 0x36 and word 0x37 and charges 0x3C < 2 is merged: total + its
  count ≤ 255 → added, the item leaves (START 0x20A9); else the item being filled gets 255, the other keeps the rest
  and becomes the one being filled. At the end that one gets the total. The program compares 0x3C, 0x3D and 0x3E of
  the other item **with itself**, so only "0x3C < 2" filters; readied, hidden and value are not compared.
- **Drop money** (0x234E1): until no money is left or the player stops: a coin kind among those held (list 0x2D669,
  names DS:0FFA + 11·kind) and an amount (input 0x24F1D, bounded by what is held); 0x2568A: coins − amount, weight
  0x102 − amount (0x24CA9); in a shop or with DS:49F3 = 6 the pool (DS:67F4 + 4·kind) gains them, in a fight they
  are lost.

**Checked under emulation** (`tools/scripts/por_ai_oracle.py`, `AiCastProgramTests`): Halve and Join on 160
generated item lists match the program item for item; the item use 0x22AC4 on 120 cases (`combat-ai.md`).

## Random draws, in order

- Menu, Done menu, Aim, Move, View: none of their own. The step, flee (0x13465: 1d2 on a speed tie), attacks,
  spells and items draw in their routines.
- Sweep: only the attacks' draws, target by target in list order.
- Turn undead: 1d12, 1d20, nothing else.
- Quick / Alt-Q: the AI's draws.

## The combat screen (Client) — D measured, drawing routines not read

Measured on DOSBox-X screenshots (s13: save A, a random orc fight at 10,2 and its menus, Move, Aim, Manual, the Done
and Speed menus, an AI attack at game speed 9). Code: `src/GoldBox.Client/Screens/CombatScreen.cs`,
`Play/CombatPlayer.cs` (`CombatChooser`, an `IFightMenuChooser`), `Play/CombatArt.cs`, `Play/CombatText.cs`.

- **Layout** (8 × 8 text cells): braid frame on rows 0 and 22 and columns 0, 22, 39, yellow corners where they meet;
  the map window of 7 × 7 squares of 24 px at pixel (8, 8); the status panel in columns 23–38: the creature's name on
  row 1 (party cyan, hostile yellow), hit points row 3, AC row 5, the readied weapon's name row 7 (none for a creature
  with no weapon); attack messages from row 10. Row 23: Aim's range line; row 24: the menu, or Move's prompt in
  green. A screen square (c, r) shows arena cell (view x + c, view y + r): no skew in the drawing.
- **Palette**: EGA colours 0 and 8 are swapped for the whole screen (ground, panel and menu background dark grey,
  the art's colour 8 black). Every DUNGCOM tile of a start screen matched pixel for pixel only with the swap
  (25 367 against 11 158 matching pixels over the 49 squares). The palette write is not located `[?]`.
- **Ground**: the terrain table's tile byte (DS:2744 + 4t + 3) indexes DUNGCOM block 1 (25 tiles of 24 × 24) in a
  3D area; tile 22 is all colour 0. WILDCOM outdoors and the RANDCOM tiles for the rare terrains are not checked `[?]`.
- **Icons**: player characters (record 0x84 < 0x80) get the party icon, other creatures the CPIC block = their icon slot's picture (LoadCombatIcon; a joined NPC not checked `[?]`), attack pose + 128 (seen in the AI
  burst); party members are CBODY + CHEAD (head on the top rows) recoloured by 0xC1–0xC6, + 64 for icon size 2: all six
  members of save A matched pixel for pixel (`CombatArt.Party`). Colour 0 is see-through. Facing 5–7 mirrors the
  icon (party at 7 mirrored, orcs at 3 not, both pixel-exact); s15 (save K, Rhiannon stepping): facing 0 and 4 show
  the stored icon, 6 the mirrored one. The party's attack pose (+ 128) is from the art only `[?]`; DOSBox-X drew a
  creature that had just stepped with the sword raised (the other pose?) `[?]`.
- **Cursor box**: under the icon of the creature whose menu is up (and of the aim candidate, the manual cursor): fill
  colour 7, top row and left two columns white, right two columns and bottom row black (colour 8 before the swap).
  None while moving or during the AI's turns.
- **Menus**: the menu routine draws the prompt (colour 13) and the entries right after it with no space (Aim's prompt 0x15296 then
  the first entry); the highlighted entry all white, the others with a white hotkey. Aim adds the range line on row 23;
  Manual shows its own prompt (GAME.OVR 0x155E2) with Target (when the square's creature can be taken), Center, Exit,
  and the panel shows the creature under the cursor (blank on an empty square). The speed menu's prompt is
  0xA8D6 + the speed + ") : ". The highlighted entry when a menu opens `[?]`: the first combat menu after loading
  a save highlighted View (index 1, as the area menu's Cast before it), later ones Move (s15).
- **Attack messages** (strings 0x12A2D–0x12AEA): attacker name, the attacks word, defender name, then on a hit the
  damage on two lines (the hitting word + the number; the points word + the damage word) or the misses word; a creature
  that drops: a blank line, its name in light red, the down word (0x12AB3). A skull on red (COMSPR 11) covers the hit
  defender's square while the message is up. **Read (s15, 0x12D4B)**: a defender out of play (0x10D = 0) gets the
  down message (START 0x16EC, defender + 0x12AB3) two rows on; then status 5 → the dying words 0x12ABD in green at
  column 23 on the next row; status 6, 7 or 8 (Pascal set at 0x12ACA) → START 0x16EC with the killed words 0x12AEA.
- **Pauses** (read s15): START 0x709:0x0DBE is `Delay(DS:49E2 × 225)` ms (game speed; 0 = no wait), called after the
  attack message, by the message routine and the item use. Steps and turn changes `[?]` (the Client: 60 + 30 × speed ms).
- **Messages** (read s15): START 0x1693 clears row 24, writes the text at column 0 in green, pauses, clears row 24
  again (the item screens' refusals). START 0x16EC writes a creature and a text into the panel from a given row
  (cols 23–38). The cast message (0x27D48, only when 0x27C38's third argument ≠ 0 and DS:6DBF = 0, i.e. not from an
  item or scroll) comes **before** the target pick: caster, 0x27C16, spell name (0x2A332). The AI's 0x27C38(spell, 1, 1)
  shows it. The Client announces the player's cast-now spells from the spell list (before the aim) and everything
  else through `FightSpells.Announce` (a begun player spell after its aim `[?]`). Item use (0x22B58, DS:6DBF = 1, not
  scrolls): START 0x16EC with 0x22AB1 from row 10, "Item:" (0x22ABE) on row 23 col 0 green and the item name at col 5,
  the pause (`FightSpells.ItemUsed`). The AI's own messages: 0xABC0, 0xBB6E, 0xBB77, 0xBC7A, 0xBC8C (panel, layout `[?]`).
- **Not read `[?]`**: the view's scrolling (0x310D0; the Client keeps the creature in the inner 3 × 3 squares and the
  view inside the arena),
  the spell list 0x239D6 (the Client: a full-screen list; spell names from START.EXE's string array of the widest
  entries, taken to start at spell 1), the Guard message, monster icon recolouring (orc icons matched unchanged).
- **Manual in the Client**: Aim's Manual answers `AimKey.Manual` and each cursor pass is Core's (`ChooseManual`):
  arrows / pad move it one square, Return / T Target (ending Manual even where it is not offered), E / Escape Exit,
  C / shoulders re-centre; the panel shows `ManualView.Creature`, row 23 the range to the square in attack mode when the line got
  through (`ManualView.LineReached`). Bare squares work in a spell's square mode. A mouse click on another square
  walks the cursor there one pass per square (our mapping; the target check runs on each square crossed, as with the
  keys); a click on the cursor's square takes it when offered.

- **Parity check** (s15): save K's fight start (Rhiannon's first menu) in the Client and in DOSBox-X: the map window
  and the status panel equal pixel for pixel; differences only in the braided frame (stand-in) and the menu's starting
  highlight (above).

### View / Use screens in a fight (Client, s15) — D measured, P partly

DOSBox-X, save K (Darkstar, Thrender), `Play/CombatItemScreens.cs` (`CombatChooser` as `IItemScreenChooser`):

- **Items** (0x21F32): braid on rows 0, 2 and 23 and columns 0 and 39 (yellow corners); row 1 the name + "'S" in the
  name colour and 0x21EEC at column name length + 4 in green (0x2220B); row 3 the heading 0x21EF2 in white; the list
  window (1,5)–(38,22) (0x2229B): readied word at column 2, the item's name at column 7, the selected row white, the
  others green; menu row 24 with no prompt: Ready, Use (when allowed), Drop, Halve, Join, Exit, first entry
  highlighted each time, the row kept. Opened by the menu's Use it stays on the combat palette (grey); opened from
  View it is black. The keypad's Home / End move the list (the arrow keys do not).
- **Drop**: a warning naming the item (it will be lost for good) in yellow on row 21 (0x22401, window rows 21–22), every row green,
  then a drop question with Yes / No and No highlighted. Must be readied etc.: the message routine 0x1693 (row 24).
- **View** (0x21AED, page 0x21084): black, braid rows 0 and 23, name row 1 cyan; rows 3–5 gender, race, "Age", the
  alignment, the class; rows 7–12 the abilities (col 1) with the money by kind from col ~12 (Platinum, Silver right-
  aligned); row 15 Level and Exp (col 17); rows 17–18 AC / HP, THAC0 / Damage, Encumbrance / Movement in three
  columns; row 20 Weapon; row 22 Status; row 24 "VIEW:" and the entries. The Client draws its own sheet `[?]`.
- **Drop money** (0x234E1): a coin-type prompt (colour 13) then Exit; the sheet's money lines are the list (picked
  one white); then an amount question naming the coin, in green on row 24 and the typed number.

## Open `[?]`

- Trade (0x2276E, 0x23190), Sell (0x22D28) and Id (0x22F8C) on the item screens (not shown in a fight); the money
  input 0x24F1D in detail; the area search's order (0x3045B) and the path distance; the step
  routines (0x130AD, 0x12F0D) and flee (0x13465) in detail (combat-map work).
- Manual: the view scrolling 0x310D0 and the cursor drawing 0x30A0F (display only); no DOSBox capture of Manual yet.
- The abort question's wording and what it shows (0x27EF2); the 1–7 mode's repeat message (0x14B7F).
- Sweep with a defender missing from the adjacent-enemy list.
- Turn undead against real undead (no capture yet).
- Status 8 after turn undead in the after-combat step.
