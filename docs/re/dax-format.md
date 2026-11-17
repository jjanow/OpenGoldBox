# DAX container format (DOS Gold Box)

Status: **verified** against every DAX file of Pool of Radiance DOS v1.1, v1.2 and v1.3, and the
Curse of the Azure Bonds DAX files shipped in the coab repo (2026-11-16, prior-art review).
Amiga DAX is a different container and is not covered here (see the end).

## Sources

- Stephen S. Lee, *Pool of Radiance – Exhaustive Game Information* v2.00 (2026-04-02), section 12.2.1
  "DAX File Format". Written for PoR DOS v1.3. CC BY-NC 4.0.
  https://gamefaqs.gamespot.com/pc/564785-pool-of-radiance/faqs/73869
  (GameFAQs refuses bots; archived copy: http://web.archive.org/web/20260417124344/https://gamefaqs.gamespot.com/pc/564785-pool-of-radiance/faqs/73869)
- Simeon Pilgrim, coab, `Classes/DaxFiles/DaxFileCache.cs` (loader + `Decode`).
  https://github.com/simeonpilgrim/coab
- Bil Simser et al., Gold Box Explorer, `src/Common/Plugins/Dax/DaxFile.cs`.
  https://github.com/bsimser/Gold-Box-Explorer
- wish-goldbox docs describe the same DOS index (`id:u8 offset:u32 raw:u16 compressed:u16`) and a separate
  Amiga reader: https://wish-goldbox.readthedocs.io/en/latest/api/generated/goldbox.amiga_dax.html

All three agree on the layout below. Our own check is described in "Verification".

## Layout

All integers little-endian.

```
offset 0      u16   H = length in bytes of the entry table that follows (always a multiple of 9)
offset 2      H/9 entries of 9 bytes each:
                +0  u8   id          block number, unique within this file (not sorted; not contiguous)
                +1  u32  offset      start of the block's packed data, relative to the data area
                +5  u16  raw_size    size after decoding
                +7  u16  comp_size   size of the packed data in this file
offset 2+H    data area: packed blocks
```

- Block `i` packed data = `file[2 + H + offset_i .. 2 + H + offset_i + comp_size_i]`.
- In every PoR file checked, the blocks are stored back to back in table order (`offset_0 = 0`,
  `offset_{i+1} = offset_i + comp_size_i`) and the last block ends exactly at end of file. A reader should still
  use the offsets, not assume this.
- `raw_size` must be read **unsigned**. PoR `SQRPACI.DAX` block 1 has raw_size 36881 (> 32767), comp_size 35755.
  coab reads it with `ReadInt16` (would go negative; CotAB's largest block is 32017, so coab never hit it);
  Gold Box Explorer reads `ReadUInt16` (correct).
- Lookup is by `id`, never by table position. The game opens a DAX by name and asks for a block id
  (e.g. ECL/GEO block id = map/script number; HEAD/BODY/PIC id = picture number).

## Compression (run-length)

Applied per block, to exactly `comp_size` input bytes, producing exactly `raw_size` bytes.

```
while input remains:
    c = next byte, read as signed 8-bit
    if c >= 0:  copy the next c+1 bytes literally            (consumes 1 + c+1 bytes)
    else:       write the next byte (-c) times               (consumes 2 bytes)
```

- Lee notes that control bytes 0x7F and 0x80 make the original decoder loop forever ("DAX file contents are
  expected to be well-behaved"). They never occur in PoR (0 of 485,996 control bytes in v1.2 + v1.3).
  Our decoder should treat them as an error, not guess.
- No block in PoR has `raw_size == 0`. Gold Box Explorer treats `raw_size <= 0` as "stored, not compressed";
  whether any game uses that is `[?]`.

## Verification (2026-11-16)

Script: decode every entry, require `decoded length == raw_size` and `bytes consumed == comp_size`, and check for
trailing bytes. Source images: `reverse-engineering/dos/original-images/por/` (files unpacked to a scratch dir with
`mcopy` + `unar`/`7z`; masters untouched).

| Set | DAX files | Blocks | Failures |
|---|---|---|---|
| PoR v1.1 (3.5" 720K, ARC archives) | 89 | 1044 | 0 |
| PoR v1.2 (3.5" 720K, ARC archives) | 89 | 1044 | 0 |
| PoR v1.3 (WizardWorks 1.44M, ARJ) | 113 | 1245 | 0 |
| CotAB (coab repo `Data/*.DAX`) | 86 | 824 | 0 |

Notes from the check:
- PoR v1.1/v1.2 ship the main-view pictures as **loose, unpacked files** (`HEAD*`, `BODY*`, `PIC*.PIC`, one picture
  per file, no DAX header). v1.3 packs them into `HEAD?.DAX`, `BODY?.DAX`, `PIC?.DAX` like the later games.
  The importer must handle both.
- The digit in a PoR DAX name is the original floppy "disk" number (GBC's ECL_Tool.txt says the same): an ECL
  script can only use monsters/items/art from DAX files with its own number.
- The block contents are covered (briefly) in `docs/re/prior-art.md` section 3; they are not part of this
  container spec.

## Block payloads seen in PoR (sizes only; formats live in their own specs)

| File family | Block size(s) | Note |
|---|---|---|
| `GEO?.DAX` | 1026 | u16 0x0400 + 4 × 256-byte planes (16×16 map) |
| `ECL?.DAX` | 2171–7679 | 2-byte prefix `[?]`, then script loaded at VM address $9900 |
| `WALLDEF?.DAX` | 780 / 1560 / 2340 | multiples of 780 = 5 walls × 156 bytes |
| `MON?CHA.DAX` | 285 | same 285-byte record as `CHRDATA*.SAV` |
| `MON?ITM.DAX`, `ITEM?.DAX` | n × 63 | 63-byte item records |
| `MON?SPC.DAX` | n × 9 | 9-byte effect records |
| `8X8D?.DAX`, `CPIC`, `CHEAD`, `CBODY`, `COMSPR`, `TITLE`, `ICON`, `SQRPACI`, `BACPAC`, `DUNGCOM`… | varies | EGA bitmap blocks (prior-art.md 3.4) |
| `SPRIT?.DAX`, `PIC?.DAX`/`PIC*.PIC` | varies | multi-frame animation blocks (prior-art.md 3.4) |

## Amiga

The Amiga ports use a different DAX container (and keep all ECL scripts in one `ecl.dax` on disk 2) per the
wish-goldbox docs (GPL-3.0 code; reference only). Not verified by us `[?]`.
