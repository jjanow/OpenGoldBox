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
right-aligned at 34 / 38, current member white, the others in the name colour of START 0x23B6; HP 0x11B yellow below 0x32: the party list START 0x1307, read s21, also used by the party menu and the explore screen); the shown
entries from row 12 at column 2, first letter white, rest green; the prompt on the bottom line in colour 13. The menu
routine 0x2CC8C takes the item line DS:05F0 (the hotkeys, drawn invisibly) so every letter is a key; a key whose entry
is hidden does nothing. Extended Home / End (set at 0x19A75) pick the member (0x23819). Keys: C 0x19EEB, D 0x1B8B6,
M 0x1BA1E, T 0x1C32D, V View 0x21AED, A 0x1C9EC, R 0x1E684 + 0x1CC10, L 0x1F5E8 (sets DS:49F8), S the save 0x1FD3E,
B DS:49F3 := 4 and return, E the quit question then the save loop and halt.

**J** (0x19E2C, read s22) reads **no line**: it takes the program's first command-line word, ParamStr(1) (Turbo
Pascal System 0AF8:16BC = START 0xC63C: the first word of the DOS command tail after `START`), and compares it with the
string at 0x19AC2 (the unit's CS:0x143; 0AF8:0C10, exact, case counts). Equal: DS:470C := 1 (the cheat: Train without
status, money, hall or experience checks, and Train shown, 0x19B59) and the message at 0x19AC8 through START 0x2233
(row 24, green, the game-speed pause, row 24 cleared). Not equal: nothing (no prompt, no echo, no message). Either
way the menu is drawn again; the flag stays until the menu routine is entered again (0x19AF4 clears it). The same
word (byte-identical copies) is compared with ParamStr(1) by GetKey (START 0x89E3: Ctrl-D toggles DS:49F7, Ctrl-C
quits; view3d.md "Keys while the program is busy"), by Alt-X in a fight (combat-menu.md; its copy at GAME.OVR 0x16374 is inferred from the bytes and its place just
before the routine 0x1638E, the unit's CS:0x3B79 was not resolved) and at
start-up (START 0x10C, string at the main unit's CS:0: equal skips the two overlay calls 0019:0025 and 001C:0025, the
introduction and the code wheel, start-menu.md §1; **D** s24: `START STING` boots straight to the party menu). No game
text: the Client reads the word by offset (`PorTextCatalog` `Hall.Password`).

Client (s22): the word after `START` is the Client's start word: `--param WORD` (remembered in client.json as
`ProgramWord`; `--no-param` drops it) or the controls screen's START WORD row (typed, or picked on the character grid
with the pad or the mouse); its first word is used, as ParamStr(1). J in the hall (`HallScreens.Password`) compares it with
the files' word, sets the cheat and shows the message as above; without the game files J does nothing. J has no entry
on screen in the original; the Client binds it as the action `HallJ` (key J, the pad's left-stick press, rebindable on
the controls screen) and a click on the hall's prompt (bottom line) is J for the mouse. Alt-X in a fight reads the word
since s23 (combat-menu.md).

**Start-up party menu (the same routine), D** (s24, DOSBox-X, `START STING`, 3000 cycles): J showed the message on the
bottom line for 0.23–0.26 s (game speed 1 at start-up × 225 ms) over the menu, with or without a party; once a
character was current Train was offered (the flag set with no party counts later), and it stayed after Remove and Add
(the flag lives until the menu routine is entered again). Train on a new level-1 dwarf fighter (0 experience, 120 gold):
the page "BOB will become: / a level 2 fighter", the question with No highlighted, Yes, "Congratulations..." in green
on the bottom line, the menu back with the hit points 11 → 16 and no gold taken. Client (s24): `PartyMenuFlow` runs
J and Train through the hall's routines (`HallTrainer`: `HallScreens.Password`, `HallScreens.Train`) on the menu's
party and generator, with $6DA8 and the speed from a save loaded at the menu (else 0 and 1); the party menu screen
draws the message, the page and the Learn list, and takes J as in the hall (`HallJ`, a click on the prompt). The page
and question equal DOSBox-X's pixel for pixel (Xvfb). A start word equal to the program's word also skips the
introduction (the Client has no code wheel).

Engine (s20): the Client's hall (`HallScreens.Run`) runs Train, View, Save, Begin and Exit itself; Create, Drop, Modify,
Add and Remove are the party menu's (`PartyMenuFlow` through `HallHooks`, on the game's party and generator; the
creation and roster pages open as a `PartyMenuScreen` overlay over the explore screen), with $6E3E following the list
(the join 0x1F42C adds one, 0x1CC10 takes one off) and their messages (Drop's farewell, Modify's refusal) on the
bottom line. Load is offered only without a current character (the flags above), so in the hall it needs every member
removed first. A save of interface state ≠ 0 ends the game and the screen loads it (`HallLoadException`; the program
leaves the menu after a successful load, 0x19D91, DS:49F8 := 1).

**Load of a state-0 save in the hall, P + D (s25).** Load (0x1F5E8) copies the save's areas, script area, square,
location type, speed and disk byte over the running game and re-reads the map and wall sets; it does not touch the ECL
PC (DS:49ED), the vectors DS:49E3–49EB or the current block DS:84DC, and with state 0 the menu stays up (0x19D8A,
DS:49F8 stays 0) with the save's party, the first current. Begin returns to the PROGRAM 0 handler and the ECL run goes
on at its PC in the **loaded** script area. DOSBox-X: a scratch save whose vector 4 runs SAVE 0xFF → $6DA8, PROGRAM 0,
PRINT, a one-word HMENU (disk 3, map 0); in the hall S wrote a hall save, a copy of it had the PRINT text changed; R × 6,
L, the copy: the menu stayed with its party and Train, and Begin printed the copy's text. Client:
`PorGame.LoadInMenu` (the hall's party object keeps its identity, the members replaced; $6E3E comes with the memory),
pixel-equal to DOSBox-X before and after Begin (apart from the Client's help line). `[?]` The Client cannot open the
hall from a vector 4 run by a load from the command line or the start-up menu (its memory hook needs the game object,
which exists only after the entry): the Client test used a step (vector 0) instead.

**After the hall (PROGRAM 0 returns), P + D (s22).** Begin leaves DS:49F3 = 4 and the handler calls START 0x3379, whose
case 4 draws every window again through 0x32CA (03F1:02E8 with 1, 1, 38, 22; 1, 17, 38, 22; 1, 1, 15, 15; 3, 3, 13,
13: each clears its inside), then the 3D view, the party list (0x1307) and the position line (0x34AB). So the text box
is **cleared**, but the text cursor DS:5E24/5E25 is not touched. DOSBox-X, a test save whose step runs PRINT, PROGRAM 0,
PRINT, a one-word HMENU: after Begin the first text is gone and the second starts on row 17 where the first ended (column
28), the HMENU's word all white (the shared cell, Begin's entry, is past the one-word menu). Before that, the hall
screen and, after the step, the command line opening on its first word (the cell past its 6 entries) were seen in
another run (DAMAGE, then PROGRAM 0). Client: `ClientPlayer.TrainingHall` blanks the box and keeps the cursor; the
hall's hotkeys set the shared cell (`ExploreScreen`, as the party menu's do); the hall and the screen after it equal
DOSBox-X pixel for pixel apart from the Client's help lines.

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
((10 − speed) × 5) shr 1; the clock is read at entry (before the block loads), then each pass draws the current frame
and moves to the next once the DOS clock (START 0x7CCF, hundredths) has moved **more than** (speed + 3) × its delay
since the last change (0x2076E: jg / ja; the change time is the reading that saw it); the loop ends showing the last
frame. No key is waited for anywhere. The script then
prints its epilogue and NEWECLs to civilised Phlan: **no return to the menu**, the game goes on.

**D**: DOSBox-X with a scratch copy whose PROGRAM 0 call was redirected to 0x20941 (and FINAL5 copied to FINAL3):
texts, pictures, positions (changed-pixel boxes of the animations) and order as above; durations consistent with
speed 1 (dragon ~5 s, spirit ~13 s). **D** (s22, a test save whose step runs PROGRAM 8 on disk 2 with FINAL5 copied to
FINAL2, 3000 cycles): every distinct DOSBox-X frame of the dragon, the spirit and the last picture has a Client frame
equal to it pixel for pixel (after two fixes: later frames of a FINAL block are XOR deltas against the first, as PIC's,
which the Client had drawn raw (speckled colours, a black box); the command line stays on row 24 during the sequence).
A text line of 39 characters (the last group's second line) draws its last character on column 39, over the frame,
on a black cell (the glyph routine draws up to column 39); the Client keeps such an overhang cell
(`TextWindow`). **D** (s24, save H, speed 4, 3000 cycles, window grabs about 70 a second): the DOS clock moves once a
BIOS tick (5.49 hundredths), so frames hold whole ticks: the dragon's delays 2, 1, 2, 1 (limits 14, 7) held 3, 2, 3, 2
ticks, one loop 0.550 s; the spirit's 4, 2, 2, 3, 2, 2 held 6, 3, 3, 4, 3, 3 ticks, 1.199 s; 15 loops each (speed 4);
block 8's first frame showed only 0.06 s because loading it took longer than its 105 hundredths (the clock starts
before the load); its last frame and the three Delays took 2.81 s. Client (`EndGame.FrameTime`, `DosClock`): the
whole ticks until the clock is past the limit, each frame timed from the end of the one before (`DelayRequest.Chained`),
so the loops are the program's (Xvfb s24: 0.550 s and 1.209 s). Not modelled: load time (the Client loads at once, so
a first frame shows its whole time) and a clock reading one hundredth off with the tick's phase (a limit within a
hundredth of a tick multiple can hold one tick more: 21 hundredths is 3.82 ticks, and 4 ticks read 21 or 22).

**Amiga v1.0 end game (hunk 13 +0x26AE, read s24 from the program; P, no emulator run).** The same texts at the same
rows (the Amiga strings drop the quotation marks and one double space; the Client keeps the module's texts), but only
the Amiga's own three FINAL blocks (`final.dax` has nothing else), each through hunk 13 +0x25CE (block, step): load
(hunk 19 +0x11B0, later frames XORed with the first as for PIC; no mask is made for FINAL, so every draw is whole), set
table 4 for block 2 and table 3 for the others, draw the start frame in the picture window, then for each frame from
the start: draw it at (3, 3) (hunk 19 +0x10CE: the view buffer at (2, 2), which the refresh hunk 25 +0x896 copies one
cell right and down: screen pixel (24, 24), where PIC close-ups go, hunk 19 +0x51F4) and dos.library Delay(100)
(hunk 36 +0, DOSBase from the start-up's OpenLibrary: 2 s). Order: state byte := 7, text cleared, lines 0–2; block 2
(the dragon, frames 1→4); cleared, lines 3–8; block 7 (the spirit, 1→4); cleared, lines 9–12; block 8 with step −1
(start frame := the count: 6→1, the spirit fades and the pool dries up); state restored and the screen redrawn
(hunk 13 +0x5B16). No arena (DOS blocks 1, 3), no party figures (4–6), no loops by game speed, no closing pauses.
**Engine:** Core `EndGame.AmigaSteps` (`EndGameFrames`, `AmigaFrameTime`); the Client plays it under Best / Amiga when
the chosen art gives the Amiga version of blocks 2, 7 and 8 (`HallScreens.AmigaEndGame`, never a DOS/Amiga mix;
`ClientPlayer.AmigaEndGame` switches it off for the DOS sequence), drawn in the blocks' own palette run
(`HallView.DrawScene`). Under Original DOS the DOS sequence and art, as before.
