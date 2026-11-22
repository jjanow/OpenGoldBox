import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

import json, collections
d = json.load(open(_WORK + 'cg.json'))
c = json.load(open(_WORK + 'cites.json'))
R = d['routines']
bys = collections.defaultdict(list)
for r in R:
    if r['space'] == 'S':
        bys[r['seg']].append(r)
for sg in sorted(bys):
    rs = bys[sg]
    cited = [r for r in rs if 'S:%x' % r['addr'] in c]
    print('seg %x: %d routines, %d cited, range %x-%x' % (sg, len(rs), len(cited), min(r['lo'] for r in rs),
                                                         max(r['hi'] for r in rs)))
print('uncited O:')
for r in sorted(R, key=lambda r: r['addr']):
    if r['space'] == 'O' and 'O:%x' % r['addr'] not in c:
        print(' %x n=%d main=%s ints=%s calls=%s' % (r['addr'], r['n'], r['main'], r['ints'],
                                                     ' '.join('%s%x' % (a, b) for a, b in r['calls'])[:80]))
