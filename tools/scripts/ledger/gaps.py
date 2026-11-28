# ---- hand-ranked gap lists (exec'd at the end of render.py) ----
GAP_PLAY = [
]
# rows closed in s60 (W or N now): their notes, shown under "Closed in s60"; prune at the next session's rebuild
GAP_DONE = [
    ('O:2d669', 'done s60 lane T: run whole with its helpers and the menu routine under emulation (`por_listwin_oracle.py`, 1468 cases); digit-row keys fixed'),
    ('S:3379', 'read whole s60 lane T (the open `[?]` is about indirect callers)'),
    ('O:239d6', 'read whole s60 lane T'),
    ('O:2cc8c', 'key layer under the list oracle, drawing read whole s60 lane T; `[?]` settled ($4AFF)'),
    ('S:1307', 'read whole s60 lane T with its helpers; seen in DOSBox-X'),
    ('S:2b8b', 'read whole s60 lane T'),
    ('O:2e8f7', 'read s60 lane V (it is the area map draw); internals read; seen 0 px'),
    ('S:4751', 'segment 0464 read branch by branch s60 lane V'),
    ('O:1e468', 'icon builders read whole s60 lane V; head ORed into the body fixed; icon editor 0 px in DOSBox-X'),
    ('O:32123', 's60 lane T: a debug display (START STING + Ctrl-D), not ported by design'),
    ('O:2e0ce', 'read whole s60 lane V; the sprite renderer\'s nil frame fixed'),
    ('S:8c85', 'game-side sound calls read s60 lane V (= sound.md §2.1)'),
    ('S:4354', 'read whole s60 lane V (five POOL.CFG lines)'),
    ('S:7d3b', 's60 lane T: machine-level text helpers, not ported by design'),
]
GAP_PRES = [
    ('O:2581c', 'number / money text builder: the C# names only inside addresses'),
    ('O:32270', 'picture block load with disk check (wall sets, start-up): spec only; the module replaces the disk check'),
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
G.append('### Closed in s60')
G.append('')
G.append('Rows that left the ranked lists this session, with the lane notes (their status stays in the unit tables). '
         'The lanes\' reads are in `s52-lane-A.md` … `s52-lane-F.md`, `s60-lane-T.md`, `-V`, `-A`, `-C`, `-M` '
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
