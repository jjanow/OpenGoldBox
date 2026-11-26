# s52 lane F: START segments and the presentation units, W-by-citation rows

Units: START 0000, 00BA, 03F1, 0464, 0709, 0899, 08AE; GAME.OVR stubs 19, 1C, 3C5, 3CA, 3CD, 3D0, 3D5, 3E9, 3EE, 428.
W-by-citation rows by the ledger's own rule (`render.py` `wexec`): 62 (9 play, 53 pres). The empty routines and rows
already W by execution are not listed.

Statuses: `exec` oracle and test added; `read` read branch by branch against the C# it names, port matches; `fixed`
difference found and fixed; `bug?` original bug, question for the user; `open` not finished.

Two kinds of `read` here. Routines the Core / Client port as code (keys, lists, messages, text) were compared branch by
branch. Low-level drawing (blitters, frame and tile draws) and the art loaders are **not** ported as code: the importer
and the Client's renderer build the same pictures and have been pixel-checked against DOSBox-X (cited per row). Their
`read` means the branches were read and each one's visible effect has a counterpart. It is not a line-by-line port.

## New oracles (this lane)

- `tools/scripts/por_list_oracle.py` → `program-v1.3/list_steps.json`, Client `ListStepProgramTests`: the list window's
  moves 0x2D594 (step), 0x2D3D2 (label skip), 0x2D502 (page) run as nested procedures on a built parent frame, 3000
  cases; `ListWindowSteps.SkipLabels` (moved out of `ListPage`) matches. 15 cases ask 0x2C8CF for an entry outside the
  list (a page down with fewer entries than rows; the program then reads the label flag at 0000:0029) and are skipped.
- `tools/scripts/por_input_oracle.py` → `program-v1.3/line_input.json`, Core `LineInputProgramTests`: START 0x7A73
  (line input) on 600 key sequences and START 0x7BF6 (INPUTNUM) on 400 sequences of lines; Core `DosLineInput` matches
  (Turbo Pascal Val included: sign, `$` hex, leading blanks).

## Rows

| Routine | Kind | Status | Note |
|---|---|---|---|
| START 0x0006 main program | play | read | read whole s51 (start-menu.md §1); not re-read |
| START 0x3226 Guard | play | read | 0x31D7, +7 := 1, the word at CS:0x321D through 0x2233, returns 0x31D7's AL; `Fight.Guard` |
| 0x2CFCA row-24 clear | play | read | 0x767B(0, 24, 0, 40): no branch |
| 0x2D3D2 list: past labels | play | exec | `por_list_oracle.py`; at most `rows` moves, each 0x2D594's wrapping inlined |
| 0x2D594 list: step | play | exec | `por_list_oracle.py` |
| 0x2D502 list: page | pres | exec | `por_list_oracle.py`; the rows drawn (0x2D073) from the new top checked too |
| 0x2E03F overland layout | play | read | windows (1,1)–(15,15), (17,1)–(38,15), (1,17)–(38,22) through START 0x41F8 |
| 0x2F9BE GEO loader | play | read | block → DS:6A5C (4 × 0x100), the game record's +0x18A := block ($49C5); the size ≠ 0 / 0x402 branch is the disk-error halt (N) |
| 0x31A8E tile set loader | play | read | first tile > 0x7F → halt; copies count × size from tile `first` into DS:5E52 when loaded; `PorOverlandArt` / `CombatArt` |
| 0x31C34 icon loader | play | read | CHEAD / CBODY (a last letter T → block + 0x40, no disk digit), COMSPR / ICON (no digit; ICON recoloured DS:0CD2 → 0CF2), else CPIC<disk> recoloured DS:0CD2 → 0CE2; both poses (+0x80) per slot; `CombatArt` |
| START 0x1C13 Str(word) | pres | read | |
| START 0x2233 row-24 message | pres | read | 40 characters, row 24 blanked, colour 10 at column 0, 0x7E4E, blanked |
| START 0x228C (00BA:16EC) creature message | pres | read | DS:49F3 = 5: (23, row)…(38, 21); else row 17, or 18 with DS:6DDA (frame only at 17); pause flag → 0x7E4E + 0x2383; `CombatPlayer.PrintCreatureMessage`, `OutsideEffectMessages` |
| START 0x23B6 name in colour | pres | read | +0x10D = 0 → 12, +0x10E = 1 → 14, else 11; flag → the word at CS:0x1813 after the name; `NameColour` |
| START 0x2432 icon at a cell | pres | read | EGA width / 4; arg 0 → the cursor box copy; `FlightAnimation` |
| START 0x257E icon flash | pres | read | four 0x2432 calls (stored, mirrored, +128 mirrored, +128); `FlightAnimation` |
| START 0x25C9 moving icon | pres | read | path, view-centre branch (abs ≤ 6, midpoint by idiv) and follow mode against `CombatChooser.Flight`, whose comments carry the routine's ranges; frame-equal to DOSBox-X s27 / s37 |
| START 0x2EEA healed message | pres | read | HP +0x11B = +0x32 → fully, else partially; 0x228C colour 10 with pause; outside a fight 0x1307 |
| START 0x32CA 3D layout frame | pres | read | (1,1)–(38,22), (1,17)–(38,22), (1,1)–(15,15), with the flag (3,3)–(13,13) |
| START 0x34AB position line | pres | read | overland x, y from the game record +0x186 / +0x188 (+13 / +26 in modes 3 / 4), the hours and minutes zero-padded, x,y only with +0x1F6 = 0, CAMPING (DS:49F3 = 2) or SEARCH; `ExploreScreen.StatusLine` |
| START 0x36FE overland interface | pres | read | DS:49F3 := 3, 0x2DE38, the redraws, 0x34AB, 0x1307 |
| START 0x3728 screen clear | pres | read | combat view off, windows (1,1)–(21,21) and (23,1)–(38,21), row 24 blank, then the combat view redrawn (stub 3E0 entries +0x2A, +0x52) |
| START 0x37E8 member pick | pres | read | key set {13, 27, E, S}; End next, Home the one before (wrapping), Escape → nil and on, E → nil and out; with no pick End reads 0000:0104 (already `[?]` in camp.md / shop-format.md); `MemberPick` |
| START 0x3F10 window frame | pres | read | the ring one cell outside the box: corners piece 3·style + 0x11, sides + 1, top / bottom + 2 (0x32392) |
| START 0x4047 window blank | pres | read | by video mode (CGA, EGA via 0x7147, Tandy) |
| START 0x41F8 window | pres | read | blank, frame, the title centred on row y0 ((x0 + x1 − len) idiv 2) |
| START 0x53B1 picture draw | pres | read | blitter: clipping branches, video modes; Client renderer pixel-equal (view3d.md, por-art-formats.md checks) |
| START 0x5C07 mirrored draw | pres | read | blitter, per video mode; `CombatScreen.Mirrored` pixel-equal |
| START 0x6192 buffer to screen | pres | read | copies only the rows flagged in DS:4A28 (167 rows), by video mode; the Client redraws whole frames (no visible difference) |
| START 0x704D palette register | pres | read | INT 10h 1000h: colour < 8 as is, else + 8 (the EGA bright set); `Intro.FlashRegister` |
| START 0x726F glyph | pres | read | nothing past column 39 / row 24 or with no font; upper-case flag: UpCase mod 64; `TextWindow.DrawGlyph` |
| START 0x767B blank cells | pres | read | nothing past column 39 / row 24; `count` blanks in the colour |
| START 0x76B6 string draw | pres | read | nothing when column or row > 39 (the glyph routine then cuts at 24); one glyph per character |
| START 0x7737 text window word | pres | read | run inside 0x77F8 by `por_print_oracle.py` (PrintProgramTests) |
| START 0x77A4 text window typing | pres | read | as 0x7737 |
| START 0x77F8 text window | pres | exec | `por_print_oracle.py` calls it directly (PrintProgramTests); not in the ledger's oracle list yet, see below |
| START 0x7A73 line input | pres | exec + fixed | `por_input_oracle.py`. Fixed: Escape ends a DOS line with the text typed (the Client made it a Backspace). The Client's keyboard typed the grid's characters only (A–Z, 0–9, space, '-', '.'); fixed s55: every printable key with Shift as the keyboard gives it, 0x20–0x7A taken (`TextRequest.Takes`) |
| START 0x7BF6 INPUTNUM | pres | exec + fixed | `por_input_oracle.py`. Fixed: the line is asked again while Val rejects it or the value is outside 0..65536 (an empty line too), the answer its low word; the Client took the first line, 999999 as is and empty as 0 |
| START 0x7C85 row-24 key prompt | pres | read | row 24 blanked, text in the colour, one 0x89E9 |
| START 0x7CCF GetTime | pres | read | h·360000 + m·6000 + s·100 + hundredths |
| START 0x7DF8 timed bottom line | pres | read | blank, text, 0x7E4E, blank |
| START 0x7E4E game-speed delay | pres | read | Delay(DS:49E2 · 225) |
| START 0x7E63 box blank | pres | read | by video mode (EGA: 0x7147 on both pages) |
| START 0x8990 quit | pres | read | sound mode restored, silence, palette, Halt(0) |
| START 0x89E9 key read | pres | read | Ctrl-S sound toggle; with START STING Ctrl-D toggles DS:49F7 and Ctrl-C quits (debug, not ported); a non-zero key drains the buffer and returns the last; `ActionInput.ReadingKeys` |
| START 0x8A94 key flush | pres | read | 0x89E9 while KeyPressed |
| START 0x8AB2 key poll | pres | read | one 0x89E9 when KeyPressed |
| START 0x8BBC sound command | pres | read | matches sound.md 0x00DC line for line (mode 2 off, auto-stop DS:5374, 0 / 1 / 255 / tune − 1 by device) |
| START 0x9F57, 0x9FCC, 0xA05A, 0xA0EB sound tick helpers | pres | read | run by `por_sound_oracle.py` through the timer interrupt; their port writes are what SoundDriverProgramTests compares |
| 0x00D4 intro wait | pres | read | GetTime + time or KeyPressed, then flush |
| 0x02DA credits page | pres | read | windows and fixed-cell lines; `Intro` credits lines |
| 0x06FB introduction | pres | read | DS:49D1 = 0 → logo only; lightning: counter + Random(4) > 50000 → Random(3) + 2 flashes of 9, 11, 15, 11, 9; `Intro` |
| 0x2D9A0 yes / no | pres | read | No lit each pass (DS:6B2B := 2), asks until N or Y; the Client's Cancel = No is its own (combat-menu.md) |
| 0x2DCF4 view redraw | pres | read | checked s51 |
| 0x2E170 picture loader | pres | read | same name and block (DS:6B0A / 6B1C) → nothing, the frame kept (por-art-formats.md); XOR frames; recolour by mode |
| 0x2EA4C view colours | pres | read | four bytes to DS:6A60–6A63 |
| 0x2EEF4 3D view draw | pres | read | the view pixel-equal to DOSBox-X from the formats (view3d.md); the routine's internals are U rows |
| 0x2F6FF wall-set loader | pres | read | slot 1..3 only; WALLDEF<disk> block, size check (halt), pieces through 0x32270; importer + pixel-equal views |
| 0x31B5D tile draw | pres | read | tile > 0x7F from DS:5E56 (& 0x7F), else DS:5E52; `PorOverlandArt` |

Counts: exec 6 (0x2D3D2, 0x2D594, 0x2D502, 0x77F8, 0x7A73, 0x7BF6), of which fixed 2 (0x7A73, 0x7BF6); read 56; bug? 0
new (the 0x37E8 nil End is already `[?]`); open 1 part (0x7A73's keyboard character set).

## Proposed ledger changes (main thread)

`cite.py` `ORACLE_O` / `ORACLE_S` and `build.py` `ORACLES`:

```python
ORACLE_O.update({0x2D594: 'list', 0x2D3D2: 'list', 0x2D502: 'list'})
ORACLE_S.update({0x77F8: 'print', 0x7A73: 'input', 0x7BF6: 'input'})
ORACLES.update({
    'list': ([O_(x) for x in (0x2D594, 0x2D3D2, 0x2D502)], [O_(0x2D073)]),
    'print': ([S_(0x77F8)], [S_(x) for x in (0x726F, 0x7E63, 0x7C85, 0xA69C)]),
    'input': ([S_(0x7A73), S_(0x7BF6)], [S_(0x89E9)]),
})
```

`overrides.py` (W with oracle-via-caller evidence counts as execution):

```python
'S:7737': dict(status='W', why='s52: run whole inside 0x77F8 by por_print_oracle.py; every glyph compared (PrintProgramTests)'),
'S:77a4': dict(status='W', why='s52: as 0x7737'),
'S:9f57': dict(status='W', why='s52: run by por_sound_oracle.py through the timer interrupt; port writes compared (SoundDriverProgramTests)'),
'S:9fcc': dict(status='W', why='s52: as 0x9F57'),
'S:a05a': dict(status='W', why='s52: as 0x9F57'),
'S:a0eb': dict(status='W', why='s52: as 0x9F57'),
```

Hand-checked `read` rows (status stays W, `why` for the record): every `read` row above as
`dict(status='W', why='read whole s52 (lane F notes)')`.

## Open

- Both closed in s55: the Client's keyboard types every printable key (Shift included, `DosKeys.Printable`) and DOS's
  line inputs take 0x20–0x7A (`TextRequest.Takes`; INPUTNUM's Val refuses the rest and the line is asked again, now
  from an empty line); the party menu's name entry (creation, 0x1B7BE → START 0x7A73 with 15) ends on Escape as well.
