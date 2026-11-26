# ---- hand-ranked gap lists (exec'd at the end of render.py) ----
GAP_PLAY = [
]
# rows closed in s52 (W now): their hand notes, shown under "Closed in s52"; prune at the next session's rebuild
GAP_DONE = [
    ('O:6184', 'done s52 lane A: an empty list ends at once and the screen is redrawn (DOSBox-X); run under emulation inside 0x62B6 (`por_venue_oracle.py` lootTake)'),
    ('O:2eef4', 'read whole s52 lane F: the view pixel-equal to DOSBox-X from the formats (`view3d.md`); its internals stay in the presentation list'),
]
GAP_PRES = [
    ('O:2d669', 'list window and its never-cited helpers 0x2C936, 0x2CFE0, 0x2D073, 0x2D1A6, 0x2D1F2, 0x2D238, 0x2D2D7: the Client rebuilt the pages from DOSBox-X (s45–s46)'),
    ('S:3379', 'screen redraw by case (hall, camp, loot, shop): two `[?]` and three observed-only lines'),
    ('O:239d6', 'spell list window: four `[?]` lines; the Client draws its own page'),
    ('O:2cc8c', 'menu routine (every horizontal menu): one `[?]` line; its key handling is run by the oracles but its drawing is not compared'),
    ('S:1307', 'party list redraw and its colour helpers 0x148F, 0x1513, the creature panel 0x1583, the icon flash 0x257E, the View text helper 0x1C4D: run under the oracles, drawing compared only in DOSBox-X'),
    ('S:2b8b', 'effect message and pause'),
    ('O:2e8f7', '3D view internals 0x2E8F7, 0x2EA6B, 0x2EB08, 0x2EEEA never cited, the piece draw 0x32392 cited only inside (0x2EEF4 itself read whole s52): the view is pixel-equal to DOSBox-X from the formats (`view3d.md`), so only a check'),
    ('O:20d50', 'after a load or script switch, never cited: overland / picture set-up (calls 0x2DE38 and the picture draw)'),
    ('S:4751', 'pictures, START segment 0464 (0x4640–0x6C72): read, decode, draw, recolour, free; the importer decodes the formats and the Client draws them (`por-art-formats.md`); only some draw entries are cited'),
    ('O:1e468', 'combat icon slots and their builders 0x1E315, 0x1CE5E, 0x1CF3D (icon editor preview, recolour)'),
    ('O:32123', 'text page with a key prompt on the level-drain path, never cited (trapped by the spell oracle)'),
    ('O:2e0ce', 'picture frame draw / free (0x2E0CE, 0x2E55D) and the sprite renderer 0x2E7D6: spec lines only'),
    ('S:8c85', 'sound API (cue 0x8C85, device set-up 0x8C5B, driver start 0x8BBC and helpers): the driver itself is oracle-checked, the game-side calls are spec only'),
    ('S:4354', 'start-up configuration (video, sound, installer lines), read in part'),
    ('S:7d3b', 'text helpers 0x7090, 0x7101, 0x7130, 0x7147, 0x720C, 0x7D3B never cited'),
]
G = []
G.append('## Gaps on the title-to-ending path')
G.append('')
G.append('Ranked by how much of play they can change (rules, state, branching), then by size and callers. Each line '
         'is a candidate [play] item: read the routine whole, port every branch, add an oracle check where the '
         '`por_*_oracle.py` pattern fits, then confirm in DOSBox-X.')
G.append('')
G.append('### Play (rules, state, branching)')
G.append('')
G.append('| # | Routine | Role | St | Why it is open |')
G.append('|---|---|---|---|---|')
for i, (k, note) in enumerate(GAP_PLAY, 1):
    G.append('| %d | %s | %s | %s | %s |' % (i, addr(k), role(k), ST[k], note))
if not GAP_PLAY:
    G[-2:] = ['None since s52: every play row is W or N (the W-by-citation rows are listed in the unit tables).']
G.append('')
G.append('### Presentation only (drawing, text, sound, input timing)')
G.append('')
G.append('| # | Routine | Role | St | Why it is open |')
G.append('|---|---|---|---|---|')
for i, (k, note) in enumerate(GAP_PRES, 1):
    G.append('| %d | %s | %s | %s | %s |' % (i, addr(k), role(k), ST[k], note))
G.append('')
G.append('### Closed in s52')
G.append('')
G.append('Rows that left the ranked lists this session, with the lane notes (their status stays in the unit tables). '
         'The s52 lanes\' branch-by-branch reads of the W-by-citation rows are in `s52-lane-A.md` … `s52-lane-F.md` '
         'and under "Statuses set by hand".')
G.append('')
G.append('| Routine | Role | St | Note |')
G.append('|---|---|---|---|')
for k, note in GAP_DONE:
    G.append('| %s | %s | %s | %s |' % (addr(k), role(k), ST[k], note))
G.append('')
G.append('Not gaps: the N rows (DAX reader START segment 0802, disk numbers, memory release, the code wheel) and the '
         'library segments, which the module format and .NET replace.')
G.append('')
G.append('### Statuses set by hand')
G.append('')
G.append('| Routine | St | Why |')
G.append('|---|---|---|')
for k in sorted((k for k in OV if OV[k].get('status') and k in rows), key=lambda k: (k[0], rows[k]['addr'])):
    G.append('| %s | %s | %s |' % (addr(k), OV[k]['status'], OV[k].get('why', '')))
G.append('')
missing = [k for k in rows if ST[k] in 'PU' and KIND[k] == 'play' and k not in dict(GAP_PLAY)]
print('play gaps not ranked:', [addr(k) for k in missing])
print('pres gaps not ranked:', [addr(k) for k in rows if ST[k] in 'PU' and KIND[k] == 'pres' and k not in dict(GAP_PRES)])
print('ranked but not P / U:', [addr(k) for k, _ in GAP_PLAY + GAP_PRES if ST[k] not in 'PU'])
print('closed but still P / U:', [addr(k) for k, _ in GAP_DONE if ST[k] in 'PU'])
body = open(W + 'ledger_body.md').read()
i = body.index('## ECL opcode handlers')
doc = body[:i] + '\n'.join(G) + '\n' + body[i:]
open(ROOT + 'docs/re/routine-ledger.md', 'w').write(doc)
print('written', len(doc))
