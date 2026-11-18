# Start of the game: party menu, character creation, new game (Pool of Radiance DOS v1.3)

Status: **program read (s19)**, the creation routine **checked under emulation** on 798 cases (every race, gender,
class and alignment pick, re-rolls), the menu flow and the new game's start **checked in DOSBox-X** (screens). Code:
Core `CharacterCreation`, `PartyMenu`; Data `PorRoster`, `PorGame.NewGame`; Client `PartyMenuFlow`,
`PartyMenuScreen`. Oracle: `tools/scripts/por_chargen_oracle.py` → `tests/fixtures-private/por/program-v1.3/chargen.json`
(`CharacterCreationProgramTests`). Addresses are GAME.OVR file offsets unless marked START (unpacked image) or DS.
No game text is quoted here: strings are named by their catalogue keys (`PartyMsg.*`, `PorTextCatalog`).

## 1. Start-up sequence

START's main program (image 0x00F6): start-up 0xE600; unless the first argument is the debug word, the introduction
(0x06FB) and the code wheel (0x0AA9); then the party menu 0x19AE0; the view colours (0x2EA4C); the game loop 0x403E.

- **Start-up 0xE600** allocates and zeroes the VM zones, sets the party square DS:6AAD/6AAE/6AAF := 15, 1, 6 (west),
  the disk byte DS:5376 := 1, the game speed DS:49E2 := 1, the location type DS:49F9/49FA := 1, the party lists
  DS:5D92/5D96 := nil, $6E3E := 0, DS:6B1C := 0xFF and others (0xE965–0xEA30).
- **The party menu 0x19AE0** sets DS:5376 := 3 (the character disk) and DS:49F3 := 0, then loops (section 2). Begin
  sets DS:49F3 := 4 and returns; Load returns when a game was loaded (DS:49F8 := 1).
- **The game loop 0x403E** takes the script id from VM $49F2 (the zone the start-up zeroed: 0 on a new game). With
  DS:49F8 = 0 it loads `ECL<DS:5376>` block id (0x7814), so a new game runs **ECL3 block 0**, then the script init
  0x7689 and the entry vector. DOSBox-X (s19): after Create, Add, Begin the screen shows "15,1 W 00:00" and the
  area's first text and close-up portrait. `PorGame.NewGame` + `RunNewGameEntry` do this: fresh VM memory, the party
  and $6E3E, the square, disk 3, script 0 (init, then vector 4). This settles module-format §18's start-up `[?]` for
  PoR: the manifest's `start` is not used by the PoR VM profile.
- Load from the party menu runs the program's Load (0x1F5E8) and the game loop on the loaded state (engine:
  `PorGame.FromSave`).

## 2. The party menu (0x19AE0)

The window (START 03F1:02E8 with 1, 1, 38, 22), the party list (START 00BA:0767: the name, AC and HP header at row 2,
members from row 4, the current one white, the others cyan), then the entries from row 12: hotkey letter white at
column 2, the rest green from column 3. The entries are DS:0619 + 42·i (a 40-byte name, then the enabled byte at
+41); their hotkeys are the DS:05F0 string, entry i at character 2·i (twelve: the last is a debug key). The menu is the
horizontal menu routine 0x2CC8C with the prompt key `PartyMsg.ChooseFunction` and the hotkeys drawn black (not seen).

Enabled bytes (0x19B2D–0x19BA3), `PartyMenu.Enabled`:

| Entry | With a current character | Without |
|---|---|---|
| 0 Create, 5 Add, 10 Exit | initial byte (1 in v1.3), never written | same |
| 1 Drop, 2 Modify, 4 View, 6 Remove, 8 Save, 9 Begin | on | off |
| 3 Train | on when $6DA8 ≠ 0 or the debug flag DS:470C; never cleared in this branch | off |
| 7 Load | off | on |

Home / End select the previous / next member (0x23819, shared with the camp; wrapping `[?]`). Any key but E and S
clears DS:557D (saved since the last change). Exit asks yes/no (`PartyMsg.QuitToDos`); with a party it saves until a
save succeeds (DS:557D), then quits. The training hall (ECL PROGRAM 0) runs the same routine; there $6DA8 holds the
hall's classes and Train is offered.

## 3. Create (0x19EEB)

`CharacterCreation`, step by step; each step's record writes and generator draws are the program's.

1. A zeroed 285-byte record; icon colour bytes 0xC1..0xC6 from DS:3CF5[1..6] (c → (c + 8)·16 + c); base AC 0x32,
   base THAC0 0x28 (60 − x); status 0; in play 1; portrait head and body 1; icon footprint 0x6C := 1; byte 0xAB :=
   Random(256), the first draw.
2. **Race** list (vertical menu 0x2D669, exit word only): race names DS:0F03 + 10·code for codes 1..5 and 7 (half-orc,
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
7. **Name** (`PartyMsg.CharacterName`, 15 characters; asked again while empty), **portrait** (0x1CD66: words key
   `PartyMsg.PortraitMenu`, head 1..14 and body 1..12 step and wrap, the third word keeps; pictures HEAD3/BODY3 blocks
   DS:286F[head] / DS:287D[body]), **icon** (0x1D1AC, below), then SaveChar (section 5). The record is freed: the
   character is in the roster, not the party.

Cancelling any list leaves Create at once (0x1B84D), without a character.

**Icon editor 0x1D1AC** (combat palette, grey ground): OLD at row 6 and NEW at row 12 (column 8), the ready and action
frames at x 32 and 104 over bevelled boxes (icon slot 25's frames `[?]`), labels at rows 10 and 16. Menus (DS strings
near the entries): parts (head 0..13, weapon 0..31, each next / previous / keep / exit), the two colour menus (low or
high nibble of a part's colour byte), size (small / large), exit; then yes/no `PartyMsg.IconOk`. Built: head, weapon,
size and the yes/no; `[?]` not built: the colour menus and the previous/keep/exit sub-menus (the engine steps forward
and keeps at once).

## 4. Add, Remove, Drop, View

- **Add (0x1C9EC)**: the roster list (LoadCharlist 0x1DFF6, party members' names marked with the mark string), words
  key `PartyMsg.AddExit`, prompt `PartyMsg.AddPrompt`. A picked unmarked name is loaded (0x1EAD2) and marked **before**
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
- **View**: the character page (0x21084, as `CombatSheet` documents it) with the portrait. `[?]` The engine shows the
  page only; the program's View here is the camp's View menu (Items, Spells, …).
- **Modify (0x1BA1E)**, **Save** before the game starts, **Train**: `[?]` not built in the engine (a notice says so).
  Modify is not read beyond its strings (`PartyMsg.CantModify`, `ModifyPrompt`, `KeepExit`) and its HP-limit helper
  0x1D932 (which calls the Con bonus 0x1D8B0).

## 5. Roster files (SaveLoad unit 0x1DFE6)

The save disk holds `charlist.txt` (one name per line) and per character `NAME.cha` (the record), `NAME.itm` and
`NAME.spc` when it has items / effects. SaveChar with no file name (Create, Remove) re-reads the list with the party's
names marked, appends the name, writes the unmarked lines (so party members drop off the list) and writes the files.
DOSBox-X (s19): after Create the list holds the name and `NAME.CHA` / `NAME.SPC` exist; after Add the list is empty.

Engine (`PorRoster`, the Client's `por-saves/characters/`): the same list file; each character one JSON file in a
save's party-entry shape (`SavedCharacter`, module-format §15). A DOS `NAME.CHA` (+ `.ITM`, `.SPC`) is read when no
JSON file has the name. `[?]` The program's file name is the name through its 8.3 clean-up; ours keeps letters, digits,
'-' and '_'.

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
| 3CF5 | default icon colours 1..6 |
| 286F / 287D | portrait head / body pictures 1..14 / 1..12 |

These lie in `rules/program.json`'s data segment; the engine checks the level-stats tables sit at their v1.3 places
before it uses them (`PorProgramData.CreationTables`). `[?]` Not decoded into a `rules/*.json` file yet.

## 7. Open `[?]`

- Home / End wrapping on the party menu; the drop message's line and how long it stays.
- Exit with a party before Begin: the program saves the (unstarted) game; the engine quits without saving.
- Save from the party menu before Begin; Modify; the icon colour menus.
- The icon editor's frame boxes (icon slot 25) and its palette are drawn as seen, not decoded.
- File names of roster characters (8.3 clean-up).
