# ---- hand-ranked gap lists (exec'd at the end of render.py) ----
GAP_PLAY = [
]
# rows closed in s63 (W now): their notes, shown under "Closed in s63"; prune at the next session's rebuild
GAP_DONE = [
    ('O:2581c', 'done s63: read whole and run under emulation (`por_coinrow_oracle.py`, `CoinRowProgramTests`); the Client takes the kind back from the row (`CombatSheet.PickedCoin`)'),
    ('O:32270', 'read whole s63 (view3d.md "The tile-block loader 0x32270"): slot guard 0–4, 8X8D<disk>, fatal when missing; `PorViewArt`'),
]
GAP_PRES = [
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
if not GAP_PRES:
    G[-2:] = ['None since s63: every presentation row is W or N.']
G.append('')
G.append('### Closed in s63')
G.append('')
G.append('Rows that left the ranked lists this session (their status stays in the unit tables). Earlier lanes\' reads are '
         'in `s52-lane-A.md` … `s52-lane-F.md`, `s60-lane-T.md`, `-V`, `-A`, `-C`, `-M` and under "Statuses set by hand".')
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
