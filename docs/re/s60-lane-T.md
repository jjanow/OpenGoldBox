# s60 lane T: DOS presentation, the text, list and menu helpers

Scope: the ledger's "Presentation only" rows 1–6, 10 and 14 (list window 0x2D669 and helpers, START 0x3379, spell list
window 0x239D6, menu routine 0x2CC8C, party list redraw START 0x1307 and helpers, effect message START 0x2B8B, text page
0x32123, text helpers START 0x7090–0x7D3B), the W-by-citation drawing rows of START segments 00BA / 03F1 / 0709 and
GAME.OVR stubs 3C5 / 3EE, and PLAN Next (1) "the keypad flag's digits as cursor keys in non-fight menus". Every
routine disassembled whole (capstone on the EXEPACK-expanded image, `por_emu.load_image`).

Status: `seen` (matched on screen), `exec` (oracle + test), `read` (branch by branch, port matches), `fixed` (difference
fixed), `bug?` (question), `open`.

## 1. The list window 0x2D669 (row 1)

**exec + fixed.** New oracle `tools/scripts/por_listwin_oracle.py` runs 0x2D669 whole with the menu routine 0x2CC8C
under it (KeyPressed / ReadKey answered from a key script, GetTime 0, the text draws START 0x76B6 / 0x726F / 0x4047
trapped and logged) on 1500 generated lists, windows, colours, words and key scripts (fixture
`program-v1.3/list_window.json`). `ListWindowRoutine` (Client, new) is the routine as one loop over the existing
`ListWindowSteps`; `ListWindowProgramTests` replays every case: position, window top DS:6B29, menu cell DS:6B2B, the
entry and key returned, the menu line of every pass and the whole draw log outside the menu routine are equal in the
1468 cases that stay inside the list (32 skipped: 0x2C8CF asked for an entry past the list, so the program read the
interrupt table at 0000:0029).

The routine and its helpers, read whole:

| Routine | What it is |
|---|---|
| 0x2C8CF | entry k of a list (walks +0x2A; nil past the end) |
| 0x2C936 | the entries counted |
| 0x2CFE0 | the last entry with the label flag at or above index k (the head when it is one); 0x2D073 calls it and **never uses the result** (no sticky heading is drawn) |
| 0x2D073 | the rows from top t: the window's inside blanked (START 0x4047 x0, y0, x1, y1), then from y0 while an entry is left and the row ≤ y1: the text at (x0, row) through START 0x76B6 in c24 (label) or c22, then, when shorter than the width, START 0x726F(x0 + length, row, colour 0, width − length, ' ', 1) |
| 0x2D1A6 / 0x2D1F2 | the first non-space index (from 1, stopping at the length) / the last (down to 1) |
| 0x2D238 | the highlight: Copy(text, first, last − first + 1) at (x0 + first − 1, y0 + position − top) in c20 |
| 0x2D2D7 | the same text unlit: c24 for a label, else c22 |
| 0x2D3D2 / 0x2D502 / 0x2D594 | label skip, page, row step (already `exec`: `por_list_oracle.py`) |
| 0x2D669 | below |

0x2D669(prompt, word, c24, c22, c20, x0, y0, x1, y1, head, exit, var redraw, var position, var entry): a nil head
returns at once (position 0, entry nil, key 0; nothing drawn, DS:6B2B / 6B29 untouched). Else DS:6B2B := 1; the top
down to the position when above it, to 0 when past the count (each sets redraw); position + 1, one row up and past
labels upwards; the rows drawn when redraw is set, then redraw := 0. Each pass: the highlight; the menu line (word,
" Next" when count − rows > top, " Prev" when top > the leading labels, " Exit" with the flag) through 0x2CC8C(prompt,
line, c24, c22, c20, keypad 1, animate 0); the row unlit; then extended Home / End a row up / down, PgUp / PgDn a page
while Prev / Next shows; plain P / N a page **whatever shows** (they reach the routine only when the menu line holds
them as capitals, so only as the shown words' hotkeys in every v1.3 caller); 0 (Escape) or E: no entry, key 0; any
other key: the entry and the key. Facts the oracle showed that the specs did not say: the highlight is drawn wherever
position − top puts it, even outside the window (a caller's position below the window: the entry only moves the top
up); Escape reaches the routine as 0 (the menu routine turns 27 into 0).

The 15 callers (arguments read at each call site): heal list 0x50E7 ("" / "Heal Exit", (2, 4)–(38, 15), no Exit
flag), loot take 0x615F ("Items: " / "Take", (1, 4)–(38, 22), Exit), Buy 0x6E98 ("Items: " / "Buy", same), VMENU 0x8CD9
(its caller's), Display 0x18BE7 ("" / "", labels colour **11**, (1, 4)–(38, 22), Exit), creation 0x1A0F4 / 0x1A38A /
0x1A4DF / 0x1A821 ("" / "", (1, 2)–(38, 22), Exit flag from its caller), Add 0x1CA7A ("Add a character: " / "Add Exit",
(1, 2)–(38, 22)), Items page 0x222C9 (its entries, (1, 5)–(38, 22), Exit), coin lists 0x233B0 / 0x236AF ("Select type
of coin" / "", (12, 7)–(25, 13), Exit) and 0x25B14 (same words, (2, 2)–(15, 8)), spell lists 0x271EF. Every other
caller passes 13 / 10 / 15. The Client pages match these (s33–s46 DOSBox-X work; Display's colour 11 is
`SpellPageLayout.LabelColour`).

**Fixed (Client):** the keypad flag turns the digits 1–9 (either row: DOS reads both as characters) into the cursor
pad's codes (DS:288C), so on every list window the **digits' row 7 / 1 move the highlight and 9 / 3 page** as Home /
End / PgUp / PgDn; the Client read only the keypad's digits (NumPad7 … as the step bindings). Now
`ListWindowRoutine.DigitRowKey` in `ListPage` (paged and unpaged), VMENU (`ExploreScreen.ListAnswer`), the Items page
and coin list (`CombatScreen.DecidePage`, `PartyMenuScreen.PageKeys`). The creation lists (`PickRequest`, 0x2D669
windows) took only Up / Down: now Home / End / keypad 7 / 1 and the digits' row 7 / 1 move the highlight as well.
These keys have no Client meaning on a list, so this is the s50 / s53 rule, not a new decision. Tests
`ListWindowProgramTests` (3).

## 4. The menu routine 0x2CC8C (row 4)

**read + exec (key layer).** Its one `[?]` (por-art-formats.md "Playback": "VM $4CFE") is settled: the word it tests
and clears is [DS:49D2] + 0x3FE, VM **$4AFF** (DS:49D2 + 2 × (cell − $4900)), as training.md already had; the line is
corrected. Read whole: the prompt and the line copied; DS:6B2C := 0; Return gives 13 only when either of its two word
colours ([bp+0x10], [bp+0x0E]) is non-zero (0x19C77, the party menu, passes 0, 0); the words parsed by 0x2C99A (**each
digit or capital letter starts a word**, its hotkey); the cell DS:6B2B back to 1 when past the count; the prompt at
(0, 24) in [bp+0x12] when not empty, the words from its length (0x2CAA9). Then the loop: the picture frame (animate
flag or $4AFF; por-art-formats.md); a timeout from DS:6B2E / 6B30 returning DS:6B32 — **dead in v1.3**: no writer of
those words in START.EXE or GAME.OVR (they sit past the data image, so 0); KeyPressed / ReadKey (START 0x89E9: Ctrl-S
sound, Ctrl-D DS:49F7 and Ctrl-C handled there, and after a character key it **drains the buffer and keeps the last
key**); an extended key: returned with the flag only with the keypad flag, else ignored; Escape → 0; Return → 13 (see
above); ',' / '.' the cell back / on (wrapping); other characters upper-cased; space ends; a digit or letter the line
holds lights its word (the last match) and ends; then, with the keypad flag, the digits 1–9 and '\' become DS:288C's
codes (1 End, 2 Down, 3 PgDn, 4 Left, 5 ' ', 6 Right, 7 Home, 8 Up, 9 PgUp, '\' 0x37) with the extended flag. At the
end $4AFF := 0 and DS:6B2C := the extended flag (DS:6B2C has no reader). The key layer is modelled as
`ListWindowRoutine.MenuKey` and checked through the list oracle (every key script passes through it). Callers with the
keypad flag (36 call sites read): START 0x37E8 (its animate flag from DS:49F3 2 / 6), 0x5280, 0x5E94, 0x62B6, 0x644B, 0x6866, 0x7212, 0x8B57
(animate from its caller), 0x9E6D, 0xA1E6, 0x155F5, 0x17054 (both), 0x18C22, 0x18FB1, 0x192C8, 0x1948A (first), 0x196A0, 0x19AE0,
0x1BA1E, 0x244ED, 0x2663B (three), and 0x2D669; without: 0xA64C, 0xA8FD, 0x1529B, 0x17438 (both), 0x1948A (Pics),
0x1CD66, 0x1D1AC, 0x1F5E8, 0x1FD3E, 0x202BB, 0x21AED.

## PLAN Next (1): the keypad flag's digits in non-fight menus

**fixed.** The menu routine with the keypad flag turns the digits 1–9 of either row into the cursor pad's codes
(DS:288C, above). Per menu, what the program does with them and what the Client did:

| Menu | Program | Client before | Now |
|---|---|---|---|
| list windows 0x2D669 (every list page, VMENU, Items page, coin lists, creation, Add) | 7 / 1 a row up / down, 9 / 3 a page | keypad digits only (step bindings); creation lists not even Home / End | both rows (`ListWindowRoutine.DigitRowKey`); creation lists Home / End / 7 / 1 |
| party menu 0x19C77 (its set CS:00F6 = {G, O} → 0x23819) | 7 / 1 the member before / after | Home / End only | both rows' 7 / 1 (`DosKeys.KeypadFlagCode`; `PartyMenuScreen` at the start-up menu, `HallView` in the hall) |
| member pick START 0x37E8 (Trade, camp Cast, WHO; 0x38C9 / 0x38F7 take only End / Home) | 7 / 1 the member | Home / End only | both rows (`MemberPick` `ExtendedKeys`) |
| Speed 0x193BD (0x50 / 0x48), rest time 0x24535 (Up / Down / Left / Right) | 8 / 2 / 4 / 6 as the arrows | arrows only | both rows (`ExploreScreen`, requests with Up / Down / Left / RightAnswer) |
| camp, Magic, Alter, Order, 0x8B57, command line (s50 rule); Take:, shop, temple, Appraise, loot (s51 / s53) | 0x23819 / their own key sets | already both rows (`DosKeys.MemberPick`, `KeypadMenuScan`) | unchanged |
| Modify 0x1BA1E | 2 / 8 / 4 / 6 as the arrows | already (`PartyMenuScreen`) | unchanged |

None of these keys had a Client meaning on those screens, so this is the s50 / s53 rule (keys the Client gives no
meaning act as the program); no new question. The fight's menus were not touched (lane C).

**seen** (DOSBox-X 3000 cycles against the Client `--art dos`, every frame pixel-equal at 320 × 200): `START STING`,
Create: the race list, then the digits' row 1, 1 (two rows down) and 7, 7, 7 (up, wrapping to the last row): 0 pixels
off on each of the three frames (the Client before the fix ignored the digits). Test save C (`por_test_saves.py`):
in the area the command line's 1, 1, 7 (s50 rule) equal but the Client's help lines; in the hall after the DAMAGE
pages, 1 then 7, 7 at the party menu (the member lit moves as Home / End): 0 pixels off on both frames once
`HallView` took the digits (it ignored them before). Seen on the way: the Client's keyboard Return at the party menu
runs the lit entry; the program's party menu passes word colours 0, 0, so its Return gives nothing. That is the
Client's own highlight for keyboard and pad play (`HallView` / `PartyMenuScreen`), left as is; noted for the user
below.

## 2. START 0x3379, the screen redraw by case (row 2)

**read.** By DS:49F3: 0 (party menu, hall) the window (1, 1)–(38, 22) through START 0x41F8 (style 0, colour 15, no
title); 1 (shop, temple) 0x32CA(1), the venue picture 0x2E6A7(DS:49FE, DS:49FD) and its frame at (3, 3) (0x2E765); 2
(camp) 0x32CA(1), PIC 0x1D loaded into the frame table (0x2E170 with 0, not drawn), the party list, the position
line; 3 0x32CA(0) (no 3D box), the view (0x2DCF4), party list, position line; 4 the same with 0x32CA(1); 6 (loot)
0x32CA(1), PIC 1 loaded, the party list, **no** position line; 5 (fight) and 7 (end game) nothing. The Client
(`ClientPlayer.InterfaceDrawn` / `VenueDrawn` / `WindowsDrawn`, `ExploreScreen`'s position line off in cases 1 and
6, `PartyMenuFlow` case 0) follows every case. The remaining `[?]` (ecl-format.md "PROTECT", view3d.md "Not drawn") is
about which callers reach 0x3379 through indirect calls, not the routine: left open.

## 3. The spell list window 0x239D6 (row 3)

**read.** Whole (title words by kind 0–6, nothing drawn for an empty list, the title strip and name for a position
below 0, then 0x270A2); combat-menu.md "The spell list page's title" written, its two `[?]` lines on 0x239D6
settled. The Client's `SpellPageLayout` matches (frames compared s33 / s38).

## 5. The party list START 0x1307 and helpers (row 5)

**read.** 0x1307(lit): column x0 = 1 when DS:49F3 = 0, else 17; "Name" at (x0, 2) and "AC  HP" at (33, 2) in 15; from
row 4 each record of DS:5D96 (no row limit): its row (x0..38) blanked (START 0x4047), the name in 15 when it is the
lit one, else START 0x23B6; the AC through 0x148F at column 32 + its width (|0x111 − 60| with '-' when 0x111 > 60,
right edge 34, colour 10); the HP through 0x1513 (colour 14 below the maximum 0x32, else 10; 13 with its flag) at
36 + width (right edge 38); after the last, one more row blanked. 0x1583 (the fight's panel): (23, 1)–(38, 21) blanked,
the name line (START 0x228C), "Hitpoints" at (23, 3) and the HP at column 33, "AC" at (23, 5) and the AC at column
26, DS:5E25 := 5, the weapon's name printed into (23, 7)–(38, 9) (START 0x77F8), then two rows under DS:5E25 the
status word (out of play) or "(Helpless)" (START 0x1713) in 15. 0x257E: four 0x2432 calls building cell n's frames
(first, first mirrored, second mirrored, second; no delay of its own). 0x1C4D: Str of a long integer into a string
(no drawing); 0x1BD7 is Str of a byte. The Client's `FramePiece.PartyList`, `CombatScreen.DrawPanel` and
`FlightAnimation.Burst` match; the stale `[?]` in por-art-formats.md (the status line "not built": it is) and two
code comments (ExploreScreen's LOADMON note, CombatScreen's AC note) updated. Difference not changed: the Client
stops the list at row 11 (the program has no limit; only monsters appended by LOADMON could pass 8 rows, and the
program draws no list between LOADMON and the fight, DOSBox-X s29).

## 6. The effect message START 0x2B8B (row 6)

**read.** Its text cut to 40 characters; outside a fight (DS:49F3 ≠ 5) START 0x228C(creature, text, 10, pause 1);
in a fight: cue 0 (resident), 0x257E(0x16 effect / 0x17 damage), the map brought to the creature when not wholly in
view (0x310D0(8, 3, x, y)), cue DS:261C / 261A, 0x228C(…, pause 0), then DS:49E2 + 1 rounds (effect) or one round
(damage) of the four frames at its cell, 70 ms each with the background put back after each, cue 10, and for damage
the game-speed pause. `CombatPlayer.ShowHit` / `CampScreens.EffectMessage` match.

## 10. 0x32123 (row 10)

**read; a debug display, not ported.** It draws only while DS:49F7 is set, which only Ctrl-D toggles, and only
under the debugging start word (`START STING`, START 0x89E3): row 0 blanked, the caller's text with three numbers
(Str, separated by ", ") at (0, 0) in 14, a row-24 key prompt, row 0 blanked again. Its one caller is the level-drain
handler 0x10D7D (the maximum HP, HP and a level byte). The Client has no debug mode (s52 lane F: Ctrl-D not ported),
so this is "not ported by design".

## 14. The text helpers in START segment 0709 (row 14)

**read; machine-level.** 0x7090 resets the EGA graphics controller and sequencer map mask; 0x7101(page) shows a video
page (INT 10h AH 5) and sets DS:4A16; 0x7130(page) sets the draw page DS:4A18; 0x7147(x0, y0, x1, y1, page, colour)
fills a rectangle of cells in write mode 2 (the core of START 0x4047); 0x720C loads 8X8D1.DAX block 201 (the font)
into DS:5E20 (the importer's job); 0x7D3B clears the whole screen by video mode (CGA, EGA both pages, Tandy four banks;
called by View's page 0x21084 and the Items page 0x21F32: the Client's page clear). Nothing to port beyond what the
Client's single frame does.

## W-by-citation drawing rows (00BA, 03F1, 0709, stubs 3C5, 3EE)

Every one of them (START 0x2233, 0x36FE, 0x3F10, 0x4047, 0x41F8, 0x726F, 0x767B, 0x76B6, 0x7737, 0x77A4, 0x7C85,
0x7CCF, 0x7DF8, 0x7E4E, 0x7E63) was read whole in s52 (s52-lane-F.md). New evidence here: the list oracle runs
START 0x76B6 / 0x726F / 0x4047's arguments as 0x2D669 and 0x2CC8C pass them (exec, at the call level), and the
DOSBox-X frames above (race list, the area command line, the hall's party menu: windows 0x41F8 / 0x3F10, strings
0x76B6, glyphs 0x726F, the row-24 menu, the party list) were pixel-equal. Stub 3C5's W-by-citation entries are covered
by the list oracle; stub 3EE has none (0x32123 above).

## Questions for the user

None needed a decision. Noted only: at the party menu (start-up and hall) the Client's keyboard Return runs its own
lit entry, where the program ignores Return there (0x19C77 passes word colours 0, 0); it is part of the Client's
highlight for keyboard and pad play and stays as it is unless you want it otherwise.

## Ledger evidence

- 0x2D669 → W, exec (`por_listwin_oracle.py` → `ListWindowProgramTests`: whole routine with the menu routine, 1468 cases) + seen (DOSBox-X s60 race list)
- 0x2C936 → W, exec (inside 0x2D669 under the oracle); role: the list's length
- 0x2C8CF → W, exec (same); role: entry k of a list
- 0x2CFE0 → W, exec (same); role: last label at or above k, result unused by 0x2D073
- 0x2D073 → W, exec (draw log compared); role: the rows from the top
- 0x2D1A6 / 0x2D1F2 → W, exec; role: first / last non-space index
- 0x2D238 → W, exec; role: the highlighted row (trimmed text in c20), not "page up"
- 0x2D2D7 → W, exec; role: the row unlit, not "page down"
- 0x2CC8C → W, exec (key layer under the list oracle) + read; its `[?]` settled ($4AFF); the timeout DS:6B2E–6B32 dead in v1.3
- 0x2C99A / 0x2CAA9 → roles: parse the menu words (digits and capitals start words) / draw the menu line (not "camp.md Rest")
- START 0x3379 → W, read (every case; the open `[?]` is about indirect callers)
- 0x239D6 → W, read whole (combat-menu.md updated)
- START 0x1307 → W, read whole + seen (DOSBox-X s60 hall party list)
- START 0x148F / 0x1513 → W, read whole (AC / HP number draw)
- START 0x1583 → W, read whole (por-art-formats.md `[?]` cleared: the status line is built)
- START 0x257E → W, read (frame builder, no delay of its own; the "with delays" in its role is wrong)
- START 0x1C4D → W, read; role: Str of a long integer (no drawing). START 0x1BD7 role: Str of a byte
- START 0x2B8B → W, read whole
- 0x32123 → N, debug display (DS:49F7, `START STING` + Ctrl-D only)
- START 0x7090, 0x7101, 0x7130, 0x7147, 0x7D3B → N (machine level: EGA registers, video pages, cell fill, full clear); 0x720C → N (font load, the importer)
- START 0x76B6, 0x726F, 0x4047, 0x41F8, 0x3F10, 0x7C85 → add "seen (DOSBox-X s60)" to their W rows
