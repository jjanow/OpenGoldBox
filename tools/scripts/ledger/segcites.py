import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

import json, re, sys
ROOT = _ROOT
d = json.load(open(_WORK + 'cg.json'))
c = json.load(open(_WORK + 'cites.json'))
want = [int(x, 16) for x in sys.argv[1:]]
for r in sorted(d['routines'], key=lambda r: r['addr']):
    if r['space'] != 'S' or r['seg'] not in want:
        continue
    k = 'S:%x' % r['addr']
    cc = c.get(k)
    line = ''
    if cc:
        f, ln = cc['lines'][0].rsplit(':', 1)
        txt = open(ROOT + f).read().split('\n')[int(ln) - 1]
        i = txt.upper().find('%X' % r['addr'])
        line = txt[max(0, i - 60):i + 100].strip()
    print('%x n=%d ints=%s calls=%d %s' % (r['addr'], r['n'], ','.join(r['ints']), len(r['calls']), line))
