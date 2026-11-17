# DOS Gold Box games: graphics modes and art colour depth

Status 2026-11-17. This file answers the open survey in PLAN 4.3: which DOS games have 256-colour art that could beat
the Amiga art. Sources:
- the clean images listed in `reverse-engineering/dos/original-images/SOURCES.txt`, unpacked in a scratch folder;
- strings in the setup and start programs;
- a classification of every picture block in every DAX/TLB file;
- a few pictures decoded to PNG and checked by eye;
- the IBM PC data cards in `docs/manuals/`, OCR'd where they are scans.

Decoders used for the check: `tools/scripts/dax.py`, plus throwaway readers in scratch modelled on Gold Box Explorer
(bsimser, MIT: `EgaBlock`, `VgaStrataBlock`, `VgaBlock`, `VgaSpriteBlock`, `FruaTlbFile`). Nothing new went into the repo.

## Summary

| Game | Version checked (earlier) | Modes offered | Art colour depth | Picture formats seen | Confidence |
|---|---|---|---|---|---|
| Pool of Radiance | v1.3 WizardWorks (v1.2) | CGA, EGA, Tandy 16 | **EGA 16** | EGA 4bpp only | high |
| Curse of the Azure Bonds | v1.3 WizardWorks (v1.0) | CGA, EGA, Tandy | **EGA 16** | EGA 4bpp only | high |
| Champions of Krynn | v1.2 WizardWorks (v1.1) | CGA, EGA, Tandy (card: VGA owners pick EGA) | **EGA 16** | EGA 4bpp only | high |
| Secret of the Silver Blades | v1.3 WizardWorks (v1.00) | CGA, EGA, Tandy (card: VGA owners pick EGA) | **EGA 16** | EGA 4bpp only | high |
| Countdown to Doomsday | v1.3 (v1.00) | CGA, EGA, Tandy, **VGA** | **VGA 256** (one 8-bit art set) | VGA 8-bit only | high |
| Death Knights of Krynn | v1.0 (v1.10 1992 disk) | CGA, EGA, Tandy (card: VGA owners pick EGA) | **EGA 16** | EGA 4bpp only | high |
| Gateway to the Savage Frontier | v1.2 WizardWorks (v1.00) | CGA, EGA, Tandy 1000, VGA (card adds MCGA) | **32 colours**: 16 EGA + 16 per-picture custom colours, 5 bitplanes | 5-plane for pictures, portraits, title, big pictures, combat icons and some walls; EGA 4bpp for the other walls, combat bodies, sky and frames | high on depth, medium on palette details |
| Pools of Darkness | v1.10 WizardWorks (v1.00) | EGA, Tandy, VGA (card adds MCGA) | **VGA 256** | VGA 8-bit only | high |
| Matrix Cubed | v1.0 (only English build) | EGA, Tandy, VGA | **VGA 256** | VGA 8-bit only | high |
| Treasures of the Savage Frontier | v1.00 (the only English floppy version) | EGA, Tandy, VGA (card adds MCGA) | **VGA 256** | VGA 8-bit only | high |
| Dark Queen of Krynn | v1.1 WizardWorks (v1.0) | VGA (256 colours), EGA (16), Tandy 16 | **VGA 256** | TLB library, 8-bit (new container) | high on depth, medium on the EGA path |
| Unlimited Adventures | v1.2 (v1.0) | VGA (256), EGA (16), Tandy 16; the data card lists VGA as minimum | **VGA 256** | TLB library, 8-bit | high on depth, medium on the EGA path |

### Corrections to the PLAN 4.3 expectation
- **Countdown to Doomsday is VGA 256, not EGA.** Its 1990 v1.00 already offers VGA in the start menu. All of its art
  is 8-bit chunky with per-picture palettes. Encounter pictures decode to 94 colours (median) and up to 254.
- **Death Knights of Krynn is EGA only.** The menu, the data and the data card all agree; the card tells VGA owners to
  choose EGA.
- **Gateway is not 256 colours.** Its VGA mode shows a 32-colour image (5 bitplanes): the 16 EGA colours plus 16
  custom colours carried in each picture. Walls in part, combat bodies, sky and frames stay EGA 16.
- 256 colours: Countdown, Pools of Darkness, Matrix Cubed, Treasures, Dark Queen, Unlimited Adventures.
- So the DOS art may beat the Amiga art for: Countdown, Pools of Darkness, Treasures and Dark Queen (all four have
  Amiga ports). It may also beat it, with less margin, for Gateway's pictures and portraits (32 colours, against
  16 or 32 on Amiga). Matrix Cubed and Unlimited Adventures have no Amiga port. The Amiga art is the likely winner for
  PoR, Curse, Champions, Silver Blades and Death Knights.

### What "EGA mode" means in the VGA games
Each of these games ships **one** high-colour art set. Nothing on disk is a separate EGA picture set, so the EGA,
Tandy and CGA modes are reductions of the same pictures, done at runtime. They can never look better than the
VGA mode, and the importer needs only the VGA set.
- DAX VGA games (Countdown, Pools, Matrix Cubed, Treasures): every 8-bit block has an extra n/2 + 4 bytes after its
  n-colour palette, in every block type. That is 4 bits per palette entry, which fits a per-picture EGA/Tandy
  remap table `[?]`. A nearest-colour test against the table was inconclusive, so how the table is used is not
  known `[?]`.
- Gateway: the 5-plane header has optional sections (by flag bits): a 2-bit CGA map for 32 colours, a 4-bit
  32-to-16 EGA map, and the 16-entry custom palette. That is what Gold Box Explorer's decoder reads.
- Dark Queen / Unlimited Adventures: the installer and the game contain a palette-table builder for the
  256-colour palette. Their game executables refuse to start in a mode other than the one the installer set up.
  This is inferred from the strings `[?]`.

## Picture formats seen (field level)

- **EGA 4bpp** (`docs/re/por-art-formats.md`): a `u16 height, u16 width/8, i16 x, i16 y, u8 count` header, then an
  8-byte CGA map, then packed nibbles. Animation wrapper: `u8 frames`, then (`u32 delay` + block) per frame.
  Death Knights' wall sets store one more tile than the count field says (Gold Box Explorer notes the same).
- **5-plane 32-colour** (Gateway only): the same first header fields, then a flag byte. If the flag has none of
  bits 2, 3, 6 and 7 set, an 8-byte CGA map follows (as in EGA). Otherwise these optional sections follow:
  bit 0 = CGA map (2 bits × 32), bit 1 = EGA map (4 bits × 32), bit 3 = 24-byte palette (16 colours at 4 bits per
  gun, used for indices 16-31). Then come 5 bitplanes per row (width/8 bytes each, plane 0 first).
  Gold Box Explorer fills only indices 18-31 from the palette. What indices 16-17 show is `[?]`.
  The game's own palette upload was not traced `[?]`.
- **VGA 8-bit, DAX** (Countdown, Pools, Matrix Cubed, Treasures). Two header forms:
  - Still blocks: `u8 height, u8 width/8`, two words (position `[?]`), `u16 count`, `u8 first colour`,
    `u8 colour count - 1`. Then 6-bit RGB triples for colours first..255, then the n/2 + 4 byte table above, then
    `count` chunky 8-bit images.
  - Encounter pictures and sprites (`PIC`, `SPRIT`): `u8 height, 0, u8 width/8`, `u8 frames` at offset 8, then
    `first colour`, `colour count`, the palette, and a signed-RLE stream. Frames 2+ are XOR deltas against the base
    frame.
  - Typical palette ranges: pictures and big pictures use colours 32-255 (224 per picture). Combat icons use 144-255,
    combat bodies 96-143, combat sprites 64-95, backgrounds 144-175 and the title the full 0-255. Colours 0-15 are
    the EGA set.
  - The wall (`8X8D`) blocks use this container, but our throwaway reader did not get their layout right `[?]`.
    No colour counts are given here for walls.
- **TLB libraries** (Dark Queen, Unlimited Adventures; MicroMagic engine): a library header with 4-char tag, size,
  entry count, a has-ID-table flag and a content type, then sub-libraries. Each sub-library has its own 8-byte colour
  table header (cycling, first colour, count, cycle ranges) with **8-bit** RGB values, usually colours 32-255.
  Images are 8-bit, stored in 4-way interleaved (unchained VGA) order, raw or RLE (drawing methods 16/17/18/21/23).
  The Dark Queen big pictures decode to 210-222 colours each.

Distinct colours per decoded picture in the DAX VGA games (median / max, first frame or first item):

| Game | Encounter pictures | Big pictures | Combat icons | Title |
|---|---|---|---|---|
| Countdown v1.3 | 94 / 254 | 89 / 168 | 19 / 37 | 7 / 15 (title blocks are text bands) |
| Pools v1.10 | 103 / 185 | 118 / 170 | 29 / 75 | 112 / 171 |
| Matrix Cubed v1.0 | 79 / 235 | 118 / 208 | 23 / 42 | 12 / 211 |
| Treasures v1.00 | 61 / 108 | 107 / 145 | 18 / 62 | 93 / 236 |

Gateway pictures use up to 32 distinct indices (index 31 is the highest). EGA games never exceed 16.

## Per-game evidence

Block counts below are pictures per file family, latest version, and are the same in the earlier version unless
noted.
- **Pool of Radiance** (v1.3 WizardWorks; v1.2 checked too)
  - Every picture block is EGA 4bpp: PIC/SPRIT animations, HEAD/BODY, CPIC/CBODY/CHEAD, walls, title, FINAL.
  - The v1.2 installer offers CGA, EGA or Tandy 16-colour. The 1992 data card says the first line of the config
    file holds the mode letter (CGA, EGA or Tandy).
  - The start programs of v1.2 and v1.3 have no adapter menu.
  - Matches PLAN 4.2.
- **Curse of the Azure Bonds** (v1.3; v1.0 checked too)
  - The start program menu offers CGA, EGA and Tandy; sound offers PC, Tandy and silent.
  - All 600+ picture blocks are EGA 4bpp.
  - The reference card mentions only CGA, EGA and Tandy.
- **Champions of Krynn** (v1.2 WizardWorks; v1.1 checked too)
  - The menu offers CGA, EGA and Tandy.
  - All blocks are EGA 4bpp.
  - The 1990 data card (OCR) tells VGA owners to pick EGA.
  - The 1992 v1.2 re-release adds nothing: same menu, same data.
- **Secret of the Silver Blades** (v1.3 WizardWorks; v1.00 checked too)
  - The menu offers CGA, EGA and Tandy.
  - All blocks are EGA 4bpp.
  - The data card and the quick reference card tell VGA owners to choose EGA.
- **Countdown to Doomsday** (v1.3; v1.00 checked too)
  - The start menu offers CGA, EGA, Tandy and VGA, in both versions.
  - Every art file is VGA 8-bit: PIC, SPRIT, BIGPIC, CPIC, COMSPR, CHARS (combat bodies), BACK, SHIPS, walls, TITLE,
    and HITEK/LOTEK/MARSCOM/VENUSCOM (combat backdrops `[?]`).
  - No EGA-format block exists anywhere.
  - Decoded and inspected: a big picture (space scene, 98 colours) and an encounter frame and combat icon. These are
    clearly painted 256-colour art.
  - The data card (OCR) gives no mode list; it says only to enter the adapter number.
- **Death Knights of Krynn** (v1.0; the 1992 v1.10 disk checked too, `[m]` per TOSEC)
  - The menu offers CGA, EGA and Tandy, in both versions.
  - All blocks are EGA 4bpp.
  - The data card (OCR) tells VGA owners to pick EGA.
- **Gateway to the Savage Frontier** (v1.2 WizardWorks; v1.00 checked too, same layout)
  - The game menu offers CGA, EGA, Tandy 1000 and VGA.
  - The data card lists VGA, MCGA, EGA, Tandy 16 colour and CGA.
  - 5-plane: PIC (73 animations), HEAD (20), BODY (13), BIGPIC (12), CPIC (154), TITLE (5), and 20 of 42 wall blocks.
  - EGA: the other 22 wall blocks, CBODY, CHEAD, COMSPR, SKY, the frame sets (DUNGCOM/WILDCOM/RANDCOM) and one PIC.
  - Decoded and inspected: a portrait, a head and a big picture with the per-picture palette. The colours look right.
- **Pools of Darkness** (v1.10 WizardWorks; v1.00 checked too)
  - The setup program menu offers EGA, Tandy and VGA (no CGA).
  - Ships separate EGA, Tandy and VGA display drivers. The driver tool's name table also lists other adapters, but
    no driver for them is on the disks.
  - The data card lists Tandy, EGA, VGA and MCGA.
  - Every art file is VGA 8-bit, including combat bodies and heads.
  - Decoded and inspected: the title (171 colours), a big picture and an encounter picture.
  - The v1.10 disk 1 archive needs Info-ZIP `unzip`; unar fails on four non-art files.
- **Matrix Cubed** (v1.0; the German v1.01D was not checked)
  - The start menu offers EGA, Tandy and VGA.
  - The data card lists EGA, VGA and TGA.
  - Every art file is VGA 8-bit. It uses the same file families as Countdown; several (CHARS, HITEK/LOTEK, SHIPS)
    are byte-identical in size.
  - Decoded and inspected: an encounter portrait.
- **Treasures of the Savage Frontier** (v1.00)
  - The setup menu offers EGA, Tandy and VGA.
  - The data card lists TGA, EGA, VGA and MCGA.
  - Every art file is VGA 8-bit, including the new BORD/OVER/PIC1A sets.
  - Decoded and inspected: an encounter picture and a combat sprite.
  - Not checked: the 1995 CD build and the German v1.03 `[?]`.
- **Dark Queen of Krynn** (v1.1 WizardWorks; v1.0 checked too)
  - The installer's video menu offers VGA (256 colours), EGA (16 colours) and Tandy 16-colour.
  - The game executable refuses to start in a mode other than the installed one.
  - The data card (OCR) lists TGA, EGA and VGA.
  - All art is in TLB libraries with 224-colour (pictures) or 256-colour (title) palettes.
  - Decoded and inspected: a big map picture (222 colours) and an encounter picture.
- **Unlimited Adventures** (v1.2; v1.0 checked too)
  - Same installer menu as Dark Queen and same TLB container.
  - The data card lists VGA as a minimum requirement, while the installer still offers EGA and Tandy. This conflict
    is recorded, not resolved `[?]`.
  - The editor's picture import accepts only 256-colour PCX.
  - BIGPIC decodes to 114-188 colours.
  - Not checked: the CD releases `[?]`.

Mode lists count only the options in the setup or start menu. The lower-case adapter name tables inside the
Dark Queen and Unlimited Adventures executables and installers (CGA, Hercules, PCjr, MCGA, extended VGA and others)
belong to the graphics library, and those modes are not offered. MCGA appears only on data cards. It presumably runs
the VGA path, since the 320×200×256 mode is shared `[?]`.

## Mac Dark Queen of Krynn (the open PLAN 4.3 check)

Checked against the installed Mac v1.0 folder (`reverse-engineering/mac/original-images/dqok/v1.0-preinstalled-PLAYED`;
its art files are factory data). The retail disks hold the same art.

The colour disk (disk 4, `.CTL` files) is the **DOS VGA 256-colour art**:
- the same TLB libraries in a big-endian variant ('GLIB' tag), with the same 32-255 palette ranges and 8-bit RGB;
- pictures stored as plain RLE rows, not interleaved (method codes in the 190s instead of 16-23);
- four encounter pictures compared index by index against DOS v1.1: two are identical and two match in 98-99% of
  pixels;
- 220 of 224 palette entries are equal.

The base set (disks 1-3, `.TLB`) carries a 16-entry EGA-like table. Its pixel layout was not decoded `[?]`.

So the Mac version gives no better art than DOS for Dark Queen.

## Open items
- Gateway: palette precision (4 bits per gun in the data; how the game expands it) and what indices 16-17
  show `[?]`.
- DAX VGA games: the purpose of the n/2 + 4 byte table after each palette, and the wall-block layout `[?]`.
- CD builds (Treasures 1995, Unlimited Adventures CDs, the 1994/1995 Collector's Edition CDs, Forgotten Realms
  Archives) were not compared against the floppy builds `[?]`.
