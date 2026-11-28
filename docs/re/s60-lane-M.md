# s60 lane M: Amiga release items (land's recovery, DELAY, FS-UAE checks)

Scope: the Amiga [release] items in PLAN Next. The source of truth is PoR Amiga v1.0's `program`, with hunks numbered
from 0 in file order and disassembled with capstone m68k after loading the hunks with relocations, as in
`por_amiga_sound_oracle.py`. Checks ran in FS-UAE 3.2.35 on Xvfb (`:94`), with sound to `agents_mute`, a disk-A copy
whose code wheel was patched, and test saves written to the copy. The Client ran on Xvfb (`:93`) with `--art amiga`.

Statuses: `seen`, `exec`, `read`, `fixed`, `bug?`, `open` (as in the lane rules).

## 1. The Amiga land's recovery: exec, fixed, seen in the Client

The routine is hunk 13 +0x2872. It is a copy of GAME.OVR 0x20D50:

- **Callers.** The script switch (hunk 16 +0x50F8) tests $49E6 = 0, block 26, $4AB3 = 0xFE and $4AA2 = 0. The game Load
  (hunk 16 +0xFC2) calls it at its end (+0x1544) when $4AA2 ≠ 0, without animation.
- **VM cells.** They are big-endian words at 2 × (cell − $4900) from hunk 32 +0x98.
- **Tables and views.** The tile lists (hunk 14 +0x22B and +0x236) and the pan (+0x258 and +0x26A) are DOS's. The
  views, screen copies and the two pauses come in DOS's order. The view routine is hunk 25 +0x1AD6, the copy of
  0x2DE38. The pause is hunk 25 +0xD7A.

It differs from DOS only in the tiles:

- **One colour map for both lists** (hunk 14 +0x248): pens 5 → 1, 12 → 2, 13 → 2 and 14 → 10. There is no "from" list.
- **The recolour** (hunk 27 +0x7160, assembly) works in place on the sheet's 4 bitplanes, one pixel at a time.
  - The planes are 0x2400 apart after a 0x14-byte head. Rows are 6 bytes and hold two 24-pixel tiles.
  - Item t is at 72·t, plus 3 when t is odd. The sheets therefore number tiles as DOS does.
  - In the random mode, each pixel whose pen the map changes calls the generator. The pixel stays when the result's
    bit 7 is set, so about half the pixels change per pass. DOS changes about a quarter.
- **The generator** (hunk 27 +0x72C8) is not RandSeed.
  - It turns its own state byte through two nibble tables, then adds the beam position bytes ($DFF006 and $DFF007).
  - The pattern therefore depends on machine timing `[?]`. The game's random numbers do not move.
- **Session lifetime.** The sheets are loaded once, by the game init (hunk 25 +0x12A8 → +0x15F6). The combat grounds
  and BACPAC overwrite only tiles 0–41 (hunk 22 +0x177A). The recolour therefore lasts for the session, as on DOS.

What was built:

- **Core:** `AmigaOverlandRecovery`, which takes `AmigaOverlandRecoveryTables`. Core holds no constants for it.
- **Module:** the tables live in `art/amiga/overland-recovery.json` (`AmigaOverlandRecoveryDocument`, with a schema).
  The importer (0.7.42) reads them from `program` after checking the code bytes and following the routine's relocated
  `lea`s (`PorAmigaOverlandRecovery`). The round trip compares them.
- **Engine access:** `IPorContent.AmigaOverlandRecovery()`.
- **Client:**
  - `ClientPlayer.OverlandRecovery` runs the DOS pass as before. DOS rules and RandSeed's draws apply under every
    art setting.
  - In step with it, through `OverlandRecovery.Run`'s new `pass` callback, it runs the Amiga pass on sheets taken from
    the Amiga overlay (`OverlandArt.AmigaTile`, with the palette run applied). The generator is its own, and the
    beam bytes come from the Client's random source.
  - An art change re-applies the final state.
  - The pan keeps the s59 hold. The two pauses run at 100 ms a step under the Amiga pages (item 2).

Evidence:

- **Oracle and tests.** `tools/scripts/por_amiga_recolour_oracle.py` runs the program's code under unicorn: 30 cases of
  hunk 27 +0x7160, and hunk 13 +0x2872 animated and still. The view, copy and pause calls are trapped, and the beam
  bytes come from a seeded LCG. The fixture is `tests/fixtures-private/por/amiga-v1.0/tile_recolour.json`.
  `AmigaOverlandRecoveryTests` matches the pixels, the generator state, the beam read count and the 24 calls. The
  tables read from `program` have DOS's lists and pan.
- **Client on Xvfb.** The `save-overland` save at script 27, square 3,20 facing W ($4AB3 = 254, $4AA2 = 0), then Left
  twice. The pyramid's grey ground turns green over about four views, then come the river and lake views, then the
  block-26 text.
- **FS-UAE (seen, continuation).** An Amiga save on the overland was built from disk A's save A: the script area
  (offset 5120) from the DOS `save-overland` save, $49C5 = 27, $4A00–$4A1F = 0, $49E6 = 0, $49C3 / $49C4 = 3 / 20,
  $4AB3 = 254 and $4AA2 = 0 as big-endian words at 2 × (cell − $4900), the position bytes 12800–12802 = 0, 0, facing,
  and the location type 12810 = 4. Load put the party on the overland at 29,20 W, as the Client. Keypad 4 twice (a
  5-frame-a-second recording) gave the same sequence as the Client: the pyramid's square, its grey ground turning
  green, the river square, the lake square, the redraw at 28,20 W and then the recovery text.
  - **Pace.** On the A500 the recolour is machine time: from the first view to the last was about 6 s (about 0.9 s a
    random pass), and the last view held about 2.4 s. The Client keeps the s59 hold (5 s), which is a user decision
    and is not changed.
  - **After the text: open.** After Return (with E sent next, which did nothing visible), the Amiga stood at 20,17 W
    on the overland with the text still shown. The Client, after Return, went to 19,16 W and the pyramid's encounter
    (the text that the party stands before the pyramid). The two runs are not like for like (the key after Return, the
    party), and the DOS side was not checked in DOSBox-X. `[?]`

## 2. Amiga ECL DELAY: fixed

DELAY (hunk 16 +0x43DA) adds 1 to the VM's PC (hunk 32 +0xB4, the opcode's own step; GOTO stores it at hunk 16
+0x1BF6) and calls hunk 25 +0xD7A, which is Delay(speed byte · 5 ticks), 100 ms a step. `ClientPlayer.Delay` now uses
`AmigaView.MessagePause`: 100 ms a step under the Amiga pages, DOS's 225 ms otherwise. Tests: `AmigaDelayTests`
(DELAY, and the recovery's two pauses).

## 3. PLAN Next (2) items

- **The summary's press-enter line (4 pixels): fixed, seen.**
  - The cause: the Amiga menu routine draws its entries one glyph at a time (hunk 19 +0x4FD2–0x5070). Each glyph gets
    both passes before the next is drawn, so the next character's pass 1 covers the pass-2 pixel left by the one
    before. The Client drew the menu-style key line whole (`Chrome.Text`).
  - The fix: `ExploreScreen.DrawKeyLine` now draws it glyph by glyph in pen 28 (`AmigaView.MenuKeyPen`).
  - FS-UAE save B (NPC share, then summary) against Client save E: the summary is **0 pixels off** outside the
    pointer corner. The NPC share window's line had the same 4 pixels before the fix and goes through the same path.
- **The coin list's "Electrum" (1 pixel): fixed, seen.**
  - The cause: the Amiga builds the coin take's rows from a capitalised 11-byte table (hunk 19 +0x3B08 → hunk 31
    +0x2D6: "Copper" … "Jewelry"). The Client used the DOS / amount-prompt names, which are lower case ("electrum",
    "silver"; hunk 20 +0x3A8). The font's capital E differs from its e on row 3.
  - The fix: the importer reads the table into the Amiga strings (`PorAmigaText.CoinListKey`), and `Trading.Take`
    uses it (`AmigaView.CoinListName`).
  - Every coin kind (FS-UAE save C against Client save F): the list is **0 pixels off** in rows 20–189. Row 24
    differs only by the Client's pressed-gadget highlight (decided s39).
  - **View / Trade / Drop (seen, continuation).** The View page's money names (Platinum, Silver, Copper) are
    0 pixels off against FS-UAE (the Client's sheet names already match the program's table). Trade's lit kinds row
    is 0 pixels off, apart from the amount. The Trade and Drop amount requesters ("How much Platinum will you
    trade?" / "… drop?") are **0 pixels off** in rows 184–199.
- **Escape in the loot take: traced; decided (user s61): Escape always leaves, as DOS, under every art setting.**
  - The cause, read whole:
    - The menu routine (hunk 19 +0x4DD6) reads RAWKEY through its table (hunk 20 +0x629). Esc (raw 0x45) gives
      0xFE, and the routine returns 0 for it at once (hunk 19 +0x533C–0x5388).
    - It does so without touching the caller's digit-flag byte. Only its key lookup (hunk 19 +0x57D0) writes that
      byte: 0 for any other key, 1 for a digit.
    - The list routine (hunk 13 +0x2B76) passes its own local -1(a5) as that flag and never sets it. On a returned
      key with the flag set it tests only the keypad digits (+0x2E88). With the flag clear, key 0 makes it return the
      lit row with key 0 (+0x2F7C), and the coin take ends on key 0 (hunk 19 +0x3B96).
    - So Escape leaves an Amiga list only when the key before it, in the same list call, was a non-digit key. On the
      first key, the byte is stack residue.
  - **FS-UAE (s60):**
    - Escape as the first key: nothing (three times, s47 included).
    - Down, then Escape: nothing (Down gives the digit '2').
    - X, then Escape: the list left.
  - **Decided (user s61): (a).** The question was: (a) leave it as DOS; (b) port the Amiga's flag under the Amiga
    art (Escape ignored until a non-digit key has been read in that list call, the first key's residue taken as set).
    The flag is an uninitialised byte, an original quirk, and is **not followed**: Escape in every Amiga list always
    leaves, as DOS does, under DOS, Best and Amiga art. This is how the Client already behaved; no code change.
- **The exit question (C1): seen.** Leaving the loot with treasure left: the treasure-left line with the
  question in pen 24, then Yes / No. The text window and row 24 are **0 pixels off** against the Client, apart from
  the Client's gadget highlight.
- **The end-of-COMBAT redraw: seen, matches under the machine-time rule.** No on that question: the Amiga showed the
  3D view with the panel's heading for about 0.4 s, an empty frame for about 0.8 s, then the shop (portrait, party,
  the shop menu). The Client goes from the question to the shop with the redraw in between not held.
  That is disk and machine time, which is not copied (s40).
- **"is cured.": not reached, open.** The temple's Cure Blindness on a member who is not blind shows the name and the not-blind
  line, the cast-anyway question, then the price line and the pay question (all **0 pixels off** against the
  Client, apart from names and the gadget). Paying gave the not-enough-money line (green, row 24, brief; the same in the
  Client). The party has no 1000 gold, so the cure line itself was not reached.
- **The camp quit wording: seen, fixed.**
  - The camp's Save on the Amiga asks for the save letter A–J, shows the disk-access line on row 24, then
    the quit-to-Workbench question with Yes No. Row 24 is **0 pixels off** against the Client, apart from the gadget.
  - Found on the way: the Amiga game save (hunk 16 +0x155A) leaves the text window alone. Its saving line
    (+0x1780) and the character files' disk-access line (hunk 16 +0x49C: +0x564, …) go to row 24
    through the string routine (hunk 25 +0xC3C). DOS prints its saving line in the text window and clears it.
  - The Client cleared the text window under Amiga too. **Fixed:** `CampScreens.Save` keeps it under Amiga. The row-24
    lines are disk time and are not shown, as with the script load's line. Text window 0 pixels off at the quit
    question.
- **The heal list's starting highlight: seen.** Temple → Heal: the first row (Cure Blindness) lit. **0 pixels off** in
  rows 24–139.
- **Status words: seen.** DAMAGE 1d1+5 left Melcar at 0 HP. View shows "STATUS UNCONSCIOUS" and HP 0 in red. Client
  (DAMAGE 1d1+6, Bakshi at 0): the cells are **0 pixels off**. "(Helpless)", the scribe warning and the pen-24 timed
  messages were not set up. **open**
- **The blank row after a DAMAGE line ending on column 38: seen.**
  - With DAMAGE 0xE0, 1, 1, 5, 0, the hit line for 6 points ends on column 38 and the next row stays
    blank.
  - The Client's DOS party has Bakshi, whose line ends the same way. That line's tail and the blank row after it are
    **0 pixels off**; one pixel differs because of the name's last glyph.
- **Left Shift / left Alt: seen (Shift), fixed.**
  - On the FS-UAE 3D command line, left Shift (the program's '7', Home) lit the previous member, and from the first
    member it went to the last.
  - The Client did nothing. **Fixed:** under the Amiga art, `ActionInput.AmigaMemberKeys` adds left Shift and left Alt
    to Previous / Next member (test `KeyBufferTests.Amiga_left_shift_and_left_alt_pick_the_previous_and_next_member`).
    Seen in the Client.
  - Left Alt did nothing visible in FS-UAE (from the first member, and from the last). FS-UAE uses left Alt for its own
    keys, so it is read, not seen `[?]`.
  - The overland command line takes the same keys (same handler), not tried. In a fight the Amiga's '7' / '1' are
    digits (the fight menu drops them), and the move routine was not checked.
- **INPUTSTR's stored case: seen.** A test script does PRINT, INPUTSTR into $9F00, then PRINTCLR of type 0x81 $9F00.
  "gem" typed in lower case prints with the lower-case g, e and m glyphs, which differ from the capitals in
  `poolrad.font`. The case is kept. **0 pixels off** against the Client.
- The FS-UAE method is now quicker (see Notes).

## 4. s49 Amiga bits: open

The camp-only message on row 24 needs a fight with a memorised camp-only spell, and the three handler texts' case
needs their handlers in play. Neither was set up. **open**

## Notes for the next FS-UAE run

- **Disk.** Use a copy of disk A. Patch `program` (hunk 25 +0x47AE `6626` → `4e71`, file offset 0x40076) and write it
  back with `xdftool … delete program + write program`.
- **Test saves.** Build them from disk A's `save/savgamA.dat`, with the script area at offset 5120 replaced as in
  `por_test_saves.py` (vector 0 is the test code). Write them as `save/savgamB.dat` …, and put the letters into
  `save/save` (10 bytes).
- **Loading.**
  - The code-wheel prompt comes about 20 s after boot. Any word works.
  - The intro takes about 60–90 s and cannot be skipped.
  - At the Load requester, the path is `poolgame:save/` (xdotool needs the `colon` and `slash` key names). Then press
    the letter.
  - Disk A's save A stands at 0,4 facing W. Keypad 8 steps forward and runs the test code; the arrow keys do not move.
  - The first key after the main menu appears is lost: send the Load again if the path prompt does not come.
  - A requester (amount, INPUTSTR) takes typed keys only after a click on its field (Amiga pixel (163, 196)).
  - A camp Save (letter F…) writes into the running copy `run/df0.adf`, not the scratch master.
- **Exact frames.** Use FS-UAE's `fs-uae-full-*.png`, halved. The Amiga screen starts at (35, 19) in it. The pointer
  covers about 20 × 20 pixels at the top left.

## Ledger evidence

- hunk 13 +0x2872 (Amiga 0x20D50) → exec: oracle (whole routine, call order) and Core test; ported as
  `AmigaOverlandRecovery`, with the tables in the module.
- hunk 27 +0x7160 (Amiga recolour) → exec (oracle, 30 cases).
- hunk 27 +0x72C8 (Amiga recolour generator) → exec (oracle). Beam input `[?]`.
- hunk 16 +0x43DA (Amiga DELAY) → read and built (`ClientPlayer.Delay`, `AmigaDelayTests`).
- hunk 25 +0xD7A (Amiga PauseOnly) → read (Delay(speed · 5 ticks)).
- hunk 19 +0x4FD2–0x5070 (Amiga menu routine, entry drawing) → read; seen in FS-UAE (summary 0 pixels off).
- hunk 19 +0x3A60–0x3B84 (Amiga coin take kinds list) → read; seen in FS-UAE (0 pixels off). Names from hunk 31 +0x2D6.
- hunk 13 +0x2B76–0x2FAC (Amiga list routine) → read for its keys: the digit-flag byte -1(a5) is never set by the
  list. Escape exits only when the menu routine's lookup cleared it before (FS-UAE s60: X then Escape leaves, Escape
  first does not). Not followed (user s61): Escape always leaves, as DOS.
- hunk 19 +0x4DD6–0x5884 (Amiga menu routine: RAWKEY → table hunk 20 +0x629, Esc 0xFE → 0 at +0x533C, lookup
  +0x57D0) → read for its keys; left Shift as '7' seen on the 3D command line.
- hunk 16 +0x155A (Amiga game save) → read: its messages go to row 24 and the text window is kept; Client fixed;
  seen (camp Save, quit question).
- hunk 16 +0x49C (Amiga character files save) → read (row-24 disk lines).
- hunk 25 +0x12A8 / +0x15F6 (Amiga game init, sheet load) and hunk 22 +0x177A (low-sheet overwrite) → read
  (the recolour lasts for the session).
