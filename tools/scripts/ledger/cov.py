import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

import json, sys
d = json.load(open(_WORK + 'cg.json'))
R = d['routines']
spans = sorted((r['lo'], r['hi'], r['addr']) for r in R if r['space'] == 'O')
tot = 0
for p, (fo, size) in sorted(d['units'].items(), key=lambda x: x[1][0]):
    inside = [s for s in spans if fo <= s[0] < fo + size]
    cov = set()
    for a, b, _ in inside:
        cov.update(range(a, b + 1))
    gaps = []
    a = fo
    while a < fo + size:
        if a not in cov:
            b = a
            while b < fo + size and b not in cov:
                b += 1
            if b - a > 24:
                gaps.append((hex(a), b - a))
            a = b
        else:
            a += 1
    tot += sum(g[1] for g in gaps)
    print('unit', hex(int(p)), hex(fo), hex(size), len(inside), 'gaps', len(gaps), gaps[:6])
print('total gap bytes', tot)
spans = sorted((r['lo'], r['hi'], r['addr']) for r in R if r['space'] == 'S')
segs = d['segs']
cov = set()
for a, b, _ in spans:
    cov.update(range(a, b + 1))
gaps = []
a = 0
end = d['dseg'] * 16
while a < end:
    if a not in cov:
        b = a
        while b < end and b not in cov:
            b += 1
        if b - a > 24:
            gaps.append((hex(a), b - a))
        a = b
    else:
        a += 1
print('START gaps', gaps)
