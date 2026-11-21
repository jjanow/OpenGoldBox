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
| Alt-X | 0x1638E | a debugging switch: only when the program was started with a particular command-line word (ParamStr(1) compared with the word at GAME.OVR 0x16374, a byte-identical copy of the hall's J word, training.md; read s23), its message (0x1637A, START 0x2233), every hostile creature (0x10E = 1) gets in play 0 and status 6 and every creature's actions end, the map redrawn; done. Otherwise 0x1638E returns 0 and the menu shows the refusal (0x9B0F), not done. The AI's key check (0xBB81) calls the same routine on scan 0x2D and ignores its result (no refusal there) |

Client (s23): the Alt keys are actions with modifier bindings (`Alt+M`, `Alt+Q`, `Alt+X`; the bindings file takes
"Alt+" / "Ctrl+" prefixes, and a bare key does not count while Alt or Ctrl is held): `FightQuickMagic` (left
trigger), `FightAllQuick` (right trigger), `FightAltX` (left-stick press, the hall J button); with the mouse or pad,
row 23 shows them under the main menu ("ALT: MAGIC ALL-QUICK ALT-X", click a word). Alt-X works when the start word
(`--param`, the controls screen's START WORD) equals the program's word (Core `FightKit.AltXEnabled`). Alt-R is not
offered (it does nothing).

**Messages of the Alt keys (P + D, s24):** Alt-M's two messages (0x9CD1 / 0x9CE7), Alt-X's refusal (0x9DC0) and
Alt-X's own message (0x1637A in 0x1638E) all go through the message routine START 0x1693 (0x2233): row 24 cleared
(0x3F1:0x137 over columns 0–39), the text at column 0 in **colour 10**, PauseOnly (DS:49E2 × 225 ms), row 24 cleared.
The panel keeps the current creature and the map its cursor box meanwhile; the menu line comes back after it. The AI's
key check uses the same routine for its Alt-M messages (0xBBD7 / 0xBBED) and through 0x1638E for Alt-X. After Alt-X's
message, 0x1638E removes the hostile creatures and redraws the map (0x3E0:0x52): the goblins and the cursor box go,
then the end of the fight asks the continue question. DOSBox-X (test save with eight goblins, game speed 4): each
message about 0.9 s on row 24, the refusal and Alt-M the same. Client: `CombatChooser.Notice` / `AiSays` →
`PageSay` (row 24 over the map in `CombatScreen.DrawLines`), the cursor box dropped after Alt-X's message.

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
  three darts thrown, count 11 → 8). Manual (s18, `fight_manual_use`): the arrow keys move the cursor; it starts on
  the aimer ("Range = 0", no Target); a bare square shows the range and no Target in square mode 0; an ally and an
  enemy in reach get Target; Target makes the same melee attack or dart throw as the candidate list, and the play
  matches to the next menu (`FightManualUseCaptureTests`).

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

**D** (s18, `fight_manual_use` p01 → p03): Phineas uses a readied wand (power 88 = spell 65) from the menu: U, U, the
item's aim (the menu's candidate list), Target: 2d4 + 2 damage, one charge off, his +0x0A on the target, and the
play matches to the next menu (`FightManualUseCaptureTests`).

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
- **Palette** (located s18): EGA colours 0 and 8 are swapped for the whole screen (ground, panel and menu background
  dark grey, the art's colour 8 black; every DUNGCOM tile matched only with the swap). GAME.OVR 0x3081D writes EGA
  palette register 0 = colour 8 and register 8 = colour 0 (START 0x464:0x2A0D, INT 10h), only in EGA mode (DS:4A1E =
  1); 0x3083F writes them back. The combat screen's setup (START 0x3729, before its frame) sets the swap; the fight's
  end (0x986B) restores it. View (0x21AED) restores it on entry (0x21AFB) and sets it again on exit (0x21D4A), both
  only when DS:49F3 = 5 (a fight): so View and the Items opened from it are black, Items opened by the menu's Use stay
  grey. The icon editor uses the same pair (0x1D1D9 / 0x1D8A7). The Client swaps the frame's indices 0 and 8
  (`CombatScreen.SwapGrey`; DOS art only, Amiga indices ≥ 16 untouched).
- **Ground**: the terrain table's tile byte (DS:2744 + 4t + 3) indexes DUNGCOM block 1 (25 tiles of 24 × 24) in a
  3D area; tile 22 is all colour 0. The rare terrains 0x1A–0x1F draw RANDCOM block 1 tiles 0–5 (tile bytes
  0x22–0x27, `CombatArt.RareFirstTile`); DOSBox-X s24 (save K's Slums fight, several boots, cells scrolled into view
  with Manual aim): terrain 26 (tile 0), 27 (tile 1) and two bodies 31 (tile 5) pixel-exact with the 0/8 swap
  (`CombatScreenGameTests.Rare_terrain_in_a_dungeon_fight_matches_dosbox`).
- **Ground, program read (P, s26).** The fight start 0xD815 picks the set by DS:49FA alone: 1 (a 3D area) → DUNGCOM
  (0xD81B–0xD837), anything else → WILDCOM (0xD843–0xD853), both block 1 loaded at tile 0, then RANDCOM block 1 at tile
  0x22 (0xD867). The loader 0x31A8E takes (file, first tile, a last-tile byte): the last-tile byte (0x18, 0x21, 5; 0x28
  for BACPAC) is **never read**; it copies the block's own item count × item size into the low tile sheet DS:5E52 at
  first tile × item size, so the v1.3 files fill tiles 0–0x18 (DUNGCOM, 25 items), 0–0x21 (WILDCOM, 34 items) and
  0x22–0x27 (RANDCOM, 6 items). All four ground draws (0x30938, 0x30DAF, 0x30E10, 0x31098) take the cell's terrain t,
  read the tile byte DS:2744 + 4t + 3 and call the tile draw 0x31B5D, which draws item [tile] of DS:5E52 for a byte
  ≤ 0x7F (DS:5E56's item [tile AND 0x7F] above): no special case for any terrain. The table's tile bytes (v1.3):
  terrains 0–25 → 0–24 (0 and 1 both 0), 26–31 → 0x22–0x27, 32–65 → 0–33 (64, 65 read from the next table). So
  terrains **28, 29, 30** draw RANDCOM items 2, 3, 4 by the same code that drew 26, 27 and 31 pixel-exact (28/29 are
  the 1-in-10,000 rare features, 30 = 0x1E the area-spell cloud of `combat-map.md`); outdoors the wilderness builder's
  terrains (23 → 22, 26–31, 32–65) draw WILDCOM items 0–33 and RANDCOM the same way, with the same screen-wide 0/8
  swap. In a dungeon fight tiles 0x19–0x21 keep what the sheet held before (SQRPACI / BACPAC, or a previous WILDCOM);
  no dungeon terrain reaches them. Client: `ClientPlayer.Fight` sets `CombatView.Dungeon` from DS:49FA
  (`EclMemory.ViewMode`), `CombatArt.Ground` indexes the set by the tile byte below 0x22 and RANDCOM from 0x22
  (`CombatScreenGameTests.Every_rare_terrain_draws_a_randcom_tile`, `Every_wilderness_terrain_draws_a_wildcom_tile`).
  **D** (s26, `MACHINE=ega`): save A put on script 26's overland at 12,13 N (`goldbox-tools save-overland`) with
  vector 0 = CLRMON, LOADMON 1 ×2, COMBAT, one step north: an outdoor fight (DS:49FA = 3); on its first screen every
  cell with no creature, against the arena from the state, matched the Client's WILDCOM tile pixel for pixel with the
  0/8 swap: terrains 23, 33, 37, 42, 44, 46, 48, 57 (tiles 22, 1, 5, 10, 12, 14, 16, 25;
  `CombatScreenGameTests.Wilderness_ground_in_an_outdoor_fight_matches_dosbox`).
  **D** (s28, the same fight): terrains **28, 29 and 30 seen on screen** and pixel-exact against the Client's RANDCOM
  items 2, 3, 4 with the 0/8 swap. 28/29 come only from the rare-feature roll (1/10,000 per tile-22 cell), so the
  seed was chosen: RandSeed DS:4428 patched in a DOSBox-X state taken before the step (`dosbox-trace-method.md`); the
  arena builder starts 5 draws after it (the monster loader's; the boot's own seed 0xED17FD58 rebuilt its arena byte
  for byte), and `ArenaBuilder.BuildWilderness` picked seeds with a 28 or 29 near the party: 0x3A6D3172 gave 28 at
  31,14 (one column right of the first screen, shown by moving the Manual aim cursor past it) and 0x7A17D593 gave 29 at
  31,13 on the first screen, as predicted (`CombatMapProgramTests.Rare_features_28_and_29_match_dosbox_seeds`).
  Terrain 30: Darkstar given Stinking Cloud (`CHRDATA5.SAV` 0x2B = 34) cast it on an empty 2 × 2; the four cells were
  0x1E in the arena and drew RANDCOM item 4. The same screens added terrains 26 and 27 outdoors and WILDCOM 32, 34, 36,
  38, 40, 45, 47, 56 (one picture per terrain throughout).
- **Icons**: player characters (record 0x84 < 0x80) get the party icon, other creatures the CPIC block = their icon slot's picture (LoadCombatIcon; a joined NPC not checked `[?]`), attack pose + 128 (seen in the AI
  burst); party members are CBODY + CHEAD (head on the top rows) recoloured by 0xC1–0xC6, + 64 for icon size 2: all six
  members of save A matched pixel for pixel (`CombatArt.Party`). Colour 0 is see-through. **Facing 4–7 mirrors the
  icon** (read s29: the icon drawer GAME.OVR 0x31F68, far 03E9:0039, copies the slot's picture mirrored through START
  464:15C7 when the direction is above 3; every caller passes the combat record's +9; party at 7 mirrored, orcs at 3
  not, both pixel-exact; **D** s29: a vampire attacking the member straight below it, facing 4, mirrored; s15 had
  read facing 4 as stored, wrongly). The drawer takes the picture by the creature's icon slot (+0xBF, table DS:5E5A,
  two pictures a slot), so a **charmed member** (NPC byte 0xB3) keeps the party icon (**D** s29; the Client used to
  draw nothing for it). The party's attack pose (+ 128) is from the art only `[?]`; DOSBox-X drew a
  creature that had just stepped with the sword raised (the other pose?) `[?]`.
- **Cursor box** (read s19): the box sprite (icon cell 25 = COMSPR 25) painted into the view by DrawCursorCell through
  ScrollView 0x310D0 while the arena's cursor flag (+4) is set, on every cell of the footprint in +5 (0x319D2 sets
  the creature's icon size; the aim cursor size 1); fill colour 7, top row and left two columns white, right two
  columns and bottom row black (colour 8 before the swap). It is never erased on its own: it goes when its cell is
  redrawn (the creature steps or is drawn in its attack pose, 0x310D0 redraws the cell it scrolls to, the turn's end
  0x30A0F) or the view is rebuilt. The turn (0x99FD) calls 0x319D2(creature, 2, 1) for **every** creature, the AI's
  too. **D** (s19, save K all-Quick): the box under each AI creature at its turn start and under a casting MU, gone
  once it steps or attacks; none while the player moves.
- **Menus**: the menu routine draws the prompt (colour 13) and the entries right after it with no space (Aim's prompt 0x15296 then
  the first entry); the highlighted entry all white, the others with a white hotkey. Aim adds the range line on row 23;
  Manual shows its own prompt (GAME.OVR 0x155E2) with Target (when the square's creature can be taken), Center, Exit,
  and the panel shows the creature under the cursor (blank on an empty square). The speed menu's prompt is
  0xA8D6 + the speed + ") : ". The highlighted entry when a menu opens (read s18): the menu routine
  keeps one cell for every menu it draws, DS:6B2B (1-based): a menu opens on that entry, or on its first when the cell
  is past its last entry (0x2CCDE); a hotkey moves the cell to its entry (0x2CF48), ',' / '.' move it, Escape leaves
  it. **D** (s18, save K loaded as B from "A B J"): the area line on Cast, Darkstar's first menu on View, its View menu
  on Spells; Exit (4th) → the main menu's Done (7th) → the Done menu (Delay Quit Speed Exit) on Delay; Quit (2nd) →
  Thrender's menu on View, his View menu (Items Drop Exit) on Drop. Read s19: the item list routine 0x2D669 (Items screen, the coin
  list, VMENU) stores 1 in the cell once on entry (0x2D6B9) and then runs its menu through 0x2CC8C (so: first entry
  each time, and the cell left where its menu ends); the yes/no routine 0x2D9A0 stores 2 (0x2D9B9) before its menu
  (it opens on No, the cell left on the answer); past the last entry 0x2CCE7 stores 1 back. No other code writes the
  cell. DOSBox-X (s19, save A): Encamp → the camp menu on Rest (4th); R → the rest menu on Mins (4th); its Exit (7th) →
  the camp menu on Save; A → the Alter menu on its 5th; Escape → the camp menu on Alter; Save, C → the quit question
  on No; N → the camp menu on View. Return does nothing in the camp menu (0x2CC8C returns 13 only when its
  [bp+0x10] / [bp+0x0E] are set, and the camp does not use it); the Client's Confirm on the highlight is its own. The yes/no routine loops until N or Y (its set at cs:0x10B1), so Escape does nothing there; the Client's Cancel = No is its own too. Client: `MenuHighlight` (shared by the area line and the combat screen, seeded
  with the loaded slot's place among the saves present).
  **In a fight (P, s29):** the lists that go through 0x2D669 (overlay stub 03C5:0034) are the Items page (0x222C9),
  the coin list of drop money (0x236AF) and the spell list 0x239D6 (Cast, View's Spells, a scroll's spells: it calls
  0x270A2, which calls 0x2D669 at 0x271EF when the list is not empty). 0x2D669 with an empty list returns before the
  store. Its menu is the caller's text, then " Next" when rows remain below the window, " Prev" when the window is
  scrolled, and " Exit" when the flag [bp+0x12] is set (the words at CS:0D88/0D8E/0D94 of its unit, GAME.OVR
  0x2D657–0x2D668); the Items call and the coin list set the flag. The Items window is rows 5–22 (18 rows, more than
  a character's 16 items), the coin window rows 7–13 (7 kinds), so neither shows Next / Prev. The coin list's caller
  text (CS:248C of 0x234E1's unit) is empty: its menu is Exit alone, and a row is taken by the key the menu returns
  (0x234E1 takes the row whatever the key), so the cell is 1 when drop money ends and View's menu comes back on its
  first entry, not on Drop. The yes/no routine stores 2 once (0x2D9B9): an answer outside Y / N reopens the menu at
  0x2D9BE, on the cell as left. So: Items → U leaves the cell on Use (2nd), and an item's aim menu (0x1529B, 03C5:002A)
  opens on its 2nd entry, Prev; the abort question's No leaves it on the 2nd too. Client: `CombatScreen` sets and
  follows the cell as `ExploreScreen` does (yes/no to No, item lists and spell lists to the first entry, Left /
  Right move it, the chosen entry stays, Escape leaves it). **D** (s30): an item's drop question opens on No, the
  aim menu after Prev opens on Prev, the continue-battle question on No; frames equal ("s30 DOSBox-X checks").
- **Spell lists' Next / Prev** (P, s30): the spell list 0x239D6(kind, action, var position, var not empty) builds
  the list (0x275B4, kinds as camp.md "Spell lists"; 2 = a scroll's spells) and, for a position below 0, draws the
  window and the title (name through START 0x1816, then the list's words with 0x76B6); 0x270A2(action, var position)
  takes the action word (1 Cast, 2 Memorize, 3 Scribe, 4 Learn; 0 none: View's Spells and the camp's pending lists),
  the choose-spell prompt (CS:003B) for actions 1–4 (its set CS:001B = {1..4}), Exit for every action but Learn, and the
  window (1, 5)–(38, 22), Memorize's (1, 5)–(38, 15) (0x2713C; the slot lines go below). For a position below 0 it
  draws the window (1, 3)–(38, y1) and sets the position 0. It calls 0x2D669 until the key is in its set CS:004A
  (0, Enter, C, E, L, M, S). Callers: combat Cast 0x14C0F (kind 0, action 1, position −1 each time), View's Spells
  0x21AED (0, 0), a scroll 0x22AC4 (2, 1), camp Cast 0x17CD2 (0, 1; −1 before each list), Memorize 0x1813B (5, 0
  the pending list; 1, 2 the book, position −1 once before the loop, 0x181B3, then kept), Scribe 0x1838C (6, 0; 3, 3,
  −1 once, 0x18404), the hall's Learn 0x1C32D (4, 4; −1 once at 0x1C8EB, the loop back to 0x1C8F0 keeps it).
  **0x2D669** (rows R = y1 − y0 + 1: 18, Memorize 11; N = the entries, headings included; L = the leading entries
  with +0x29 ≠ 0, the headings at the head; the window's top DS:6B29 is one global word, 1 after the start-up,
  0xE9DA): on entry the top goes up to the position when below it, to 0 when past N; the position + 1, then one row
  up (0x2D594(0): below the top → top + R − 1; past N − 1 → N − 1) and past headings upwards (0x2D3D2(0), at most R
  steps, same wrapping). So a list whose row 0 is a heading starts on the window's last row, or the list's (the hall's
  Learn: DOSBox-X showed the last row highlighted). Each pass: the menu line is the caller's word, then " Next" when
  N − R > top, " Prev" when top > L, " Exit" with the flag (CS:0D88 / 0D8E / 0D94 of the unit: the words the aim menu
  has too), through 0x2CC8C with keypad flag 1 (colours 13, 10, 15). Keys: N / P (only as shown entries' hotkeys) and
  PgDn / PgUp (only while the word is shown) move the window by R (0x2D502: top + R, at most N − R; top − R, at least
  0) with the highlight at the same place in it, then past headings in that direction; Home / End (keypad 7 / 1) move
  the highlight up / down inside the window, wrapping there (down: past the window's last row or the list's → the
  top); Escape, E, 0 → nothing; any other key (Enter, the action's letter) → the entry at the position, and the key.
  The window never scrolls with the highlight. All its text is fixed-cell (START 0x76B6, the menu routine): nothing
  types. Client: `ListRequest.Paging` / `ListPaging`, `ListPage` (the window, Next / Prev put before Exit, Up / Down
  as Home / End), `SpellLists` (rows, words from `CombatMsg.AimNextPrevManual`; no importer change); used by the
  combat Cast and View's Spells (now the memorised list with level headings, camp-only spells included: Core refuses
  those after the pick), the camp's lists (Memorize / Scribe keep the row and top, `ListPlace`) and the hall's Learn;
  tests `SpellListPagingTests`. Enter (decided, user, s30: as the program): the program's Enter always returns 13, which picks the row and ends the list (13 is in CS:004A; View's Spells, with no action, just ends), so a keyboard or pad Confirm with Next / Prev lit runs the first action word on the highlighted row (a click on the word still pages); PgDn / PgUp page while their word shows, and the pad pages with its shoulders / triggers (no original; no paging list picks a member). **The screen** (P + D, s33): 0x239D6
  (position below 0) draws the window (1, 1)–(38, 1) with START 03F1:02E8 (inside cleared, frame one cell outside),
  the name through START 0x1816 with its flag (the name, then CS:1813 "'s", in the name colour of START 0x23B6) at
  (1, 1), and "Spells " + the list's words at (name length + 4, 1) in green; 0x270A2 draws the window (1, 3)–(38, y1)
  (y1 22, Memorize's 15) and passes the choose-spell prompt and colours 13 / 10 / 15 to 0x2D669: labels 13, rows 10, the
  highlight 15, no cursor glyph; row 24 the prompt (13) then the words, or with no prompt " Next" / " Exit" from column
  1. Memorize's slot lines (0x17DD5, read s33): per class with any slot byte, START 0x16EC(member, "can memorize:",
  10, 0) in the camp's text box (the frame drawn again, the name on row 18, the words typed from row 19 while DS:4A00 is
  set), then each class line (CS:057E / CS:0591) at (1, 20 + i) and its free slots through Str at columns 20, 23, 26
  (blank where the slot byte is 0), each pass before the book (the first over the camp screen, row 24 keeping the Magic
  menu). The keep questions (0x3C5:0x39 with prompt colour 14, as every camp-unit yes/no) come over the pending list.
  When 0x2D669 returns it draws its rows again unlit. Cast, Memorize and Scribe end with START 0x27D9 when a list was
  drawn (0x17D96, 0x182E1): the text window comes back empty. The fight's Cast list keeps the combat palette (0 and 8
  swapped: dark grey); View's Spells is black. **D** (s33, DOSBox-X 3000 cycles against the Client, every distinct
  frame): Memorize (Rhiannon, a 34-row book in 11 rows, paging both ways, two picks, the keep question, then Bakshi's
  cleric and magic-user slot lines) 39 of 89 equal; the misses are the Client's help lines (42: the camp menus, the fire animating under them) and the
  camp picture's animation phase while the slot message types (8). Combat Cast and View's Spells (Darkstar, 21
  spells, 2 pages each; seed 12345) 38 of 39 (the help lines before the step). Camp Cast and Display: the Cast list
  frames equal; the rest are the help lines and Display's window (0x187ED: centred white title on row 1, the list
  from row 5, " Exit" at column 1; the Client keeps its own page `[?]`). Scribe (a scroll of 3 spells, two picks, the
  question) every frame equal but the help lines. Client: `SpellPageLayout` (`ListPage`), `SpellLists.Layout` /
  `SlotBox`, tests `SpellPageLayoutTests`, `CampPlayTests.Memorize_types_its_slot_lines_first_…`. The scroll list
  (kind 2) keeps the Client's layout and the hall's Learn uses the same screen; neither seen in DOSBox-X `[?]`.
- **s33 DOSBox-X checks** (`fight_frames.py`, now with `sleep:S` entries in key lists and `--ignore` boxes for the
  camp fire; `fight_save.py --item`): the spell screens above; handler 131 (spells-effects.md); the wild boar's
  revive (all-Quick party, MON6 block 120 with 1 HP, seed 12345): 406 of 538 frames equal, every revive frame among
  them (the monster-side revive text typed from row 10, the pause, the clear). Fixed: the AI's start 0xABCF clears row 24
  after the menu's Quick too (the menu row stayed in the Client). Left `[?]`: after Quick, the attacker's pose while
  darts fly (Darkstar, Phineas: the icon differs in ~30 frames per throw) and the AI panel's draw-clear-draw (machine
  time, s30). Keypad 7 / 1 move list highlights (they come as the step bindings' NumPad7 / NumPad1).
- **View scrolling** (read s19, 0x30F0E / 0x310D0): ScrollView(x, y, delta, dir) aims at (x, y) + one step in dir;
  the view's centre is (view x + 3, view y + 3); a point within delta of it on both axes leaves the view (delta 255:
  always rebuilt, band 0); else on each axis where the point is outside the band the centre steps to the point, x kept
  in 3..46 and y in 3..21. Deltas by caller: turn start and the menu loop 2 (0x319D2), a player's step 1, an AI's step
  3 after ScrollView(old square, 2) when the new square is off the view (0x12F0D), the AI's attack 2 toward the target
  (0xBA22), the aim candidate 3 (cursor on), the manual cursor 3 in its direction (0x1566C), Center 0, Move's Escape 0
  (0xA343), a message on a creature not wholly in view 3 (START 0x2BFE), end of round 255 at the centre (0xA15E).
  **D** (s19): Phineas at view column 6 → the view moved 3 right, rows unchanged; Sean's AI steps inside the view
  moved nothing (the s18 "one square off" was the Client's inner-3 × 3 rule). Code: `CombatView.Rebuild` / `Scroll`.
- **s29 DOSBox-X checks of the panel and rows 23–24** (a fight entered by a step's COMBAT after a load, so DS:4A00 = 1;
  every Client frame compared with DOSBox-X's as pixels; Client fixes with tests `CombatPanelTypingTests`):
  - The status panel's **weapon name types** (0x77F8, Delay(DS:49E2 × 10) a character, spaces too); name, HP and AC
    lines show at once. Once per turn for a menu-driven member (the menu loop's 0x1583), at each aim candidate, at an
    attack's panel. The HP number is yellow below the maximum 0x32 (as the party list), green otherwise.
  - **Row 24 keeps the last menu** drawn until something writes the row (an AI's start 0x2CFCA, its move prompt, a
    START 0x1693 message): the menu row stays, with the chosen entry lit (a hotkey moves the highlight, 0x2CF48),
    while the panel types after Aim / Next / Prev, and a Done menu stays through the next creature's panel. Aim's
    range line (row 23) is drawn before the panel types.
  - The cast's facing (0x27E00: 0x31232 with frame 1) redraws the caster's cell, so its cursor box goes. The flight's
    view rebuild (START 0x27BB, delta 255) draws frame 0 before its first delay, and 0x27EA4 draws frame 0 when the
    caster is visible after the flight (s32 program read). MakeAttack holds frame 1 through Delay(100). The cast's
    end (0x27F44–0x27F5C) clears rows 10–21 and, in a
    fight, row 23 (the spell label).
  - The end of round (0xA087) shows the teammate-dying words (GAME.OVR 0xA05F) through START 0x1693 when 0xA806
    finds a party member dying (0xA11A–0xA134; **D**: row 24, the pause, cleared). Client `Fight.PartyMemberDying`.
  - `[?]` left: an AI creature's panel is drawn, cleared and drawn again at its turn's start (~50 ms in DOSBox-X;
    the Client draws it once); an attacker's pose went with a downed picture once (Thrender below the vampire) and
    stayed once (Bakshi beside it); 3 pixels of the downed picture (COMSPR 11, the skull's mouth) show the ground grey
    in DOSBox-X and black in the Client. (s30: the panel's second draw read, the skull pixels fixed; see below.)
- **s30 DOSBox-X checks** (method of s29, now kept as `tools/scripts/fight_frames.py`, dosbox-trace-method.md "s30";
  save A on disk 2, seed 12345 on both sides; counts are Client frames equal to a DOSBox-X frame with every s30 fix in,
  each Client side run again against the same DOSBox-X capture; the misses listed):
  - **Darkstar's Sleep** (4 orcs, Next to Brother Sean, Target): 288 of 293 (98.3 %). The cast message, label, flight, the
    sleep messages and the slept members equal. Fixed: a **spell's aim keeps the spell label on row 23** — 0x1529B
    draws "Range = n" only in attack mode (0x152CD), Manual writes row 23 only in attack mode (0x15722 / 0x15768);
    the Client had put the range line over the label. Left: the help lines before the step (the Client's own), the
    spell list (Client's own page, `[?]` 0x239D6 not read), three AI panel frames (below; one of them a turn start's box under the half-drawn panel).
  - **Charm Person** (Prev to an orc, Target) with Rhiannon's **Drop answered No** first: 259 of 261 (99.2 %) after two fixes.
    The aim menu after Prev opens with **Prev lit**, the drop question opens on **No** and the Items menu comes back
    after No; both equal. Fixed: the **charmed orc's morale** — the charm handler 11 writes 100 into DS:6825 for the
    morale check 0xBC97 to read, but Core kept two cells (the handlers' and the check's): the charmed orc showed the
    panic message and fled in the Client, fought on in DOSBox-X (Core `FightState.Morale` is now the one cell).
    Fixed: the **flight's end** (moving icon, below). Left: the help lines and the spell list.
  - **AI surrender and flee** (combat-ai.md "The messages on screen"): surrender 99 of 100 (the help lines; a cursor box
    fixed: the turn's end redraws the creature's cell, 0x99FD 0x9AD9–0x9AF1, so no box under the last member
    while the **continue-battle yes/no** is up; it opens on No and No ended the fight in both); flee 129 of 130 (the help lines) after
    three fixes: the **guard message** (START 0x3226: its word at START CS:0x321D through the nested START 0x2233, read
    s30: row 24 cleared, the word at column 0 in colour 10, PauseOnly, row 24 cleared; called from the Done menu at
    GAME.OVR 0xA787 and the AI's finish at 0xBB5D; DOSBox-X: on row 24 for 0.906 s at speed 3, the PauseOnly's 675 ms
    plus drawing time `[?]`; was "not located"), the
    **stepping creature's cell redrawn by its facing** (0x31232 in 0xB3B2) before the attacks of opportunity (its
    cursor box goes), and the **mover's panel drawn again after each attack of opportunity** (0x13418–0x13429, START
    0x1583 while it is in play).
  - The **AI panel's draw-clear-draw** (s29 `[?]`): read — the second draw is the weapon readying 0xBF8A's START
    0x1583 at 0xC4D7, called when anything changed (`[bp−0x1D]`; always for a creature with no weapon and no
    shield, as the orcs and spectres). In DOSBox-X at 3000 cycles the cleared panel stays ~50–70 ms (CPU time between
    the clear and the redraw; no Delay). The Client draws the panel once; the frames that differ are the in-between
    states DOSBox-X's refresh catches (panel half drawn under the old menu row). Not changed (machine time) `[?]`.
  - **Poisoned hit** (four huge scorpions MON4 39, handler 64; seed 4242, chosen on the Client side): 234 of 235 (the help lines). A
    scorpion's hit on Rhiannon, then the poisoned words and the killed words (handler 64's texts, typed from row 10
    with their pauses), the downed picture and the pause: equal frame by frame. The one miss was s29's **3 skull
    pixels**: COMSPR 11 is drawn whole, its three colour-0 pixels included (grey under the combat palette); the
    Client drew colour 0 see-through, showing what lay under it (fixed, real-data test). The attacker's pose under a
    downed picture (s29: kept once, gone once) not seen in s30 `[?]`. Set-up note: party saves written into CHRDAT 0x6D were back to the class values in DOSBox-X's
    memory after Load. Load's party join at 0x1F9C9 → 0x1F42C calls the full level-stat routine at 0x1F55A → 0x2AA87,
    including the saving-throw routine 0x2ACDC. The Client applies the full routine when loading a legacy save (s32,
    `save-format.md` "How a save game ties the files together").
  - **Fights entered through ENCMENU and after a PRINT** (this tool's own texts; ENCMENU sprite 4, distance 1,
    close-up 4, every reaction 1, then COMBAT; or PRINT, COMBAT): the **panel's weapon name shows at once** in both
    (DOSBox-X: the whole name in one frame on every member's panel), as read (DS:4A00 = 0 after ENCMENU's 0x32E0 and
    PRINT's 0x17DC). Every fight frame equal but one AI panel frame (PRINT: 12 of 32 in all, ENCMENU: 15 of 37; every other miss is before
    the fight, below, or the help lines). Before
    the fight, outside combat `[?]`: (1) DOSBox-X keeps the area command line on row 24 while the step's script types
    its PRINT / ENCMENU text, until a menu or fight-start line replaces it. The Client keeps an already visible
    command line during typing as of s31 (headless regression test).
    (2) The s30 capture's red pixels span (60,67)–(73,85), exactly the bounds of indices 4 and 12 in SPRIT2 block
    4's second frame (32 × 65) at its draw position. The loader changes index 13 to opaque black, which explains
    the earlier frame identification from the visible colours. The test used distance 1, and the second slot is
    the 3D renderer's distance 1. A fresh DOSBox-X state at the same menu confirmed $6DC0 = $6DC1 = 1 and
    DS:6F81 = 4. The Client uses that slot and applies the colour remap (s32; see SETUPMON in `ecl-format.md`).
  - The **cast's plain icon** at its facing (s29): equal again in both casts (Darkstar's Sleep and Charm Person).
    CLEARBOX and the runes: ecl-format.md "PROTECT" (s30).
  - "A battle begins" (s29 `[?]`, DOSBox-X 3000 cycles only): on row 24 for 0.817 s (three boots: from the full line to
    the cleared row), 0.75 s after the blank pause began; the PauseOnly at speed 3 is 675 ms, the rest presumably the
    fight set-up's CPU time `[?]`. The Client guarantees a rendered frame for the battle-start line even if updates
    catch up before drawing (s31). The measured interval is still a reference observation.
- **The panel's text** (read s19): START 0x16EC in a fight clears (23, row)–(38, 21), writes the name at (23, row)
  (START 0x23B6: out of play light red, hostile = 1 yellow, else cyan) and word-wraps the text into
  (23, row + 1)–(38, 21) in green through START 0x77F8 (the text window routine, `TextWindow`; break set DS:0D16 =
  ! , - . : ; ?), with its pause flag: the game-speed pause then START 0x2383 (clear rows 10–21). The cursor
  DS:5E24 / DS:5E25 stays after the text, so a word ending on column 38 moves it to the next row (the "blank line"
  before a down message). The status panel START 0x1583 clears rows 1–21; its weapon name (ending in a space) is
  wrapped into (23, 7)–(38, 9) (a 16-letter two-word weapon name plus its space takes two rows, **D**). While DS:4A00 is set 0x77F8 types each
  character with Delay(DS:49E2 × 10) (**D**: seen in the panel, s19). Read s25: 0x7737 draws each glyph then, when
  DS:4A00 ≠ 0 (START 0x777B), Delay(DS:49E2 × 10) (0x778D); spaces too, nothing for a word-wrap's skipped spaces. The
  only writers of DS:4A00 (every direct-address write form searched in START.EXE and GAME.OVR): PRINT/PRINTCLR 0x174F
  sets 1 at 0x1763 and 0 at 0x17DC after its text (so PRINT always types), ENCMENU 0x2C3E 1 at 0x2C4F and 0 at 0x32E0,
  the after-combat end 0x6BAF 1 (every fight, after the summary / loot or party-destroyed screens; combat-format.md
  "After combat"), the start-up 0xE974 1 (s29: 0xE974 is in the start-up 0xE600, next to 0xE965–0xE979, not in the fight
  start; s25 called it the fight start). Nothing in a fight writes it, so a fight's 0x77F8 texts type only when the
  fight begins with it set: after the start-up or a fight with no PRINT or ENCMENU since (a fight reached through
  ENCMENU or after a PRINT shows its panel texts at once; P, s29; **D** s30: the weapon names at once in both; the
  s19 **D** typing in the panel fits a fight entered by a step's COMBAT after a load). Fixed-cell strings (the name START 0x23B6, row 24 START 0x1693) never type. Client:
  `EclMemory.TypesText` (set by the VM's PRINT, `EncounterHost.EncounterMenu`, `AfterCombat.Finish`), read by the
  panel's `CombatChooser.Typing` and by `ClientPlayer.Text` (view3d.md "Text window").
- **Attack messages** (read s19, 0x12AF4, called per hit and for the miss by 0x13C01: 0x13CBE helpless, 0x13F2E hit,
  0x13F92 miss): START 0x16EC(attacker, the attacks word — backstab / slays-helpless words for manner 2 / 3 —, row 10);
  the defender's name at (23, 12) in its colour before the damage; the text in (23, 13)–(38, 16): the from-behind words
  for manner 1, then the cruel-blow words (manner 3), or the hitting words + the number + the point / points word +
  the damage words, or the miss words; then the damage (START 0x2DC8) and the pause (with damage and a spell being cast:
  pause, then the lost-spell message at row 12 with its pause). Out of play: START 0x16EC(defender, the down words) at
  the cursor row + 1, then two rows on the dying words at column 23 (status 5) or START 0x16EC(defender, the killed
  words) (status 6–8); 0x2BEF4, trigger 0x0D; still out of play CreatureDowned 0x3154B (the downed picture, icon cell
  24 = COMSPR 11, on its footprint, then the pause) else the pause; last START 0x2383 (a defender still up: only the
  clear, 0x12D56). Core keeps this order since s29 (`FightState.AttackMessage` after the damage, `AttackEnd` after
  0x2BEF4 and 0x0D), so a handler's text from the drop (handler 32's) comes between. **D** (s19): the rows of a
  3-point hit (attacks word, defender, hitting words + 3 on row 13, points and damage words on row 14) and of a miss. A plain hit draws no picture (the s13 skull
  was CreatureDowned's). MakeAttack (0x1407F) first shows the attacker in the panel (0x140FE), draws it in its attack
  pose (0x31232 frame 1) and waits Delay(100) (0x1412E); the end (0x142C5) draws frame 0 again.
- **Spell results** (read s19, non-weapon damage 0x2C22E): the takes word, n and the points-of-damage words (n = 1: its own
  string 0x2C18B), then the element by DS:6819 AND 0xF7 (1 fire, 2 cold, 4 electricity, 0x10 acid) and the magic words when
  DS:6819 AND 8 = DS:6819 (mask 0 too; 9 and 0x0C get no magic words), shown by PrintNameStringAnim START 0x2B8B
  before the damage: ScrollView(its square, 3) when not wholly in view, START 0x16EC from row 10, the four frames of
  icon cell 23 (COMSPR 10: stored, mirrored, 138 mirrored, 138; 0x257E) with Delay(70) each, then the pause. An
  effect's message (0x2C540 with a text) uses cell 22 (COMSPR 9), DS:49E2 + 1 rounds and no pause; the unaffected
  words (0x2C532) are START 0x16EC with their pause. Lost spell: row 12 with pause. Out of play: the down words (0x2C1EB) + the dying words
  (0x2C1F5, status 5) or the killed words (0x2C224, 6–8) at the cursor row + 1, then CreatureDowned or the pause. The fight start
  (0xEADC) loads COMSPR i into icon cell i + 13. Each spell routine passes its own effect text to 0x278DB (33 call
  sites in the unit at 0x27018); 0x2C540 shows it only when not empty, then START 0x2383 clears rows 10–21 (s25: the
  spell → string table in `spells-effects.md` "Effect messages"; Client `EffectTextMessage`, not checked in DOSBox-X `[?]`).
- **Pauses** (read s15, s19): PauseOnly START 0x709:0x0DBE = Delay(DS:49E2 × 225) ms. Every Delay call site
  (`9a 9c 02 40 0a`): GAME.OVR 0x848–0x898 (150/100/50 ms, not combat), 0x37BB (3000), 0x9C7D and 0x9D82 (200: Alt-Q and
  Quick), 0x14132 (100, attack pose); START 0x2884 / 0x2B4A (the moving icon's pace), 0x2CCB (70, hit frames), 0x778D
  (typing), 0x7E5C (PauseOnly). **A step and a turn change wait nothing** (the sound is timer-driven); the Client
  shows such a moment for one frame.
- **Messages** (read s15): START 0x1693 clears row 24, writes the text at column 0 in green, pauses, clears row 24
  again (the item screens' refusals). The cast message (0x27D48, only when 0x27C38's third argument ≠ 0 and DS:6DBF
  = 0) comes **before** the target pick: 0x2A332 in a fight writes the caster and the casts-a-spell words through
  START 0x16EC from row 10 **with its pause** (flag 1 at 0x2A36B; s18 said none), then clears row 23 and writes
  the spell label + the name there in green; **D** (s19): an AI magic-user's cast message, then its Charm Person on
  row 23 while the target's unaffected message follows. Item use (0x22B58): START 0x16EC with 0x22AB1 from row 10, the item label on row
  23, the pause. The AI's own messages: 0xABC0, 0xBB6E, 0xBB77, 0xBC7A, 0xBC8C.
- **AI turn** (read s19): 0x99FD shows the box and the panel; the AI (0xABCF) starts by clearing row 24 (0x2CFCA)
  and rows 10–21; each step (AiMoveStep) first writes 0xB396 + movement ÷ 2 + " " on row 24 in green, which stays
  until row 24 is written again. **D** (s19): 9, 6, 4, 3 under Brother Sean's steps.
- **Not read `[?]`**: the spell list page's title layout (its list window and menu line: "Spell lists' Next / Prev", s30), the Guard message.
- **Moving icon** (read s25, P; START 0x25C9 = 0xBA:0x1A29, `retf 0x0C`; the Delays at 0x2884 / 0x2B4A are inside it):
  arguments x0, y0, x1, y1 (squares), frame count, delay (ms, passed to Delay as is: not the game speed). The sprite
  DS:6E36 is built before the call by START 0x257E(cell) (four frames: first picture, first mirrored, second mirrored,
  second) or by START 0x2432(cell, picture 0/1, slot, mirror). The path: line 0x2FCF2 / 0x2FE1E (stub 0x3DB:0x2A /
  0x2F) from (x0·3, y0·3) to (x1·3, y1·3), in thirds of a square (8 px); each step stores its direction (DS:25C0 by
  (dx, dy), line record +0x19); the failing last call stores DS:25C0's centre entry, no step (s30: 0x2FF4A–0x2FF67
  writes +0x19 from the step it took, (0, 0) when none; s25 had read "the last one again"), so with the walk starting
  at entry 1 the last pass draws again one third short of the target (**D** s30, Charm Person: two pictures there,
  then the end frame on the target; this was s29's "one extra picture at a flight's last square"); under 2 entries
  nothing happens. View:
  both ends in view (0x30E23) → ScrollView(centre, 255); else both |dx|, |dy| ≤ 6 → ScrollView(midpoint, 255) (fixed);
  else the view follows (ScrollView(current centre, 255)). Walk: position (x0 − view x)·3 + sub, …; drawn (0x464:0xD71
  frame, 0x464:0x1B52, Delay, background back) when delay > 0 or, with 0, the position is a whole square on an axis;
  the frame advances only on a drawing; then path[++i] (the walk starts at entry 1: the first direction is skipped, a
  quirk) moves it; every 3 thirds the square and the centre move. A position past 0..18 leaves the view: the centre goes
  3 squares past the icon on the axis it moved along, ScrollView(…, 255); in follow mode the walk goes on from there,
  else the flight ends. End: ScrollView(target, 3) when out of view, the frame drawn on the target, with delay > 0
  Delay and the background back (with 0 it stays drawn).
  - **Callers** (all GAME.OVR; arguments from (x0, y0) = the first creature's square 0x31321 / 0x31349):
    - the missile 0x15057 in 0x14EAB (sprite by the item's type, Core `FlightAnimation.Missile`; resident bracket
      0x15011–0x15069 with cues 2620 / 262C), called from MakeAttack 0x1407F at 0x14152 with the ammunition and
      (s26) **again at 0x1417E** with the attacker's weapon (+0xCC) when its type is 0x2F, whatever the ammunition, in
      melee too (so COMSPR 8's two frames, 20 ms, in its own bracket). Base type 0x2F is the **Sling** (words table
      47, base row 47: one hand, rate 2, range 21; every type-47 record of ITEM1/3/5.DAX is a sling): it has no
      ammunition item, so a sling shot is this one flight. **D** (s27): Phineas with a sling at range 5, one stone
      flight (COMSPR 8) before the attack message, the same positions in the Client. With no weapon the program
      reads the type byte at 0000:002E, the high-segment low byte of INT 0Bh: **0x00** in DOSBox-X (vector F000:CE60,
      read from a state), so no flight there, as the Client does. **Real DOS** (s29, reasoning, not seen): the flight
      comes only when that byte is 0x2F (0x1415D; the flight 0x14EAB then takes the nil item, whose type byte is the
      same 0x2F). The cases:
      - No IRQ 3 handler installed: the BIOS vector, segment F000 on IBM and compatible BIOSes → 0x00, no flight.
      - MS-DOS STACKS= (installed by default as 9,128 on AT-class machines, none on PC/XT): it hooks the shared
        levels 0Ah–0Eh only when something is already there. The MS-DOS 4.0 source (`STKINIT.INC`, comments dated
        3.30) leaves a vector alone when its segment is 0, it points to an IRET, or it is the BIOS dummy (segment
        F000 and offset = the word at F000:FF01). So a stock BIOS vector stays F000 → no flight. 3.3 and 5.0–6.22 are
        taken to do the same (not read).
      - A driver for an IRQ 3 device (a serial mouse on COM2/COM4, a bus mouse set to IRQ 3, a network card at IRQ 3,
        a resident comms program) puts its own segment there; when it was loaded from CONFIG.SYS, STACKS then chains
        it from its relocated code (also an arbitrary segment). The byte is then whatever the memory layout gives:
        0x2F for about one layout in 256.
      - The game itself: no INT 21h AX=250Bh / 350Bh and no direct word write to 0000:002C / 002E in START.EXE or
        GAME.OVR (a byte-pattern search, not a proof that nothing else sets the vector).
      So on a stock machine (no IRQ 3 driver) the original shows no flight; with an IRQ 3 driver it depends on where
      the driver landed `[?]`. The Client keeps 0 → no flight (the stock case and DOSBox-X's).
    - the cast 0x27E4D (cell 0x12, 4 frames, 30 ms; bracket 0x27E08–0x27E56), Hold Person's further targets 0x28745
      (same), Lightning Bolt 0x29B02 (cell 0x13, 4, 50 ms, to the cell about to be hit, **from the cell the walk
      went on from**: [bp−0x37] / [bp−0x38] are set at 0x29A68 from the line's current cell, on entry and again on
      every jump back after a hit (0x29BBE), so each flight starts on the previous one's end; s26 had read the line's
      start. **D** (s27): a breath's third flight started on the second's end square and centred the view on the
      midpoint first; fixed in `FightSpells.Bolt`).
    - (s26) effect handlers, **no sound bracket**, each after its message (START 0x16EC from row 10; rows, pauses and texts in spells-effects.md "Handler messages", s27): the petrifying
      gaze 83 (0x10908): 0x257E(cell 0x12) at 0x10948, gazer → its last target (+0x0A), 4 frames, 45 ms (0x10987);
      reflected (0x7F and a mirror item): after the reflect message (row 12) target → gazer, same sprite and pace
      (0x10A59). The charm gaze 84: gazer → target after the target check, the sight line and the message (0x10BC6,
      cell 0x12, 4, 45 ms), before DS:681B = 10. The acid breath 121: 0x257E(cell 0x17) but a frame count of 1, so
      COMSPR 10's first picture, breather → target, 30 ms (0x11930). The lightning breath 88 (0x2A0F1, see
      spells-effects.md): 0x257E(cell 0x13), breather → the nudged aim, 4, 50 ms (0x2A1E4), then the bolt 0x29973
      with its own flights.
    - (s26) the aim's Next / Prev (0x15D02, in Aim's nested 0x15B84(step, fly)): the cursor box sprite 0x2432(0x19,
      0, 0, 0) (cell 25 = COMSPR 25, set at 0x15D66 and again after Manual 0x15EDD and Target 0x15F24), 1 frame,
      **delay 0**, from the shown candidate's square (DS:5F27 / 5F28 by its slot) to the square of the list entry one
      step on, **before** that entry's target check. 0x15F6B: after every key 0x30A0F redraws the shown square, then
      0x15B84([bp−0xDD] = +1 Next, −1 Prev, 0 otherwise; fly = not done and the key not E / #0); the skip of an entry
      that fails the check (0x15E4D) passes fly = 0 and does not fly; with step 0 the path is one entry, so nothing.
      With delay 0 positions are drawn only on whole squares with no wait and the last stays (erased by the next
      0x30A0F), so in effect the view follows the box. No sound.
  - The tune started before a bracketed call sounds for the flight's length (the Client's logic thread waits out
    each frame; with delay 0 nothing waits).
  - **Pixel position** (read s26): 0x464:0xD71(x, y, frame, 5, sprite DS:6E36, buffer DS:4A20) at 0x1CD4 / 0x1F94
    takes the position in 8-pixel units (x added to the buffer column in bytes, y shl 3 = rows), the same units and
    buffer the map's own cell draws use (0x31679 icons and 0x32097 cells at view cell × 3; the view cell of a slot,
    DS:604A / 6092, is its square − the arena's view corner +2 / +3, written at 0x307E3 / 0x3080D), so the icon's
    origin is the view's: square (col, row) of the view at (col·3, row·3). The Client places it as the squares
    (`CombatScreen.DrawMap`).
  - Client: `CombatChooser.Flight`, `CombatView.Flying`. Core: `FightState.Flight` (`FlightAnimation.Gaze`,
    `AcidBreath`, `AimCursor`, `Bolt`, `Missile`, `Spell`).
  - **D** (s27, DOSBox-X 3000 cycles against the Client with `--seed` = DS:4428 from the same scratch saves:
    Tyranithraxus (MON5 66) and a medusa (MON5 49) loaded on disk 2 with MON5's CHA/ITM/SPC and CPIC5; windows
    sampled at ~500 grabs/s, the changed-pixel boxes per frame compared): the breath 88 (breather → aim, then the
    bolt's three flights), the gaze 83 (gazer → target; a diagonal one across the view), the reflected gaze
    (target → gazer) and the sling flight all give the same positions in the same order in both; the views scroll
    alike (after the Bolt fix above). A drawn position takes ~57 ms in DOSBox-X at 3000 cycles against the 50 ms Delay
    (the draw time); the Client waits the Delay alone. **D** (s29): the charm gaze 84 (12 positions) and the acid
    breath 121 (13 positions, COMSPR 10's first picture) at the same positions in both. The aim cursor 0x15D02 (Next /
    Prev over party members, orcs off view): DOSBox-X draws the bare box at each whole square on the way, ~14 ms each
    (draw time, delay 0), then the candidate's square; the Client goes there in one update, so the passing box is not
    seen. **Decided (user, 2026-11-19, s30): the Client keeps the jump** (a deliberate difference; the s27 delay-0
    rule covers picture frames only, and the spell, breath, gaze and missile flights still fly). Every other frame of the aim
    equal. Flight end (s29 `[?]`, settled s30): the walk's last position is one third short of the target, drawn twice (the
    path's last entry is no step), then the end frame on the target; the Client used to step onto the target first
    (fixed, `CombatFlight`).
  - **D** (s27) messages around them in DOSBox-X: the breath message typed from row 10 under the panel, then the
    pause (~0.9 s at speed 4), rows cleared, then the flight; the gaze message typed with no pause before the flight;
    the reflect message on row 12 (typed, no pause) before the return flight; the stone message with the hit frames
    (COMSPR 10) on its square; a poisoned hit's poison / death messages. The game state after them matched the
    Client's (same screen at the next menu). The handler messages are now shown by the Client (spells-effects.md
    "Handler messages", s27; **D** s29 frame by frame); the status-change messages (0xECB3's nine
    callers with their end, the poisoned hit's poison words, the rot and drain words) are shown since s29
    (spells-effects.md "Status changes"; **D** s29: the vampire's level drain, the dead-forever text (0x10D56) and the basilisk's stone
    messages equal frame by frame).
- **Manual in the Client**: Aim's Manual answers `AimKey.Manual` and each cursor pass is Core's (`ChooseManual`):
  arrows / pad move it one square, Return / T Target (ending Manual even where it is not offered), E / Escape Exit,
  C / shoulders re-centre; the panel shows `ManualView.Creature`, row 23 the range to the square in attack mode when the line got
  through (`ManualView.LineReached`). Bare squares work in a spell's square mode. A mouse click on another square
  walks the cursor there one pass per square (our mapping; the target check runs on each square crossed, as with the
  keys); a click on the cursor's square takes it when offered.

- **s18 checks** (screenshots in the session scratch folder): the View page equal pixel for pixel (rows 1–22) on
  all six members of save K; the menus' first highlight follows DS:6B2B; the attack and "goes down" lines of an
  all-Quick run read as DOSBox-X's. The differences noted then (box on AI turns, the AI's panel and move prompt, the
  view scrolling) are settled above (s19).
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
- **View page** (0x21084, read s18; **D** pixel for pixel on all six members of save K, rows 1–22): braid rows 0 and
  23; row 1 the name (START 0x1816: in play 0 light red, hostile yellow, else cyan; an NPC (0x84 > 0x7F) gets the NPC
  tag in green at column name length + 3); row 3 gender (DS:0FEC + 7·0x9E), race (DS:0F03 + 10·0x2E) and the age word
  with word 0x30, white, each one column after the last; row 4 alignment (DS:0F53 + 17·0xA0), row 5 class (DS:0D38 +
  27·0x2F), white; rows 7–12 the ability labels and values (0x2193B: green, value at column 5, 6 below 10; strength 18
  with 0x16 > 0 adds "(nn)" at column 7, two digits, 100 as "00"); money (0x21547, of the current creature DS:5D92):
  kinds 6 down to 0 with a non-zero word 0x88 + 2k from row 7, the name (DS:0FFA + 11·k) right-aligned to end at
  column 19 and the amount at column 21, green; row 15 the level label, the class levels above 0 joined by "/" at
  column 7, the experience label with the dword 0xAC at column 17, white. Then 0x21642 recalculates the creature
  (START 0x1758: every draw of the page) and writes rows 17–18: AC label (1,17), |0x111 − 60| at column 4 with "-"
  first when 0x111 > 60 (START 0x8EF); HP label (1,18), 0x11B at column 4, yellow below 0x32 else green (START
  0x973); THAC0 label (9,17), 60 − 0x110 at column 15; damage label (8,18), 0x115 "d" 0x117 and the signed 0x119 as
  "+n"/"-n" at column 15; encumbrance label (22,17), word 0x102 at column 34; movement label (25,18), 0x11C doubled
  with effect 0x2A, halved with effect 0x27, at column 34; labels white, values green. Row 20: a readied weapon
  (pointer 0xCC) → the weapon label (1,20) and its name (START 0x442) at column 8; row 21: body armour (0xD4) → the
  armour label at **column 2** and its name at column 8; row 22: status label (1,22) and the status name (DS:1047 +
  13·0x10C) at column 8, green. All numbers are unsigned. The View loop (0x21AED, read s19) loops until the key is in
  the set at CS:0A4C (#0, E) or "done"; it draws the page (and so recalculates) on entry and again only after a key in
  the set at CS:0A93 (GAME.OVR 0x21ACD: I, S, T). D runs drop money (0x234E1) and then only 0x21547, which clears the
  box (12, 7)–(26, 14) and writes the money lines; the rest of the page (encumbrance, movement…) keeps its old values,
  and no recalculation runs (the weight word is lower, the movement byte stands until the next START 0x1758). Other
  keys redraw nothing. Client: `Play/CombatSheet.cs`, the money-only redraw in `CombatChooser.ChooseView`; Core:
  `FightItemScreens.View` / `RedrawsViewPage`.
- **Drop money** (0x234E1): a coin-type prompt (colour 13) then Exit; the sheet's money lines are the list (picked
  one white; the Client lists them in the sheet's order, kinds 6 down to 0); then an amount question naming the coin, in green on row 24 and the typed number.

## Open `[?]`

- Trade (0x2276E, 0x23190), Sell (0x22D28) and Id (0x22F8C) on the item screens (not shown in a fight); the money
  input 0x24F1D in detail; the area search's order (0x3045B) and the path distance; the step
  routines (0x130AD, 0x12F0D) and flee (0x13465) in detail (combat-map work).
- Manual: settled s19 (ScrollView 0x310D0 with delta 3 per pass, the box through DrawCursorCell; see "View scrolling", "Cursor box").
- The abort question's wording and what it shows (0x27EF2); the 1–7 mode's repeat message (0x14B7F).
- Sweep with a defender missing from the adjacent-enemy list.
- Turn undead against real undead (no capture yet).
- Status 8 after turn undead in the after-combat step.
