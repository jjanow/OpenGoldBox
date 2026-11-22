# ---- hand-ranked gap lists (exec'd at the end of render.py) ----
GAP_PLAY = [
    ('O:27c38', 'every spell cast, fight and camp; open: whether bit 7 of a scroll pick is kept (`combat-ai.md`). Read the routine whole, oracle it (scroll and memorised casts, aim abort)'),
    ('O:127fb', 'start of round per creature, marked "not read" in `combat-format.md`; it sets what each creature may do this round'),
    ('O:9938', 'turn order picks; the RNG contract of a round\'s start depends on it (`combat-format.md` "Start of round")'),
    ('O:12f0d', 'one-square move: cost, attacks of opportunity hand-off, terrain effects 0x2B9D1 `[?]` (`combat-map.md`)'),
    ('O:14c0f', 'AI and menu combat cast: spell 0 → the spell list 0x239D6 `[?]` (`spells-effects.md` "Combat cast")'),
    ('O:f91e', 'handler 40: stinking cloud cells of the arena `[?]` (map side)'),
    ('O:10ad2', 'handler 84 charm gaze: its range reads an uninitialised stack word `[?]`'),
    ('O:2a0f1', 'handler 88 lightning breath: built from a DOSBox-X run plus a read; recheck the whole routine'),
    ('O:11134', 'handler 90: Constitution save bonus `[?]` on the spec line'),
    ('O:112e5', 'handler 97: as 90'),
    ('O:282bd', 'spell 12 Enlarge: levels outside 1–6 keep the previous value `[?]`'),
    ('O:27466', 'reading a scroll: the scroll list `[?]` (`item-format.md`)'),
    ('O:27534', 'scroll read helper, never cited: sets DS:6B80 / 6B82 and DS:4843 around 0x27466'),
    ('O:4864', 'temple cure 1: DS:681F = 1 meanwhile `[?]` (what the flag changes in 0x2AF10)'),
    ('O:21aed', 'View\'s menu: which entries it offers per caller (`combat-format.md`)'),
    ('O:22f8c', 'shop Id / Sell box `[?]` (`combat-menu.md`)'),
    ('O:23819', 'party member selection (Home / End, camp and party menu): wrapping `[?]`, "not read" in `view3d.md`'),
    ('O:202bb', 'NEWECL helper, never cited: disk number check, DS:6AAD / 6AAE, a menu and the save routine; may decide what a script change does on a disk swap'),
    ('O:24331', 'camp time helper: C# only (CampTime), no spec line'),
    ('O:1e294', 'roster / save file-error report, never cited (error path only)'),
    ('O:b4', 'start-up: sets DS:4414 / DS:4416, never cited'),
    ('O:4a', 'no direct caller (unit 0x16): a disk prompt, then the game save; find how it is reached'),
    ('O:18668', 'Memorize / Scribe page buffer allocation (memory only; likely nothing to port)'),
    ('O:1638e', 'Alt-X debugging switch (needs the start word; no effect in normal play)'),
    ('S:6', 'main program: the order is in `start-menu.md` §1 and Program.cs, cited only inside'),
]
GAP_PRES = [
    ('S:77f8', 'text window: typing and paging; open `[?]` on the 255-character case (PLAN "Next" (1))'),
    ('O:2d669', 'list window and its never-cited helpers 0x2C936, 0x2CFE0, 0x2D073, 0x2D1A6, 0x2D1F2, 0x2D238, 0x2D2D7: the Client rebuilt the pages from DOSBox-X (s45–s46)'),
    ('S:3379', 'screen redraw by case (hall, camp, loot, shop): two `[?]` and three observed-only lines'),
    ('O:2dcf4', '3D / overland redraw: marker and backdrop seen in DOSBox-X; the Amiga marker after a same-area Load is open (PLAN "Next" (2))'),
    ('O:239d6', 'spell list window: four `[?]` lines; the Client draws its own page'),
    ('O:2cc8c', 'menu routine (every horizontal menu): one `[?]` line; its key handling is run by the oracles but its drawing is not compared'),
    ('S:1307', 'party list redraw and its colour helpers 0x148F, 0x1513, the creature panel 0x1583, the icon flash 0x257E, the View text helper 0x1C4D: run under the oracles, drawing compared only in DOSBox-X'),
    ('S:228c', 'combat / status message line: C# cites only inside'),
    ('S:2b8b', 'effect message and pause'),
    ('O:2eef4', '3D view internals 0x2E8F7, 0x2EA6B, 0x2EB08, 0x2EEEA never cited, the piece draw 0x32392 cited only inside: the view is pixel-equal to DOSBox-X from the formats (`view3d.md`), so only a check'),
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
G.append('')
G.append('### Presentation only (drawing, text, sound, input timing)')
G.append('')
G.append('| # | Routine | Role | St | Why it is open |')
G.append('|---|---|---|---|---|')
for i, (k, note) in enumerate(GAP_PRES, 1):
    G.append('| %d | %s | %s | %s | %s |' % (i, addr(k), role(k), ST[k], note))
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
print('play gaps not ranked:', missing)
body = open(W + 'ledger_body.md').read()
i = body.index('## ECL opcode handlers')
doc = body[:i] + '\n'.join(G) + '\n' + body[i:]
open(ROOT + 'docs/re/routine-ledger.md', 'w').write(doc)
print('written', len(doc))
