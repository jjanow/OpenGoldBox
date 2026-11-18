# The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3)

What Encamp runs after vector 2 (`ecl-format.md` "Vector roles"; the camp routine 0x3B19 runs vector 3 when the rest
was interrupted): the camp menu GAME.OVR 0x196A0 and everything under it. Read from the program in session 13
(2026-11-17); addresses are GAME.OVR file offsets unless marked START (offsets in the unpacked `START.EXE` image); far
calls go through the overlay stubs (`ecl-format.md` "Program references"). The camp unit starts at 0x17831 (its code
segment: `cs:0x1E23` = 0x19654), the time unit at 0x23DCC.

Status marks: **P** read in the program, **D** checked against DOSBox-X, `[?]` not settled. talonius' source was used
only to find routines; every rule below is from the disassembly (objdump, 8086).

Code: `src/GoldBox.Core/Game/CampTime.cs` (`CampState`, rest time, clock + effect timeout), `CampRest.cs` (rest loop,
rest-time menu), `CampSpells.cs` (checks, preparation time, memorise, scribe, lists, Display), `CampParty.cs` (order,
drop, speed, pictures), `CampCast.cs` (casting outside a fight); `src/GoldBox.Data/Por/PorSaveGame.cs` /
`PorParty.cs` (`Build`, `Save`). Client: `src/GoldBox.Client/Play/CampScreens.cs`. Tests: `CampTests`,
`CampRestCaptureTests`, `SaveWriteTests` (Core), `CampScreenTests`, `CampPlayTests` (Client).

**DOSBox (capture `rest_interrupted`, s9, replayed s13)**: save A's party in camp at 10:50 with $6DD2 = $6DD3 = 24
(vector 2), then R, H, I×8, R. The rest stopped after 48 ticks (clock 14:50, two 1d100 draws), vector 3 ran.
`CampRestCaptureTests` replays the rest through `CampRest` on the game's generator and then vector 3: RandSeed and the
$4900 and $6B00 areas match. **D** for the rest loop's tick, the interval counter, the roll and the clock.

## Data-segment variables (not VM cells)

The camp keeps its state in the program's data segment. Several addresses overlap VM cells of the same number that
mean something else (VM $6DCA is Search, $6DCB surprise, $49FB no area map), so the Client keeps them in
`CampState`, never in `EclMemory`.

| DS | Size | Meaning |
|---|---|---|
| 49F3 | byte | interface state: 2 while the camp runs (saved and restored by 0x196A0) |
| 49FB | word | rest ticks since the last interruption roll; cleared only by the roll (not on camp entry) |
| 471E | 72 bytes | per creature-list position 1..72: has timed effects left (0x23DCC) |
| 6DB5 | far ptr | remembered cast target; cleared when the Cast menu opens and when the camp ends |
| 6DC2 | 7 words | the rest time, shaped like the game clock (1 minutes, 2 ten minutes, 3 hours, 4 days, 5 months, 6 years); cleared on camp entry and after each rest |
| 6DD0 | 8 bytes | spell preparation timer per party position 1..8 |
| 6DD8 | word | rest ticks since the last whole-party heal; cleared on camp entry |
| 6DDA | byte | set while the rest loop runs (border suppression; display only) |

VM cells read: $6DD2 (rest ticks between interruption rolls; zone $6B00 + 0x5A4) and $6DD3 (the roll's percent,
+0x5A6), $6E3E (party size), $49E5 (anti-magic area, zone $4900 + 0x1CA).

## Camp menu (0x196A0) — P

DS:49F3 := 2 (old value kept), DS:6DD8 := 0, the rest time cleared, the camp message (0x19654) printed at column 1, row 18, the
pending memorising and scribing of the whole party cleared (0x17A45). Then the menu (DS:0508 in v1.3: Save, View,
Magic, Rest, Alter, Exit, with its prompt 0x1968C) until Exit, Escape or an interrupted rest. Extended keys (Home / End) select
the previous / next member (0x23819). Save: the save (below), then **always** the quit question (0x19693), also after
Escape at the slot menu. View: the character view. Magic: 0x18C22. Rest: 0x17FAB. Alter: 0x1948A. On the way out:
0x17A45 again, DS:6DB5 := nil, DS:49F3 restored, the position line and the text window redrawn. The routine returns
the interrupted flag to 0x3B19.

Menu lines (the menu routine, START): every capital letter or digit starts an item and is its hotkey, so a word with
its capital in the middle has that letter as hotkey and a lower-case word joins the item before it; Escape answers 0. The Client parses the lines the same way and draws the
hotkey letter white. The menu routine itself (0x2CC8C: keys, highlight, Enter, the extended byte) is in `shop-format.md` "The menu routine" (s15).

## Rest (0x17FAB → 0x24A66) — P, D

**0x17FAB** (the camp's Rest and the Magic menu's Rest): for every party member the preparation time (0x17831, below;
it also stores each member's base hours in record 0x2C); the longest m goes into the rest time: hours = m div 60, ten
minutes = (m − 60·hours) div 10, minutes = m mod 10 (days stay 0). Then the rest (0x24A66), then the rest time is
cleared and the position line redrawn.

**The rest loop 0x24A66**: each party position's timer DS:6DD0 := 0; all 72 flags DS:471E := 1; tick := 0; the text
box cleared; the rest time shown; DS:6DDA := 1; the rest-time menu. Unless it was left, while any of days, hours, ten
minutes, minutes is non-zero:

1. a key waiting (START keypressed): the rest time shown, the stop question 0x24A36 (Yes stops before the tick);
2. rest time − 5 minutes (0x24246);
3. every 5th tick the rest time is shown;
4. the clock + 5 minutes (0x241A1, which also ages the effects, below);
5. the heal count (0x2463D);
6. the preparation timers (0x248D6), then the hourly base count (0x2497E);
7. when $6DD2 > 0: DS:49FB + 1; at $6DD2 or more DS:49FB := 0 and 1d100 (the dice routine through 00B0:0048, the
   generator's `Random(100) + 1`) ≤ $6DD3 interrupts: the message box cleared, the rest time shown, the interruption
   message 0x24A45 (white, row 19), a key; the loop ends and the routine returns true.

Then the text box is cleared and DS:6DDA := 0.

**Rest-time menu 0x244ED**: highlighted word starts at 2 (minutes). Shows its label (0x243AF) at column 1, row 17, then days
and hours (two digits each, with a colon between), then minutes as 10 × ten-minutes + minutes; the highlighted word in white
(0x243BC). Menu (0x244A5): Rest, Days (hotkey Y), Hours, Mins, Inc, Dec, Exit. Extended keys: Up = Inc, Down = Dec, Left = the next word up
(past days back to minutes), Right = the next word down (past minutes to days); other extended keys do nothing.
Enter = Rest. Y / H / M pick the word; Inc adds 1 to it (5 to the minutes word when minutes are highlighted); Dec
takes 1 (5 minutes) through 0x24246; both then carry (0x2416F). Returns true on Rest, false on Exit / Escape.

**Rest-time arithmetic**: 0x2416F = the clock carry 0x240BE on DS:6DC2 (so a year overflow would age the party), then
any months fold into days (30 each), months := 0, days capped at 99. 0x24246 (count, unit): nothing when days, hours,
ten minutes and minutes are all 0; while count > word[unit] the next non-zero word above it (stopping at months) lends
one, carried down with each lower word gaining its limit; when the search reaches months the whole rest time is
cleared and count := 0; then word[unit] − count and the carry.

**Heal (0x2463D)**: DS:6DD8 + 1; at 0x120 (288 ticks, a day) each party member is healed 1 HP through 0x2C647 without
bypass (`spells-effects.md`); the rest time shown, the heal message 0x24623 (row 19), the party list redrawn when
anyone healed, a key, the box cleared, DS:6DD8 := 0. The message comes even when nobody healed.

**Preparation (0x248D6, 0x2497E)**: each tick every member's timer goes down by one (a byte: 0 wraps to 255). When it
is 0 and record 0x2C is 0, the next pending spell is finished: scribing first (0x247B3), else memorising (0x246F1);
the timer := 3 × the level of the next pending one (0 when none). Every 12th tick (an hour) each member with record
0x2C > 0 (signed) loses one; reaching 0, the timer := 3 × the level of its first pending scroll spell, else of its
first pending memorised spell (nothing is finished at that moment). So spells finish one at a time, 3 ticks (15
minutes) per level, after the base hours.

- **0x246F1** (memorise): the 21 slots in order; a slot above 0x7F: with done clear it loses bit 7, the message
  (0x246E3 after the name on row 19, the spell name on row 20, a key) shows and done is set; with done set the scan returns that
  spell's level (table byte 1). 0 when none follows.
- **0x247B3** (scribe): the readable items' scroll bytes above **0x80**; finishing one sets record 0x32 + id := 1
  (known), takes it off the scroll (far 0096:0043: the byte := 0, item word 0x30 − 1, below 0xD2 the item leaves the
  character; the scan goes on over the removed item's bytes), then its message (0x247A7). `[?]` The item removal routine
  (START) is not read beyond unlinking.

**Effect timeout 0x23DCC** (called by the clock advance 0x241A1, count and unit): outside camp (DS:49F3 ≠ 2) all 72
flags := 1; in camp nothing happens unless a flag of positions 1..$6E3E is set. The duration in minutes (count × the
clock limits below the unit, 16-bit) goes in steps of at most 10. Per step, each flagged creature of the list (flag
cleared first) walks its effect list up to the record that was last when the step began: a record with 0 minutes
(word +1) is skipped; one with more minutes than the step loses the step and sets the flag; any other is removed
(0x2AF10 through 00B0:002A, the handler with "removed" when byte 4 is set). After a removal the walk goes on at the following
record, except when the record before the removed one was the list head: then it starts again at the head, which ages
a second time in that step. Records after the walk set the flag when they have minutes left.

Outside camp the same routine runs after every clock advance (0x241A1 calls it with its own count and unit: steps,
Look, CLOCK, combat rounds), and in a fight (DS:49F3 = 5) the list DS:5D96 holds the monsters too, so they age as well.
Core: `CampTime.AdvanceOutsideCamp` / the `TimeOutEffects` overload with the flags; exploration through
`AreaLoop.EffectTimeout` (wired by `PorGame`), the fight's end of round through `FightKit.WireClock`. The flags
DS:471E are one array per session (`PorGame.Camp`, `PorPlayer.Camp`): what a fight or a step leaves in them decides
which members a later rest ages. DOSBox `fight_casts`: the Mirror Image's minutes 2 → 1 at the end of round 0, gone at
the end of round 2.

## Magic menu (0x18C22) — P

Menu DS:0531 (Cast, Memorize, Scribe, Display, Rest, Exit), no prompt; extended keys select the member; until Exit,
Escape or an interrupted rest.

**Check 0x17B9D** (activity 1 cast, 2 memorise, 3 scribe): cast with $49E5 > 0 → the no-magic message (0x17B33);
any activity with status 1 (animated) or record 0x10D = 0 → the no-condition message (0x17B53) with the
activity's ending (0x17B6A / 0x17B7A / 0x17B8A) (the character's message, a key).

**Spell lists** (the list builder behind the spell menu; menu types used by the camp): 0 memorised slots 1..0x7F; 1
the spell book, ids 1..55 with record 0x32 + id ≠ 0 (56 is never listed); 3 the spells of the character's readable
items (base slot 11..13), 6 the same above 0x80 only; 5 memorised slots above 0x7F. A level heading (DS:0A79 + 41 ×
level) comes before a spell whose level differs from the previous one; a pending spell is marked with an asterisk. For the scroll
lists a scroll identifies itself (item 0x35 := 0) when the reader has effect 0x10 or a cleric level with a cleric
scroll (slot 12); a still-hidden scroll adds nothing. The page title is the character's name, the word 0x239CE and the list's
words (0x23973–0x239BF); with an action (Cast, Memorize, Scribe: 0x27018–0x27026) the prompt 0x27053.

**Cast (0x17CD2)**: DS:6DB5 := nil; the check; then the memorised list (Cast) until nothing is chosen: the text box is
cleared and the spell cast (0x27C38); an empty list → the no-spells message (0x17CBA). **The cast 0x27C38 outside a fight**:
table byte 7 = 0 → the spell name (row 19), the combat-only line 0x27BC6 (row 20), the question 0x27BE0 (Yes: the memorised spell
is removed, START 0x202B). Else the cast message (0x27C16) and the spell name (a key), then the non-combat target handler **0x27AA9**: DS:6DB5
defaults to the caster; the target list is the caster; mode 1: done; mode 2: the member pick (prompt 0x27A96,
starting at DS:6DB5; giving up clears the targets and DS:6DB5 and nothing is cast); mode 4: every creature of the list;
other modes: nothing. On a pick: invisibility leaves the caster (0x2BEBC), the memorised spell is used up (START
0x202B), DS:681B := spell, the spell routine runs (table DS:6B84), DS:681B := 0.
Core models every spell routine 1–67 (`FightSpells.Modelled`, s13); a spell without one would be refused with a
notice, the spell kept. `[?]` The effect messages' rows and words are not matched (the
Client lists the effect engine's events).

**Memorize (0x1813B)**: the check; the pending list (type 5) first: when not empty, the keep question 0x180F0 — No
clears the pending slots (0x1798A), Yes ends here. Then until done: no slot byte at all (0x17DD5: cleric or
magic-user levels 1–3) → the no-slots message (0x18108); else the slots are shown (0x17DA1, per class with any
slot its free slots of levels 1–3, blank where the slot byte is 0) and the spell book (Memorize): nothing chosen ends;
a spell whose class and level have a slot left (0x17A8D: slot byte 0xB1 + 3·class + level less the memorised or
pending spells of that class and level, a byte that wraps) goes pending (+0x80) into the first zero byte from record
0x17 on (the walk is not bounded by the 21 slots), then the slots are sorted (0x18055: exchange sort by id, bit 7
ignored, empties first). When the book was shown, the pending list and the question again (0x18123; No clears).

**Scribe (0x1838C)**: the check; the pending scroll list (type 6) first (the keep question 0x182EA, No clears the bits
0x179CD, Yes ends). Then the scroll spells (type 3, Scribe) until nothing is chosen (an empty list → the message 0x18300): a
spell already known → 0x18318; a readable item with that spell pending → 0x18334; no slot byte for its class and
level (unchecked index) → 0x18357; else the first byte equal to the spell on **any** item (readable or not) gets bit 7. These three messages
are on the bottom line. When shown, the pending list and the question again (0x18376).

**Preparation time 0x17831**: base hours 0; 4 when any memorised slot or readable scroll byte is pending; 6 when a
pending spell's level is above 2; record 0x2C := base; minutes = base × 60 + 15 × (sum of pending memorised levels) +
15 × (sum of pending scroll levels) (byte sums).

**Display (0x187ED)**: a window (title 0x187E4) listing each member's name (heading) and its effects in list order:
ids 1, 2, 5, 8–14, 16, 17, 19–25, 28, 29, 32–34, 36–39, 41, 42, 45, 46, 49, 51–53 by the first spell 1..56 whose table
effect byte (+10) is the id (the text 0x186F1 and the id when none); ids 4, 7, 27, 31, 35, 44, 50, 54, 55, 59, 61, 71, 72, 89
by their own texts (0x186F9–0x187C6, in that order); every other effect is left out. The text 0x187D0 when none,
a blank line after each member.

## Alter (0x1948A) — P

Menu DS:055A (Order, Drop, Speed, Icon, Pics, Exit), prompt 0x19441; extended keys select the member.

- **Order (0x18FB1)**: prompt 0x18F71 with DS:0584 (Select, Exit) or DS:05AD (Place, Exit). Select mode: extended
  keys pick the member. Place mode: Home moves the current character up (0x18CDA: it swaps with the one before; the
  head goes to the end of the list), End down (0x18E32: swaps with the one after; the last goes to the head); the
  current character stays the same record. Enter, P and S switch the mode (into Place: the message 0x18F9F; back:
  the message box cleared). The Client adds the mouse: in Place mode a click on a name above the current member moves
  it up one place, below it down one (our addition; the program has no mouse).
- **Drop (0x19116)**: the only member: the quit question 0x190A8 (Yes removes it and quits). Else the message 0x190B6, the
  question 0x190C3: Yes → the farewell 0x190D5 (record 0x10D ≠ 0) or 0x190E7 (otherwise), then 0x1CC10 (free the icon, size
  decreased): the member leaves the list, the one before it (or the new head) becomes current, $6E3E − 1. No →
  the message 0x190FC.
- **Speed (0x192C8)**: the speed line (0x19261, the value, 0x1926F; row 18); the menu offers Faster while n > 0,
  Slower while n < 9, and Exit (0x19286–0x19296); Down / F speed up, Up / S slow down, until Exit or Escape. The game delay is DS:49E2
  (`EclMemory.GameSpeed`): new game 1, the save copies it into $49FC, Load takes it back. The Client's Alter Speed,
  DELAY, the combat pauses and the Done menu's Speed all use `EclMemory.GameSpeed`.
- **Icon**: the icon editor 0x1D1AC (character creation; read s20, start-menu.md §3, Core `IconEditor`). `[?]` Not wired
  in the camp yet: a notice.
- **Pics**: monsters on/off, portraits on/off, Exit (0x19449–0x19485); M and P toggle the two flags (saved as $49FF bit 1 and bit 0).

## Save (0x1FD3E) — P

The slot menu (prompt 0x1FCAB, the ten letters A–J 0x1FCBD); Escape gives up (the camp still asks the quit
question). The file `savgam<letter>.dat` in the save directory is created (a disk prompt on failure); the
saving message 0x1FD11 on the bottom line; the three wall sets' ids and slots into $4AFA–$4AFF, the game delay into
$49FC, monster pictures × 2 + portraits into $49FF, the disk number into $6E12; then the layout of `save-format.md`
(disk byte, the three word areas, the script area, the party position, location type, interface state, the count n
and eight string[40] stems `CHRDAT<letter><k>`), then each member's files (`.SAV` record; `.ITM`/`.SPC` deleted and
written again only when there are items / effects) and the roster files `<name>.CHA/.ITM/.SPC` of each member deleted.
`SaveWriteTests`: save A (shipped) and save B (written by the program in DOSBox-X) read into memory and written back
give the same bytes, except the leftover memory after each stem; the member files come back byte for byte.

The Client writes saves to its own folder (`$XDG_DATA_HOME/goldbox/por-saves/`), never into the game folder; `--save
X` looks there first. `[?]` The roster-file deletion (its name-to-file rule) is not done. Location type: the
program writes DS:49FA, the view mode (0x1FF9D; `save-format.md`); `PorSaveGame.Build(memory, n, letter, state)` takes
it from `EclMemory.ViewMode` and $49FC from `EclMemory.GameSpeed` (s15); the Client's camp Save uses it. The bytes
after each stem and unused name
slots are written as zero (leftover memory in the program).

## Open `[?]`

- The camp Cast's effect messages' layout.
- The icon editor in the camp (built for creation); the roster-file deletion on save.
- Cell positions of the camp's messages other than the rest's (the Client keeps its own frame).
