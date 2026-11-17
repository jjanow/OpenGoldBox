# Prior art review (PLAN 1.4, phase 0)

Done 2026-11-16. Each item: where it is, who made it, license, last activity, what it covers, how good it is (judged
from the code/docs, not the README), and a verdict. `[?]` = not confirmed. Licenses are what the files say, not
GitHub's metadata, unless stated.

Our code is MIT (PLAN 0). So **GPL/AGPL code and "no license" code are reference only**: we may read them to learn
a format, then write our own spec in `docs/re/` and our own code, checked against the PoR files or DOSBox.
Only MIT/BSD/Apache/Unlicense code may be copied, with attribution.

## Summary

| # | Item | License | Last activity | Covers | Verdict |
|---|---|---|---|---|---|
| 1 | coab (Simeon Pilgrim), C# port of CotAB | BSD-3-Clause `[?]` (only in installer RTF) | 2024-02 | CotAB 1.x; imports PoR chars | Reference for DAX, ECL VM structure, rules; repo ships CotAB data + IDA db |
| 2a | Gold Box Explorer (Simser, Pilgrim, marainein) | MIT (bsimser) / none (pilgrim fork) | 2022-05 / 2021-11 | All Gold Box + FRUA | Reference/code for DAX + EGA picture decoding (MIT) |
| 2b | DAXDump/ECLDump (Pilgrim), DAXBuilder (GBC) | freeware binaries | 2014 / 2021 | DAX unpack | Cross-check tools only |
| 3a | **S. S. Lee, PoR "Exhaustive Game Information" FAQ v2.00** | CC BY-NC 4.0 | 2026-04 | PoR 1.3: DAX, creature/item/effect/map/image formats, ECL opcodes, flags, save, EXE | **Main written reference for PoR formats and ECL** |
| 3b | GBC `formats.zip` | none stated (freeware) | 2021 | char/item/effect layouts, 12 games | Reference for saves across the series |
| 3c | UA Shell HackDocs (simoN et al.) | none stated | 2003 | FRUA files | Reference for FRUA import |
| 3d | talonius/pool-of-radiance (+ all other Gold Box games) | **none (explicitly no grant)** | 2026-11 | PoR v1.1/1.2/1.3 full Turbo Pascal source, byte-identical builds claimed (v1.3 target hashes = our v1.3 image) | **Ground truth for rules/ECL/formats; read-only reference, never copy** |
| 4a | FRUA designs + UA File Archive | per author, none stated | active (2025 uploads) | ~653 PC designs | Import source (phase 7) |
| 4b | Dungeon Craft / UAF | GPL-2.0 (files GPL-2.0-or-later) | v5.30 2023-01; repo 2026-01 | DC engine/editor, FRUA importer | Reference for FRUA import; no code reuse |
| 5 | Gold Box Companion (Joonas Hirvonen) | closed freeware | 2.65, 2021-06 | 12 games | Feature list in `gbc-features.md`; layouts via formats.zip + goldbox-staging |
| 6a | DrEvil pool-of-radiance-remake (PoR decode specs) | none in tree (README claims MIT/CC BY) | 2026-10 (archived) | PoR 1.3 all formats, ECL VM, pseudocode | Reference; best cited spec set |
| 6b | AI-Concord PoR C port, retro-ware por-go (Go) | none | 2026-10/11 | PoR 1.3; por-go also Amiga art | Reference |
| 6c | wish (Python) | GPL-3.0-or-later | 2026-10 | PoR…PoD on DOS/Amiga/C64 saves, maps | Reference for Amiga/C64 |
| 6d | amberfolio (emulator), CYD/PM engine (MIT), goldbox-squire (MIT/Apache) | AGPL / MIT / MIT-or-Apache | 2026-11 | various | Addresses (amberfolio); permissive code candidates |

Most useful for us:
- **DAX / pictures:** our verified `docs/re/dax-format.md`; Lee FAQ 12.2.1 + 12.2.9; Gold Box Explorer (MIT)
  EGA bitmap/sprite decoders; DrEvil `decode/data/PICTURES.md`, `WALLDEF.md`; por-go `dax/amiga.go` for Amiga art.
- **ECL:** Lee FAQ 12.3 (PoR opcodes 0x00–0x3D with operands and semantics, VM memory map, flag list);
  talonius `eclvm.pas` (exact PoR VM; reference only); DrEvil `decode/ecl/ECL_VM.md`; coab
  `engine/ovr003.cs`/`ovr008.cs` (VM structure, CotAB 0x00–0x40); GBC ECL-Tool annotation files.
- **Saves:** Lee FAQ 12.5 + 12.2.2 (SAVGAM size verified); DrEvil `SAVES.md`; GBC formats.zip (all 12 games);
  wish (Amiga/C64); coab `PoolRadPlayer.cs` + `ConvertPoolRadPlayer` (PoR → CotAB transfer).
- **Rules:** talonius PoR source (byte-exact build claimed), Lee FAQ sections 4–13, AI-Concord `port/*.c`.

**Consequence for the plan:** PoR's formats and ECL are already decoded several times over, and a byte-exact
source reconstruction exists. Phase 1–2 work for PoR becomes "write our own clean spec from these, verify each fact
against the files and DOSBox", not fresh disassembly. The license-clean part is our job: almost none of this code
can be copied into an MIT project.

---

## 1. coab: Curse of the Azure Bonds in C#

- **URL:** https://github.com/simeonpilgrim/coab (exported from code.google.com/p/coab); blog
  https://simeonpilgrim.com/blog/ (e.g. "Reverse Engineering Pools of Darkness", 2009).
- **Author:** Simeon Pilgrim. 70 stars.
- **License:** no LICENSE at the repo root. `Installer/CotAB License.rtf` holds a BSD 3-clause text,
  "Copyright (c) 2010, Simeon Pilgrim". So BSD-3-Clause for the shipped program; for the source `[?]`.
- **Last commit:** 2024-02-09. **Language:** C# (.NET, WinForms), ~34k lines in `engine/` (one file per original
  segment/overlay: `seg001.cs`, `ovr003.cs`…) + ~7k in `Classes/`.
- **Repo also contains:** CotAB's original `Data/*.DAX` (86 files), an IDA database (`dump.idb`), IDA listing
  (`coab_new.lst`) and `.idc` scripts. Copyrighted data: never copy anything from it into our public tree.
- **Games:** Curse of the Azure Bonds only. README: "almost 100% feature complete, besides player importation
  from Pool of Radiance or Hillsfar"; the code nevertheless has `Classes/PoolRadPlayer.cs` (PoR 285-byte
  character, `StructSize = 0x11D`) and `engine/ovr017.cs ConvertPoolRadPlayer`, and loads `*.sav`/`*.cha` of that
  size. It does not run PoR content.
- **Formats implemented:** DAX container + RLE (`Classes/DaxFiles/DaxFileCache.cs`; reads raw size as signed
  16-bit, fine for CotAB, wrong for PoR's 36,881-byte block), GEO maps (`Classes/GeoBlock.cs`, 4 planes × 256),
  wall definitions (`WallDefs`, view tables used by GBE), ECL, characters (`Player.cs`, CotAB record 0x1A6 bytes),
  items (`Item.cs`, 0x3F bytes), effects, spells, combat, pictures.
- **ECL VM:** direct transliteration of the original. `engine/ovr003.cs` has a `Dictionary<int, CmdItem>`
  command table (opcode → operand-set count, name, handler) for **0x00–0x40** (CotAB; PoR stops at 0x3D).
  `engine/ovr008.cs vm_LoadCmdSets` decodes operands: each is `type, low[, high]` where type 1/2/3 take a high
  byte, 0x80 = inline compressed string (6-bit packing, `DecompressString`), 0x81 = string from VM memory.
  `vm_init_ecl` reads five header operands = the five entry points (vm_run, search location, pre-camp check,
  camp interrupted, initial entry). Globals (`gbl.ecl_offset`, `vmCallStack`, `compare_flags`) mirror the EXE.
- **Quality:** playable, faithful (game logic copied instruction by instruction from the disassembly), but names
  are often `sub_XXXXX`/`field_XX`, and it is CotAB-specific.
- **Verdict:** best structural reference for the ECL VM, combat and rules shape of the FR line, and for PoR →
  CotAB transfer. Do not copy code (license unclear, and it is a translation of SSI's code). Use the opcode
  names as cross-check only.

## 2. Gold Box Explorer and other DAX tools

### 2a. Gold Box Explorer (GBE)
- **URLs:** https://github.com/bsimser/Gold-Box-Explorer (v1.x line), https://github.com/simeonpilgrim/goldboxexplorer
  (v1.2 source as on goldbox.codeplex.com); binary v1.2 at http://frua.rosedragon.org/pc/misc/gbexplorer-1.2.zip.
  A 2026 C++/Qt conversion "with DeepSeek" exists: https://github.com/mOoNsHaDoOo/GoldBoxExplorerQt (MIT text, but the
  LICENSE file still contains an unresolved merge-conflict marker in the copyright lines).
- **Authors:** Bil Simser, Simeon Pilgrim, marainein.
- **License:** bsimser repo **MIT** ("Copyright (c) 2018 Bil Simser"). Pilgrim's fork: no license file.
- **Last commit:** bsimser 2022-05-05; pilgrim 2021-11-23. **Language:** C# WinForms, ~22k lines.
- **Covers:** all Gold Box DOS games and FRUA.
- **Implements:** DAX + RLE (`Plugins/Dax/DaxFile.cs`, unsigned sizes, "raw ≤ 0 = stored"); picture blocks with
  auto-detection by size (`RenderBlockFactory.cs`): EGA bitmap, EGA multi-frame sprite (with XOR delta frames for
  `PIC*`/`FINAL*`), VGA variants (later games), mono 8×8; EGA and "combat" palettes (`EgaVgaPalette.cs`);
  wall sets assembled from WALLDEF + 8X8D (`DaxWallDefFile.cs`, tables taken from coab `ovr031.cs`); GEO DAX maps
  (`Plugins/GeoDax`); ECL viewer/disassembler (`Plugins/DaxEcl`, built on coab's CotAB opcode table);
  FRUA GEO, GLB, TLB, items, characters, saves, vault, game data.
- **Checked by us:** its EGA bitmap and sprite size rules classify every picture block in PoR (CPIC, CHEAD, CBODY,
  COMSPR, TITLE, ICON, 8X8D, SPRIT, PIC, HEAD, BODY; section 3.4). WALLDEF blocks are multiples of 780 as its
  wall-set code expects.
- **Verdict:** MIT, so the picture decoders and wall-set view tables can be reused with attribution (but the
  wall-set tables come from coab; re-derive or confirm from the PoR EXE/FAQ). Best starting point for the
  phase-1 data viewer.

### 2b. Other DAX tools
- **DAXDump / ECLDump** (Simeon Pilgrim), Windows binaries only, 2012/2014: http://gbc.zorbus.net/daxdump.zip.
- **DAXBuilder** (in GBC): unpack/repack a DAX (same block sizes only).
- GBC's **ItemMod/MonsterMod/IconMod/FontMod** edit DAX contents (closed source).
- `daxview`, `geo2pcx`, `tlbutil2` in the UA archive (http://frua.rosedragon.org/pc/misc/, `pc/hacks/`), mostly
  for FRUA `[?]`.
- **wish** (Python, GPL-3.0) reads DOS and Amiga DAX: https://wish-goldbox.readthedocs.io/ (section 6).

### DAX format
Fully documented and verified: **`docs/re/dax-format.md`** (u16 table length, 9-byte entries
`id:u8 offset:u32 raw:u16 comp:u16`, offsets relative to the data area, signed-byte RLE). 1044/1044 blocks of PoR
v1.1 and v1.2, 1245/1245 of v1.3, 824/824 of CotAB decode exactly.

## 3. Community format notes

### 3a. Stephen S. Lee, *Pool of Radiance: Exhaustive Game Information*
- **URL:** https://gamefaqs.gamespot.com/pc/564785-pool-of-radiance/faqs/73869 (blocks bots; archived
  http://web.archive.org/web/20260417124344/https://gamefaqs.gamespot.com/pc/564785-pool-of-radiance/faqs/73869).
  Companion FAQ for CotAB: https://gamefaqs.gamespot.com/pc/564786-curse-of-the-azure-bonds/faqs/78365 `[?]` not read.
- **Author:** Stephen S. Lee. **Version:** 2.00, 2026-04-02. **License:** CC BY-NC 4.0 (we can quote with
  attribution in the private repo; our own specs restate facts in our words).
- **Covers:** PoR DOS v1.3 (also NES differences). ~22,700 lines of plain text.
- **Section 12 "Technical Analysis"** (verified headings): utilities; program overview; file manifest; DAX format;
  creature format (character = monster record, 285 bytes, offset by offset), stat min/max and XP/spell tables;
  effect format (9 bytes) and effect list; item format (63 bytes), base item table, random item arrays; 3D map
  format; wilderness special locations; combat structures; spell table; image buffer and animation formats; copy
  protection; **script (ECL) overview, operand types and every opcode 0x00–0x3D with operand list and
  semantics**; script flag list and other VM addresses; **save file format**; executable/overlay/data-segment
  walkthrough with addresses; code hacking. Section 13 lists technical and scripting bugs (useful for "bug fix as
  option").
- **Checked by us:**
  - DAX section matches the files (`dax-format.md`).
  - Save: SAVGAM?.DAT = 1 + 2048 + 2048 + 1024 + 7680 + 5 + 1 + 1 + 1 + 328 = **13,137 bytes**; PoR v1.2's
    `SAVGAMA.DAT` is exactly 13,137 bytes. `CHRDATA?#.SAV` = 285 bytes, `.ITM` = n × 63, `.SPC` = n × 9: all
    sample saves match.
  - ECL: scripts load at VM $9900. In PoR v1.2 `ECL1.DAX` block 18, block byte 2 sits at $9900 (the 2-byte
    prefix, here 0x1388, is not loaded `[?]` meaning); the five entry vectors are `01 01 lo hi` (GOTO-style
    operands) and every one lands on a valid opcode (COMPARE, AND, GOSUB, LOAD FILES…). Inline strings decode with
    the documented 6-bit packing into the expected encounter text.
  - 3D map: blocks are 1026 bytes with a 2-byte prefix (u16 0x0400) as stated. **Discrepancy:** the FAQ describes
    "a 16×16 array of 4-byte records"; the file data is **planar** (256 bytes N/E walls, 256 S/W walls, 256 event,
    256 door bits), as coab `GeoBlock.cs` and goldbox-staging read it. The planar reading gives clean planes (event
    plane: 13 distinct values; door plane: 2-bit fields); the interleaved reading does not. Use planar.
- **Verdict:** primary written reference for PoR. Every PoR spec we write in `docs/re/` should cite its section and
  then be checked against the files/EXE (talonius source or Ghidra) and DOSBox.

### 3b. GBC `formats.zip`
- https://gbc.zorbus.net/formats.zip (also `Resources/` in gbc.zip). Joonas Hirvonen with FRUA forum
  contributors. No license stated.
- Character record layouts for all 12 games (PoR: 255-line table; name is a Pascal string, length byte + 15
  chars — our sample `CHRDATA1.SAV` starts with length byte 0x0E followed by 14 characters), `Offsets compared.txt` (same field across
  games, e.g. race PoR 0x2E), "still unknown offsets" per game, item DAX record + ITEMS property table with PoR
  examples, effects by id/game/monster.
- GBC also ships per-game ECL annotation files (`Games/01. Pool of Radiance/ECL-Tool/ECL - Addresses.txt`,
  `Flags.txt`, `Comments.txt`, `Descriptions.txt`, forced data offsets), ~9k lines over all games; PoR/CotAB parts
  credited to Stephen S. Lee.
- **Verdict:** the cross-game save reference for the character manager (PLAN 5). Facts only; restate in our spec.

### 3c. Other notes
- **Picture formats** (Lee 12.2.9 + GBE, checked on PoR):
  - EGA bitmap block: `u16 height_px, u16 width_in_8px, u16 x/8 [?], u16 y/8 [?], u8 count, 8 bytes [?]` (Lee: colour
    map + bytes per image + pointer, i.e. in-memory fields), then `count` images of `height × width × 4` bytes,
    4 bits per pixel, high nibble first. Header = 17 bytes. Used by HEAD/BODY (88×40, 88×48), CPIC/COMSPR/CBODY
    (24×24 units), CHEAD (24×8/24×10), TITLE (320×200), 8X8D (8×8 tiles), ICON, SQRPACI…
  - Animation block (PIC, SPRIT, FINAL): `u8 frames (≤ 8)`, then per frame `u32 delay, u16 height, u16 width,
    u16 x, u16 y, u8 ?, 8 bytes ?` + EGA data; GBE XORs later frames of PIC/FINAL with frame 0. PoR v1.2
    `PIC0.PIC`: 4 frames of 88×88, 1 + 4 × (21 + 3872) = 15,573 bytes = file size.
  - Combat pictures use a palette with colour 0 transparent and 8 black (GBE `EgaCombatColors`).
  - WALLDEF: 156 bytes per wall view set (10 views), tile indices into the 8X8D tiles; view tables in GBE/coab.
- **GEO, ECL, monster, item, save:** see 3a; FRUA equivalents in 3d below.
- **Forums:** the FRUA "Hacking UA" board (http://ua.reonis.com/, thread 2519 is the ECL introduction) did not
  respond in 2026 `[?]`; successor forum https://forums.goldbox.games/ ("Gold Box Games Forums") is up, contents
  not reviewed `[?]`.
- **No dedicated Gold Box technical wiki found** (web search 2026-11-16). ModdingWiki (shikadi.net) has no Gold
  Box pages; general wikis (Wikipedia, handwiki "Software:Gold Box") are history only. The PoR Wikipedia/fan-wiki
  pages are not technical sources.

### 3d. talonius source reconstructions
- **URLs:** https://github.com/talonius/pool-of-radiance, plus `curse-of-the-azure-bonds`,
  `pools-of-darkness`, `champions-of-krynn`, `death-knights-of-krynn`, `dark-queen-of-krynn`,
  `forgotten-realms-unlimited-adventures` (CKIT.EXE), and others; `bc-pool-of-radiance` ("with improvements").
  Announced on the GBC homepage news, 2026-11-08.
- **License:** none. README: "this repository does not make an open-source license grant". Says AI coding tools
  were used throughout.
- **PoR repo (checked):** 84 Turbo Pascal 6 files, ~34.5k lines (57 units in `src/`, 32,184 lines, plus
  `src/includes/`; `eclvm.pas` 1,884 lines, `daxio.pas`, `saveload.pas`, `types.pas`, `combat*.pas`, `magic.pas`,
  `view3d.pas`, …) + one TASM file (`sounddrv.asm`); author Brian Schkerke; tags v1.1, v1.2, v1.3 each claimed
  to build byte-identical `START.EXE`/`GAME.OVR` in DOSBox-X with TP 6.0, TASM 1.0, EXEPACK 4.06. The SHA-256s
  pinned in `build.sh` on main (`START.EXE d825df2b…`, `GAME.OVR 14e2b132…`) **match our v1.3 WizardWorks image
  exactly**; our v1.1 and v1.2 EXEs differ from those (as expected) and the v1.1/v1.2 tags pin no hashes. We did not
  run the build `[?]`. No game data in the repo. `eclvm.pas` opcode constants end at `CLEAR_BOX = $3D`, matching Lee.
- **Verdict:** the most exact description of PoR behaviour that exists. Use it to answer "what does the EXE do"
  questions instead of re-disassembling (it can stand in for much of the planned Ghidra work), but treat it like
  disassembly: private reference only, never copied or paraphrased line by line into code, never in OpenGoldBox.
  Rules/formats we take from it are written up in `docs/re/` as facts and tested against DOSBox.

## 4. Unlimited Adventures (FRUA) and Dungeon Craft

### 4a. FRUA designs
- **Design layout** (UA Shell HackDocs `UAFILES.TXT` and a sample design checked by the sub-review): a `NAME.DSN/`
  folder holding only what the designer changed: `GAME001.DAT` (388 bytes), `GEO001–040.DAT` (12,962 bytes each;
  001–004 overland, 005–040 dungeon), `MONSTnnn.DAT` (450 bytes each), `STRG00x.DAT`, imported art as `.TLB`
  files, and `SAVE/` (`*.CCH`, `SAVGAMx.CSV`, `VAULTx.DAT`). No DAX/ECL: events are fixed-form records
  (100 × 20 bytes at GEO offset 3786), not bytecode. Defaults (items, monsters, art, spells inside `CKIT.EXE`)
  come from the FRUA install, so **import needs the player's FRUA install too**.
- **Docs:** UA Shell HackDocs (simoN, 2003-08-10), 57 text files: http://frua.rosedragon.org/pc/uashell/hackdocs.zip
  (GEODATA, GEOEVENT, GAMEDAT, MONSTDAT, ITEM(S), TLBFORM, STRGFORM, CCHFORM, SAVGAM, CKITFORM…; byte tables with
  gaps marked "not used?"). Also `pc/misc/geospec.zip`, `geoevents.zip`, `geoxxx_d.txt` (Sarnath, 1999);
  `pc/hacks/geosrc.zip` (Brian O'Donnell, source, license `[?]`).
- **Library:** "UA File Archive" (Steven Brobst), http://frua.rosedragon.org/ (http only). Still active (last
  upload Nov 2025). Lists: 653 PC modules, 313 on the "hacked" list (need UAShell/hacks), 148 Mac
  (http://frua.rosedragon.org/modulelist/file.php). One zip per design (`NAME.DSN/` + info text).
  **Licensing: none stated**; upload rules ask only for a description and contact, so rights stay with each author;
  many bundle third-party art. We can ship an importer, not the modules, unless an author agrees.
- **Open parsers:** Gold Box Explorer (MIT; FRUA GEO, GLB, TLB, items, characters, saves, vault, game data),
  Dungeon Craft importer (GPL), pilgrim GBE fork (no license).

### 4b. Dungeon Craft (UAF)
- **URLs:** https://github.com/grannypron/uaf (current; SourceForge https://sourceforge.net/projects/uaf/ points
  there), older export https://github.com/GaidamakUA/DungeonCraft (2019).
- **License:** GPL-2.0 LICENSE; most file headers "version 2 or any later version" (GPL-2.0-or-later).
- **Activity:** latest release v5.30, 2023-01-26; last push 2026-01-24 (README). Slow but maintained.
- **Tech:** C++, Windows only (MFC, DirectX 8, Visual Studio).
- **Design format:** `name.dsn/` folder with binary MFC-serialised databases (`game.dat`, `LevelNNN.lvl`,
  `items.dat`, `monsters.dat`, `spells.dat`, `classes.dat`, `races.dat`, …), text `config.txt`/`ReqArt.txt`, and
  loose PNG/BMP/WAV/MP3. Databases export/import as tagged text (`\(BEGIN) name = …`); default databases:
  https://github.com/manikus/default_databases_for_Dungeon_Craft (AGPL-3.0).
- **FRUA import:** editor "Import FRUA" (`UAFWinEd/ImportFRUAData.cpp`, `UAImport.cpp`; menu item disabled in the
  .rc `[?]` whether enabled at runtime); a separate `src/FRUAExport` (DSN → JSON) and `src/ConvertFRUA` (JSON → DC,
  one file per event type) exist only in the 2019 GaidamakUA export. Note: `src/FRUAExport/TestDesign.dsn` there
  contains SSI's `CKIT.EXE`.
- **Verdict:** reference for FRUA event semantics and a DC-format reader (phase 7). No code reuse (GPL).
  Importing DC modules means parsing MFC binary archives or the text export `[?]`.

### 4c. Other FRUA-like projects
bsimser/frua (MIT, README only), Pavelius/frua (C++ FRUA-like engine, no license, 2020), djmorton42/jua (Java,
no license; imports DC walls), michelemilesi/dua (Scala skeleton, Apache-2.0), talonius FRUA (CKIT.EXE source, no
license). None is a usable FRUA importer for us.

## 5. Gold Box Companion

- **URL:** https://gbc.zorbus.net/ (download `gbc.zip`; formats `formats.zip`; UA docs `hackdocs.zip`).
- **Author:** Joonas Hirvonen. **License:** closed-source freeware. **Latest:** 2.65 (2021-06-10); Steam builds 2022.
- **Games:** all 12 (PoR 1.3 … Matrix Cubed 1.0, FRUA 1.2).
- **How it works:** scans DOSBox memory for per-game byte patterns (binary `Games/NN/Game.dat`), reads/writes
  character records, map, effects, combat lists live.
- **Full feature list:** `docs/re/gbc-features.md` (grouped, with what PLAN 7 still lacks).
- **Layouts:** save/record layouts published (3b). Memory layouts unpublished; reconstructed by
  https://github.com/gschmidl/goldbox-staging (MIT, Gunther Schmidl, v0.10 2026-11-02) in `notes/gbc.md`:
  Game.dat structure, PoR live layout (285-byte records, 9-byte effect nodes with far "next" pointer, 1 KB map in
  4 planes), area records, combat lists, SAVGAM offsets, GBC's exact fix/drain/level-up rules. goldbox-staging also
  runs GBC on DOSBox Staging via its HTTP memory API and has browser rewrites of GBC/ASE/Ultimapper (`web/`).
- **Verdict:** feature target (PLAN 7). goldbox-staging is MIT and its notes are directly citable.

## 6. Other notable work

Reviewed from shallow clones and `gh api`; nothing built or run. Since about August 2026 a wave of mostly
AI-assisted Gold Box projects has appeared. Many learned from coab, so agreement between them is weaker evidence than
it looks: check against the PoR files, talonius source or DOSBox.

### 6.1 PoR-specific reverse engineering

| Repo | Author | License | Last push | Lang | What is useful |
|---|---|---|---|---|---|
| https://github.com/DrEvil-TitaniumHelix/pool-of-radiance-remake (archived) | DrEvil | README says code MIT + docs CC BY 4.0, but **no LICENSE files exist in the tree** → treat as none | 2026-10-21 | JS, Python, Markdown | Best written PoR specs, each claim with a `seg:off` citation and confidence: `decode/data/{DAX,PICTURES,GEO,WALLDEF,MONSTERS,ITEM,ITEMS,SAVES,INDEX}.md`, `decode/ecl/ECL_VM.md` (509 lines), ~15k lines of pseudocode `decode/pseudo/*.pas.md`, tools `decode/tools/{dax,ecldis,geo,daxpic}.py`. SAVES.md matches the 13,137-byte SAVGAM and 285/63/9 records. Successor: https://github.com/DrEvil-TitaniumHelix/silver-machine (MIT, Python, plays SotSB and CoK, not PoR). |
| https://github.com/AI-Concord/pool-of-radiance-port | AI-Concord | none | 2026-10-30 | C | Hand-translated C port of PoR 1.3 checked frame-by-frame against an 8086 emulator; `re/dax.py` (pictures incl. 21-byte animation frame headers, XOR deltas), `re/ecl.py` (named disassembler), `port/{chr,rules,fight,effects}.c`. |
| https://github.com/retro-ware/por-go | Felix Ruzzoli | none | 2026-11-02 | Go | `dax/`, `engine/{geo,savefile,eclops}.go`, EXEPACK unpacker; **only Amiga picture decoder found** (`dax/amiga.go`: big-endian 10-byte DAX entries, ByteKiller-packed blocks, 4-bitplane images). Ships all PoR ECL scripts decompiled as text (original content). |
| https://github.com/malcyon/wish (+ docs https://wish-goldbox.readthedocs.io/) | malcyon | GPL-3.0-or-later | 2026-10-18 | Python | Save editor/automapper for PoR, CotAB, SotSB, PoD on **Amiga, C64 and DOS**. Amiga PoR: saves are big-endian, `CHRDATAn.sav` 288 bytes (16-byte NUL-padded name), items 65, effects 10, `savgamA.dat` 13,141; all ECL in one `/ecl.dax` (28 of 29 blocks identical to DOS); Amiga DAX `u16 id, u32 off, u16 comp, u16 raw` + ByteKiller. GEO planes doc (`docs/88-map-files.md`): plane 2 = flag bit + event id; wall value v → wallset (v−1)/5, slice (v−1)%5. `platform-differences.md`, `goldbox-bugs.md` (19 SSI bugs). Not independently verified `[?]`. |
| https://github.com/wicanr2/Pool-of-Radiance-cht | Chun-Yu Wang | custom source-available (RRSAL-1.0) | 2026-11-16 | Go | Traditional-Chinese PoR engine; ~160 behaviour specs in `docs/spec/`, rules in `internal/gamepack/`. |
| https://github.com/talonius/bc-pool-of-radiance | Brian Schkerke (talonius) | none | 2026-11-15 | TP6 + Free Pascal/SDL3 | Modded fork of the reconstruction (automap, VGA, native SDL3 builds, fixes); not byte-exact. Feature ideas. |
| https://github.com/stlhood/pool-of-radiance-js | Stephen Hood | none | 2026-04-05 | JS | Browser remake; ships raw ECL and map dumps (original content). |
| https://github.com/grannypron/PoR_L10n | grannypron | Unlicense | 2021-09-27 | C# | Localisation patcher: ECL 6-bit string codec and pointer fix-ups. Only permissive-licensed PoR ECL string code. |
| robotlions/gold-box-editor, gold-box-next (https://goldbox.robotlions.com), thepeacefulprogrammer/Pool-of-Radiance-Editor | various | none | 2021–2025 | JS / Unity C# | Field-level save editors (PoR … PoD). Minor. |
| https://github.com/yorkish/pool-of-radiance | yorkish | none | 2018 | C++ | Own-rules remake (OSRIC), no RE; ships extracted art. Not useful. |
| https://github.com/Bloodbat/PoolOfRadianceConfigurator | Bloodbat | GPL-3.0 | 2026-04 | Free Pascal | Config file editor only. |

### 6.2 Source reconstructions of other games
- talonius: CotAB, PoD, CoK, DKoK, DQK (C), FRUA CKIT (Turbo C) and others; same no-grant wording
  (section 3d).
- https://github.com/retro-ware/cotab-re (Felix Ruzzoli, no license): CotAB 1.3 TP6 rebuild, 711 of 937
  procedures byte-exact; `NOTES.md` is a good guide to TP6 code generation (useful for our own Ghidra work).
  Tracks the unpacked original EXE and Borland/Microsoft tool binaries.

### 6.3 Engines and emulators
- https://github.com/amberfolio/amberfolio (AGPL-3.0-only + commercial exception, C++23, 2026-11): emulates the PC
  and runs the unmodified EXE (PoR, CotAB) with hooks; decodes no formats itself. `docs/seams.md` (2,439 lines)
  maps memory and routine addresses; `data/editions.json` has SHA-256s of store editions (useful for identifying
  the player's install).
- https://github.com/tomtombombadil/CYD-GoldBoxGameEngine and PM-GoldBoxGameEngine (MIT, C++, all commits by an
  AI assistant, 2026-11): reimplementation said to play 9 games on handhelds; `src/engine/{dax,picture,geo,
  ecl_vm,party,combat,magic,items,exepack,hlib}.cpp`, `docs/SPEC.md` (3,767 lines). Its PoR ECL is CotAB's VM
  with an address remap. **The only MIT code covering PoR**; quality unverified `[?]`.
- https://github.com/infosecanalyst-creator/goldbox-3ds (BSD-3-Clause, C++, CotAB on 3DS, derived from coab) and
  https://github.com/DoktorCranium/coab (C/SDL port of coab; ships CotAB DAX files and journal PDF). Not useful
  beyond coab.
- https://github.com/JeffreyHartman/goldbox-squire (MIT OR Apache-2.0, Rust, 2026-09): character tool with
  record-size tables for 12 DOS titles (CotAB 422, SotSB 439, PoD/Treasures 510, CoK 409, DKoK 216, DQK 414,
  FRUA 398, Buck Rogers 259 bytes per the survey `[?]`). Permissive: candidate for the character manager tables.
- https://github.com/gschmidl/goldbox-staging (MIT): GBC on DOSBox Staging; GBC memory layouts (section 5).
- No ScummVM engine or other emulator-level Gold Box support found. Unrelated name matches: ElCoyote27/krynn-tools
  (sysadmin scripts), opd-ai/goldbox-rpg, thebracket/pyrite_box, Pavelius/frua (own engines, no original data).

### 6.4 Amiga
- Amiga DAX differs from DOS (big-endian, 10-byte entries, ByteKiller compression); sources: wish
  `goldbox/amiga_dax.py`, por-go `dax/amiga.go`. Both are reference-only licences; the Amiga art path (PLAN 4.2)
  needs our own spec + verification against `reverse-engineering/amiga/original-images/por/` `[?]`.

### 6.5 Licensing and leak notes
- Repos that contain original game bytes or disassembly: coab (CotAB DAX, IDA db/listing), DoktorCranium/coab,
  retro-ware/cotab-re, por-go and stlhood (ECL/map dumps), yorkish (art), robotlions (saves), Dungeon Craft
  2019 export (`CKIT.EXE`). Never copy files from these into either of our repos' public paths.
- Code we could reuse under its license (MIT/BSD/Apache/Unlicense): bsimser Gold-Box-Explorer, goldbox-staging,
  CYD/PM engine, silver-machine (not PoR), goldbox-squire, PoR_L10n, goldbox-3ds. Everything else: read, cite
  facts, write our own.
