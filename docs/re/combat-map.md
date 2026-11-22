# Combat map: arena, placement, lines, moves (Pool of Radiance, DOS v1.3)

Read from the program in session 12 (2026-11-17). Addresses are GAME.OVR file offsets unless marked START (offsets in
the unpacked `START.EXE` image); far calls resolve through the overlay stubs (`ecl-format.md` "Program references").
Code: `src/GoldBox.Core/Game/` — `CombatTables`, `CombatArena` (+ `ArenaBuilder`), `CombatMap` (implements
`IFightMap`), `FightPlacement`, `FightMoves` (implements `IFightMoves`); tables loaded by
`GoldBox.Data/Por/PorCombatTables`.

Status marks: **P** read in the program; **E** also checked by running the program's own routines under emulation
(`tools/scripts/por_emu.py` + `por_combat_oracle.py`, unicorn; fixture `tests/fixtures-private/por/program-v1.3/
combat_map.json`, test `CombatMapProgramTests`); **D** checked against DOSBox-X (captures `fight_quick`, `fight_moves`,
test `FightStartCaptureTests`). `[?]` not settled. Talonius' source was used only to find routines.

Program units: fight start and arena builders in stub 0x3B (0xC5D1–0xE562); the map unit stub 0x3E0 (0x3073A–0x31A39:
icon cells, cell grid, probe, bodies, view); the line unit stub 0x3DB (0x2FB52–0x3072F: lines, cone test, target
list); moves in stub 0x68 (0x12E22–0x1351B); START 0x2F57–0x31D7 helpers (enemy side, count teams, count targets,
distance, end actions).

## Tables (START.EXE data segment) — P, E

| DS | Size | What |
|---|---|---|
| 2736 / 273F | 9 + 9 | x / y step of directions 0–7 (0 N, clockwise) and 8 = centre (0, 0) |
| 2744 | 4 per terrain index | [0] move cost (255 impassable), [1] sight height of a viewer on it, [2] blocking height, [3] tile drawn (22 = open ground). Indices 64 and 65 read the first bytes of the next table, as the program does |
| 2844 | 8 sizes × 4 cells × (dx, dy) | icon footprints (size = record 0x6C AND 7): 1 one cell, 2 two cells down, 3 two cells right, 4 a 2×2 block; a negative dx ends the footprint (0x3073A). Sizes 5–7 read unrelated bytes |
| 02D2 | 4 × 4 | per team direction, the 3D direction of placement rings 1–3 |
| 02E2 | 4 × 4 | per team direction and ring, the start corner × 2 |
| 02F2 | 4 | 3D facing ÷ 2 → arena direction (the arena is drawn skewed: N → 7, E → 2, S → 3, W → 6) |
| 02F6 / 02FE | 2 × 4 | start square x / y of each corner, [ring 0 / later rings] |
| 0306 | 5 × 6 × (lo, hi) | the open columns of each row of a placement grid, per team direction 0–3; grid 4 is used for ring 1 |
| 3648 | 36 rows × 44 | the wilderness terrain codes: the overland tile map (1-based tile numbers; scripts 25–27 see 16 columns from 0 / 13 / 26, `geo-format.md` "Wilderness (overland)"). The engine reads it from the overland (`rules/overland.json` in a module) |

## The arena (DS:6716) — P, E, D

A far pointer to a 0x4E9-byte block allocated by 0xD815 (not cleared): +2/+3 the view's upper-left cell (signed),
+4 cursor shown, +5 cursor size, +6 line mode (0: lines stop at blocking terrain; 1: terrain ignored), +7.. 25 rows ×
50 columns of terrain indices, cell (x, y) at +7 + 50y + x. Terrain 0x1E is the cloud of an area spell, 0x1F a body.

**0xD815**: tile sets (dungeon or wilderness set, then the rare-feature set; drawing only), the block, +4 = 0, +5 = 1,
+6 = 0, then the dungeon builder when DS:49FA = 1 (3D area), else the wilderness builder.

**Dungeon builder 0xCDE1.** For dy = −2..2 (outer) and dx = −6..6 (inner), the 3D square (party x + dx, party y + dy)
becomes a skewed block: its spot (i, j) is arena cell (6dx + 5dy + 21 + i, 5dy + 10 + j), clipped to the arena, and a
spot gets terrain t + 1. With W, N, E = the wall between the square and its west / north / east neighbour
(0xC77E), per square in this order:
- 0xC88B: floor (t 22) on rows 2–4, columns 0–5; W solid: a diagonal wall on rows 2–4 (columns r−1, r, r+1: t 4, 3,
  13); W a door/archway: t 8 at (1, 2) and t 0 at (5, 4).
- 0xC948: the north band (3–4, 0–1): solid N → t 5 on row 0 and 10 on row 1, else floor.
- 0xC9E0 / 0xCBE6: the north-west corner (1–2, 0–1) and north-east corner (5–6, 0–1, the 6th column spilling into
  the next block), chosen from N/W (N/E) and the walls of the neighbouring squares (`ArenaBuilder` lists the cases).
- Every arena cell is written by some block (the leftmost block's column 0 is covered by its neighbour's column 6).

**The wall between two squares** (0xC77E): each side (0xC6FC) is 1 (solid) when its barrier is 0 (0x2EC47), else 0 when
it has no wall (0x2ED72), else 3 (a drawn wall that can be crossed or opened); the two sides are OR-ed. A side off the
16×16 map is open (0) for the east and west faces on the party's row, solid otherwise (the accessors' wrap is never
reached).

**Rare features 0xC5D1** (both builders, last): columns 0–49 outer, rows 0–24 inner; every cell whose tile is 22 rolls
1d100: 98 → terrain 26, 99 → 27, 100 → (unless the current ECL block DS:84DC is 10) 1d100, and on 1 a 1d10: 1–6 → 28,
7–10 → 29.

**Wilderness builder 0xD785.** Every cell 23; the code under the party (DS:3648[y][x], x from VM $49C3 + 13 when
DS:49FA = 3, + 26 when 4; y from $49C4) gives a class (0xCE75, a bit mask by code ranges; first matching arm wins; once
VM $4AB3 = 255 a class with bit 7 loses it and gains bit 0; a code no arm names leaves the class as stack residue
`[?]`). Then:
- river 0xD1AF: 1d100 against 35 (class bit 0x20) or 75 (bit 0x10), else 0; start column 34 − 5d4, moved left until
  (column + 2) mod 7 = 0; each row while the column ≤ 49: terrain 50/51, a ford (52/53) on 1d20 = 1 or while the ford
  count is odd, column + 1; no ford at all: one at row 16 − 1d9 and the next row, one column further.
- trees 0xD2E4: density 1/3/4/7/0 % by class bits 2/4/0x40/8/0x80 (later wins); columns outer, rows 1–24 inner, a
  floor cell with floor above rolls 1d100 ≤ density; height 1d10 (9–10 → 1d2 + 4, else (h + 1) div 2; class 0x40:
  1d3 + 3, an extra roll); terrain h + 35, below height 5 also h + 31 above.
- features 0xD46F: five stacked bands (swamp, rocks, bushes, scrub, mounds) sized by class; 1d255 per floor cell, then
  one roll for the kind.
- rare features.

## Fight start (0xE402) — P; D for the goblin fight

Beyond the VM bookkeeping already in `EncounterHost` ($6E33, $6DC6, DS:5D9A): DS:6E30 (quick magic) = 0, DS:6DE3 (round
counter) = 0 (settles `FightState.RoundCounter`'s `[?]`), DS:6822 = 0, DS:6715 (bodies) = 0, DS:6DBB = nil, the 8 body
entries DS:66DD cleared; 0xD815 (arena); 0xD8B2 (records: also facing = DS:02F2[party facing ÷ 2], + 4 mod 8 for hostile
1); 0xDFC6 (placement); the moving-icon picture; view upper-left = first creature's cell − 3; the combat screen; for
every creature the trigger-8 effects; enemy HP % (0x15074); DS:49F3 = 5.

**Start messages** (s27, P + D; s29, P): before any of this, 0xE430 calls 0x3C5:0x2F (= 0x2CFCA: START 0x767B clears
row 24, columns 0–39), PauseOnly (0x709:0xDBE, DS:49E2 × 225 ms; speed 0 waits nothing), then writes the unit's
string CS:0x1E1E (the battle-start text, GAME.OVR 0xE3EF, one fixed string) through 0x709:0x626 (START 0x76B6) at
column 0, row 24, colour 10. 0x76B6 draws each glyph through 0x726F itself, never through the typing path
(0x7737 / 0x777B), so the line is **not typed** whatever DS:4A00 holds. **No pause follows**: the fight start goes on
(arena, records, placement, the moving-icon picture, COMSPR loads 0xEADC) and the combat screen replaces the area
screen. So the blank pause comes first and the line stays up only for the time of those loads and builds `[?]`
(machine and disk time; no code timing). DOSBox-X showed the line on the area screen's bottom row ~0.9 s after the
step (the speed-4 pause), then the combat screen. **Every fight** shows it: 0x9881 is the only caller of 0xE402
(stub 0x3B:0x25, call at 0x9899), and the text comes before 0x9881's "done at once" test (duels and fights with an
empty side included). No draws come between the line and the arena build, so the Client, which builds the fight
first (`FightKit.Create`), shows it afterwards with the same result: `CombatChooser.BattleBegins` (from
`ClientPlayer.Fight`, before the combat screen attaches): the blank row-24 pause, then the line (imported as
`CombatMsg.BattleBegins`, importer 0.7.18) for one Client update. **D** (s29, 3000 cycles, ~70 grabs a second): the
line appears whole in one grab (not typed) on the blank row 24 after the speed-4 pause (the Client's blank pause
measured 0.92 s); it stayed 0.73 s on the area screen, then the screen was cleared to the combat grey over 0.3 s with
the line still up, then the combat map: about 1 s of load and draw time on that machine, against one update in the
Client (machine and disk time: P, s38, the static call trees from the line to the combat screen, 0xD815, 0xD8B2,
0xDFC6, START 0x464:0x248E and START 0x3728, hold no Delay, PauseOnly, key wait or BIOS-tick read; a user decision
if the line should stay longer). The Amiga plays song 2 with this line (sound.md
§5.3; not in the Client). The trigger-8 effects run
after the combat screen is drawn: DOSBox-X showed Tyranithraxus's fear aura as one message per party member
(paralysis / unaffected texts, with the effect frames) on the combat screen before the first turn. The
Client built the fight (and ran those triggers) before its screen attached, so they showed nothing: the build now
leaves them to the run when a display is attached (`FightKit.Create(deferStartEffects)`, `Fight.PendingStart`,
`PorGame` with a fight menu), and the five messages came at the same squares and spacing as in DOSBox-X (~2.55 s
each at speed 4).

**DS:6DBB** is not part of the start: a list of 19-byte records made by an area spell (0x28B29: +0 caster, +4 next,
+0x0B.. one flag per cell covered, +0x10/+0x11 the centre cell, +0x12 a count; the cells hold terrain 0x1E). The fight
start sets it to nil and the clean-up (0x9807) frees it (Core: `FightPlacement.Start` empties
`EffectHandlers.Clouds`; spell 34 and handler 40 in spells-effects.md). The line walk 0x2FCF2 / 0x2FE1E
(`CombatMap.LineWalk`) is also Lightning Bolt's line (0x29973).

**DOSBox** (`fight_quick`, the Slums goblin fight entered at 10,1 facing N after bashing the door north of 10,2): from
the RandSeed of `pre_fight`, after the monster loader's draws, the dungeon builder reproduces the captured arena byte
for byte; placement reproduces every slot of DS:5F2B/65BD; the facings are 7 (party) and 3 (goblins); and exactly one
1d6 and one 1d100 per creature later the seed is `r0_start`'s. So the arena's draws are the only ones between the
loader and the first round, and placement draws none.

**DOSBox, facing W** (s26, `fight_west`: test save D of `por_test_saves.py`, the fight of s24's Alt-X check, 15,4 W,
6 party + 8 goblin guards): the same holds (arena, all 14 slots, facings 6 and 2, 28 draws to the first menu).
s24 had noted that the Client placed this fight's combatants differently; that was the **seed**, not placement. The
Client draws its seed from the clock as the game does, and placement gave the same slots for every seed tried. The
round start's 1d6 per creature, though, picks who acts first, and the view is centred on that creature (Client seed 1:
Darkstar's menu, view moved; that boot: Phineas's). To compare a fight screen with DOSBox-X, take a state before the
step that starts it and start the Client with `--seed` = that state's RandSeed DS:4428 in decimal (here 576852995):
the Client then showed DOSBox-X's first screen.

## Where creatures are (map unit) — P, E, D

A creature's place is not in its record. The fight start gives each creature in list order a **slot** k (1-based);
DS:65B9 + 4k is its pointer, and DS:5F27 + 4k is its entry: x, y (signed bytes), k, icon size (0 = not on the map).
DS:5F2A (entry 0's size byte) is the slot count, one more than the creatures registered. `locations_lee` in the
captures (DS:5F2B, DS:65BD) is entry 1 onwards: confirmed. The cell grid DS:60DB (25 × 50 slot numbers) is rebuilt from
the entries by 0x30ADA without bounds checks (a footprint cell past the last row would overwrite the creature table
after it).

- Slot of a creature 0x31399 (0 if none); x 0x31321, y 0x31349, size 0x31371.
- Contents of a cell 0x30BF8: slot and terrain, both 0 off the arena.
- **Probe 0x313F3** (one step in direction d, 8 = in place), over the footprint: the slot of another creature there;
  terrain starts at 23; any cell off the arena → 0 (kept); else a cloud (0x1E) → 0x1E; else the cell of highest cost so
  far (ties: the later; costs start at 1).
- **Bodies** (0x3154B in a fight, the non-drawing part): a party member (+0x13 = 0) leaves a body: entry DS:66D6 + 7·n
  (pointer, x, y, terrain under it), the cell becomes 0x1F unless it is 0x1E; the icon leaves the map (size 0); combat
  record initiative, movement, spell, guard = 0. `[?]` Its first test reads body entry DS:70C7 (a drawing index), not
  each entry. Only 8 entries are cleared at the start; a 9th would overwrite DS:6715 and the arena pointer `[?]`.
- **0x317E1** (put a creature at x, y; used by the move menu's undo): size = 0x6C AND 0x7F, probe in place; another
  creature, terrain 0 or cost 255 → size 0, false; else true, and when moving a party member its bodies are cleared and
  the cell gets back the terrain under the last one (unless that was 0x1F) when no other body lies there.

## Lines, direction, distance (line unit) — P, E

**Line 0x2FCF2 / 0x2FE1E**: a Bresenham walk on the major axis (x when |dx| > |dy|, else y); a step that moves on both
axes adds 3 to a byte distance, a straight step 2.

**Walk 0x2FF74** from (x, y) to (tx, ty) with range r: the viewer's sight height is that of its own cell; on each cell
from the start to the target, when the line mode is 0 and the cell's blocking height is above the sight height, or the
distance so far exceeds 2r + 1, the walk stops there (tx, ty, distance = that cell's; false); else true with the full
distance.

**Cone 0x300CD** (is t in the 90° cone of direction d from s?): false when either cell is off the arena; d = 255 means
8 (always true); true when t = s or t is the next cell n in direction d; else two half-planes per direction, e.g. d = 0:
(tx ≥ nx and ty ≤ ny + nx − tx) or (tx ≤ nx and ty ≤ ny + tx − nx). Other values of d (9–254) match no arm (stack
residue `[?]`).

**Target list 0x3045B** (x, y, range, cone filter, own size): for every slot with a size, over each pair of its cells
(outer) and our cells (inner) inside the cone, a walk within range; a slot with any clear walk is listed with its
shortest distance and a direction (the filter when below 8, else the first d whose cone holds that pair). It includes
the viewer itself (distance 0). Entries are 3 bytes at DS:671B (slot, distance, direction), count DS:671A; then an
exchange sort (0x2FB80): nearer first; at equal distance a lower direction moves ahead unless it is diagonal and the
other straight.

**Count targets START 0x2FD3** (creature, range): the target list from its cell with no cone, kept to the other side
(hostile byte = 1 when the creature's is 0, else 0: START 0x2F57), compacted in order; slots also to DS:6DE4 + i.
Range 1 = the adjacent enemies (one step, distance 2 or 3) with a clear line; 255 = every enemy in sight.

**Direction 0x14E38**: the first d = 0, 1, … whose cone (from cell to cell) holds the other creature; the same cell
gives 0. `[?]` With a cell off the arena no cone holds and the loop never ends (the emulator confirms it runs away).

**Distance START 0x30FB** (a, b): the 216 bytes of target entries are saved, line mode 1, target list from a's cell with
range 255 and no cone, line mode 0; the result is the distance of b's entry shr 1 (so straight squares count 1, a
diagonal 1.5, rounded down over the line); the entries are put back **but not their count** (DS:671A keeps the new
count). When b is not listed the search stops at the last entry and uses its distance `[?]`. So the range penalty
(0x14319) sees 1 for an adjacent diagonal, 4 for three diagonal squares.

## Moves (stub 0x68) — P, E; D for plain steps

Both the player's move menu (0xA3CF–0xA512) and the AI (0xB663–0xB6E2) do: face the direction (0x31232, combat record
+9 = d), then — the menu after probing: a creature there → melee attack (0xA54C); terrain 0 (edge) → the flee question
→ 0x13465 on yes; terrain cost above movement +6 → the step is refused with a message — free attacks (0x130AD), actions end if the mover is out of
play, the move (0x12F0D), actions end if out of play, the terrain effects (0x2B9D1, run after a step refused for want
of movement too, not after free attacks that dropped the mover; `spells-effects.md` "Terrain"); the menu also ends the actions
of a helpless mover. The AI's own test (0xB235) differs: it steps only when the terrain cost is strictly below +6, and
it flees (0x13465) on its own choice (combat record +0x14 set and the edge ahead). `FightMoves.Step` is the menu's
sequence (a step off the edge is the flee, `StepResult.Fled` when away); `FightMoves.MoveAfterChecks` is the part both
share (free attacks, move, terrain effects). The menu loops while +6 > 1 and sets +6 = 0 below 2 at the end; Escape restores the start cell
(0x317E1), +6 and facing (`fight_moves` m05: the whole move is undone).

**One square 0x12F0D**: cost 3 for an odd (diagonal) direction, else 2; cost above +6 → +6 = 0 and no move (the free
attacks were already taken); else +6 − cost, the slot moves (x, y + the signed direction tables DS:2736 / DS:273F, as
bytes), the grid is rebuilt (0x30ADA), +0x0F and +0x12 (flank counters) = 0, the step sound (START 0x8BBC with DS:2628),
the guards attack (0x12E22), and out of play or helpless (START 0x1713) → +6 = 0. Terrain cost only gates the step; it
is not spent. **Drawing**, in two halves: before the slot write (0x12FB4–0x13011) mode 1, or for a computer-controlled
mover (0x10F ≠ 0) mode 3 and ScrollView(old square, 2) when the new square is outside the 7 × 7 view (0x30E23 on the
offsets from the view origin, arena +2 / +3), then the old cell redrawn (0x30C65, drawing only: a box there goes);
after the grid rebuild (0x1303F–0x1304E) ScrollView(new square, mode). `Fight.StepDrawing` and `Fight.StepDrawn` are
those points; the Client draws the step there (before the guards' attacks and the terrain messages), and nothing
when the step does not happen. DOSBox-X shows no frame of the first half alone (box on the mover, then the step).
DOSBox `fight_moves` m02–m04: three steps of Darkstar (S, S, N) give 29,15 / 29,16 / 29,15, +6 0x18 → 0x16 → 0x14 →
0x12, facing 4, 4, 0, no draws — reproduced.

**Guards 0x12E22** (after a move): the adjacent enemies (range 1, START 0x2FD3: its count and the slots DS:6DE4 + i,
read once; nothing MakeAttack calls rebuilds that list), in that order while the mover is in play: one with +7 ≠ 0 and
not helpless (START 0x1713) gets ScrollView(mover's square, 2) (display, `Fight.GuardAttacking`), +7 = 0, notes the
attack (0x13FF1(guard, mover): the mover's +0x0F / +0x12) and attacks (MakeAttack 0x1407F(guard, mover, not from behind,
no ammunition)) — i.e. `Fight.Attack`.

**Checked under emulation** (s49, `tools/scripts/por_round_oracle.py`, `RoundProgramTests`): 200 one-square moves on
random arenas (movement left 0–5, 12, 0x18, 255 around the costs, all 8 directions, player and computer movers, up to 9
neighbours with guards set, helpless or out of play, MakeAttack trapped and logged; a scripted guard attack putting the
mover out of play or helpless): slots, the cell grid, records, +6, +7, +0x0F, +0x12 and the guard attacks in order
equal `FightMoves.MoveOne`; no draws. 80 terrain checks 0x2B9D1 (see `spells-effects.md` "Terrain"). The callers'
hand-off (menu 0xA48F–0xA512, AI 0xB678–0xB6E2) read whole: both as `FightMoves.MoveAfterChecks`.

**D** (s49, guards after a step): `fight_save.py BASE OUT 1C0B0004000400042400 --hp 200` (save A, 4 orcs of MON2),
RandSeed 12345 written into a state, every party member Guard on its Done menu (Quit when Guard is not offered), 60
keys, the same keys on both sides (`fight_frames.py`). An orc stepping next to Bakshi on guard: the step is drawn, then
Bakshi's guard attack (0x12E22) and its messages. With `Fight.StepDrawing` / `StepDrawn` / `GuardAttacking` 738 of 743
Client frames are found among DOSBox-X's (the guard sequence all found); the Client before the change 737 of 742. The
compare looks each Client frame up among DOSBox-X's; it does not check their order, so this does not by itself show the
order change. A first try that showed the first half as a frame of its own gave a frame DOSBox-X never shows (dropped).
Misses, the same in every build: the first frame, the panel's name box three times, and one frame with the cursor box
on an orc after a step where DOSBox-X's same scene has none `[?]` (not traced).

**Free attacks 0x130AD** (before a move): the adjacent enemies now, minus those still adjacent from the cell one step on
(the slot is moved for that count and back). Each remaining one, while the mover is in play: not helpless, may attack
it (0x13880), carries neither effect 0x4B nor 0x4A (START 0x2D39), and initiative > 0 **or** +0x0F = 0 **or** the mover in
the cone of one of its facing − 2 … + 2 (tried in that order): phase = 2 when record 0xA1 = 0 else 1, then the last
phase (1, then 2) with attacks left (0x112 + p) wins; none left → that phase gets 1; +4 = phase; MakeAttack from behind
with no weapon — **without** the defender's note 0x13FF1 (`Fight.MakeAttack`) — and the attacker's +0x0A put back. As
MakeAttack ends the actions of an attacker with no attacks left (START 0x31D7), a free attack usually costs the
attacker its turn.

**May attack 0x13880**: no target → false; itself → true; DS:681E = 0, the target's trigger-1 effects, and when still 0
the attacker's trigger-0 effects (with +0x0A = target for the call); true when DS:681E stayed 0
(`FightState.TargetRefused`).

**Flee 0x13465** (the menu's yes at the edge; the AI's own choice when +0x14 is set and the probe shows the edge):
away when no enemy is in sight (count targets, range 255); else when the fastest enemy in play (0x15103: movement
0x1291E shr 1, list order) is slower than the creature's movement shr 1, or as fast and **1d2 = 1** (the only draw).
Away: 0x2BDF0 with status 3: in play 0, status 3, HP kept (other statuses get HP 0), icon off the map, actions end, the
dropped-on-down effects go (0x2BEF4); a message otherwise. Actions end either way. Team counts are not lowered here.
Messages (read s30, P): away, 0x2BDF0's own (0x319D2(creature, 3, 0), START 0x16EC from row 10 with the pause, CS:0C4F
of unit 0x127FB) before the status change and the map removal; not away, START 0x1693 (row 24) with CS:0C58
(spells-effects.md "Revive and put out of play"). Core `FightState.FleeMessage`, Client `CombatChooser.FleeMessage`.

## Placement (0xDFC6) — P, E, D

Team counts (START 0x2F7B: in-play creatures per hostile byte); every slot size 0; grid rebuilt. Team 0 (the party
side) at 3D offset (0, 0) with direction facing ÷ 2; team 1 at the facing's step × $6DC1 (VM word, the encounter
distance; low byte of the product) with direction ((facing + 4) mod 8) ÷ 2. Rows per team = (count + 1) ÷ 2. Each
team has four 11 × 6 grids (one per ring) whose free squares are the row bands DS:0306 of its direction (ring 1: band 4).
Each creature in list order: next slot, team = hostile byte, size = 0x6C AND 7, then the search 0xDB48:

- From the ring's start corner c (DS:02E2 ÷ 2), legs: state 1 starts a leg at the corner's start square moved back
  legShift steps along DS:02F2[c + 2]; states 2 and 3 alternate legLen steps along c + 1 and c + 3, legLen growing after
  state 3. A leg ends at a square partly off the grid, after the team's rows of tries on the first leg, or after more than
  11 tries later; the next leg starts one step further back (two when the party side faces an odd direction, ring 0, the
  first time, and one of rings 1–3 is not walled off). A square fully off the grid moves to the next ring whose 3D
  direction DS:02D2 is not walled off at the team's square (always outdoors, DS:49FA > 1), with the grid origin one 3D
  square that way; no ring left → not placed.
- A square (0xDA05) must be free in this ring's grid; the slot's cell becomes (qx + 6·bx + 5·by + 22, qy + 5·by + 10)
  — written before the test, so a failed try leaves it there — and is taken when the probe in place finds no creature,
  terrain ≠ 0 and cost < 255; it is then no longer free.
- Placed: out of play → size 0, and outside a duel (DS:84D4) a party member leaves a body (terrain saved, cell 0x1F).
- Not placed: size 0; a creature with +0x13 = 1 is removed from the combat list (0x1CC10 with "keep $6E3E", icon kept)
  and its slot reused; a party member keeps its slot off the map at the last square tried.

No random draws.

## Open `[?]`

- The area-spell list DS:6DBB; the 9th body; body test via DS:70C7.
- Direction to or from a cell off the arena (endless loop); distance to a creature not on the map; the class of an
  unlisted wilderness code (residue).
- The menu's undo (0x317E1) and the AI's own step choice are left to their callers (`CombatMap.TryPlace`).
- The drawing: view scrolling 0x310D0 is read (combat-menu.md "View scrolling", Client `CombatView.Scroll`); icons 0x31232 are not modelled in Core.
