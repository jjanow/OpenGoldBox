# The monster AI (Pool of Radiance, DOS v1.3)

The AI that acts for every creature with record 0x10F ≠ 0 in a fight: the monsters, and party members put on Quick
(menu Q 0xAA32, Alt-Q). GAME.OVR overlay unit 0x38 (file 0xABC0–0xC4E7), entry 0xABCF, called from the turn routine
0x99FD (`combat-format.md` "A creature's turn"). Read from the disassembly in session 12 (2026-11-17); addresses are
GAME.OVR file offsets unless marked START (unpacked `START.EXE` image). Far calls resolve through the overlay stubs
(`ecl-format.md` "Program references"). talonius' source was used only to find routine boundaries.

Status marks: **P** read in the program, **D** checked against DOSBox-X, `[?]` not settled.

Code: `src/GoldBox.Core/Game/MonsterAi.cs` (`MonsterAi : IFightController`). It acts through `Fight` (`Attack`,
`EndActions`, `Guard`), `SweepAttack.Try`, `TurnUndead.Run` and the seams in `FightSeams.cs`: `IFightMap` (`Search`
for 0x3045B, and for the AI `Line`, `Look`, `MoveCost`), `IFightMoves` (`Step`, and for the AI `Flee`) and
`IFightSpells` (`ContinueCasting`, and for the AI `AiInfo`, `Range`, `FindCureTarget`, `CastAuto`,
`ContinueCastingAuto`, `UseItem`; `FightSpells.Ai.cs`). The step pattern table is read from the player's START.EXE
(`PorProgram.AiStepOffsets`, found through the code at 0xB235).

## Data the AI uses

- **Combat record** (22 bytes, `combat-format.md`): +0 spell begun, +1 cast allowed, +2 item use allowed, +3
  initiative, +6 movement (half squares), +7 guarding, +9 facing, +0x0A target (far pointer), **+0x10** turned
  (set by turn undead 0x13965; the AI treats it as "forced to flee"), **+0x11** tried turning (set by 0x13965; the AI
  then does not try again). AI-only: **+0x14** fleeing, **+0x15** step pattern row (1–6).
- **Step pattern table** DS:02AE, indexed 5 × row + attempt (attempts 1–5; 0xB56D also reads attempt 6, past the
  row, but only when the result is not used). Each entry is a direction offset (1–8; 8 ≡ straight on). Rows 1–4 try
  straight on and the near sides first; rows 5 and 6 (reached through the 1d8 = 8 branch) also try backwards.
- **Spell table** DS:31FA + 16 × spell: byte 13 AI priority, 14 needs targets, 15 area (as the AI reads them; the
  other bytes belong to the spell code).
- DS cells: 6825 morale value (seen by trigger-0x11 effects), 681E target refused (set by trigger 1/0 effects),
  6E30 quick magic (0 at the fight start 0xE457), 6E2F enemy-side HP % (0x15074, end of round), 443C last step
  direction, 443D blocked count, arena (DS:6716) byte +6 "lines ignore obstacles" (read by the line routine
  0x2FF74). VM: $49E5 (word [DS:49D2]+0x1CA; ≠ 0 blocks item use, meaning `[?]`), $6DC6 morale threshold.
- Sides: START 0x2F57 returns 1 for a creature with hostile 0x10E = 0, else 0: "the enemy side" value. A creature
  is an enemy when its 0x10E equals that value. Team counts DS:6814/6815 are indexed by it.

## Turn (0xABCF) — P

1. Key check (0xBB81, below). Out of play (0x10D = 0) → actions end (START 0x31D7) and the turn is over (it still
   draws the opening dice).
2. **Opening dice** (0xAC05–0xAC71), always: row = +0x15. When the row is 1–4, **1d4**; a result other than 1 keeps
   the row. Otherwise (row 0, 5, 6, or the 1d4 gave 1): **1d8**; 8 → row = **1d2** + 4, else row = **1d4**.
3. Morale (0xBC97) unless the key check ended the turn. A fleeing creature (+0x14) not forced (+0x10 = 0) shows the
   panic message.
4. Turn over if 1 or 3 said so. Else the first of these that acts ends the turn:
   - an item (0xAFA4) → actions end;
   - a spell begun earlier (+0 > 0): 0x27C38(spell, 1, 1) → actions end;
   - turn undead (0xADC3) → actions end;
   - a spell (0xB105); the turn ends without START 0x31D7 (the cast routine does what it does).
5. Ready the best weapon (0xBF8A), key check, then until the turn is over: target search 0x15FDF(range 0xFF,
   relaxed, keep) and initiative > 0 → move/attack (0xB6FC, its result ends the turn when true); else finish
   (0xBB0F).

**Finish** (0xBB0F): helpless (START 0x1713), a ranged weapon (START 0x39A3) or initiative 0 → actions end; else
guard (START 0x3226: actions end, +7 = 1, message). Returns true.

**Key check** (0xBB81), only with a key waiting: M (scan 0x32) toggles quick magic DS:6E30 (message); space (0x20)
sets 0x10F = 0 on every creature with 0x84 < 0x80 and status ≠ 1 (the party back to manual) and, when this creature
is among them, initiative 20 and the turn ends; X (0x2D) calls 0x1638E (the fight's Alt-X, its result unused: combat-menu.md
"Entries", read whole s51). Headless: no key.

## Morale (0xBC97) — P

+0x14 = 0; effects 0x4A and 0x4B (DS:0C24, 0C25) lose their first instance (0x2BF2B → 0x2AF10). +0x10 ≠ 0 → +0x14 =
1 (forced-flee message), done. Creatures with 0x84 ≤ 0x7F (party members, non-NPCs) skip the rest. Then:

- m = 2 × (0x84 AND 0x7F) as a byte; above 102 → 0; into DS:6825; trigger 0x11. Steady when m ≠ 0 and m ≥ 100 −
  HP% (HP 0x11B × 100, 16-bit, signed div max HP 0x32; signed compare). `[?]` max HP 0 would divide by zero.
- DS:6825 = DS:6E2F; trigger 0x11. Steady when DS:6825 ≠ 0, DS:6825 ≥ 100 − $6DC6 (unsigned word) and the creature
  is hostile (0x10E ≠ 0).
- Otherwise: when the fastest enemy in play (0x15103: max of movement 0x1291E shr 1) is no faster than its own
  movement shr 1 → flee: +0x14 = 1, effects 0x4A/0x4B removed again. Else, with Int (0x11) > 5, it **surrenders**
  (0x2BDF0 with status 4: message, in play 0, status 4, HP 0, off the map, actions end, the dropped-effects list
  0x2BEF4) and the turn is over. With Int ≤ 5 it fights on.

So monsters with 0x84 = 0xFF (the goblins) get m = 0 and are steady while their side's HP stays ≥ 100 − $6DC6 %.
No draws.

**The messages on screen** (s29, P). The turn starts by clearing row 24 (0x3C5:0x2F = 0x2CFCA) and rows 10–21
(START 0x2383), after the turn's 0x319D2(creature, 2, 1) has put the view and the cursor box on the creature.
- Forced flee (0xBCF0, text 0xBC7A) and panic (0xACC1, text 0xABC0, after the morale call and only when +0x14 ≠ 0
  and +0x10 = 0): START 0x16EC(creature, text, row 10, pause 1). The name on row 10 (START 0x23B6, not typed), the
  text word-wrapped into (23, 11)–(38, 21) in colour 10 and typed (START 0x77F8 while DS:4A00 = 1, combat-menu.md
  "The panel's text"), PauseOnly (DS:49E2 × 225 ms), rows 10–21 cleared. So a forced creature shows one message, a
  panicking one the panic message. Neither moves the view.
- Surrender (0xBE19 → 0x2BDF0(creature, 4, text 0xBC8C)): a creature already out of play (0x2BE0C) gets nothing,
  not even the message. Else 0x319D2(creature, 3, 0) (ScrollView on its square with delta 3, the arena's cursor
  flag off, so no box), the same START 0x16EC(creature, text, row 10, pause 1), then in play 0, status 4, HP 0 (status
  ≠ 3), its cell redrawn and its slot size 0 (off the map), the grid rebuilt, actions end (START 0x31D7, always true)
  and 0x2BEF4. Back in morale, START 0x31D7 is called again (0xBE3C) whatever the creature's state, and its result
  (always 1) ends the turn. 0x2BDF0 is the shared status-change routine (also behind other status texts, PLAN (1)).
- Client: `CombatChooser.AiSays` → `Say` (START 0x16EC with the pause) for all three, the surrender first scrolls
  (`StatusChangeScroll`). Core `MonsterAi.Surrender` checks in play before raising the message.
- **D** (s30, DOSBox-X frame by frame, combat-menu.md "s30 DOSBox-X checks"): save A with $6DC6 = 0 (limit 100) and
  monster records at half HP in the copied `MON2CHA.DAX` (side HP % 50 from the placement): goblin guards (Int 10,
  movement 6) and aides (Int 14, movement 12; they surrendered, so their movement after the fight start's recalculation was below
  the party's fastest `[?]` not read from a state) surrendered
  at their first turn, each with the message typed from row 10 and the pause, then the continue-battle question
  (opens on No; No ends the fight); two spectres (movement 30) showed the panic message, stepped away (Bakshi's attack
  of opportunity on the way) and ended with the guard message (0xBB0F → START 0x3226). Client frames equal: 99 of 100
  and 129 of 130 after the fixes there (the misses: the Client's help lines). A charmed monster is steady: the charm handler's 100 in DS:6825 is what the
  check reads (Core had kept a second cell; fixed s30).
- Not checked: the forced-flee message (turn undead's +0x10).

## Targets — P

**Enemies in range** (START 0x2FD3): the map list 0x3045B(x, y, range, direction 0xFF, size 0x31371, arena) — every
creature whose footprint is within range and reached by a line (0x2FF74), in map-slot order (DS:5F27, 4 bytes per
slot: x, y, ?, size) — filtered to the enemies, compacted into DS:6718 (3 bytes each) and copied into DS:6DE4 (slot
numbers, 1-based). Seam: `IFightMap.Search` through `FightSearch.EnemiesNear`. (Read here: the list is walked in slot
order with the creature's own slot in place, not first; for the enemy list that makes no difference.)

**May target** (0x13880): the same creature → yes. Else DS:681E = 0, trigger 1 on the target; when not refused,
trigger 0 on the attacker with +0x0A temporarily the target. Yes when DS:681E = 0. No line test here.

**Target search** (0x15FDF, args range, relaxed, forceNew): the remembered +0x0A is cleared when forceNew, or when it
is set and not (on the other side by 0x10E, in play, may target). Without one, up to two passes; the second (when
not relaxed) sets arena byte +6 so lines ignore obstacles. Each pass lists the enemies in range and makes up to 20
tries of **1d(count)**: a struck-out entry wastes the try; an entry is taken when (relaxed and arena +6) or it may be
targeted, else it is struck out and the pass ends when none is left. Arena +6 = 0 at the end. With no effects every
first try succeeds: one draw.

## Move and attack (0xB6FC) — P

DS:443C = 8, 443D = 0, 443E = 0; trigger 0x0E; initiative 0 → nothing. Loop while not done:

1. A fleeing creature steps (0xB3B2) while +6 > 0 and 0 < initiative < 20. Initiative 0 or 20 ends the loop.
2. After 20 passes: done, and finish (the rest of the pass still runs).
3. Range = weapon base byte 12 − 1 (no weapon: 1; 0 or −1 → 1).
4. The remembered target is kept only when in play and **its 0x10E ≠ 0** (0xB85C). So a monster (targets on the
   party side, 0x10E = 0) never uses its remembered target here; a Quick party member does. A kept target is in reach
   when it may be targeted and the line 0x2FF74 from the creature (max 2 × range + 1) reaches it with length shr 1 ≤
   range.
5. Not in reach: enemies within range. None → target search (0xFF, not relaxed, keep): found → one step, else
   finish. Some → **1d(count)** picks one (a draw even for count 1). A ranged weapon that is not throwable with an
   enemy adjacent (enemies within 1) → ready the best weapon (0xBF8A) and the loop ends. Else in reach when the
   distance (START 0x30FB) is 1 or it may be targeted.
6. In reach: ScrollView(its square, 2, toward the target) (0xBA22, drawing only: +9 is set later by MakeAttack's
   0x31232 at 0x14113, after the panel; combat-menu.md "MakeAttack's drawing order"), sweep (0x1367D; made → actions end, done), else note the attack (0x13FF1)
   and MakeAttack (0x1407F) with the ammunition (START 0x3A2B; none for a throwable weapon at distance 1). Actions
   done → the loop ends; a target out of play → this pass is done (the outer loop searches again).

Returns true when the loop ended through initiative/actions (the turn is over), false when it stopped with the
creature still able to act (the caller searches a new target).

## One step (0xB3B2) — P

Status line "move left" = +6 shr 1. Key check. +6 shr 1 ≤ 0 or initiative ≤ 0 → finish. Not fleeing and class
(0x2F) 5 (magic-user) → finish: magic-users never close in.

Base direction: towards the target (0x14E38 on +0x0A; `[?]` with no target the program reads a nil pointer); when
fleeing **+0x15 = 1d2** and d = f − ((f + 2) mod 4) shr 1 from the area facing f = DS:6AAF (0, 2, 4, 6), byte; on
the party side + 4; mod 8. (Facing 0 → 7, 2 → 2, 4 → 3, 6 → 6 for monsters.)

Attempts 1–5 (**can step**, 0xB235): direction = (base + table[5 × row + attempt]) mod 8; the square ahead 0x313F3
(ground type, highest-cost over the footprint with 0x1E winning, and another creature there): ground 0 = the edge →
no (edge noted); a creature → no; cost (DS:2744 + 4 × ground, byte 0) ≥ +6 → no; need = cost × 2, × 3 for odd
(diagonal) directions; ground 0x1E without effect 0x20, 0x1E, 0x6F or 0x7D needs +6 + 1. Yes when +6 ≥ need. A
fleeing creature that meets the edge tries to flee (0x13465) at the same attempt; the step loop then stops whether it
got away or not (0x13465 returns START 0x31D7's result, always 1, not its "away" flag; s52): +6 = 0, +0x14 = 0,
actions end.

Then new direction = (base + table[5 × row + attempt]) mod 8. When all five failed or the new direction is the
reverse of the last step (DS:443C): DS:443D + 1, +0x15 = row mod 6 + 1; from the second time the target is cleared
and searched again (relaxed; none → finish); from the third +6 = 0 and the step is off. DS:443C = the new direction
(five failures end the step instead). Otherwise: facing = new direction (0x31232), attacks of opportunity (0x130AD),
out of play → actions end; else the move (0x12F0D), out of play → actions end; terrain effects (0x2B9D1). Seam:
`IFightMoves.Step` covers 0x130AD, 0x12F0D and 0x2B9D1.

## Items, spells, turning — P

**Should cast** (0xAEAB, spell, minimum priority): priority (spell byte 13) below the minimum → no. Spell ≠ 3 with
byte 14 = 0 → yes. Spell 3 with a cure target (0x143F3) → yes. Else the enemies within the spell's range (0x27741,
computed with DS:6DBF = 0, so an item's range here is the creature's own; START 0x2FD3): none → no; area (byte 15) 0 →
yes; else for each of them (DS:6DE4) 0xAE15: the target list 0x3045B around its slot square with radius byte 15 and
size 1; a creature in it whose 0x10E is not the side the current creature fights (START 0x2F57) → no. Otherwise yes.
No draws. Spell 3 with no cure target goes on to the enemy test, so a cleric next to an enemy may still choose it; the
aim then fails and the spell is lost (below).

**Item** (0xAFA4): **1d7** always (n). With item use allowed (+2), enemies left (DS:6814[side]) and $49E5 = 0: passes
1..n with priority 7, 6, …: the first readied item (0x34) in list order, not of base slot 11–13 (0x2A255), with 0x3E
< 0x80 and spell 0x3D > 0 (above 0x38: − 0x17), that should be cast. The pick is used (0x22AC4); actions end.

**Spell** (0xB105): the memorised spells (record 0x17–0x2B, non-zero, in order) when cast is allowed (+1). **1d7**
always (n). With spells, NPC (0x84 > 0x7F) or quick magic on, and enemies left: passes 1..n with priority 7, 6, …,
each of up to three tries of **1d(count)**, until one should be cast. Cast with 0x14C0F(spell, ai = 1). So Quick
party members cast only with quick magic on.

**Turn undead** (0xADC3): +0x11 = 0, cleric level 0x96 > 0 (signed) and an undead target (0x13B4C) → 0x13965.

## Casts by the AI (0x14C0F / 0x27C38 with ai = 1, aim 0x14611, pick 0x148BE, cure 0x143F3) — P, emulated

The AI's cast is 0x14C0F(spell, ai = 1): a casting time ≠ 0 only begins the cast (`spells-effects.md`); later 0xACEE
casts it with 0x27C38(spell, ai = 1, 1) and ends the actions. 0x27C38 hands the AI flag to the target pick 0x148BE
(DS:6B84), which calls the aim 0x14611 with it. Code: `FightSpells.Ai.cs` (`PickAuto`, `AimAuto`, `CureTarget`,
`CastAuto`, `ContinueCastingAuto`); the aimed square is the same DS:6DB9/6DBA cell the cast's facing reads.

**Target pick** (0x148BE, spell, ai, var picked), the AI side: picked = 1, list DS:6C94 = 0, DS:6820 = 0, DS:6DB9/6DBA =
the caster's square. Mode = area byte 6 AND 0x0F:
- 0: the caster.
- 0x0F: aim; failed → not picked. Else the caster's +0x0A when set, else the list around DS:6DB9/6DBA with radius
  (mode shr 4) = 0, DS:6820 = 1. An aim of a spell with byte 14 = 0 does not write +0x0A, so a remembered target would
  be taken (no PoR spell has both; spell 25 has mode 0x0F and byte 14 = 1).
- 8–0x0E: aim; failed → not picked. Else the list around the aimed square with radius area AND 7, DS:6820 = 1.
- 1–7: (area AND 3) + 1 aims; each successful aim adds its creature unless already listed; a repeat (the AI gets no
  message) and a failed aim each use up a pick (0x30A0F marks the square: drawing only). Empty list → not picked.

**Aim** (0x14611, spell, ai, square mode, var result {creature, x, y}) with ai ≠ 0:
- Spell byte 14 = 0: result = the caster; spell 3: result = the cure target (0x143F3), none → fail.
- Else up to **20 tries**: target search 0x15FDF(range 0x27741, not relaxed, **forget the old target**), so each try
  draws its 1d(count) again and +0x0A ends as the last creature found (nil when none). None found → next try. The
  creature T found is rejected when the target list around T's square with radius byte 15 (0x3045B, size 1) holds a
  creature whose 0x10E is not START 0x2F57(caster) (the caster itself counts), or when T is helpless (START 0x1713)
  and the spell's effect byte 10 is one of DS:286C–286F (0x33, 0x34, 0x35, 0x1F, the same table START 0x1713 walks).
  Accepted → result = T and its square.
- Success: DS:6DB9/6DBA = the result square. Failure: the result is zeroed (7 bytes).

The player's side (ai = 0: the aim 0x15D53 with range 0x27741, then +0x0A = the result) is the menu's
(`combat-menu.md`).

**Cure target** (0x143F3, caster, var result): the nine cells around the caster's square (directions 0–7, then the
square itself; DS:2736 / DS:273F), each looked up with 0x30BF8 (slot, ground):
- A slot: its creature counts when 0x10E equals the caster's and HP 0x11B < max HP 0x32 (unsigned). It becomes the
  best when its HP is below the best HP (start 255), or when it is the caster with HP < max shr 1.
- No slot and ground 0x1F: the bodies list (DS:66D6 + 7k, k = 1..DS:6715) is searched for an entry still set, whose
  creature's status 0x10C is not 6, 7 or 8 (Pascal set at 0x143D3) and whose square is this cell; the last match is
  kept. No side test, so a monster cleric may pick a downed party member.
- Result: the body's creature when a body was found and the best HP is ≥ 8 (or there is no living candidate); else
  the best (possibly none). Returns result ≠ nil. No draws.

## Item use (0x22AC4, item, var done) — P, emulated

Used by the AI (0xAFA4) and the Items screen's Use (`combat-menu.md` "View / Use"). Code: `FightSpells.UseItemNow`.

1. DS:6DBF = 0. A scroll (0x2A255): DS:6B80 = the item, the spell is chosen from it (spell list 0x239D6, mode 2).
   The list returns DS:476A[row], which the row routine 0x272A5 stores as the byte AND 0x7F (0x2744F; 0x270A2 reads
   it at 0x27265): **bit 7 of a scroll pick is not kept** (settled s49, oracle `por_spellcast_oracle.py`), so 0x27C38 and
   0x2A2A2 get the plain spell number.
   Else an item with power 0x3D > 0 and kind 0x3E < 0x80: DS:6DBF = 1, spell = 0x3D. No spell → done = 0, return.
2. A player in a fight (0x10F = 0): START 0x3728 (screen). Non-scrolls: spell above 0x38 → − 0x17 (item powers
   57–67). A message naming the user and the item (drawing).
3. **DS:6DBF = 1** (scrolls too: casting level 6, range per level × 6 + fixed, no memorised copy removed), then the
   cast 0x27C38(spell, ai = the current creature's 0x10F, 0, var done) at once, whatever the casting time; done =
   picked. DS:6DBF = 0.
4. In a fight, spell byte 11 ≠ 0 → actions end (START 0x31D7) and done = 1 (its return value), **even when the aim
   failed**: the charge is spent.
5. Done: a scroll loses the spell (0x2A2A2: the last of 0x3C–0x3E equal in the low 7 bits is cleared, name word 0x30
   − 1, below 0xD2 the scroll leaves the list). Else, charges 0x3C > 0: count 0x39 > 1 → count − 1; else charges − 1,
   at 0 the item leaves the list (START 0x20A9 only unlinks it; 0xC7 is rewritten by the next recalculation).

## Weapons (0xBF8A, 0xBE4D, 0x2250A) — P

**Worth** (0xBE4D): small dice × sides (base bytes 9, 10, byte product); + 8 × plus (0x32) when > 0; + 2 × small
bonus (byte 11) when > 0; type 0x55 against a target whose byte 0x76 > 0 → 8; ammunition weapons (flags bit 3) + 2 ×
(rate byte 5 − 1); one-handed (byte 1 ≤ 1) + 3. Zero when hands used 0x100 + its hands > 3, when 0x3E = 0x84 and
(0x3D AND 15) ≠ the creature's 0xA0, when 0x3D = 0x53, or cursed (0x36 ≠ 0).

**Best weapon** (0xBF8A): the readied weapon's and off-hand item's hands come off 0x100. Over the items whose class
mask (base byte 13) fits 0xB0: weapons (base slot 0) with flags bit 3 or 4 compete as ranged (start worth 1), those
without bit 3 as melee (start worth = natural dice 0xA3 × sides 0xA5, + 2 × bonus 0xA7 when > 0); off-hand items
(slot 1) by plus + 1 (0 when negative; start 0). The ranged one is chosen when its worth > melee worth shr 1, it has
ammunition (as START 0x3A2B) and it is throwable or no enemy is adjacent; else the melee one (possibly none). A
weapon that is neither the choice nor cursed is unreadied, 0x100 rebuilt (START 0x1758), a non-cursed off-hand
item's hands taken off again, and the choice readied. Rebuild, attacks left (0x1351E). Then 0x100 > 2: unready the
off-hand item, or the chosen weapon when that item is cursed or missing; 0x100 < 2 and the off-hand item is not the
best shield and not cursed: it is unreadied and the best shield readied. Rebuild; redraw when anything changed.

**Ready / unready** (0x2250A, for DS:5D92; read whole s51, `ItemCommands.Ready`): readied and cursed → message only;
readied → unreadied (items with 0x3E > 0x7F run their handler, 0x2AEEA, "removed"). Unreadied → readied only when 0x100
+ hands ≤ 2, its slot (0–8; a ring → the second ring slot; arrows 0x49 / bolts 0x1C → slots 11/12) is free and the
class mask fits; otherwise a message (camp.md "The Items page's Ready, Drop, Halve and Join"), the hands-full one not
for a creature with 0x10F ≠ 0 in a fight. `[?]` The AI's own calls (the weapon pick) show the class and slot messages
too when they refuse; Core's `MonsterAi.Toggle` drops them (not seen in DOSBox-X).

## Random draws, in order

Per AI turn: [1d4] 1d8 (1d2 | 1d4) — morale none — 1d7 (items) — 1d7 (spells) [+ 1d(count) per spell try] — target
search 1d(count) per try — per move/attack pass: 1d(count) when picking from the list in range, 1d2 per fleeing
step — the attack (1d20, damage dice; `combat-format.md`). The effect handlers, attacks of opportunity, sweeps, turn
undead and casts add their own.

## DOSBox — D

`fight_quick` (all-Quick goblin fight, s12): from `r0_menu6` (Brother Sean's menu; Q) to `auto_01`, 8 draws, taken
inside his attack. `MonsterAiCaptureTests` replays the turn from the captured records, items and squares with the
program's step table: 1d8 = 3, 1d4 = 4 → row 4 (captured +0x15 = 4); 1d7, 1d7; the target search among the 7
goblins on the map (the real target list 0x3045B: line distance, then direction) takes goblin guard 7 at 26,12; no
enemy within 1 → one step: row 4's first offset is 1, so north-east to 29,13 (captured). Goblin 7 is now 3 squares
away: the line walk 0x2FF74 with range 1 stops past 2 × 1 + 1 half squares, so the remembered target is not in
reach and the enemies within 1 are listed: a 1d1 pick of goblin guard 8 (28,12), 1d20 = 17 hit, 1d6 + 2 = 5 on 4 HP →
dying with 1 over (captured status 5, +0x0E = 1), RandSeed equal. **D** for the opening dice, the item/spell 1d7s,
the search draw, the step pattern, the reach test and the in-range pick (`MonsterAiCaptureTests`, real combat map).

**The whole fight** (`FightQuickReplayTests`): from the arena builder's seed, the wired fight (`FightKit`) with Quick
chosen at every menu replays all 23 states: RandSeed, round, current creature, squares, HP, status, Quick flag,
combat records and remembered targets; 715 draws, 3 rounds, as the capture. A capture taken between two draws
(inside an attack's message delay, or between steps) is compared with the replay's states in that window. Phineas's
turn in round 0 needed the stat recalculation (START 0x1758): the weapon pick readies his darts and the recalculation
gives the darts' 1d3 (checked under emulation on the captured RAM: the program's turn throws three darts in one
attack call, the third at the goblin it has just wounded).

## Checked under emulation

`tools/scripts/por_ai_oracle.py` runs the v1.3 routines under unicorn on generated fights (effect handlers trapped to
do nothing) and writes `tests/fixtures-private/por/program-v1.3/ai_cast.json`; `AiCastProgramTests` compares: 200
cure-target cases with bodies (0x143F3); 240 AI picks, four per spell castable in a fight, with remembered targets,
helpless creatures and DS:6DBF set or not: picked flag, target list, DS:6820, DS:6DB9/6DBA, +0x0A and RandSeed after
(0x148BE → 0x14611 → 0x15FDF); 0xAEAB on the same fights; 120 item uses with the cast trapped (spell, AI flag, DS:6DBF,
done, combat record, item list after); 160 Halve / Join cases. All match.

## Open `[?]`

- Other writers of combat record +0x10 (effects?); the meaning of $49E5.
- 0x14E38 with a nil target.
- No DOSBox capture of an AI cast or item use yet (no casts in `fight_quick`: no NPCs, quick magic off); the rules
  below "Casts by the AI" and "Item use" are checked under emulation only.
