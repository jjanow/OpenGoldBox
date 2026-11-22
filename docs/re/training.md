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

**Amiga (read s47, FS-UAE s47).** One routine too (hunk 10 +0x64E; the area script's PROGRAM handler, hunk 16
+0x4BCE, calls it for 0, the end game hunk 13 +0x26AE for 8), copying its eleven entries from hunk 11 +0xD2 (the same
42-byte stride and shown flags): the program's own words, mixed case ("BEGIN Adventuring"), the last naming the
Workbench where DOS names DOS. The Client takes them under the Amiga art from the strings the importer reads there
(`PorAmigaText.PartyMenuKey`, as the start-up party menu already did; `HallText.Entry`). The quit question (E, hunk 10
+0xA3C) still names DOS on the Amiga, with pens 24 / 28 / 16 (the warning pen), as DOS's. **Original bug, fixed
(user, s48)**: under the Amiga art the Client's question (the hall's and the start-up party menu's, one routine)
names what the menu's Exit entry names, that entry's last word in place of DOS (`AmigaView.HallQuit`; the words
come from the player's program at import, none in the source). Client s49 (`--art amiga`, test save C's hall, E):
the question named the Workbench; the camp's quit
after Save (hunk 1 +0x50B8) and the one when the last member leaves (+0x4B5E) are the Amiga's own Workbench wording,
read at import (`PorAmigaText.CampQuitKey`, `DropQuitKey`; seen in the code, not run). Each entry: its first letter as a
character through the string routine (pen 16, hunk 10 +0x794), then the rest from the next column (pen 28, +0x7EE),
so the rest's JAM2 pass 1 covers the letter's pass-2 pixel (the Client draws the rest as a whole string, `Chrome.Whole`,
in the hall and the start-up party menu), and the prompt through the menu routine (x + 3 / x + 4). FS-UAE s47 (the
hall after a test save's PROGRAM 0, and the start-up menu) against the Client `--art amiga`: the entries and row 24
equal pixel for pixel (the Client had kept the letter's pass-2 pixel and drawn the hall's prompt as plain text).

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

Engine (s20): the Client's hall (`HallScreens.Run`) runs Train, Save, Begin and Exit itself; Create, Drop, Modify,
View, Add and Remove are the party menu's (`PartyMenuFlow` through `HallHooks`, on the game's party and generator; the
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
pixel-equal to DOSBox-X before and after Begin (apart from the Client's help line); that check used a step (vector 0).
**Load of a game save in the hall (state ≠ 0), P + D (s39).** The same Load; then 0x19D91 sets DS:49F8 := 1 and
the menu ends at once (0x19E6C). PROGRAM 0 calls START 0x3379 (0x3B95) with the save's DS:49F3 and returns: the
running ECL run goes on at its PC in the loaded script area, with the running game's vectors DS:49E3–49EB and block
DS:84DC; vector 4 does not run and the area is not entered again (START 0x13D calls the area loop 0x403E once a
session; its only exit is DS:49FF). DS:49F8 stays 1 until the next script init (NEWECL), which then keeps $4A00–$4A1F
and $6E79–$6E82. DOSBox-X (scratch saves I and J from save A, both state 2: vector 0 = PICTURE 2; PROGRAM 0; PRINT;
a one-word HMENU, J's text changed, J's vectors 1 and 4 printing): Up in I, Remove × 6, Load J: "TEXT OF SAVE J."
with no vector 4 text; a state there read PC $9940 (the HMENU), vectors I's ($9920, $991F, …), DS:84DC 20 and DS:49F8 1;
after WAIT the step ran I's vector 1 (J's $991F: EXIT, no vector 1 text). State 2 (a camp save, the usual kind) drew
case 2: the camp picture (PIC 29, animated by the HMENU since 84E0 was still set from PICTURE 2) and "CAMPING" after
the position line, which stayed after the step (START 0x366D tests DS:49F3 = 2; nothing set DS:49F3 again). Client
(s39): `PorGame.LoadInHall` (the copy in place, DS:49F8 set) and `HallState.LeaveAfterLoad`; the text and the run
match (before s39 the Client loaded the save as a new entry and ran its vector 4). A save from another area runs
whatever bytes lie at the old PC in the program.
**The Amiga's hall Load, P + FS-UAE (s47).** The same path as DOS, read whole. PROGRAM 0 (ECL 0x38 → hunk 16
+0x4B88; vector 0 runs before the step: area loop hunk 16 +0x5384–0x5396) calls the party menu (hunk 10 +0x64E via
+0x5538), then hunk 13 +0x5B16 (START 0x3379's copy, by $BA = DS:49F3) and returns into the run. The party menu sets
$BA := 0 on entry (+0x692) and draws by it (case 0: the frame); its L (+0x9C2, offered with no current member) calls
Load (+0x9D0 → hunk 16 +0xFC2) and, when the loaded $BA is not 0, sets $BF (DS:49F8) := 1 and returns (+0x9DE); Begin
returns with $BA := 4 (+0xA16). Load: the path requester (default `poolsave:`; its `save` file lists the letters),
the letter, then fread of the four areas, the position, $C1 (DS:49FA), $BA, the party size and the stems
(save-format.md "Amiga"); $BD / $BC (the Pics flags) := $49FF / 2 and $49FF & 1, the speed $A8 := $49FC, $6E3E := 0;
each stem's `.sav` read into a new record and joined (hunk 16 +0x7FE, +0xEDE); each member's `NAME.cha`, `.itm` and
`.spc` deleted where present (hunk 0 +0x1AC); each member through hunk 16 +0x2D0 with 1 (DOS 0x1E468 with 1, the
load's refresh); with $C1 < 2 the map $49C5 read (hunk 25 +0x382E); with $C1 > 1 **ICON block 0 into icon slot 11**
(hunk 22 +0x193A, the marker's third slot: geo-format.md "A hall Load on the overland"), else the three wall sets
from $4AFA–$4AFF as DOS (+0x14AC–0x1532, hunk 25 +0x359E); with $4AA2 ≠ 0 hunk 13 +0x2872 with 0 (DOS 0x82:0x2A with
0, at the same point; not decoded `[?]`, it plays no marker or view draw); the IDCMP queue flushed (hunk 25 +0xA08) and
row 24 cleared (hunk 19 +0x58A8). No draw of the view or the marker anywhere in it. DOS's Load (0x1F5E8–0x1FC9D) has
the same tail in the same order (0x1FBB3 map, 0x1FBC9 ICON 0 → slot 11, 0x1FBEE walls, 0x1FC89 the $344 call).
Client: `PorGame.ApplyInHall` / `ReloadSavedMap`. Slot 11 left holding ICON 0 after a hall Load is an
**original bug, fixed (user, s48)**: the Client loads the marker's three slots again (ICON 0, 1, 2 into 9, 10, 11, as
the overland interface does), so the marker animates as before the Load (`geo-format.md` "A hall Load on the
overland"). FS-UAE s47: disk A with test saves (block
26's overland, vector 0 = PROGRAM 0; Remove × 6, Load C with vector 0's code an EXIT) ran as read:
the camp screen ("CAMPING" after the position line), the step, and the marker of counter 4 drawn from ICON block 0.

**The camp state after the Load, P + D (s41).** Load reads DS:49F3 back from the save (0x1F87B) and the hall's menu
ends without its Begin value (0x19DCF sets 4 only on Begin), so PROGRAM 0's START 0x3379 (0x3B95) draws case 2: the
3D layout (0x32CA with 1: every window blanked, the text box among them, the cursor kept), PIC 0x1D loaded into the
frame table by 0x2E170 with 0, which does **not** draw it (the box stays blank), the party list and the position line
with "CAMPING". The picture showed in s39 only because the script's HMENU animated it: the menu routine 0x2CC8C
animates when its flag is set **or VM $4AFF is not 0** (0x2CD5D–0x2CD6D, frames in the table) and clears $4AFF at its
end (0x2CFAD; no other writer in START.EXE or GAME.OVR). $4AFF is the save's third wall-set slot cell ($4AFC + 3, 3 in
save A), so the first menu after any Load animates; after a start-up Load the command line is that menu, which is why
PICTURE 2 + HMENU in the first step stays on its first frame there (DOSBox-X s24, again s41 with a scratch save H),
while here no menu runs between the Load and the HMENU (DS:84E1 is 0 after the start-up Load, so it is not the arm).
DS:49F3 stays 2 until one of its writers runs (EclMemory.InterfaceState): COMBAT's end, a script switch, a load's
first pass, the overland interface START 0x3701, the hall; not a step, a turn, the camp (it sets 2 and puts the old
value back, 0x196AC / 0x197F2) or View. **D** (s41, scratch saves I / J from save A, the step's vector 0 = PICTURE 2;
PROGRAM 0; PRINT; HMENU WAIT; Up in I, Remove × 6, Load J): the camp picture animating under "TEXT OF SAVE J." and
"15,4 W 10:50 CAMPING"; after WAIT the step to 14,4, still CAMPING; Left, still CAMPING; V then Escape: case 2 again,
the box **blank**, the text box empty, CAMPING; Encamp, Exit: the view, CAMPING. Client (s41): `EclMemory.
InterfaceState` with those writers, `ClientPlayer.TrainingHall` → `DrawInterface` (`AreaLoop.DrawInterface`,
`ClientPlayer.InterfaceDrawn`: case 2 shows PIC 0x1D undrawn, `ClientPlayer.PictureUndrawn`, until a menu animates it),
`ClientPlayer.CampingLine`, and `EclMemory.MenuAnimates` (the cell's state as the menus see it, set by a Load and a
save, cleared by the ECL menus, `ClientPlayer.Choose`, and the command line; the cell itself is left for the save);
every screen of the run equal to DOSBox-X's but the Client's help lines (the picture's two frames as DOSBox-X's, its
shots caught mid-XOR). Tests `ExploreScreenTests.A_camp_state_left_by_a_hall_load_draws_the_camp_screen`,
`SaveWriteTests.Hall_load_of_a_game_save_goes_on_in_the_running_script`. **D** (s41, scratch save E: vector 4 =
PICTURE 2; HMENU, loaded at the start-up): the HMENU animates the picture (no menu between the Load and vector 4), as
the $4AFF rule predicts; the picture is drawn over the party menu's screen there (START 0x3379 runs after vector 4,
0x40BE). **P + D (s42)** for the rest of that run and its neighbours:
- **The party menu under a Load's vector 4.** START 0x3379 runs inside the LOADFILES / LOADPIECES handler once both
  have run since the first pass armed it (DS:4432 set at 0x4058, DS:4434 by LOADFILES 0x19F8, DS:4433 by LOADPIECES
  0x1A66; 0x1B05–0x1B2C draws START 0x3379 and the party list and clears DS:4432), else after the vector (0x40B7–0x40C3,
  not at DS:49F3 = 3). Until then the party menu's page stays and vector 4 draws over it: scratch save I (vector 4 =
  PRINT, PICTURE 2, HMENU WAIT, loaded from the start-up menu) showed the four entries, the picture at cell (3, 3)
  with no view box, the text at row 17 column 1 and WAIT on row 24; after WAIT the whole explore screen. Client:
  `EclVm.EntryInterfaceDue` (the pair's draw, `PorGame.RunLoadedEntry`), `ClientPlayer.PartyMenuUnder` (set by the
  party menu's Load until START 0x3379 or the entry's end) with `ExploreScreen.PartyMenuPage`; 0 differing EGA pixels
  against DOSBox-X on that screen. Tests `CommandLineMenuTests.A_loads_interface_is_drawn_inside_its_vector_4_after_the_file_pair`,
  `ExploreScreenTests.A_party_menu_loads_vector_4_draws_over_the_menu_page`.
- **A `--save` start and a hall Load of another area, P + D (s43).** Neither has a party menu page in the Client (user
  decisions: the `--save` start keeps the explore screen under vector 4; a hall Load of another area enters it), so the
  explore screen is up from the start; what DOSBox-X shows once the pair has drawn START 0x3379 (a start-up Load of the
  same save) is the reference. Areas checked (scratch saves from save A with `goldbox-tools save-area`, the wall sets
  of each area): **Sokal Keep** (block 21, `$4A02` = 0, 8,14 N, walls 1/5/9: vector 4's pair, the arrival text, the
  one-word HMENU) and **civilised Phlan** (block 0, `$4AC5` = 0, 15,1 W, walls 1/3/5: the guide's tour, his close-up
  and the first text with its one-word HMENU): the Client's `--save` screens equal DOSBox-X's but the help line (Phlan:
  the close-up's animation frame too); the hall Load of the Sokal save (scratch save G: the step's PROGRAM 0, Remove
  × 6, Load) gave the same screen. No v1.3 vector 4 shows a vertical menu or a text input, so scratch saves did
  (vector 4 = LOADFILES 20 / LOADPIECES 2,4,1, PRINT, VMENU, INPUTSTR, PRINT; another with INPUTSTR, INPUTNUM; one
  with no pair): that found the Client's own VMENU layout (prompt cut to one row in white, rows from column 2 with a
  cursor glyph) and its input line (from column 0, INPUTNUM with a prompt of its own). Both now follow the program
  (`ecl-format.md` "VMENU's screen", "The ECL line inputs"): the VMENU screens equal DOSBox-X's but the help line,
  over the explore screen and over the party menu page (no pair: 0 differing pixels); the inputs differ only by the
  Client's character grid (decided, s25) and its "_" after the typed text. Tests
  `EntryScreenTests.A_vmenu_prints_its_prompt_in_the_box_and_lists_from_column_1_under_it`,
  `The_ecl_inputs_have_no_prompt_and_type_from_column_1`.
- **Every menu clears $4AFF.** 0x2CC8C has one exit (0x2CFA7–0x2CFAD), and the yes/no 0x2D9A0 and the list routine
  0x2D669 call it too (near calls at 0x2D9EA, 0x2D8AC; 36 far callers: camp, hall, party menu, venues, combat, View,
  ECL menus). DOSBox-X (scratch save C: the step's PICTURE 2; PROGRAM 0; PRINT; HMENU WAIT; loaded at the start-up;
  Up, Save E in the hall, Begin): the HMENU over the view stayed still. Client: `ClientPlayer.MenuEnded` on
  `ScriptRunner.Answered` clears `EclMemory.MenuAnimates` after every program menu (`ClientPlayer.IsProgramMenu`).
  Test `HallPlayTests.The_halls_menu_clears_the_menu_animate_flag_a_save_set`. While $4AFF ≠ 0 the frames advance
  on every pass without waiting for their delay (0x2CDD6); the Client copies it, each frame lasting one Client update
  as delay-0 frames do (user, s42; `ChoiceRequest.EveryPass`).
- **The picture over the hall page after a Save, P + D (s43).** The menu routine draws the frame table DS:6ABE at
  cell (3, 3) on every pass whenever $4AFF ≠ 0 (or its flag) and a picture is loaded (DS:6ABF > 0; 0x2CD5D–0x2CD9B),
  over whatever page is up; with flag 0 (the hall's menu, 0x19C77) by the plain blit (0x2E0CE → 0x464:0x1951), with 1
  (the camp's) by the other path (0x464:0xD71 + 0x1B52). The table holds the last picture the loader 0x2E170 read
  (PICTURE 0x1625, START 0x3379's camp and loot pictures, the encounter close-up) until a free (0x2E55D: the 0x42
  bytes zeroed, the name DS:6B0A emptied, the id DS:6B1C := 255), which the command line does at its end (0x17746,
  after every command) and 0xE402 (from 0x9899); PROGRAM 0 and the hall free nothing. 0x2E170 skips the load when the
  file name and id equal DS:6B0A / DS:6B1C, else frees the table, stores both and reads the block from the current
  disk's file (DS:5376); a block missing there leaves the table empty (0x2E24B). A Save writes $4AFF (the wall-set
  slots, 0x1FE70), so the hall's menu after it draws a picture still in the table. **D** (scratch save C from save A,
  the step's vector 0 = PICTURE 2; PROGRAM 0; PRINT; HMENU WAIT; loaded at the start-up; Up, Save E; states taken at
  each step): in the hall before the Save DS:6ABE = 2 frames, DS:6ABF = 1, $4AFF = 0, DS:5376 = 3 (set by the hall,
  0x19AE7; 2 when PICTURE 2 ran); after the Save $4AFF = 3 and PICTURE 2's two frames ran over the party list and the
  entries, the hall's prompt on row 24; every DOSBox-X row of the window equals one of the Client's two frames
  (the shots catch the blit mid-way). S kept the last frame under the slot question (no menu redraws the page); Escape:
  the loop drew its page again (its redraw flag [bp−0xF], set after every entry at 0x19E65), no picture. End (a member
  pick: 0x19C9E, then the party list START 0x1307 at 0x19CAB, and 0x19E69 skips the redraw flag) left rows 3 and
  11–13 of the picture: the list blanks each member's row from column 1 to 38 and the row after the last (0x1473).
  Begin, the WAIT, the command line (count 0, DS:6B0A empty, 6B1C 255: freed), Up again: PICTURE 2 read PIC3.DAX (disk
  3 still), which has no block 2, so the table stayed empty (count 0, DS:6B0A "PIC", 6B1C 2) and the second Save drew
  nothing (the Client too, on Xvfb with DOS and Best art: no block 2 on disk 3). With PICTURE 29 (in PIC2 and PIC3) in
  the same run both Saves drew it. Camp: Encamp, Save E, then the quit
  question (yes/no 0x2D9A0, the first menu after the Save) ran the camp picture's frames on every pass (every capture
  different); No, and the camp menu held them about 0.22 s again. Client: `ClientPlayer.MenuAsked` on `ScriptRunner.
  Asking` (a program menu asked while `EclMemory.MenuAnimates` is set and a picture is loaded: a horizontal menu gets
  `EveryPass`, the hall's menu `HallMenuRequest.EveryPass` and `HallState.PictureOver`), `HallScreens.Run` (the redraw
  flag; a member pick leaves the picture under the list, `HallView.DrawListOver`): on Xvfb the Client's screens equal
  DOSBox-X's, each window row one of the same two frames, and after End 0 differing EGA pixels but the rows of the
  frame left (which one depends on the pass the key ends). Tests
  `HallPlayTests.A_hall_menu_after_a_save_draws_the_loaded_picture_over_the_page`,
  `HallPlayTests.A_member_pick_draws_the_party_list_over_the_picture_left_on_the_hall_page`,
  `CampPlayTests.The_quit_question_after_a_camp_save_runs_the_camp_picture_every_pass`.
- **The picture under $4AFF on other pages, P + D (s44).** Which menu comes first after a Save or a Load: the hall's
  (hall Save, above), the camp's quit question (camp Save), the command line (a Load whose vector 4 asks nothing), or
  whatever the entry script (vector 4) or, after a hall Load of a game save, the running script asks first. Of the
  page menus, only VMENU reaches the list routine 0x2D669 straight from an ECL handler (0x8CD9); its 14 other callers
  (the shop's Buy 0x6E98, the heal list 0x50E7, the loot's item take 0x615F, camp Display 0x18BE7, the hall's Create
  0x1A0F4–0x1A821 and Add 0x1CA7A, View's Items 0x222C9, Trade and Drop 0x233B0 / 0x236AF, Take's kinds 0x25B14, the
  spell list 0x271EF) and View 0x21AED's eight (shop 0x730D, temple 0x5341, loot 0x65B4, the fight 0x9BC0, the
  command line 0x17118 / 0x17330, camp 0x197B3, hall 0x19D23) all run under a menu of their own that has already cleared
  the cell. PROGRAM's cases (0x3B56) are the hall, the end game and the camp (whose menus are covered above). So in v1.3
  the full-page menus with $4AFF set are VMENU's list, the shop's and temple's menus and the summary's and NPC share
  window's press-enter menu (0x6071, 0x6A40). **D** (DOSBox-X v1.3, 3000 cycles; scratch saves from save A with vectors
  0–3 = EXIT and vector 4 = PICTURE 2 and then: CLRMON, TREASURE (none), $6E6C := 1, COMBAT (shop); CLRMON, $6DE2 := 1,
  COMBAT (temple); CLRMON, TREASURE 100 copper, COMBAT (no monsters); CLRMON, LOADMON 2, 8, 2, COMBAT (a fight); a
  two-line VMENU; each loaded from the start-up menu):
  - Shop and temple: the entry's START 0x3379 case 1 (0x721E, 0x528B) drew the screen over the party menu page; the
    menu (flag 0, $4AFF = 3) ran PICTURE 2 in the empty view box, a different frame on every capture. After Pool the
    last frame stayed (four captures equal); View's exit (START 0x3379) left the box empty. The loops draw the screen
    again after B and T (0x743C, 0x5479), after A when 0x2663B's flag is set (gems or jewellery at the start; 0x744D,
    0x548A), at the heal list's end (0x51A0) and at View's exit; not after Pool, Share, G / O or Exit's question.
  - COMBAT with no monsters skips the fight 0x9881 and with it the fight start's free: the summary's full-screen window
    (03F1:02E8) and its press-enter menu ran PICTURE 2 over the window's text on every pass; Enter, and the loot menu's
    START 0x3379 case 6 loaded and showed the chest (held for its delays: the press-enter menu had cleared the cell).
  - The fight: the fight start 0xE402 frees the frame table first (0xE40D, the same free as the command line's
    0x17746), so the combat menu with $4AFF set drew nothing (captures equal, no picture); the battle line came over
    the party menu page with the picture, as before vector 4's pair (s42).
  - A fight with no player menu (s45, P + D): a won round still asks "continue battle" (the yes/no 0x2D9A0 from
    0xA1A5) whenever the party side is left, the enemy side is empty and DS:49F4 = 0 (0xA178–0xA18B). DS:49F4 is
    cleared at start-up (0xE99E) and no other instruction in GAME.OVR or START.EXE names it; VM $49F4 is a cell of the
    $4900 area (writer 0x8161: far pointer DS:49D2 + 2 × address + 0x6E00), not DS:49F4, so a script SAVE to it changes
    nothing (seen). That question is a menu and clears $4AFF, so the summary's press-enter menu after a won fight
    never has it set. Only a fight that ends with no party member left standing and at least one fled (DS:443B; e.g.
    the forced flee +0x10, combat-ai.md "Morale") skips the question and reaches the summary with $4AFF set, and the table is empty there (freed at
    0xE40D): nothing drawn. **D** (DOSBox-X v1.3, 3000 cycles; save A from `fight_save.py BASE OUT
    0E000209000101F4491C0B00780001007824 --mon 6 --monpatch 120:0x32=1,0x11B=1 --char N:0x10F=1` for all six, its
    code moved to vector 4: PICTURE 2, SAVE 1 → $49F4, CLRMON, LOADMON 120, 1, 120, COMBAT; loaded from the start-up
    menu): no player menu ran; the question came with no picture, No, the summary window with no picture (still).
    With the question patched out (0xA18B `jne` → `jmp` on a scratch GAME.OVR) the same fight went straight to the
    summary: the same screen, still for 3 s (0 differing pixels). Client (Xvfb, DOS art, the same folder): the
    question and the summary, the summary frame equal to DOSBox-X's (0 differing pixels; the question frame differs
    only by the fight's last positions and panel, a different random fight).
  - VMENU: the list over the party menu page and PICTURE 2 at cell (3, 3) on every pass.
  Client: `ClientPlayer.MenuAsked` sets `VenuePictureOver` for a shop's or temple's menu (drawn in the box instead of
  the blank, `ExploreScreen`; cleared by `TradeContext.VenueRedrawn` at the loops' START 0x3379 points) and
  `KeyRequest.EveryPass` for the press-enter menu (the frames over the window); the chest only from the loot menu
  (`ClientPlayer.LootDrawn`); the fight start marks the script picture freed (`FramesLoaded`); the shop, temple, loot
  and fight end `PartyMenuUnder` (the Client drew the party menu page under the shop's menu and the summary before,
  and the chest over it). On Xvfb with DOS art: the shop, temple and summary screens equal DOSBox-X's but the Client's
  help lines, every row of the picture one of the same two frames (which one is left after Pool depends on the pass
  the key ends); VMENU and the fight as DOSBox-X. Tests `MenuPictureTests` (s45:
  `A_won_fight_asks_to_continue_and_that_menu_clears_the_flag_before_the_summary`, the real fight menu with all six
  on Quick).
- **A turn draws the view.** After Load D's case 2 (blank box), V and Escape (box blank again), Left drew the 3D view
  in the box, CAMPING kept: a turn (0x2ED72 + 0x2EEF4) and the area map toggle (0x2EEF4) draw the view over a
  picture or blank. Client: `AreaLoop.ViewDrawn` → `ClientPlayer.ViewDrawn` (the CLEARBOX ring stays blank as after
  a redraw). Tests `ExploreScreenTests.A_turn_or_the_area_map_draws_over_a_picture`, the camp-state test above.
- **Load over an empty party.** Remove × 6 left the menu's four entries (Create, Add, Load, Exit) with no NAME / AC /
  HP heading, and L kept them under the which-game-to-load prompt. Client: `HallView` draws no list for an empty party and keeps
  the last menu's entries under the bottom-line questions. Test
  `HallPlayTests.The_hall_over_an_empty_party_keeps_its_entries_under_the_load_question`.

**Decided (user, s39):** the Client
does that only for a save of the running area (its $49F2, DS:84DC after a load, and its disk byte equal to the running
script's block and disk, `PorGame.LoadInHall` → `HallLoad.CarryOn`); a save of another area is not copied in and is
entered as a loaded game (its entry and vector 4, `HallLoad.EnterArea` → `HallLoadException`), a deliberate
difference. Tests `SaveWriteTests.Hall_load_of_a_game_save_goes_on_in_the_running_script` /
`Hall_load_of_a_save_from_another_area_enters_it`.

A load's vector 4 can open the hall too (`--save`, the start-up menu's Load, a Load in the game): the loads hand the
game to the Client before the entry runs (`PorGame.FromSave` / `LoadAtPartyMenu` / `Enter`, `started`), since the
hall reads the game's memory while vector 4 is still running (s26; before, the Client crashed there).

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
**maximum HP** — := 1 (E). The first highlighted row (DOSBox-X showed the last one) is the list routine's entry rule: row 0 is a heading, so the highlight steps up past it and wraps to the window's last row (P, s30; combat-menu.md "Spell lists' Next / Prev"; the Client derives it there).

**E**: 900 generated characters (`program-v1.3/training.json`): record bytes, refusal, RandSeed, Learn list, halts.
**D** (`dosbox-v1.3/training_hall`): thieves' hall (mask 116) refusing the dwarf fighter with the cap; the halfling
thief to level 2 (coins 500/300/20/900/20 → 0/0/1/2/5, 1d6); magic-users' hall (113): a magic-user to level 2 learning
spell 20. **D** (s38, `fight_frames.py`, 3000 cycles, seed 12345; save A with vector 0 = PROGRAM 0, $6DA8 = 113,
DARKSTAR XP 2600 and 1000 platinum, `fight_save.py … 380000 --vm 6DA8=113 --char 5:0xAC=0x28,0xAD=0x0A,0xAE=0,0xAF=0,0x90=0xE8,0x91=3`):
the step, Home × 2, Train, Yes, the Learn list (Home, Escape, keypad End, Home, L), the menu back with the new hit
points: every Client frame equal to a DOSBox-X frame but the first (the Client's help lines), 10 of 11. The list is
the spell list's own screen (combat-menu.md "The screen": title strip "NAME'S SPELLS TO CHOOSE FROM", the window
from row 3, row 24 "CHOOSE SPELL: LEARN", no Exit); Escape leaves the list on screen with the same row lit (nothing
visible changes). The page draws in about 0.32 s of machine time in DOSBox-X (19 frames: the row 24 clear, the frames,
the title, the rows); the Client draws it at once. A level-1 list of 9 spells fits the window, so Next / Prev do not
show. Hall masks seen in ECL3 block 11: 113 magic-users, 114 clerics, 116 thieves, 120 fighters.

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
