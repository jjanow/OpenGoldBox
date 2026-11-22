import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

"""Collect program-address citations from docs/*.md, docs/re/*.md and src/**/*.cs (comments); map them to routines.

Rules:
- "START 0xN" / "START.EXE 0xN" (and numbers joined to it by , / – and or then → ..) = START linear address.
- "SSSS:OOOO" with SSSS a START code segment = START seg*16+off; with SSSS an overlay stub = GAME.OVR via the stub.
- Other 0xNNNN (4-5 digits, or 3 digits >= 0xC00) = GAME.OVR file offset.
- Skipped: after '+', '$', 'DS:', 'hunk N', 'stub', 'Amiga'.
- A citation counts for a routine when the number is one of the routine's decoded instruction addresses.
"""
import json, re, os, glob, collections, struct, sys
sys.path.insert(0, _ROOT + 'tools/scripts')
import por_emu
ROOT = _ROOT
G = _GAME
S, cs0, ip0 = por_emu.load_image(open(G + 'START.EXE', 'rb').read())
d = json.load(open(_WORK + 'cg.json'))
R = d['routines']
DLIN = d['dseg'] * 16
SEGS = set(d['segs'])
stubs = {}
for p in range(len(S) // 16):
    o = p * 16
    if S[o:o + 4] == b'\xcd\x3f\x00\x00':
        stubs[p] = struct.unpack_from('<I', S, o + 4)[0]
HEX = re.compile(r'0x([0-9A-Fa-f]{3,5})\b')
SEGOFF = re.compile(r'(?<![0-9A-Fa-fx+$])(?:0x)?([0-9A-Fa-f]{2,4}):(?:0x)?([0-9A-Fa-f]{2,4})\b')
SEP = r'(?:\s*(?:[,/;]|–|-|\(|\)|\band\b|\bor\b|\bthen\b|→|\.\.|\s)\s*)*'
RANGE = re.compile(r'0x([0-9A-Fa-f]{4,5})\s*(?:–|\.\.)\s*(?:0x)?([0-9A-Fa-f]{4,5})\b')
START_RUN = re.compile(r'START(?:\.EXE)?(?:\s+image)?\s+((?:0x[0-9A-Fa-f]{3,5}\b' + SEP + r')+)')

insn = {'S': {}, 'O': {}}
entries = {'S': set(), 'O': set()}
for r in R:
    entries[r['space']].add(r['addr'])
RI = json.load(open(_WORK + 'insns.json'))
for k, lst in RI.items():
    sp, a = k.split(':')
    a = int(a, 16)
    io = insn[sp]
    for x in lst:
        if x not in io or abs(io[x] - x) > abs(a - x) and a <= x:
            io[x] = a


def owner(space, a):
    if a in entries[space]:
        return a
    return insn[space].get(a)


def segoff(seg, off):
    if seg in stubs:
        lin = seg * 16 + off
        if S[lin:lin + 2] == b'\xcd\x3f':
            return 'O', stubs[seg] + struct.unpack_from('<H', S, lin + 2)[0]
        return None
    if seg in SEGS:
        return 'S', seg * 16 + off
    return None


cites = collections.defaultdict(lambda: {'docs': collections.Counter(), 'src': collections.Counter(),
                                         'entry_docs': collections.Counter(), 'entry_src': collections.Counter(),
                                         'q_lines': 0, 'seen_lines': 0, 'lines': [], 'elines': []})
files = [(f, 'docs') for f in glob.glob(ROOT + 'docs/re/*.md')] + [(f, 'docs') for f in glob.glob(ROOT + 'docs/*.md')]
files += [(f, 'src') for f in glob.glob(ROOT + 'src/**/*.cs', recursive=True)]
raw = collections.Counter()
s_as_o = collections.Counter()


def add(sp, a, kind, rel, ln, line):
    o = owner(sp, a)
    if o is None:
        return
    raw[(sp, a)] += 1
    c = cites[(sp, o)]
    c[kind][rel] += 1
    if a == o:
        c['entry_' + kind][rel] += 1
        if len(c['elines']) < 40:
            c['elines'].append('%s:%d' % (rel, ln))
        if kind == 'docs':
            if '[?]' in line:
                c['q_lines'] += 1
            if re.search(r'DOSBox|FS-UAE|\bseen\b', line):
                c['seen_lines'] += 1
    if len(c['lines']) < 40:
        c['lines'].append('%s:%d' % (rel, ln))


start_named = collections.defaultdict(set)
start_named_all = set()
for f, kind in files:
    rel = os.path.relpath(f, ROOT)
    for line in open(f, encoding='utf-8', errors='replace'):
        for m in START_RUN.finditer(line):
            for h in HEX.finditer(m.group(1)):
                start_named[rel].add(int(h.group(1), 16))
                start_named_all.add(int(h.group(1), 16))
for f, kind in files:
    rel = os.path.relpath(f, ROOT)
    if '/survey-' in rel or rel.endswith('prior-art.md') or rel.endswith('routine-ledger.md'):
        continue
    last_start = -99
    for ln, line in enumerate(open(f, encoding='utf-8', errors='replace'), 1):
        if 'START' in line:
            last_start = ln
        if kind == 'src':
            i = line.find('//')
            if i < 0:
                continue
            line = line[i:]
        taken = set()
        for m in SEGOFF.finditer(line):
            pre = line[max(0, m.start() - 12):m.start()]
            if re.search(r'(hunk \d+ |stub |Amiga )$', pre):
                continue
            t = segoff(int(m.group(1), 16), int(m.group(2), 16))
            if t:
                add(t[0], t[1], kind, rel, ln, line)
                taken.update(range(m.start(), m.end()))
        if '0x' not in line:
            continue
        for m in RANGE.finditer(line):
            pre = line[max(0, m.start() - 12):m.start()]
            if pre.endswith('+') or re.search(r'(stub|Amiga|hunk \d+)\s*$', pre):
                continue
            a, b = int(m.group(1), 16), int(m.group(2), 16)
            if not (0 < b - a < 0x2000):
                continue
            sp = 'S' if re.search(r'START(\.EXE)?\s*$', pre) else 'O'
            for e in sorted(entries[sp]):
                if a < e <= b:
                    c = cites[(sp, e)]
                    c[kind][rel] += 1
                    c['entry_' + kind][rel] += 1
                    if len(c['elines']) < 40:
                        c['elines'].append('%s:%d' % (rel, ln))
        starts = set()
        for m in START_RUN.finditer(line):
            for h in HEX.finditer(m.group(1)):
                starts.add(m.start(1) + h.start())
        for h in HEX.finditer(line):
            if h.start() in taken:
                continue
            pre = line[max(0, h.start() - 12):h.start()]
            if pre.endswith('+') or pre.endswith('$') or pre.endswith('DS:'):
                continue
            if re.search(r'(stub|Amiga|hunk \d+)\s*$', pre):
                continue
            a = int(h.group(1), 16)
            if h.start() in starts:
                if a < DLIN and owner('S', a) is not None:
                    add('S', a, kind, rel, ln, line)
                    continue
                # a GAME.OVR number chained after a START one (e.g. "START 0x1713, 0xA4F2"): no START routine holds it
            if len(h.group(1)) < 4 and a < 0xC00 and a not in entries['O']:
                continue
            if a in entries['S'] and a not in entries['O']:
                if 'START' in line or a in start_named_all or ln - last_start <= 15:
                    add('S', a, kind, rel, ln, line)
                    continue
                s_as_o[(a, rel)] += 1
            add('O', a, kind, rel, ln, line)

# oracle-run routines (e.call targets in tools/scripts/por_*_oracle.py; trapped ones are stubbed and not counted)
ORACLE_O = {0x143F3: 'ai', 0x148BE: 'ai', 0xAEAB: 'ai', 0x22AC4: 'ai', 0x22827: 'ai', 0x228D7: 'ai',
            0x19EEB: 'chargen', 0xCDE1: 'combat', 0xD785: 'combat', 0xCE75: 'combat', 0xDFC6: 'combat',
            0x30ADA: 'combat', 0x313F3: 'combat', 0x300CD: 'combat', 0x3045B: 'combat', 0x14E38: 'combat',
            0x1D1AC: 'icon', 0x2AA87: 'level', 0x1BA1E: 'modify', 0x1D932: 'modify', 0x1DA0B: 'modify',
            0x29C5B: 'restore', 0x11DFD: 'spell', 0x2A42B: 'spell', 0x2AEEA: 'spell', 0x2AF10: 'spell',
            0x250D5: 'trade', 0x2276E: 'trade', 0x6F0D: 'trade', 0x1C32D: 'train',
            0x23819: 'venue', 0x4864: 'venue', 0x22F8C: 'venue',
            # s49 oracles: por_round_oracle.py, por_handler_oracle.py, por_spellcast_oracle.py, por_cast_oracle.py
            0x127FB: 'round', 0x9938: 'round', 0x12F0D: 'round', 0x2B9D1: 'round',
            0x2BC4D: 'handler', 0x27C38: 'spellcast', 0x275B4: 'spellcast', 0x14C0F: 'cast', 0x24331: 'cast',
            # s51: por_altx_oracle.py
            0x1638E: 'altx',
            # s51: por_items_oracle.py
            0x21F32: 'items'}
ORACLE_S = {0x9E5C: 'sound', 0x9ECA: 'sound', 0xA222: 'sound', 0xA28E: 'sound', 0x2FD3: 'combat', 0x30FB: 'combat', 0x3AF7: 'level', 0x1758: 'trade'}
disp = json.load(open(_WORK + 'disp.json'))
for sid in [35, 36, 37, 39, 41, 42, 43, 44, 46, 47, 48, 51, 55, 56, 57, 58, 59, 60, 62, 64, 65, 67, 34]:
    ORACLE_O[int(disp['spells'][str(sid)][2:], 16)] = 'spell'
for hid in [81, 82, 83, 85, 86]:
    ORACLE_O[int(disp['handlers'][str(hid)][2:], 16)] = 'spell'
for hid in [40, 84, 88, 90, 97]:  # por_handler_oracle.py (90 / 97 inside the saving throw 0x2BC4D)
    ORACLE_O.setdefault(int(disp['handlers'][str(hid)][2:], 16), 'handler')
oracle = {}
for a, n in ORACLE_O.items():
    oracle['O:%x' % a] = n
for a, n in ORACLE_S.items():
    oracle['S:%x' % a] = n

out = {'%s:%x' % k: {kk: (dict(v) if isinstance(v, collections.Counter) else v) for kk, v in c.items()}
       for k, c in cites.items()}
json.dump({'cites': out, 'oracle': oracle, 'raw': {'S': sum(1 for k in raw if k[0] == 'S'), 'O': sum(1 for k in raw if k[0] == 'O')}},
          open(_WORK + 'cites.json', 'w'), indent=0)
print('distinct raw addrs S', sum(1 for k in raw if k[0] == 'S'), 'O', sum(1 for k in raw if k[0] == 'O'))
print('routines cited S', sum(1 for k in cites if k[0] == 'S'), 'O', sum(1 for k in cites if k[0] == 'O'))
print('routines entry-cited S', sum(1 for k, c in cites.items() if k[0] == 'S' and (c['entry_docs'] or c['entry_src'])),
      'O', sum(1 for k, c in cites.items() if k[0] == 'O' and (c['entry_docs'] or c['entry_src'])))
print('START entries cited as bare hex (counted as O):', len(s_as_o))
for (a, rel), n in sorted(s_as_o.items())[:60]:
    print('  %x %s %d' % (a, rel, n))
for k in oracle:
    sp, a = k.split(':')
    if int(a, 16) not in entries[sp]:
        print('oracle addr not a routine entry', k)
