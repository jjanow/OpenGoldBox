# The training hall and the end game (Pool of Radiance, DOS v1.3)

ECL PROGRAM (opcode 0x38, handler GAME.OVR 0x3B56): 0 → the hall menu 0x19AE0 then START 0x3379 (screen redraw by
DS:49F3); 8 → the end game 0x20941; 9 → the camp. Read from the program in session 19 (2026-11-18); addresses are
GAME.OVR file offsets unless marked START (unpacked START.EXE image). Status marks: **P** read in the program, **E**
checked under emulation (unicorn), **D** checked against DOSBox-X, `[?]` not settled. No game text is quoted here; the
Client reads the strings from the player's files by offset (`PorTextCatalog` keys `Hall.*`, `End.*`).

Code: Core `TrainingHall` (`src/GoldBox.Core/Game/TrainingHall.cs`), `EndGame` (`EndGame.cs`); Data
`PorTrainingTables`; Client `HallScreens` / `HallView`. Tests: `TrainingProgramTests` (oracle, 900 cases),
`TrainingHallCaptureTests` (DOSBox), `EndGameTests`, Client `HallPlayTests`. Oracle: `tools/scripts/por_train_oracle.py`.

## The hall menu (0x19AE0) — P, D

The same routine is the start-up screen (agent A's start menu) and the hall entered by PROGRAM 0. On entry: disk
DS:5376 := 3, cheat flag DS:470C := 0, DS:49F3 := 0. The eleven entries are DS:0619 + 42·i (string[40], shown flag at
+41): Create, Drop, Modify, Train, View, Add, Remove, Load, Save, Begin, Exit. Flags (0x19B2D): with a current
character Drop, Modify, View, Remove, Save, Begin on, Train on when the $6B00 area's word +0x550 (VM **$6DA8**, the
hall's class mask) is above 0 or the cheat flag is set, Load off; without one those off and Load on. Create, Add and
Exit keep their data-segment defaults (on).

Screen (DOSBox-X): outer frame; the party list across the top (header at column 1 row 2, members from row 4, AC / HP
right-aligned at 34 / 38, current member white, a damaged member's HP yellow `[?]` rule from the screen only); the shown
entries from row 12 at column 2, first letter white, rest green; the prompt on the bottom line in colour 13. The menu
routine 0x2CC8C takes the item line DS:05F0 (the hotkeys, drawn invisibly) so every letter is a key; a key whose entry
is hidden does nothing. Extended Home / End (set at 0x19A75) pick the member (0x23819). Keys: C 0x19EEB, D 0x1B8B6,
M 0x1BA1E, T 0x1C32D, V View 0x21AED, A 0x1C9EC, R 0x1E684 + 0x1CC10, L 0x1F5E8 (sets DS:49F8), S the save 0x1FD3E,
B DS:49F3 := 4 and return, E the quit question then the save loop and halt. **J** reads a line and, when it equals the
password string at 0x19AC2, sets DS:470C (the cheat: Train without status, money, hall or experience checks) and shows
a message. `[?]` The Client does not offer J (no hidden text entry yet); Core takes the flag.

Engine (s20): the Client's hall (`HallScreens.Run`) runs Train, View, Save, Begin and Exit itself; Create, Drop, Modify,
Add and Remove are the party menu's (`PartyMenuFlow` through `HallHooks`, on the game's party and generator; the
creation and roster pages open as a `PartyMenuScreen` overlay over the explore screen), with $6E3E following the list
(the join 0x1F42C adds one, 0x1CC10 takes one off) and their messages (Drop's farewell, Modify's refusal) on the
bottom line. Load ends the game and the screen loads the save (`HallLoadException`; the program leaves the menu after a
successful load, 0x19D91). `[?]` A save with interface state 0 loaded here would keep the program's menu up with the
save's party (start-up's Load does, start-menu.md §5a); the engine starts the loaded game instead.

## Train Character (0x1C32D) — P, E, D

In order (Core `TrainingHall.Check` / `Train`):

1. Status 0x10C ≠ 0 (no cheat) → message (bottom line, colour 14, START 0x7DF8: shown for speed × 225 ms).
2. Gold total (0x238F8, rounded) below 1000 (no cheat) → message.
3. Over classes 0..7 with level L > 0: the class's hall bits DS:05DE are **added** (byte) into "all"; a class the race
   limit stops (inline copy of START 0x3AF7) is skipped; T(L+1) = DS:409B + 0x38·class + 4·(L+1) (signed dword, read
   past the decoded rows as the program does) above the experience → skipped (unless cheat); T(L+1) ≤ 0 → skipped;
   else its bits are added into "ok", and when T(L+2) > 0, XP ≥ T(L+2) and T(L+2) above the cap so far, cap :=
   T(L+2) − 1. **A cap above 0 is written into the experience now**, before the hall check and the question (D: a
   refused fighter's 5000 became 4000).
4. No cheat: (all AND low byte of $6DA8) = 0 → wrong-class message; (ok AND mask) = 0 → not-enough-experience
   message. Trained bits = ok AND mask (ok alone with cheat).
5. Page: the name (column 4, row 4), the will-become text after it, then for each class with L > 0 whose bits meet the
   trained bits (the bits alone: paladin 10 and ranger 10 share the fighter's 8 and cleric's 2), from row 5 at column 6:
   first line's prefix, then the and-prefix, the new level, the class name (DS:0D38 + 27·class). The yes/no question.
6. Yes: a message (colour 10, START 0x2233); the payment 0x1DC6C(1000) unless cheat; n = classes with L > 0; each
   trained class + 1 and, when drained (0x74 > 0), 0x75 − 0x75 div 0x74, 0x74 − 1; level stats 0x2AA87; when the
   magic-user level 0x9B rose (signed), the Learn list (0x239D6 mode 4, below), the spell known := 1; hit points: rolls
   = 0x1DBB4 (per trained class DS:08B6[c] d DS:08BE[c], 0x2BCCD; at level 1 a roll below 2·sides div 3 is raised to
   it; byte sum); 0xB1 + max(1, rolls div n); CON = 0x1D8B0 (per class with a level: DS:408E[Con] signed, +1 each for
   Con > 16 and > 17 when byte 0x2F = 2; byte sum); 0x32 + max(1, (rolls + CON) div n); current HP keeps its distance
   below the maximum (bytes).

**Payment 0x1DC6C** (character, gold): C = gold × 200; from coin k = 0 up while C > 0: n = word(C div value[k] + 1),
at most the coins held, C − n·value, coins − n; then when C < 0, change from k = 4 down: n = word(C div value[k]),
coins + n. Values are DS:0D24 + 4k (signed dwords), coins record 0x88 + 2k, both read past the five coins (gems,
jewellery, the next record words; values from the class-name bytes). A zero value is run-time error 200
(`ProgramHaltException`): reachable with a purse that rounds up to 1000 gold but holds less (oracle cases).

**Learn list (mode 4, 0x275B4)**: spells 1..56 whose slot byte 0xB1 + 3·class + level (table DS:31FA bytes 0, 1) is
above 0 and not known. The page title is the name with a possessive, the spells word and the list words; the only
action is Learn; Escape shows it again. With an empty list the routine returns 0 and Train writes byte 0x32 + 0 — the
**maximum HP** — := 1 (E). `[?]` the first highlighted row (DOSBox-X showed the last one; the Client starts there).

**E**: 900 generated characters (`program-v1.3/training.json`): record bytes, refusal, RandSeed, Learn list, halts.
**D** (`dosbox-v1.3/training_hall`): thieves' hall (mask 116) refusing the dwarf fighter with the cap; the halfling
thief to level 2 (coins 500/300/20/900/20 → 0/0/1/2/5, 1d6); magic-users' hall (113): a magic-user to level 2 learning
spell 20. Hall masks seen in ECL3 block 11: 113 magic-users, 114 clerics, 116 thieves, 120 fighters.

## End game (0x20941) — P, D

Run after Tyranthraxus (ECL5 block 7 $A82A). DS:49F3 saved, := 7; the text window cleared; three lines (green, column
1, rows 17–19); picture block 1 of `FINAL<disk>` (name from 0x20812 + DS:5376) at cell (1, 1); the dragon animation
(block 2) at (3, 5); picture 3 at (1, 1); party figures by $6E3E: > 1 block 4 at (1, 12), > 2 block 5 at (14, 10), > 3
block 6 at (1, 10); six lines (rows 17–22); the spirit (block 7) at (3, 1); four lines (rows 17–20); block 8 at (3, 1)
once; three Delays (speed × 225 ms); DS:49F3 restored, the screen redrawn. Animation 0x206A3: loops =
((10 − speed) × 5) shr 1; frame k stays until (speed + 3) × its delay hundredths of a second (START 0x7CCF: the DOS
clock in hundredths) have passed; the loop ends showing the last frame. No key is waited for anywhere. The script then
prints its epilogue and NEWECLs to civilised Phlan: **no return to the menu**, the game goes on.

**D**: DOSBox-X with a scratch copy whose PROGRAM 0 call was redirected to 0x20941 (and FINAL5 copied to FINAL3):
texts, pictures, positions (changed-pixel boxes of the animations) and order as above; durations consistent with
speed 1 (dragon ~5 s, spirit ~13 s). `[?]`: exact frame timing not measured; the Amiga program's end sequence (it has
blocks 2, 7, 8 of a repainted dragon) is not read, so the Client draws the DOS blocks under every art setting.
