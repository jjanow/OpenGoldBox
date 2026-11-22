import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

"""Call graph of PoR v1.3 START image + GAME.OVR units. Output: JSON of routines and edges."""
import sys, struct, json
sys.path.insert(0, _ROOT + 'tools/scripts')
import por_emu
from capstone import Cs, CS_ARCH_X86, CS_MODE_16

G = _GAME
S, cs0, ip0 = por_emu.load_image(open(G + 'START.EXE', 'rb').read())
O = open(G + 'GAME.OVR', 'rb').read()
DSEG = por_emu.data_segment(S, cs0, ip0)
md = Cs(CS_ARCH_X86, CS_MODE_16)
stubs = {}
for p in range(len(S) // 16):
    o = p * 16
    if S[o:o + 4] == b'\xcd\x3f\x00\x00':
        fo, size, fix, n = struct.unpack_from('<IHHH', S, o + 4)
        stubs[p] = (fo, size, n)
units = {p: (fo, size) for p, (fo, size, n) in stubs.items()}
stubpara = set()
for p, (fo, size, n) in stubs.items():
    for k in range((0x20 + 5 * n + 15) // 16):
        stubpara.add(p + k)


def stub_target(seg, off):
    fo, size, n = stubs[seg]
    if S[seg * 16 + off:seg * 16 + off + 2] != b'\xcd\x3f':
        return None
    return fo + struct.unpack_from('<H', S, seg * 16 + off + 2)[0]


def unit_of(fo):
    for p, (ufo, size) in units.items():
        if ufo <= fo < ufo + size:
            return p, ufo, size
    return None


def far_target(seg, off):
    if seg in stubs:
        t = stub_target(seg, off)
        return ('O', t) if t is not None else None
    lin = seg * 16 + off
    if lin < DSEG * 16:
        return ('S', lin)
    return None


routines = {}
pend = []
cov = {'S': {}, 'O': {}}


def explore(space, entry, seg):
    key = (space, entry)
    if key in routines:
        return
    if space == 'S':
        base = seg * 16
        lo, hi = base, min(base + 0x10000, DSEG * 16)
        m = S
    else:
        u = unit_of(entry)
        if not u:
            return
        p, ufo, size = u
        base, lo, hi = ufo, ufo, ufo + size
        m = O
    r = routines[key] = {'seg': seg, 'insns': set(), 'calls': set(), 'ints': set(), 'ret': None, 'icalls': [],
                         'ijmps': [], 'unk': []}
    work = [entry]
    while work:
        a = work.pop()
        while lo <= a < hi:
            if space == 'S' and (a >> 4) in stubpara:
                break
            if a in r['insns']:
                break
            insn = next(md.disasm(m[a:a + 16], a - base), None)
            if insn is None:
                break
            r['insns'].add(a)
            mn, op, b = insn.mnemonic, insn.op_str, m[a]
            nxt = a + insn.size
            if mn in ('call', 'lcall'):
                if b == 0xE8:
                    t = base + int(op, 16)
                    r['calls'].add((space, t))
                    pend.append((space, t, seg))
                elif b == 0x9A:
                    off, sg = struct.unpack_from('<HH', m, a + 1)
                    ft = far_target(sg, off)
                    if ft:
                        r['calls'].add(ft)
                        pend.append((ft[0], ft[1], sg if ft[0] == 'S' else None))
                    else:
                        r['unk'].append(a)
                else:
                    r['icalls'].append(a)
            elif mn in ('ret', 'retf', 'iret'):
                r['ret'] = mn
                break
            elif mn in ('jmp', 'ljmp'):
                if b in (0xE9, 0xEB):
                    work.append(base + int(op, 16))
                elif b == 0xEA:
                    off, sg = struct.unpack_from('<HH', m, a + 1)
                    ft = far_target(sg, off)
                    if ft:
                        r['calls'].add(ft)
                        pend.append((ft[0], ft[1], sg if ft[0] == 'S' else None))
                else:
                    r['ijmps'].append(a)
                break
            elif mn.startswith('j') or mn.startswith('loop'):
                try:
                    work.append(base + int(op, 16))
                except ValueError:
                    pass
            elif mn == 'int':
                r['ints'].add(op)
            elif mn == 'hlt':
                break
            a = nxt


def run():
    while pend:
        sp, a, sg = pend.pop()
        explore(sp, a, sg)


pend.append(('S', cs0 * 16 + ip0, cs0))
run()
pend.append(('S', 0x9ECA, 0x8AE))  # timer ISR (installed through INT 21h 25h)
run()
for p, (fo, size, n) in stubs.items():
    for k in range(n):
        t = stub_target(p, 0x20 + 5 * k)
        if t is not None:
            pend.append(('O', t, None))
run()
reach = set(routines)


def covered_set(space):
    s = set()
    for k, r in routines.items():
        if k[0] == space:
            s |= r['insns']
    return s


segs = sorted({r['seg'] for k, r in routines.items() if k[0] == 'S'})
cs_ = covered_set('S')
swept = set()
for i, sg in enumerate(segs):
    lo = sg * 16
    hi = segs[i + 1] * 16 if i + 1 < len(segs) else DSEG * 16
    for a in range(lo, hi - 3):
        if S[a:a + 3] in (b'\x55\x89\xe5', b'\x55\x8b\xec') and a not in cs_ and ('S', a) not in routines:
            pend.append(('S', a, sg)); run(); swept.add(('S', a)); cs_ = covered_set('S')
co = covered_set('O')
for p, (ufo, size) in units.items():
    for a in range(ufo, ufo + size - 3):
        if O[a:a + 3] == b'\x55\x89\xe5' and a not in co and ('O', a) not in routines:
            pend.append(('O', a, None)); run(); swept.add(('O', a)); co = covered_set('O')
out = []
for k, r in routines.items():
    ins = r['insns'] or {k[1]}
    out.append({'space': k[0], 'addr': k[1], 'seg': r['seg'], 'n': len(r['insns']), 'lo': min(ins), 'hi': max(ins),
                'calls': sorted([list(c) for c in r['calls']]), 'ints': sorted(r['ints']), 'ret': r['ret'],
                'icalls': r['icalls'], 'ijmps': r['ijmps'], 'unk': r['unk'], 'main': k in reach,
                'swept': k in swept})
json.dump({'routines': out, 'dseg': DSEG, 'entry': cs0 * 16 + ip0,
           'units': {str(p): [fo, size] for p, (fo, size) in units.items()}, 'segs': segs},
          open(_WORK + 'cg.json', 'w'))
json.dump({'%s:%x' % k: sorted(r['insns']) for k, r in routines.items()}, open(_WORK + 'insns.json', 'w'))
print(len(out), sum(1 for o in out if o['space'] == 'S'), sum(1 for o in out if o['space'] == 'O'),
      'reach', len(reach), 'swept', len(swept))
print('segs', [hex(s) for s in segs])
