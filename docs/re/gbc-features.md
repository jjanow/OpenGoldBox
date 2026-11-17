# Gold Box Companion (GBC): full feature list

Input for PLAN section 7. Transcribed from GBC's own documentation, not from summaries.

## Sources

- `GBC.txt` (v2.65, 10-Jun-2021) and the per-tool readmes (`SGE.txt`, `ECL_Tool.txt`, `ECL_Monitor.txt`,
  `DAXBuilder.txt`, `FontMod.txt`, `IconMod.txt`, `ItemMod.txt`, `MonsterMod.txt`, `FRUA_Tool.txt`,
  `FRUA_Module_Manager.txt`, `GBC_Audio.txt`) inside https://gbc.zorbus.net/gbc.zip (14,089,278 bytes, fetched
  2026-11-16; nothing executed).
- Homepage https://gbc.zorbus.net/ ; format notes https://gbc.zorbus.net/formats.zip (also `Resources/` in gbc.zip).
- Memory-layout reconstruction of GBC: https://github.com/gschmidl/goldbox-staging `notes/gbc.md` (MIT).

## Facts

| | |
|---|---|
| Author | Joonas Hirvonen (also Zorbus, The All-Seeing Eye, Ultimapper 5) |
| Latest | 2.65, 10-Jun-2021 (older 2.01 kept for other DOSBox builds; Steam builds 2022) |
| License | Freeware, closed source, no license file (donations). "GBC does not modify the game executables in any way." |
| Platform | Windows (XP+; audio build not XP). Reads/writes DOSBox process memory; needs vanilla DOSBox 0.74 or the GOG builds. goldbox-staging ports it to DOSBox Staging via its HTTP memory API. |
| Status | No release since 2021; site still updated (news Nov 2026). Dormant `[?]`. |

**Games and versions** (GBC.txt "About game versions"): Pool of Radiance 1.3, Curse of the Azure Bonds 1.3
(not GOG's 1.2), Secret of the Silver Blades 1.3, Pools of Darkness 1.1, Champions of Krynn 1.2, Death Knights of
Krynn 1.1, Dark Queen of Krynn 1.1, Gateway to the Savage Frontier 1.2, Treasures of the Savage Frontier 1.0,
Unlimited Adventures 1.2 (with or without UAShell; design chosen from a list), Countdown to Doomsday 1.0,
Matrix Cubed 1.0.

Per-game limits: CotAB world map is static pictures only, no world-map teleport; world-map teleport only in
Krynn, Savage Frontier, PoD, CoK; no level-up and no auto-ammo in
the Buck Rogers games; no event setting on the FRUA map; SGE cannot add/replace characters or add items, and
inventory duplication is disabled, for DQK and FRUA.

## Features (GBC.txt unless noted)

### 1. HUD (docked above the DOSBox window)
- Per character: name, hit-point bar, combat icon, "XP needed" meter (ignores race/game level limits), effects
  (good in green, bad in red), level-drain indicator.
- Custom combat icons per character name (`<name>_1.bmp`, `<name>_2.bmp`, 24×24).
- Hover opens the menu: Map, Explore/Unexplore map, Editor, Settings, Save map (PNG), Backup save, Journals,
  Encamp-fix, Store spells, Restore spells, Race hack, Restore races, Level up, Search wizard, Quit.
- Settings: icons on/off, XP meter on/off, effects on/off, title hack (hide DOSBox title), HUD on top,
  dock/resize/restore map window, debug dump (memory addresses of characters, inventory…), Auto-ID, Fix drain,
  Fix stats, Auto-ammo, Load/Reload `Levels.txt`.
- Quick menu on a hotkey (default Ctrl + key left of "1", configurable in `Data/Configuration.txt`):
  encamp-fix, store/restore spells, journal entry by number, level up; keyboard-only.
- Updates when party members are added, removed or reordered (2.65).

### 2. Party care (out of combat only)
- **Encamp-fix**: instantly heals all characters (PoR "FIX" for every game).
- **Fix drain** option: encamp-fix also restores drained levels (PoR: only within the same session).
- **Store / restore memorized spells** in one click (stored automatically after the search).
- **Race hack**: all demihumans become human to pass racial level limits; **restore races** undoes it.
- **Level up without a training hall** (not in Buck Rogers): free; ignores race/game level caps; several levels
  at once with no XP wasted; HP rolled automatically (CON bonus to 25); WIS bonus spells; INT caps mage spell
  level; best values used for multi/dual class; dual-class former abilities restored. Driven by editable
  `Levels.txt` per game (max levels, per-level HP dice, THAC0, attacks, saves, spells, monk unarmed damage; up to
  level 40).
- **Fix stats** option: recomputes THAC0, saves, spells per day from level (needed after over-levelling).
- **Level-up bug fixes** applied only to GBC level-ups: DQK saves dropping to 0 at level 19+ (uses level 18),
  DQK paladin capped at one 4th-level cleric spell (2 at 19, 3 at 20+), Gateway thief skills over 200
  (normalised), FRUA thief saves all zero (uses PoD values). Left alone: SotSB paladin level-15 XP (2,460,001),
  Krynn spells-per-day differing from manuals.
- **PoR class hacks**: convert a level-1 fighter to paladin or ranger, a level-1 thief to monk (experimental, no
  items); exportable to CotAB. Paladins/rangers get temporary fighter levels in combat so they can sweep (2.31).

### 3. Items
- **Auto-identify**: items in character inventories are identified whenever an inventory's item count changes
  (loot still needs Detect Magic).
- **Auto-ammo**: keeps arrows/bolts/darts at a set amount (default 50) if at least one is carried; max "plus"
  configurable per game; not in combat; not Buck Rogers.
- **Auto-disable quickfight** after each combat.
- ItemMod (separate tool): edit item stats in DAX files, add items, "identify all items in this DAX", edit the
  ITEMS property table (ItemMod.txt).

### 4. Automap and world map
- Area map with party position and facing, explored squares, reveal/hide whole map, save as PNG, dockable and
  resizable.
- Notes: left-click a square, 3-character ID plus description.
- Right-click cycles overlay: own notes / the game's event numbers / nothing.
- Ctrl + left-click teleports on the map; Ctrl + right-click sets an event on a square (not FRUA).
- World map view with party location and location titles ("T" toggles); recognises PoD's several world maps;
  world-map teleport where supported.

### 5. Combat aids
- Combat view: grid of party and monster positions, hit points, held/helpless markers.
- Hover a creature for stats and effects; click to open it in the editor (monsters editable in combat).
- Debug mode (Ctrl+D): Ctrl+W drops all enemies to 1 HP.

### 6. Character editor (live memory, out of combat)
- Abilities, levels (hover shows XP for next level), spellbook (learn all suitable / clear memorized, +/- per
  spell), items, effects (warned: may crash), class conversions above.

### 7. Journal and reference
- Journal entries shown over the game window; "a list of passwords for the game"; opens Manual, Adventurer's
  Journal and Cluebook PDFs from the game folder.
- XP tables per game; random name generator; monster manuals per game (Resources, 2.50).

### 8. Save management
- Save list in chronological order with the map name of each save.
- **Backup save** with a description to `SAVE STORAGE`.
- GOG cloud-save folder support (2.50); "Setup GOG" auto-configuration; start game / GOG settings / DOSBox
  config buttons.

### 9. Save Game Editor (SGE.txt; works on files, no DOSBox needed)
- Store / restore / delete whole saves.
- Store, delete, replace, add (party up to 8, NPC-free) and rename characters.
- Store, replace, add items; edit item properties; add/delete/modify effects.
- Duplicate one character's inventory to another; copy all inventories from one save slot to another (for
  PoD's Limbo item loss). Items arrive unreadied.

### 10. Script and data tools
- **ECL_Tool** (ECL_Tool.txt): browse and edit ECL scripts of 11 games (not FRUA), map display, global search,
  flag list with comments, forced data offsets per game, auto-comments map-event ON GOTO targets; ships
  per-game annotation files (`Games/NN/ECL-Tool/ECL - Addresses/Flags/Comments/Descriptions.txt`, PoR 1.3 data
  credited to Stephen S. Lee).
- **ECL_Monitor** (2.65): live view of the running script with PC focus, live flag and code editing, string
  search.
- **DAXBuilder**: unpack a DAX to files and rebuild it (same block sizes only).
- **FontMod** (replace game font via bitmap), **IconMod** (EGA combat icons), **MonsterMod** (edit monster
  stats in DAX/GLB; export a monster to SGE as a party member).

### 11. FRUA-specific
- **FRUA_Tool**: live keys/special items, quests (named, editable) and event states.
- **FRUA_Module_Manager** (release 3, 2020): download designs from the UA archive, install them, apply
  module hacks, patch CKIT.EXE.

### 12. Audio (GBC_Audio.exe, 2.60)
- Plays external music by situation: world map, combat, victory, per area (town/dungeon…), configurable per game
  and per FRUA design; MP3/OGG/WAV/AIFF/XM/IT/S3M/MOD/MTM/UMX; volume keys.

### 13. Diagnostics
- Search wizard (address range, window title), Ctrl+R memory read test, Ctrl+D debug logging, debug dump.

## Mapping to PLAN section 7

| PLAN 7 bullet | GBC features above |
|---|---|
| Automap with notes, reveal, overview | 4 (plus world map) |
| Character/party editor | 6, 9, PoR class hacks in 2 |
| Combat information | 5 |
| Item details, identification, sorting | 3 (no sorting in GBC; sorting is ours) |
| Speed controls, skip animations | not in GBC (only quickfight auto-off); ours |
| Bug fixes as options | 2 (level-up fixes), 3 (quickfight) |
| Not yet in PLAN 7 | encamp-fix/fix drain, spell store/restore, race hack, level up anywhere, fix stats, auto-ammo, journal/passwords/PDFs, save list + backup, inventory copy, teleport, custom icons, music by situation, ECL monitor/debug |

## Published layouts

- `formats.zip`: character record layouts for all 12 games (PoR 285 bytes: Pascal-string name at 0x00, abilities
  0x10–0x16, memorized spells 0x17–0x2B, THAC0 base 0x2D, race 0x2E, class 0x2F, age 0x30, HP max 0x32, spell
  known flags from 0x33, …), "Offsets compared" across games, "still unknown" offsets, item DAX record + ITEMS
  property table, effects by id/game/monster.
- Memory layouts are not documented by GBC; they sit in binary `Games/NN/Game.dat`. goldbox-staging
  `notes/gbc.md` reconstructs that file (search patterns, record sizes, far pointers, map planes, area records).
