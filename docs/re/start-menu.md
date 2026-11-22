# Start of the game: party menu, character creation, new game (Pool of Radiance DOS v1.3)

Status: **program read (s19, s20)**, the creation routine **checked under emulation** on 798 cases (every race,
gender, class and alignment pick, re-rolls), Modify on 1043 cases and the icon editor on 600 cases (s20); the menu
flow and the new game's start **checked in DOSBox-X** (screens), and Save before Begin, Load of that save and Exit with
a party (s20, the save byte for byte). Code: Core `CharacterCreation`, `CharacterModify`, `PartyMenu`, `IconEditor`;
Data `PorRoster`, `PorGame.NewGame`, `UnstartedSave`, `LoadAtPartyMenu`; Client `PartyMenuFlow`, `PartyMenuScreen`.
Oracles: `tools/scripts/por_chargen_oracle.py` → `tests/fixtures-private/por/program-v1.3/chargen.json`
(`CharacterCreationProgramTests`), `por_modify_oracle.py` → `modify.json` (`CharacterModifyProgramTests`),
`por_icon_oracle.py` → `icon_editor.json` (`IconEditorProgramTests`). Addresses are GAME.OVR file offsets unless marked
START (unpacked image) or DS.
No game text is quoted here: strings are named by their catalogue keys (`PartyMsg.*`, `PorTextCatalog`).

## 1. Start-up sequence

START's main program (image 0x00F6): start-up 0xE600; unless the first argument is the debug word, the introduction
(0x06FB, §1a) and the code wheel (0x0AA9); then the party menu 0x19AE0; the view colours (0x2EA4C); the game loop 0x403E.

- **Start-up 0xE600** allocates and zeroes the VM zones, sets the party square DS:6AAD/6AAE/6AAF := 15, 1, 6 (west),
  the disk byte DS:5376 := 1, the game speed DS:49E2 := 1, the location type DS:49F9/49FA := 1, the party lists
  DS:5D92/5D96 := nil, $6E3E := 0, DS:6B1C := 0xFF and others (0xE965–0xEA30), then loads the start-up art and
  ITEMS with the loading message (GAME.OVR 0xE5D4) on row 24 (the Amiga does this after its intro: §1a, "After the
  intro"; the Client draws the message on neither).
- **The party menu 0x19AE0** sets DS:5376 := 3 (the character disk) and DS:49F3 := 0, then loops (section 2). Begin
  sets DS:49F3 := 4 and returns; Load returns when a game was loaded (DS:49F8 := 1).
- **The game loop 0x403E** takes the script id from VM $49F2 (the zone the start-up zeroed: 0 on a new game). With
  DS:49F8 = 0 it loads `ECL<DS:5376>` block id (0x7814), so a new game runs **ECL3 block 0**, then the script init
  0x7689 and the entry vector. DOSBox-X (s19): after Create, Add, Begin the screen shows "15,1 W 00:00" and the
  area's first text and close-up portrait. `PorGame.NewGame` + `RunNewGameEntry` do this: fresh VM memory, the party
  and $6E3E, the square, disk 3, script 0 (init, then vector 4). This settles module-format §18's start-up `[?]` for
  PoR: the manifest's `start` is not used by the PoR VM profile.
- Load from the party menu runs the program's Load (0x1F5E8), which reads the interface state DS:49F3 back from the
  save (0x1F87B); the menu goes on to the game loop only when it is not 0 (0x19D8A, then DS:49F8 := 1). A save made at
  the party menu or in the training hall (state 0) leaves the menu up with the save's party, the first member current
  (0x1FB99), DS:49F8 still 0: Begin then runs `ECL<disk>` block $49F2 from the loaded memory as on a new game (section
  5a). Engine: `PorGame.LoadAtPartyMenu` (`LoadedInterfaceState`); Begin → `RunNewGameEntry(party)`.
- **After the game loop** the main program quits to DOS (START 0x142 → 0899:0000): section 8 (the party destroyed).

## 1a. The introduction (0x06FB)

**P** (s22, read with capstone from the unpacked START image and GAME.OVR); the flash burst **E** (unicorn,
`por_emu.py`: the loop run with the palette and delay routines trapped, 12 generator seeds, bursts of Random(3) + 2
flashes as read). **D** (s22, DOSBox-X `svga_s3`, window grabs about 175 a second): the three pages, the flash colours
and order, the key and the lightning's rate; the Client's logo, title and credits pages equal DOSBox-X's pixel for
pixel. s19's sound capture already found the intro and title silent (sound.md §2.4), and the routine calls no sound
routine.

- **Where:** the main program calls 0019:0025 (START 0x11D) and 001C:0025 (0x122) unless ParamStr(1) is the debug word
  (START 0x116). Those are overlay stubs: unit GAME.OVR 0xD4 (size 0x80E, stub segment 0x19) entry 0x627 = **0x6FB**
  (the introduction) and unit 0x9AC entry 0xFD = 0xAA9 (the code wheel). Code-segment offsets below are the unit's,
  so CS:n is GAME.OVR 0xD4 + n. **D** (s24): `START STING` (the debug word) boots straight to the party menu; the
  Client does the same when its start word is the program's.
- **The waits** use GetTime (START 0x7CCF: h·360000 + m·6000 + s·100 + hundredths, from INT 21h 2Ch; the m·6000
  product keeps only its low word, so the clock steps back where it passes a multiple of 65536, at minutes 11, 22, 33,
  44, 55: a wait that spans one runs until a key `[?]` not modelled). Near routine **0xD4**(n): end := GetTime +
  100·n, loop until GetTime ≥ end or KeyPressed (START 0xA6FD), then the "flush" START 0x8A94, which **does
  nothing**: its first instruction jumps to its own end (0x8A9A → 0x8AAE), past the ReadKey loop at 0x8A9C. So the
  key stays in the buffer, every later wait (the title's loop, the credits' 0xD4(10)) ends at once and the code wheel's
  input reads the key. **D:** one key (space or Return) on the logo: the title and the credits page show only while
  they load and draw (about 0.4 s and 0.35 s at 3000 cycles), then the code wheel with the key typed in.
- **Sequence (0x6FB):**
  1. The data path DS:547C (POOL.CFG's third line) is saved and emptied, the file name CS:0x621 (0x6F5: TITLE)
     loaded, and TITLE block 1 read (START 0x4751) and drawn at screen (0, 0) (START 0x5F91; the block's own x / y are
     not used: TITLE's header says x = 1). Wait 0xD4(**5**).
  2. **DS:49D1 = 0**: DS:547C is restored and the routine returns (logo only). DS:49D1 is 1 when POOL.CFG's fifth
     line starts with `F` (START 0x461F; the test copy's does); what the installer's `F` means is `[?]`.
  3. Otherwise TITLE block 2 is read (still from the emptied path), DS:547C restored, the block drawn at (0, 0) and
     freed (START 0x6BD5). end := GetTime + 500. **Loop 0x7E2:** in EGA (DS:4A1E = 1) the 32-bit counter [bp-8] +=
     Random(4) (START 0xC140, the game's generator); when it is above 50000 (0x808): counter := 0, n := Random(3) + 1
     and the flash runs for i = 0 … n (**n + 1 = 2 … 4 flashes**): palette register 1 (START 0x704D: INT 10h
     AX=1000h; a colour c ≥ 8 is written as c + 8, the 200-line intensity bit) := 9, Delay 150 ms (START 0xA69C), 11,
     100 ms, 15, 50 ms, 11, 100 ms, 9, 150 ms, then 1 (550 ms a flash). Then KeyPressed → leave; GetTime < end → loop.
     Keys and the clock are only read after a burst, so a burst can hold the title past its 5 seconds. Then the key
     buffer "flush" (0x8D0 calls the same no-op 0x8A94). **D** (3000 cycles, no key): the logo drawn 9.1–9.45 s
     after the emulator started, the title 16.4–16.7 s (5 s of wait plus about 1.9 s loading TITLE block 2), the
     credits 21.7–22.1 s, the code wheel at 32.0 s (10 s).
  4. The **credits page** (near 0x2DA): two windows (START 03F1:02E8 with left, top, right, bottom = 1, 1, 38, 3 and 1,
     5, 38, 23, style 0, colour 0, empty title CS:0x4A: the inside cleared, the frame one cell around it, so rows 0–24
     are all frame or window), then 33 text calls START 0x76B6 (column, row, colour, string: 29 distinct strings
     CS:0x4B–0x1E2, colours 10, 14, 11; catalogue `Intro.0`–`Intro.28`), then 0xD4(**10**). Return; the code wheel
     follows.
- **The lightning's rate** is the machine's: a pass of the loop is about 144 instructions (counted under unicorn: 288 for
  two passes) plus INT 16h and INT 21h. **D:** the first burst came 0.83 s into the title at 20000 cycles and 0.33 s
  at 50000, i.e. about 2 passes per 1000 cycles (the interrupts cost most of a pass): about **6 passes/ms** at DOSBox-X's
  3000 cycles, a first burst after 5.6 s on average, and indeed none in the title's 5 seconds at 3000 cycles; at
  `cycles=max` bursts every few tenths of a second. Flashes as read: register 1 (the blue band) 9, 11, 15, 11, 9, then
  the next flash at once (the closing "1" at 0x89D has no delay, so it is not seen between flashes) and 1 after the
  burst. The Delay lengths were about 0.65 × nominal at 20000 cycles (the CRT Delay's loop calibration under DOSBox);
  at 3000 cycles Delay(3000) measured 3.0 s.
- Tandy and CGA (DS:4A1E ≠ 1) skip the counter: no lightning. The generator draws (a timing-dependent number) come
  before the party menu's.
- **Engine:** Core `Intro` (pages, waits, a key ends the whole intro, the counter and burst). The loop runs at
  `PassesPerMillisecond` = **40**, DOSBox-X's rate at 20000 cycles (first burst 0.83 s into the title), so the
  lightning shows: one or two bursts per title (user decision s23: at 3000 cycles, 6 passes/ms, it never showed, which
  counts as a bug; `PassesPerMillisecondAt3000Cycles` keeps the measured rate for tests). Client
  `IntroScreen`, shown at start-up over the party menu (not with `--save` / `--area`, which stand for the debug start
  that skips the intro and the code wheel). Pictures DOS TITLE 1 and 2 from the module (`art/dos/title`); register 1
  is drawn by recolouring index 1. Under Best / Amiga with the Amiga intro's pages in the module (importer 0.7.8) the
  **Amiga intro** plays instead (below). With an older module that has only the painting (`art/amiga/intro/0/1`,
  s18) the title page shows it, without the lightning (an EGA register trick; index 1 is another colour there), and
  the logo and credits stay DOS. Departures: the intro draws from a generator of its own (so `--seed` stays repeatable); every page
  is shown (as with POOL.CFG's `F`); the GetTime step-back is not copied; the DOS clock's 55 ms steps are not
  modelled. A page whose picture or texts are missing is left out. The code wheel is still skipped.

**Amiga v1.0 intro (read s24 from disk A's `intro`, decrunched; hunk 4; P, no emulator run).** The program
(`program`, hunk 10 +0x556) first runs the code wheel (hunk 25 +0x4576), starts song 1 (hunk 27 +0x6E54;
played by the Client under Best / Amiga, sound.md §5; stopped after `intro` returns, hunk 10 +0x59A), then Executes
disk A's `intro`. The intro (hunk 1) finds the program's screen (by its title; it stores the screen's plane pointers,
which hunk 4 never reads), runs hunk 4 +0 and then Delays 150 ticks with the system display back (+0x26C puts the
system copper list, GfxBase+0x26, back). dos.library ticks are 50 a second on PAL and NTSC: **3 s**, with song 1
still playing and nothing read. What shows is the program's screen as the code wheel left it: after the right
answer the wheel (hunk 25 +0x47B0) does SetAPen(0) and RectFill(0, 0, 320, 204), so the screen is all pen 0, and
pen 0 is black (the screen's start-up colours, hunk 2 +0, copied by hunk 1 +0x200; no palette setter runs on the
way: the only one hunk 25 reaches is table 3's, from the 3D view module, and table 0, the one whose colour 0 is not
black, is set only at hunk 10 +0x5566 and hunk 13 +0x5F0E, later menus). The Intuition pointer over it is not read `[?]`. Hunk 4 +0: a 320×200 4-plane screen with its own copper list (one palette, all
black at first), then nine pages, each: a black wait (+0xB14), the screen cleared (+0x7EE), the page blitted, a fade
in, a hold (+0xAE4), a fade out (+0x8EC). Pages: eight credits panels from the 320×600 sheet (sheet row, screen row,
rows): SSI logo (93, 30, 48), a joining word (455, 60, 16), Ubi Soft (398, 30, 56), the presenting word (474, 60, 19), the AD&D logo
and copyright (141, 20, 146), the game's logo (0, 20, 92; the only page with the second colour table), the SSI
credits box (288, 20, 110), the Amiga version's credits (499, 20, 100); last the title painting (whole screen), faded
in and held, then +0x26C ends at once (no fade out). Fades: 16 steps, each colour component (4 bits) divided by
16, 15 … 1 in and 1 … 16 out (`divu`, integer), 9 beam passes per step. Waits and holds count passes of a loop that
returns while the beam is on line 128 (gap 81, hold 1001) and test the left mouse button (CIA-A bit 6) each pass: a
press ends the pages (+0xB02 → +0x26C, then the return address is replaced by +0x260, so hunk 1's Delay still
follows). Keys are not read. Each page blit (+0x512, the title +0x680) first waits for the beam on line 240
(`VHPOSR & $FF00 = $F000`).

**Timing (s27, from 68000 cycle counts; no emulator run).** The wait (+0x830) polls `VHPOSR & $FF00 = $8000`: it
returns while the beam is on line 128 (only line 128: PAL has 313 lines, NTSC 263), so a count of passes is a count
of returns during that one line each frame. The poll loop is 42 cycles (`move.w abs.l` 16, `andi` 8, `cmpi` 8,
`bne.w` taken 10), so the first return comes within 42 cycles of the line's start.
- Wait and hold pass (+0xAE4 / +0xB14): `jsr abs.l` 20, the poll falling through (16 + 8 + 8 + 12), `rts` 16,
  `btst #6,$BFE001` 20, `beq.w` not taken 12, `dbra` 10 = 122 nominal, the CIA-A read being an E-clock-synchronous
  access (E = CPU clock / 10). Back-to-back E-clock accesses fall a multiple of 10 cycles apart and the read takes at
  least 12 (4 plus at least 8 of wait), so a pass is 130 cycles: returns at t, t + 130, t + 260, t + 390 (≤ 431)
  inside a 454-cycle PAL line (455 NTSC), the next (≥ 520) on line 129. **Four passes a frame**: gap 81 passes =
  20.25 frames, hold 1001 = 250.25 frames.
- Fade pass: `jsr` 20, the poll 60 with `rts`, `dbra` 10 = 90 cycles, no CIA: five or six on the line, so a step's 9
  passes span **two frames** whatever the phase; the colour arithmetic of a step (16 colours, three `divu`) takes
  some 25 lines, after line 128, so the next step's passes start on the next frame's line 128. A fade = 32 frames.
- **Set-up between the gap and the fade (s28, P from the code and the HRM's DMA rules; no emulator run).** What shows:
  black. The clear and the blit write the planes while the copper colours are still the last fade-out's (all
  components divided by 16) or, before the first page, the copper list's black table; nothing reads input. The
  Client draws the page with the black palette, which looks the same. Duration: the clear (+0x7EE: BLTCON0 $0100,
  D only, BLTSIZE $C814 = 800 rows × 20 words) is waited for by the CPU (+0x822 polls DMACONR BBUSY); the blit
  (+0x512 panels, +0x680 title) waits for line 240 (+0x528 / +0x696), then blits plane by plane (BLTCON0 $09F0, A → D),
  waiting for BBUSY after each. At the HRM's two blitter cycles a word a clear needs 32,000 colour clocks (> 141
  lines), so begun on line 128 it ends past line 240 of that frame and well before line 240 of the next: the blit
  starts on line 240 one frame after the gap's last pass. The fade then computes its first step's colours (some 25
  lines, s27) before its first pass, so the blit must end by about line 103 for that pass to come on the next line
  128, **two frames** after the gap's last. From line 240 to line 103 (176 lines, 39,952 colour clocks) refresh (4 a
  line), audio (4 a line, song 1 playing) and the 4-plane fetches on lines 240–243 and 44–102 (80 a line) leave
  33,504 slots, 30,688 if sprite DMA runs (16 a line). The CPU polls BBUSY from chip memory (hunk 4 is MEMF_CHIP in
  the hunk header) and BLTPRI is the system default, off (nothing found setting it: the only DMACON writes are hunk 4
  +0x280, $8380, and the program's audio ones), so it takes one slot after every three blitter cycles (A → D has no
  idle cycle): the blitter gets 25,128, or 23,016 with sprites. Panels: the 16- to 110-row ones (≤ 17,600 cycles)
  take two frames; the 146-row AD&D logo page (page 5, 23,360 cycles) takes two without sprite DMA and three with it
  `[?]` (derivation). The **title** (200 rows, 16,000 words, 32,000 cycles) misses either way, and would fit only
  with BLTPRI on and no sprite DMA: its fade starts **three frames** after its gap (Core
  `AmigaIntroSequence.TitleSetupTime`). Page 1 has two clears (hunk 4 +0xE, +0x14, 64,000 cycles from line 128).
  **Measured s41** (FS-UAE 3.2.35, A500, Kickstart 1.3, cycle-exact with sound emulated to a null sink; console
  debugger breakpoints on intro hunk 4 +0xB14 (wait), +0xB30 (its end), +0x8D6 (the fade's first pass), +0xAE4 /
  +0xB00 (hold), each stop reading CIA-A's TOD, the vertical-blank count): **PAL: two frames on every panel, pages 1
  and 5 included; the title three.** Sprite DMA is on throughout (DMACON 0x3FD / 0x3FF / 0x3F7, SPREN set), so the
  slot count above was too pessimistic for the 146-row page. Per page (TOD): wait 21 (81 passes), set-up 2 (title 3),
  fade first pass to the hold's entry 31, hold 251 (1001 passes), fade out to the next wait 32. The Client's
  `SetupTime` (2) and `TitleSetupTime` (3) are exact. **NTSC** (`ntsc_mode = 1`, same disks): set-up **3** frames on
  pages 1, 5, 6, 7 and 8 (48 rows + two clears, 146, 92, 110, 100 rows), 2 on pages 2–4 (16, 56, 19 rows), 3 on the
  title; wait 21 and fade 31 as PAL, hold **253**. Page 6 (the game's logo) fades with its own routine (+0x994, no breakpoint
  there), so its set-up is read from the wait's end to the hold's entry: 33 TOD (PAL) and 34 (NTSC) against 33 on the
  other panels, i.e. 2 and 3 frames. The Client stays PAL (as its Amiga music), so NTSC is
  documentation only. Hunk 4 sat at 0x40E58 (PAL) / 0x3EB58 (NTSC) in chip memory on these boots.
- PAL: frame 313 × 227 colour clocks at 3,546,895 Hz = 20.03 ms; a panel page ≈ 20.25 + 2 + 32 + 250.25 + 32
  frames (the title 20.25 + 3 + 32 + 250.25) ≈ 6.7 s, all nine ≈ 60 s plus the 3 s close. NTSC (7.16 MHz, 455-cycle lines, 16.72 ms
  frames), measured s41 (above): set-ups of 3 frames on pages 1 and 5–9, 2 on pages 2–4 (a 263-line frame leaves 151
  lines from line 240 to the next line 128), a hold of 253 frames (≈ 4.2 s); the Client uses PAL, as its Amiga music
  does. The 3 s close is real time on both.
- `[?]` A faster CPU (68020+, accelerator) gives more passes a frame and so shorter gaps and holds (a fade step stays
  two frames only while 5–8 passes fit a line); an interrupt landing on line 128 (keyboard, CIA timers; the music is
  a vertical-blank server, at the frame's top) would remove passes. The intro's own copper list only sets pointers
  and colours at the top; 4 low-resolution planes take no CPU cycles. **Engine:** the importer writes each panel as a whole
320×200 screen with its colours (`art/amiga/intro/0/2`–`/9`, rows and places read from the code by
`AmigaIntro.Panels`); Core `AmigaIntroSequence` (phases incl. the set-up and the 3 s closing black, divisor, the
timing above as named constants); Client
`AmigaIntroScreen` (one palette run reserved and rewritten each frame, `ScreenPalette.Reserve` / `SetRun`), shown at
start-up under Best / Amiga when the module has all nine pages; after the title's hold (or a press) it shows 3 s of
black (the reserved run made black) with song 1 still playing, then stops the song and hands over. Departures: a key,
pad button or click also counts (full controller support), and one that comes during a fade or set-up ends the
pages at the next wait or hold (the Amiga reads the button's level only then); nothing skips the closing 3 s (the
Amiga reads nothing then).

**After the intro: hunk 25 +0x12A8 (read s28, P; no emulator run).** The program calls it right after `intro`
returns (hunk 10 +0x594) and before PlaySong(0) (+0x59A), so song 1 plays on through it. It is the Amiga's copy of
DOS's start-up 0xE600 (§1), moved after the intro: the same seven allocations (0x800, 0x800, 0x400, 0x1E00, 0x588,
0x924, 0x400), the same zeroed zones and start values (0x9900 at hunk 32 +0xB4 = DS:49ED, 0xE8 / 0xDB = DS:6F69 /
6F68, the square 15, 1, 6 at hunk 32 +0x176F, the speed 1 at +0xA8), the same loads (8X8D 0xC9 into the 0x588
buffer, hunk 25 +0xD9C; blocks 0xCA → slot 4 and 0xCB → slot 0, hunk 22 +0x1616 as DOS 0x428:0x25; sqrpaci 1 and 2;
COMSPR 0–11 into slots 13–24 and 12 into 25, hunk 22 +0x193A; then `pooldata:ITEMS`, read from byte 2, 0x800 bytes),
then two routines that only fill jump tables (hunk 22 +0x5074, hunk 7 +0x3282: no calls, no drawing). The disk
prefix `poolgame:` (hunk 31 +0x18EA) is saved, emptied for the loads and put back.
- **Visible: the loading message** (hunk 26 +0x184, the same text as DOS GAME.OVR 0xE5D4). Row 24 is cleared
  (hunk 19 +0x58A8 → hunk 25 +0x197A: SetAPen 0, RectFill of the text cells (0, 24)–(39, 24); DOS calls 0x3C5:0x2F
  at the same points, presumably the same clear, not read),
  then the message is drawn at column 0, row 24 by the text routine hunk 25 +0xC3C with colour 24: SetDrMd JAM2,
  Move(8·x, 8·y + 6), SetAPen 24, Text; then Move one pixel right, SetAPen 25, SetDrMd JAM1, Text again (a colour
  under 16 would become pen 16 + c − c mod 2 and the next one). Pens 24 and 25 are interface colours (registers
  16–31, loaded once from hunk 2 +0, §4 of por-art-formats.md), not black, so the message shows on the black screen
  the code wheel left. After the loads row 24 is cleared again (+0x16BE), then the routine returns and the music
  stops. How long the message stays up is the floppy's load time `[?]` (no code timing). DOS draws the same message
  through 0x709:0x626 at column 0, row 24, colour 10 (0xEA5E–0xEA76; that routine's arguments as in combat-map.md
  "Start messages") at start-up, before the intro, and calls 0x3C5:0x2F again after its loads (0xEBD0).
- **Input:** the loaders flush the window's IDCMP queue (hunk 25 +0xA08: GetMsg/ReplyMsg until empty; called by
  hunk 25 +0, hunk 22 +0x1616 and +0x193A), so keys typed during the intro and its closing 3 s are thrown away during the
  loads (the last flush is in the COMSPR loads; the ITEMS read comes after it). `pooldata:ITEMS` is opened by hunk 10 +0, which on failure asks for the data disk (hunk 26 +0x1CA;
  hunk 25 +0xE78 clears row 24, writes a line at column 1, row 24 and waits for input through +0xA38; hunk 25 +0xEC6
  goes through Intuition): not reached with both disks in.
- **Client:** no change. Its loads take no time, so neither platform's loading message is drawn (a departure: the
  originals show it for the disk time); `AmigaIntroScreen` swallows every input until it hands over, as the flush
  does, and stops song 1 at the end of the closing 3 s, which is where the Amiga stops it with no load time.

## 2. The party menu (0x19AE0)

The window (START 03F1:02E8 with 1, 1, 38, 22), the party list (START 00BA:0767: the name, AC and HP header at row 2,
members from row 4, the current one white, the others cyan), then the entries from row 12: hotkey letter white at
column 2, the rest green from column 3. The entries are DS:0619 + 42·i (a 40-byte name, then the enabled byte at
+41); their hotkeys are the DS:05F0 string, entry i at character 2·i (twelve: the last is a debug key). The menu is the
horizontal menu routine 0x2CC8C with the prompt key `PartyMsg.ChooseFunction` and the hotkeys drawn black (not seen).
Nothing on the party menu types (P, s30): no routine under it (Create, Modify, Drop, Add, Save, Exit, View's page,
Spells, Trade, money Drop) reaches the text routine 0x77F8; its texts are fixed-cell (START 0x76B6, the menu routine,
row-24 messages START 0x2233). Only View → Items types its Drop question and refusal (view3d.md "Typing state").

Enabled bytes (0x19B2D–0x19BA3), `PartyMenu.Enabled`:

| Entry | With a current character | Without |
|---|---|---|
| 0 Create, 5 Add, 10 Exit | initial byte (1 in v1.3), never written | same |
| 1 Drop, 2 Modify, 4 View, 6 Remove, 8 Save, 9 Begin | on | off |
| 3 Train | on when $6DA8 ≠ 0 or the debug flag DS:470C (J, training.md); never cleared in this branch | off |
| 7 Load | off | on |

Home / End select the previous / next member (0x23819, shared with the camp; wrapping `[?]`). Any key but E and S
clears DS:557D (saved since the last change). Exit asks yes/no (`PartyMsg.QuitToDos`); with a party it saves until a
save succeeds (DS:557D), then quits (section 5a). The training hall (ECL PROGRAM 0) runs the same routine; there $6DA8 holds the
hall's classes and Train is offered.

## 3. Create (0x19EEB)

`CharacterCreation`, step by step; each step's record writes and generator draws are the program's.

1. A zeroed 285-byte record; icon colour bytes 0xC1..0xC6 from DS:3CF5[1..6] (c → (c + 8)·16 + c); base AC 0x32,
   base THAC0 0x28 (60 − x); status 0; in play 1; portrait head and body 1; icon footprint 0x6C := 1; byte 0xAB :=
   Random(256), the first draw.
2. **Race** list (vertical menu 0x2D669, exit word only; each creation list's entry 0 is its label, "Pick Race" …,
   shown only when the list routine's top DS:6B29 is 0: not after start-up (1), but after a list at position 0 such
   as camp Display, DOSBox-X s44, or any other list that leaves the top at 0: View's Items or coin list, VMENU
   without paging, DOSBox-X s45; a Next left before keeps it hidden; por-art-formats.md "Creation's list labels"): race names DS:0F03 + 10·code for codes 1..5 and 7 (half-orc,
   6, is skipped; the sixth line is human). Race byte 0x2E; icon size 0xC0 := 1 for dwarf, gnome, halfling, else 2;
   permanent effects added (0x2BD3C, time 0, value 0xFF): halfling 0x5A 0x61; dwarf 0x5A 0x61 0x1A 0x2F; gnome 0x61 0x12
   0x2F 0x30; elf 0x6B; half-elf 0x7C; human none. The effects go to the character's `.spc` file.
3. **Gender** list (DS:0FEC + 7·g): 0x9E := g.
4. **Class** list: DS:3E17 + 12·race holds the count then the class codes. 0x2F := code, 0x73 := 1, level 1 in each
   base class of the code (8 C/F, 9 C/F/MU, 10 C/R, 11 C/MU, 12 C/T, 13 F/MU, 14 F/T, 15 F/MU/T, 16 MU/T); with a
   thief level the thief skills 0x2ADB0 run (Dexterity is still 0 then); THAC0 base := the best DS:3C7C[class·11 +
   level]; usability 0xB0 := Σ DS:05D6[class]; the saving throws 0x2ACDC.
5. **Alignment** list: DS:3FE7 + 10·code (count, then codes); names DS:0F53 + 17·a. 0xA0 := a. Then the **age**
   (DS:3E5B + 0x1C·race + 4·class: word base, dice count, sides): a single class rolls dice + base; a multi-class takes
   base + count × sides of cleric (8, 9, 11, 12), magic-user (13, 15, 16) or fighter (14); cleric/ranger (10) is not
   handled and keeps age 0 (no PoR race offers it).
6. **Rolls** (from 0x1AB7F, again after each "No" to `PartyMsg.KeepCharacter`):
   - each ability is the best of six 3d6. The race adjustments in the loop test the race list's loop counter, which
     is 7 when the list ends, so **no race adjustment is ever applied** (a program bug, kept; the emulation agrees);
   - age steps, DS:3F2F + 10·race (words 1..4, "age above"): Str +1 / −1 / −2 / −1 (each only while Str > 0); Int +1
     at steps 2, 4; Wis −1 when not above step 1, +1 at 2, 3, 4; Dex −2 at 3, −1 at 4; Con −1 at 2, 3, 4;
   - limits: DS:3DA3 + 16·race (Str min and max by gender, exceptional max by gender, then min/max of Int, Wis, Dex,
     Con, Cha), then the class minimum DS:3F81 + 6·class + stat; Wisdom at least 13 for the cleric multi-classes 8..12
     (set constant at 0x19EA4);
   - Str 18 with a fighter level: exceptional 0x16 := Random(100) + 1, at most the race's maximum;
   - HP current := max (the previous roll's), 0xA1 := 2, 1d2 unarmed (0xA3, 0xA5), 0xAA := 1, movement 12; per base
     class: cleric slot 0xB2 := 1, magic-user slot 0xB5 := 1, gold dice DS:4263 + 2·class (count, sides) summed as a
     byte; a cleric gets the Wisdom bonus 0x2AC36 and knows every first-level cleric spell of the spell table; a
     magic-user knows spells 11, 18, 19, 21; gold 0x8E := sum × 10 / classes;
   - hit points: 0xB1 := 0x1DBB4(mask 0xFF): per base class dice DS:08B6 × DS:08BE, at least two thirds of the sides at
     level 1; max := (that + the Con bonus 0x1D8B0) / classes, or 1 when a negative bonus leaves too little; current :=
     max; 0xB1 := 0xB1 / classes. Con bonus: DS:408E[Con] per base class, +1 above 16 and +1 above 17 for class code 2;
   - the page (View 0x21084) runs the stat recalculation START 0x1758 (derived fields in the record).
7. **Name** (`PartyMsg.CharacterName`, 15 characters, stored upper case; asked again while empty; the Amiga: 14, as
   typed, por-art-formats.md "Creation's name"), **portrait** (0x1CD66: words key
   `PartyMsg.PortraitMenu`, head 1..14 and body 1..12 step and wrap, the third word keeps; pictures HEAD3/BODY3 blocks
   DS:286F[head] / DS:287D[body]), **icon** (0x1D1AC, below), then SaveChar (section 5). The record is freed: the
   character is in the roster, not the party.

Cancelling any list leaves Create at once (0x1B84D), without a character.

**Icon editor 0x1D1AC** (combat palette 0x3081D, grey ground): OLD at row 6 and NEW at row 12 (column 8), the ready and action
frames at x 32 and 104 over bevelled boxes (icon slot 25's frames `[?]`), labels at rows 10 and 16 (`PartyMsg.IconOld`,
`IconNew`, `IconFrames`). Read s20, Core `IconEditor`, matched on 600 emulated cases (`por_icon_oracle.py`: random walks
through the menus with Keep, Exit, Escape and No; `IconEditorProgramTests`).

- **State byte [bp-8]**; each state's menu is the line DS:07BF + 41·state (string[40]) given to the menu routine
  0x2CC8C (no prompt, colours 13/10/15), which answers a word's hotkey (its first capital letter or digit, so the two
  colour words answer 1 and 2) or 0 for Escape. The exit test is the set {0, E} at CS:37F6 (0x1D7C7). The main line's
  lit spans follow the routine's rule (shop-format.md "The menu routine": Parts lit also lights the COLOR of color-1).
  **D** (s39, camp Alter → I → '.' × 3): the line opens on the shared highlight (Alter's I left it on the 4th, so Size
  is lit; the Client now shares the one cell with a `--save` start too), and every lit cell matches the rule. The
  grey ground is the combat palette: 0x1D1D9 calls GAME.OVR 0x3081D (EGA registers 0 and 8 swapped, combat-menu.md
  "Palette"), restored at 0x1D8A7 after the IconOk question, so row 24, the frame's gaps and the ground are colour 0
  shown dark grey and the icons' colour 8 shows black. Client (s39): the page is drawn in the program's colours and the
  whole frame swapped (`CombatScreen.SwapGrey`); the revive / Alter / Icon run then has 79 of 79 frames equal.
- **1 main**: P → 2; 1 → 3 on the low nibbles ([bp-0x17] := 0), 2 → 3 on the high nibbles ([bp-0x17] := 1); S → 4.
  With 1 or 2 the colour line's characters 13..16 (Pascal Delete / Insert, 0x1D3BD–0x1D3D4) are replaced by
  `PartyMsg.IconHair` (low) or `PartyMsg.IconFace` (high). [bp-0x1a] (the menu Step returns to) := 1.
- **2 parts**: [bp-0x1a] := 2; an exit key → 1, else the key is the part ([bp-0x1b]: H or W) → 5.
- **3 colours**: [bp-0x1a] := 3; the key picks the colour byte [bp-0x16]: B 0xC1, A 0xC2, L 0xC3, H or F 0xC4,
  S 0xC5, W 0xC6, any other 0xC1; an exit key → 1, else → 5.
- **4 size**: the line is the other size's word (`PartyMsg.IconSmall` when 0xC0 = 2, else `IconLarge`, 0x1D2F5) before
  the DS line. L: 0xC0 := 2, S: 0xC0 := 1 (staying in 4); K keeps ([bp-6] := 0xC0), E / Escape puts [bp-6] back; both
  → 1.
- **5 step**: after parts, head 0xBD (P: −1, 0 → 13; N: +1, 13 → 0) or weapon 0xBE (0..31 the same way); another part
  letter does nothing. After colours, the chosen nibble of the byte ±1 mod 16 (N +1, P −1). K keeps (the head's Keep
  reads the head of the record at DS:5D92, the one edited; the colour Keep copies all six bytes 0xC1..0xC6 to
  [bp-0x15]); E / Escape puts the kept value back (all six colour bytes); both → [bp-0x1a].
- An exit key in state 1 (in state 4, K and E become a space first) leaves the loop: the kept values [bp-4..6] and
  [bp-0x15] go back into the record (0x1D7D6), then yes/no `PartyMsg.IconOk`; No starts again at 0x1D1DE: the kept
  values and both pictures are taken from the record as it is (the old icon becomes the current one), state 1.
- **Pictures**: 0x1E468(0) builds the record's icon (CHEAD/CBODY) into its slot 0xBF without colours, and once into
  slot 12; each loop 0x1CF3D copies slot 0xBF to slot 12 and recolours slot 12 from the record's colour bytes; the new
  icon (row 12) is drawn from slot 12 every loop, the old one (row 6) only at the start. Recolouring (0x1D08E, the
  same as the icon builder's 0x1E585): identity, then for k = 1..6 colour DS:3CF5[k] (1, 2, 3, 4, 6, 7) := the low
  nibble of byte 0xC0 + k and DS:3CF5[k] + 8 := its high nibble, a zero byte included (`CombatArt.ColourMap`).
- With DS:49FA > 1 (camp on the overland) the editor's end loads ICON block 0 into icon slot 11 and block 1 into slot
  12 (0x1D843–0x1D879, far 03E9:0034(name, block, slot)); not at creation. Slot 11 is the overland marker's third
  animation slot (geo-format.md "Marker"), so the marker shows the wrong frames afterwards (D, s25; camp.md "Alter").

## 4. Add, Remove, Drop, View

- **Add (0x1C9EC)**: the roster list (LoadCharlist 0x1DFF6, party members' names marked with the mark string), words
  key `PartyMsg.AddExit`, prompt `PartyMsg.AddPrompt`. The page (P + D s46): the inside of the frame cleared, the list
  routine's window (1, 2)–(38, 22) (21 rows, paged by Next / Prev after the words "Add Exit", no Exit flag; the Amiga
  "Add" with the flag: "Add Next Exit"), position word 0 once (0x1CA19) and then kept; DOSBox-X with 30 names: ROSTER01–21,
  N → ROSTER10–30 (`por-art-formats.md` "Creation's list labels"). A picked unmarked name is loaded (0x1EAD2) and marked **before**
  the join test, so a refused character also drops off the list when the menu writes it back. Join test
  (`PartyMenu.TryAdd`): an empty party takes it; else the party is walked counting player characters (0x84 < 0x80)
  until a member with the same name and byte 0xAB; when none, a player character joins while fewer than 6 were
  counted, an NPC while the party has fewer than 8. The join 0x1F42C: last in the list, current, icon slot 0xBF :=
  the first free of 0..7, $6E3E + 1, level stats 0x2AA87. The menu ends on exit or when more than 5 player characters
  were counted or more than 7 members; then SaveCharlist (0x1E19E) writes the unmarked names.
- **Remove**: SaveChar (0x1E684) of the current character, then 0x1CC10: out of the list; the next member becomes
  current when it was the first, else the one before it.
- **Drop (0x1B8B6)**: yes/no `PartyMsg.Drop` + name + `PartyMsg.Forever`, then `PartyMsg.AreYouSure`; on two yeses the
  message is `PartyMsg.YouDump` / `PartyMsg.OutBack` when in play (0x10D) is 0, else name + `PartyMsg.BidsFarewell`,
  and 0x1CC10 (no file is written); otherwise name + `PartyMsg.SighOfRelief`.
- **View** (V at 0x19D13, only while its shown flag DS:06EA is set, i.e. with a current character): 0x19D23 calls
  `lcall 0085:0039`, the overlay stub of **0x21AED**, the same far call the camp's V makes (0x197B3); `[bp-4]` is its
  var done. So it is the camp's View: the character page (0x21084, as `CombatSheet` documents it, with the portrait
  rule below) and the menu routine with the prompt `View:` (0x21AC7) and the entries 0x21AED builds (Items, Spells,
  Trade, Drop as the character has them, then Exit; `combat-menu.md` "View / Use"). DS:49F3 is 0 here (0x19AE0
  clears it), so the Items menu (0x21F32) has no Use and no Sell / Id. The loop (0x21B1D–0x21D40) acts on I (0x21F32),
  S (0x239D6), T (0x23190) and D (0x234E1, then the money lines 0x21547) only, and redraws the page after keys of the
  set at 0x21ACD (0x21D35); it calls no member selection (0x23819), so Home / End do nothing inside View: the member
  is picked on the party menu. It ends on Exit or Escape (key 0) and the party menu is drawn again (0x19D28 →
  0x19E65). DOSBox-X s25: Items, Trade, Drop, Exit under the page. Client: `PartyMenuFlow.View` runs the camp's
  `CharacterView.Run` with the program's page (`SheetRequest`, the portrait as Modify's), prompt and words, no member
  switch, venue `Venue.PartyMenu` (no Use); the training hall's View goes through the same (`HallHooks.View`). The
  entries' screens are the camp's as built (Spells and the item commands other than Trade, Sell and Id are not built
  yet: our notice on the bottom line). Trade's pick here is START 0x3379 case 0: the frame and the party list in this
  menu's layout only (D s42, camp.md "Trade").
  **Keys** (P + D, s39): View calls the menu routine at 0x21CD1 with colours 13 / 10 / 15 and keypad flag 0, so
  Return returns 13 and space ' ' (0x2CE81, 0x2CEFB), which the loop ignores (exit set CS:0A4C = {0, E}, redraw set
  CS:0A93 = {I, S, T}; D redraws only the money lines, 0x21547); extended keys are swallowed (0x2CE4F without the
  keypad flag: no Home / End, no arrows); ',' / '.' move the highlight and the shared cell DS:6B2B, wrapping. The
  party menu's own menu call (0x19C77, item line DS:05F0 empty, keypad flag 1) leaves the cell past its last entry,
  so 0x2CCE7 puts it back to 1: View from the party menu always opens on its first word. **D** (s39, the `unstarted`
  slot C fixture, a dwarf fighter: Items Trade Drop Exit): View opens with Items lit; '.' '.' ',' → Trade, Drop,
  Trade; Return, Right, Left, Home, End, space changed nothing; '.' → Drop; Escape; V again → Items lit; the lit word
  white from its first to its last letter, the others green with a white hotkey, the words right after "View:"
  (column 5). Exit leaves one transient frame (~9 ms) where the party menu's prompt is written over View's line
  before the menu is redrawn (not reproduced). Client: every one of its 11 frames equal to a DOSBox-X frame (the
  page, each highlight, the menu after Escape and after E); `PartyMenuScreen` takes ',' / '.' (the cell follows,
  so Escape keeps the move) and resets the cell when the party menu comes up; a keyboard Return does nothing in
  View (`SheetRequest.IsView`), the pad's A and a click take the highlighted word, and Left / Right move it like
  ',' / '.' (the Client's, as on every menu line; the program ignores the arrows here). Tests
  `PartyMenuScreenTests.View_keys_are_the_programs`. **D** (s39, the training hall, slot C of `por_test_saves.py`,
  save A's party): V, '.', '.', Escape, V → Items lit again (the hall runs the same 0x19AE0); I, Escape → View back on
  Items (0x2D669 stored 1); Client 8 of 9 frames equal (the miss: our Items page then; since s39 the program's,
  camp.md "Items and Drop from View", party menu 13 of 13). View's Drop goes through the coin list, so View comes back
  on Items (s39).
- **Train**: the training hall's (training.md). Save: section 5a.

### Modify (0x1BA1E)

Read s20; `CharacterModify` matches the routine under emulation on 1043 cases (`por_modify_oracle.py`: 343 characters
made by Create with some bytes overwritten, 700 made-up records, key walks that run fields past their limits, each
ended by K, E or Escape; plus the HP limits 0x1D932 / 0x1DA0B called directly on the made-up records). The party menu
and the training hall (M, training.md) call the same routine on the current character (DS:5D92).

- **Refusal** (0x1BA25): the experience dword 0xAC, signed, above 0 → name + `PartyMsg.CantModify` by the message
  routine START 0x7DF8 (bottom line, colour 14, shown for game speed × 225 ms) and return. Negative experience may be
  modified; no status, NPC or class test.
- **Start**: the page (View 0x21084, which runs the stat recalculation START 0x1758); the six abilities 0x10..0x15,
  exceptional strength 0x16 and max HP 0x32 are copied for Exit; the field cursor ([bp-0xD]) on Strength. The field is
  drawn in colour 13 (0x1B9B2: an ability by 0x2193B, value at column 5, or 6 below 10, row 7 + i, Strength's "(nn)" at
  column 7 in the same colour; the hit points by START 0x973 at column 4, row 18, showing current HP 0x11B), else as
  View draws it (green; HP yellow below the maximum).
- **Menu**: the word menu 0x2CC8C with prompt `PartyMsg.ModifyPrompt`, words `PartyMsg.KeepExit`, the keypad flag set
  (keypad 2/4/6/8 act as the arrows). Extended Down (0x50) — or extended 0x0D, which no key gives — moves to the next
  field (6 → 0), Up (0x48) to the previous (0 → 6); Left (0x4B) lowers, Right (0x4D) raises the field. Not extended:
  E or Escape (the menu returns 0) is Exit, K is Keep; every other key (Return, space, Home, PgUp, …) only redraws:
  **Return does not take the highlighted word**. Home / End do not switch members here.
- **Left on an ability** (byte arithmetic): Strength with exceptional strength above 0 → that goes down by one
  instead. Otherwise, after the step, a value below the race minimum (DS:3DA3 + 16·race: Strength's at + gender; Int,
  Wis, Dex, Con, Cha at + 6, 8, 10, 12, 14) **or** below the class minimum (DS:3F81 + 6·class + i) is set to the
  **race** minimum — which can be under the class minimum (a paladin's Charisma at its class minimum drops to the race
  minimum; a program bug, kept).
- **Right on an ability**: above the race maximum (Strength's at + 2 + gender; the others at + 7, 9, 11, 13, 15) →
  the maximum; for Strength first, when the step went past 18, the fighter level 0x98 is above 0 (signed) and
  exceptional strength is below the race's (+ 4 + gender), exceptional strength + 1. The class is never consulted.
- **Wisdom** (either way): cleric slot 0xB2 := 1 when above 0, then the Wisdom bonus 0x2AC36 (0xB3 / 0xB4 are not
  reset). The multi-class cleric's Wisdom 13 floor of Create is not applied.
- **Constitution**: Left → max HP := min(max HP, 0x1DA0B); Right → max HP := max(max HP, 0x1D932); then current HP
  0x11B := max HP.
- **Hit points** (field 6): Left → max HP − 1, at least 0x1D932; Right → max HP + 1, at most 0x1DA0B; current HP :=
  max HP. Dexterity changes leave the thief skills and nothing redoes the saving throws.
- **0x1D932** (least max HP): Σ levels of the base classes with a level above 0 (signed), + 1 for a ranger (4) or monk
  (7) level, a byte; **0x1DA0B** (most): per class L × 8 cleric / druid, L × 10 fighter / paladin, (L + 1) × 8
  ranger, L × 4 magic-user, L × 6 thief, (L + 1) × 4 monk, a byte sum. Both then add the Con bonus 0x1D8B0 as Create
  does (negative bonus b: (sum + b) / classes when sum > |b| + classes, else 1; otherwise (sum + b) / classes).
  0x1DA0B takes the bonus of DS:5D92, not of its argument (the same record whenever Modify calls it).
- **After every key but Exit**: the recalculation START 0x1758, the fighting block redraw 0x21642 (which runs it again)
  and the cursor drawn on.
- **Keep** (K): the NPC treasure share 0x85 := 1 and return. **Exit** (E, Escape): the saved abilities, exceptional
  strength and max HP back, current HP := that max HP (a wounded character is healed), the recalculation, return; the
  cleric slots a Wisdom change rewrote stay (a program bug, kept).
- No generator draws, no file writes: the record in the party changes; the party menu's next key (not E or S) clears
  DS:557D as for any entry.

Engine: `CharacterModify` (Core); Client `PartyMenuFlow.Modify` (the refusal as the menu's notice) with
`ModifyRequest` drawn by `PartyMenuScreen`. Keys as the program (arrows, digits 2/4/6/8, K, E, Escape; Return
ignored), except Home / End, which move the word highlight as LB / RB do (the program only redraws). The pad: D-pad
for fields and values, B Exit, LB / RB move the word highlight (the program's ',' / '.'), A takes the highlighted word
(the program ignores Return; a controller-support departure). The mouse: a click on a field's row moves the cursor
there, on the cursor's field left click raises and right click lowers, a word click chooses it, a right click off the
fields is Exit. Seen on Xvfb (s20): the page with Wisdom and then HP in colour 13, the party list's HP after Keep.

**D** (s25, DOSBox-X, `START STING`, scratch saves: save A's party with experience 0 and interface state 0, loaded at
the menu, then M): the page, Right twice on Strength (17 → 18 → 18(01), damage +2 → +4), Down × 6 to the hit points,
Left (11 → 10), Up, K, and the menu after Keep, and Escape (all values and the hit points back) equal the Client's pixel
for pixel (EGA indices, 0 differing pixels each), the field in colour 13 and the words KEEP (highlighted) / EXIT as
drawn. Two rules came out of it:

- **The portrait window** (columns 27–39, rows 0–12, the box and the head and body) is drawn only when the portraits
  flag DS:49F5 is set and the disk byte DS:5376 is 3 (View 0x21084's end, 0x21530–0x2153E, far 03D0:0034). At start-up
  both hold (0xE965, 0x19AE7); a Load at the menu leaves the save's disk byte (0x1FBB0) and picture flags ($49FF), so a
  state-0 save of disk 2 or with portraits off shows the page with neither portrait nor box (both seen; on disk 3 with
  portraits on the portrait matched pixel for pixel). The training hall sets the disk to 3. The same holds for Create's
  and View's page (same routine). Client: `PartyMenuFlow.PortraitShown`.
- **The refusal** is the message routine's (bottom line, column 0, yellow), shown for game speed × 225 ms; the menu's
  prompt then comes back without a key (seen with a loaded save of speed 4). Client: the party menu shows it through
  `HallScreens.Message` (the hall already did); pixel-equal to DOSBox-X during and after the pause.

## 5. Roster files (SaveLoad unit 0x1DFE6)

The save disk holds `charlist.txt` (one name per line) and per character `NAME.cha` (the record), `NAME.itm` and
`NAME.spc` when it has items / effects. SaveChar with no file name (Create, Remove) re-reads the list with the party's
names marked, appends the name, writes the unmarked lines (so party members drop off the list) and writes the files.
DOSBox-X (s19): after Create the list holds the name and `NAME.CHA` / `NAME.SPC` exist; after Add the list is empty.

Engine (`PorRoster`, the Client's `por-saves/characters/`): the same list file; each character one JSON file in a
save's party-entry shape (`SavedCharacter`, module-format §15). A DOS `NAME.CHA` (+ `.ITM`, `.SPC`) is read when no
JSON file has the name. `[?]` The program's file name is the name through its 8.3 clean-up; ours keeps letters, digits,
'-' and '_'.

## 5a. Save before Begin, Exit with a party (0x19D9C, 0x19E0E)

- **Save** (entry 8, key S, 0x19D9C): when on and the party list DS:5D96 is not empty, the camp's save 0x1FD3E
  (camp.md "Save"): the slot menu (`Msg.SaveWhich`, A–J; Escape gives up, 0x1FDA3 → 0x20299), the saving message, the
  file; DS:557D := 1 at 0x2028F only when one was written. At the menu the program's variables are the start-up's
  (section 1), so the save holds: disk byte 3 (DS:5376, set at 0x19AE7; $6E12 too); VM zones zero but for what the save
  writes from variables; the party at 15,1 facing west; location type 1 (DS:49FA); interface state 0 (DS:49F3,
  0x19AE7); $49FC = 1 (DS:49E2); $49FF = 3 (DS:49F5 + 2·DS:49F6, both 1 from 0xE965 / 0xE96A, read at 0x1FEDC); the
  wall-set cells from DS:6AB2–6ABC (0xE926–0xE963 → the loop 0x1FE70: $4AFA = 0, $4AFD = 1, the other four 0xFFFF);
  $6E3E = the party size (the joins'); an empty script area; then the members' files (the roster files of the members
  are deleted, camp.md). **D** (s20, fixture `saves/unstarted/`): one roster character added, S, C gave exactly these
  bytes; the engine's `PorGame.UnstartedSave` gives the same file but for the leftover memory after the stem
  (`SaveWriteTests.Party_menu_save_before_begin_matches_the_program`). The engine's new game also starts with
  $49FF = 3 now (it was 0: pictures shown as off in the camp's Pics menu).
- **Load of such a save** (section 1): the menu stays up with its party, and Save writes the loaded memory (with the
  menu's party), not a new game: engine `PorGame.SaveAtPartyMenu` on the loaded game (a training-hall save, also
  state 0, keeps its flags, clock and script; `SaveWriteTests`). (**D**: a save made there equals it but for the
  stem; Begin starts the new game, 15,1 W 00:00 and the area's first text). The program's Load also re-reads map
  $49C5 = 0 (location type 1) and wall set 0 into slot 1 (0x1FBB3, 0x1FBEE) `[?]`; the engine notes a missing map 0
  as a diagnostic, and the block's init loads its own.
  **D** (s25): the re-read uses the save's disk byte: a scratch save with disk 3 and map 20 (no GEO3 block 20) stops the
  program at the Load with a DOS-screen "unable to load geo" message; disk 3 with map 0 (the training hall's own,
  ECL3 block 11) loads. A hall save (state 0, disk 3, $6DA8 = 0xFF) loaded at the start-up menu kept the menu up with
  Train offered (the flags 0x19B2D read $6DA8 from the loaded memory; nothing at the menu or in Load clears it; Client
  `PartyMenuFlow.HallMask` from the loaded game, screen equal pixel for pixel).
- **Exit with a party** (0x19E0E): after Yes, while DS:557D = 0 and the party list is not empty, the save 0x1FD3E
  (Escape at the slot menu just asks again: a slot must be written), then the quit 0899:0000. DS:557D is cleared by
  any non-extended menu key but E and S (the set at CS:0116, 0x19CB3), so S then E quits at once. **D** (s20): after
  S, C: E, Y quit at once; after Load: E, Y gave the slot menu, Escape gave it again, D saved and quit.
- Engine: `PartyMenuFlow` Save / Exit (`SaveGame` hook → `ClientPlayer.SaveAtPartyMenu`, an engine save `x.json`),
  Load → `PorGame.LoadAtPartyMenu`.

## 6. Tables (DS offsets, v1.3; read from the player's program, `CreationTables`)

| DS | Table |
|---|---|
| 0619 | menu entries, 42 bytes each; 05F0 their hotkeys |
| 0D38 / 0F03 / 0F53 / 0FEC | class (27), race (10), alignment (17), gender (7) names |
| 3E17 | classes by race: 12 bytes per race (count, codes) |
| 3FE7 | alignments by class: 10 bytes per class (count, codes) |
| 3E5B | starting age: 0x1C per race, 4 per class (word base, count, sides) |
| 3F2F | age steps: 10 per race (5 words) |
| 3DA3 | race limits: 16 per race |
| 3F81 | class minimums: 6 per class |
| 4263 | gold dice: 2 per class |
| 08B6 / 08BE / 05DE | HP dice count / sides / class bit |
| 408E | Con HP bonus by Con |
| 05D6 | usability bits by class |
| 3CF5 | default icon colours 1..6 (also the recolouring's colour pairs) |
| 07BF | icon editor menu lines: 41 bytes per state 1..5 |
| 286F / 287D | portrait head / body pictures 1..14 / 1..12 |

These lie in `rules/program.json`'s data segment; the engine checks the level-stats tables sit at their v1.3 places
before it uses them (`PorProgramData.CreationTables`). `[?]` Not decoded into a `rules/*.json` file yet.

## 7. Open `[?]`

- Home / End wrapping on the party menu; the drop message's line and how long it stays.
- The icon editor's frame boxes (icon slot 25) are drawn as seen, not decoded. The camp's Alter Icon
  runs the same routine (camp.md "Alter").
- File names of roster characters (8.3 clean-up); the save's deletion of the members' roster files (camp.md).
- Load at the party menu of a state-0 save: what the program's re-read of map $49C5 = 0 and wall set 0 gives.
- View's Items commands other than Trade, Sell and Id (Ready, Use, Drop, Halve, Join) and View's Spells outside a
  fight are still our notice (the pages are the program's since s39, camp.md "View").

## 8. After the game loop: the party destroyed (0x403E, START 0x013D) — P, D

v1.3 does **not** go back to the party menu: the program ends (D, s22: both paths end at the DOS prompt).

- The main program (START image 0x00F6) calls the party menu 0x19AE0 once (START 0x127), the view colours 0x2EA4C and
  the game loop 0x403E (START 0x13D); when the loop returns, START 0x142 calls 0899:0000, the same quit as the menu's
  Exit (0x19E25): DS:5372 := DS:5373, sound off (08AE:00DC with DS:2612), DS:4A03 := 0, DS:4A02 := DS:4A1C and the
  video mode back (0A32:000B), then Halt(0) (0AF8:00E9). The only other caller of 0x19AE0 is PROGRAM 0 (0x3B90, the
  training hall); 0x403E has no other caller.
- The loop 0x403E has one way out: DS:49FF ≠ 0 (tested at 0x4118, 0x4172, 0x4192, 0x41AF, 0x423B). The ECL run loop
  (0x3EB7) stops too while DS:49FF is set (ecl-format.md). At 0x41A1 a pending script switch (DS:442F) is still
  followed (0x3EFA) before 0x423B; then 0x4245 clears DS:49FF and the loop returns. Nothing is reset or saved on the
  way: no party menu, no reset of VM memory, generator, disk byte, $6DA8 or DS:49FF beyond 0x4245 (the program ends).
- DS:49FF := 1 in two places, each with its own end screen (texts not quoted; code-segment strings of their units):
  - **After combat** (0x6AA5; the results 0x5A47 set it when no party member on the party side can act, outside a
    duel; combat-format.md "After combat"): every party member has already left (0x1CC10, $6E3E = 0); 0x6B37: VM
    $6DC7 := 0x80, a window (03F1:02E8 with 1, 1, 38, 22), a title line (unit 0x557E CS 0x14CE), a message (CS 0x14D7,
    0709:0768), a press-a-key line and wait (CS 0x150D, 0709:0BF5); then DS:4A00 := 1 and $6E70–$6E72, $6DE3, $6DE6
    := 0 as after every fight. The treasure list DS:6810 is not freed.
    **D** (s22, a 1-HP party against eight goblin guards, all Quick): the window's title centred on row 1 in white
    (column (1 + 38 − length) div 2 = 15), the message through the text routine in the box (2, 5)–(37, 22), colour
    10, cleared first, so it starts at column 2, row 5 and wraps at column 37 (two rows); the key line on row 24 in
    colour 13 (0709:0BF5 = START 0x7C85: row 24 cleared, the text at column 0, then a key). The Client's screen
    equals it pixel for pixel (`ClientPlayer.PartyDestroyed`). Row 24 is not written before the key line: it keeps
    what the fight left there (**D** s39, a vampire's move line under the typed text; combat-menu.md "Attack pose").
  - **ECL DAMAGE** (0x3728–0x3740: set to 1, cleared when any member has in play 0x10D ≠ 0): 0x375A–0x37BB the same
    kind of window and two lines (unit 0xD46 CS 0x272D, 0x2736), a Delay(3000) (0A40:029C), then the handler's usual
    end from 0x37C0 (DS:5D92 restored, a press-a-key line CS 0x2752 and wait).
    **D** (s22, a test save whose step runs DAMAGE 0xE0, 1, 1, 200 on the whole party): the message call blanks the
    whole window (its box is (1, 1)–(38, 22) with clear = 1), so the title is drawn and erased at once: only the
    message shows, at column 1, row 1, colour 10. Row 24 keeps what was there (here the text routine's page prompt,
    colour 13, from the sixth "dies" line) for the pause, measured 3.0 s at 3000 cycles (Turbo Pascal's Delay is
    calibrated by a loop: at `cycles=max` it took about 0.3 s); then the white line (colour 15) and a key. The line
    messages before it and the closing key line of every DAMAGE: ecl-format.md "DAMAGE". With a single line (five members already
    dead: they get no line) there is no page prompt and row 24 keeps the command line through the pause. Client:
    `DamageEnd`, equal pixel for pixel in both cases. The page prompt's key wait (START 0x7A12 → 0x7C85: the row
    blanked, the line, a key) leaves its line on row 24, so it stands under the sixth line and the window's message
    while they are typed, as through the pause; the Client drew the command line or nothing there until s45
    (`ClientPlayer.PagePromptStands`; seen on the Amiga, whose copy hunk 25 +0xE78 is the same, FS-UAE s45,
    por-art-formats.md "Party-killed window"). DOSBox-X s46 (a script PRINT that pages, then HMENU; ecl-format.md
    "Page prompt under typed text"): the prompt stays on row 24 while the next page is typed, until the menu draws
    there; the Client the same (the first page and its prompt pixel-equal; the typing pace not compared).
- So after either screen and its key the ECL run stops, 0x403E returns and the program exits to DOS (D). Starting again
  means running the program again; the party is gone with the program's memory, the roster and save files on disk are
  as they were. A return to the party menu is an engine option (off = quit, as the original): Client
  `ClientSettings.AfterDefeatMenu`, the controls screen's AFTER DEFEAT row (QUIT / PARTY MENU).
