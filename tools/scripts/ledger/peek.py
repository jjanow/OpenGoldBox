import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

"""peek.py KEY... : compact view of a routine: DS globals read/written, calls, immediates compared."""
import sys, json, re
sys.path.insert(0, _ROOT + 'tools/scripts')
import por_emu
from capstone import Cs, CS_ARCH_X86, CS_MODE_16
G = _GAME
S, cs0, ip0 = por_emu.load_image(open(G + 'START.EXE', 'rb').read())
O = open(G + 'GAME.OVR', 'rb').read()
ins = json.load(open(_WORK + 'insns.json'))
rows = {r['key']: r for r in json.load(open(_WORK + 'rows.json'))}
md = Cs(CS_ARCH_X86, CS_MODE_16)
for k in sys.argv[1:]:
    sp = k[0]
    m = S if sp == 'S' else O
    r = rows[k]
    rd, wr, cmp_, calls = set(), set(), [], []
    for a in ins[k]:
        i = next(md.disasm(m[a:a + 16], a))
        t = i.op_str
        g = re.findall(r'(?:byte|word|dword) ptr \[(0x[0-9a-f]+)\]', t)
        if g:
            if i.mnemonic in ('mov', 'inc', 'dec', 'add', 'sub', 'or', 'and', 'xor', 'pop') and t.split(',')[0].find('[0x') >= 0:
                wr.update(g)
            else:
                rd.update(g)
        if i.mnemonic == 'cmp' and re.search(r', (0x[0-9a-f]+|\d+)$', t):
            cmp_.append(t.split(', ')[-1])
    print(k, 'n', r['n'], 'role', r['role'][:60])
    print('  reads', sorted(rd)[:20])
    print('  writes', sorted(wr)[:20])
    print('  cmps', cmp_[:20])
    print('  callers', r['callers'][:6])
    print('  callees', ['%s(%s)' % (c, rows[c]['status']) for c in r['callees']][:14])
