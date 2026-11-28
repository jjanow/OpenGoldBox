import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

"""Render docs/re/routine-ledger.md from rows.json + overrides."""
import json, re, os, sys, collections, math
sys.path.insert(0, _WORK + 'tools')
from overrides import OV
ROOT = _ROOT
W = _WORK
rows = {r['key']: r for r in json.load(open(W + 'rows.json'))}
cg = json.load(open(W + 'cg.json'))
_cj = json.load(open(W + 'cites.json'))
cites, RAW = _cj['cites'], _cj.get('raw', {})

LIB = {0xaf8: 'Turbo Pascal System unit: heap (GetMem / FreeMem), strings, sets, longint and real arithmetic, file I/O, Halt',
       0xaa2: 'Turbo Pascal overlay manager (INT 3Fh handler, overlay buffer, reads from GAME.OVR)',
       0xa40: 'Crt-style unit: BIOS keyboard (KeyPressed / ReadKey), Delay, video mode, cursor',
       0xa32: 'DOS unit helpers (INT 21h)', 0xa31: 'unit initialisation', 0x3da: 'unit initialisation',
       0x3ed: 'unit initialisation'}
SEGNAME = {0x0: 'main program', 0xba: 'game core helpers (stats, messages, distance, lists, screen redraws)',
           0x3f1: 'windows and frames', 0x42b: 'start-up configuration', 0x464: 'pictures: read, decode, draw',
           0x709: 'text, input, time', 0x802: 'DAX archive reader (open, index, block read, unpack)',
           0x899: 'disk number, keys while busy, sound stop', 0x8ae: 'sound driver (PC speaker / Tandy, timer interrupt)'}
UNITNAME = {0x8: 'start-up odds and ends', 0xd4: 'introduction', 0x9ac: 'code wheel',
            0xd46: 'ECL opcode handlers, dispatcher, run loop, area loop', 0x4577: 'temple',
            0x557e: 'after combat (share, award, removal, party defeated)', 0x6cd4: 'shop',
            0x74d3: 'ECL VM helpers (operands, VM memory, text), script loader, encounter draw',
            0x9807: 'the fight (top level, clean-up, round loop)', 0xabc0: 'monster AI',
            0xc5d1: 'combat map: build, terrain, placement', 0xe5d3: 'game start-up (VM zones, start values)',
            0xec5b: 'effect handlers', 0x127fb: 'combat rules: round start, attacks, moves, turn undead, AI casts',
            0x166cb: 'explore: doors, steps, commands', 0x17831: 'camp: Memorize, Scribe, preparation time, party order',
            0x1997f: 'party menu, creation, Modify, training hall, icon editor',
            0x1dfe6: 'roster and save files, monster loader', 0x2069d: 'end game',
            0x2103a: 'items: View, Items page, Trade, use, halve / join', 0x23dcc: 'effect timeouts, camp time, rest',
            0x24c89: 'money, treasure and loot', 0x27018: 'spells: cast, spell lists, spell routines 1–67',
            0x2aa87: 'level stats', 0x2aeea: 'effect list: call handler, add / remove effect, dice, heal',
            0x2c8cf: 'menus and lists (menu routine, list window, yes / no)', 0x2db87: '3D view / overland redraw',
            0x2de38: 'overland view', 0x2e0ce: 'pictures and sprites (loader, frames, free)',
            0x2e8f7: '3D view drawing, wall sets', 0x2fb52: 'combat screen: aim, map draw',
            0x3073a: 'combat arena: cells, targets, icons, distance', 0x31a6c: 'tiles and icons (close-ups, area map)',
            0x3211f: 'text page', 0x32235: 'picture load with disk check'}


def addr(k):
    sp, a = k.split(':')
    a = int(a, 16)
    return ('START 0x%04X' % a) if sp == 'S' else ('0x%04X' % a)


lines_cache = {}


def getlines(f):
    if f not in lines_cache:
        lines_cache[f] = open(ROOT + f, encoding='utf-8').read().split('\n')
    return lines_cache[f]


def clean(t):
    t = re.sub(r'"[^"]*"|“[^”]*”|‘[^’]*’', '…', t)
    t = re.sub(r'`[^`]*`', lambda m: m.group(0), t)
    t = re.sub(r'\(?(?:GAME\.OVR |START(?:\.EXE)? )?0x[0-9A-Fa-f]+(?:\s*[–-]\s*0x[0-9A-Fa-f]+)?\)?', '', t)
    t = re.sub(r'\b[0-9A-F]{2,4}:[0-9A-F]{2,4}\b', '', t)
    t = re.sub(r'\*\*|\(\s*\)|—\s*[PDEW](?:,\s*[PDEW])*\b.*$', '', t)
    t = re.sub(r'\s+', ' ', t).strip(' ,:;–-=(')
    return t


def heading_role(k):
    c = cites.get(k)
    if not c:
        return ''
    for ref in c.get('elines', []):
        if not ref.startswith('docs/re/'):
            continue
        f, ln = ref.rsplit(':', 1)
        L = getlines(f)
        line = L[int(ln) - 1]
        m = re.match(r'\s*(?:-\s+|\d+\.\s+)?\*\*([^*]{3,60})\*\*', line)
        if m:
            t = clean(m.group(1))
            if len(t) >= 3:
                return t + ' (' + os.path.basename(f)[:-3] + ')'
        if line.startswith('|'):
            cells = [x.strip() for x in line.strip().strip('|').split('|')]
            sp, a = k.split(':')
            pat = re.compile(r'0x0*%X\b' % int(a, 16), re.I)
            for i, cell in enumerate(cells):
                if pat.search(cell) and i + 1 < len(cells):
                    t = clean(cells[i + 1] if len(clean(cell)) < 4 else cell)
                    if 4 <= len(t) <= 80:
                        return t
        for j in range(int(ln) - 1, -1, -1):
            if L[j].startswith('#'):
                t = clean(L[j].lstrip('#').strip())
                if t:
                    return t + ' (' + os.path.basename(f)[:-3] + ')'
                break
    return ''


ECL_NAMES = {}
for k, r in rows.items():
    if r['ecl']:
        ECL_NAMES[k] = '/'.join(r['ecl'][1])


def role(k):
    r = rows[k]
    o = OV.get(k, {})
    parts = []
    if o.get('role'):
        parts.append(o['role'])
    if r['ecl']:
        parts.insert(0, 'ECL %s handler' % ECL_NAMES[k])
    if r['spells']:
        parts.append('spell routine for spell%s %s' % ('s' if len(r['spells']) > 1 else '', ', '.join(map(str, r['spells']))))
    if r['handlers']:
        parts.append('effect handler%s %s' % ('s' if len(r['handlers']) > 1 else '', ', '.join(map(str, r['handlers']))))
    if r['empty']:
        parts.append('empty routine (unit init or empty handler)')
    if not parts:
        h = heading_role(k)
        if h:
            m = re.match(r'(.*) \(([\w-]+)\)$', h)
            parts.append('see `%s.md` %s' % (m.group(2), m.group(1)) if m else h)
    if not parts:
        cl = [c for c in r['callers'] if c in rows][:2]
        parts.append('[?] not read; called by %s' % ', '.join(addr(c) for c in cl) if cl else '[?] not read; no direct caller')
    t = '; '.join(parts)
    return t.replace('|', '/')


def status(k):
    r = rows[k]
    o = OV.get(k, {})
    if o.get('status'):
        return o['status']
    if r['status'] == 'L':
        return 'L'
    if r['space'] == 'S' and r['seg'] in (0x802,):
        return 'N'
    s = r['status']
    if s == 'W' and kind(k) == 'pres' and not r['empty'] and not (r['space'] == 'S' and r['seg'] == 0x8ae):
        # (the sound driver's oracle compares its port writes, so its callees keep their credit)
        # oracles and golden captures compare state, not pixels: a drawing routine needs its own citations
        e = set(r['ev'])
        if 'oracle' not in e and (e & {'oracle via caller', 'golden'}):
            c = cites.get(k, {})
            if 'C# entry' in e and 'spec entry' in e:
                dl = sum(c.get('entry_docs', {}).values())
                s = 'W' if r['q'] == 0 and r['seen'] < dl else 'P'
            elif 'C# entry' in e or 'C# interior' in e:
                s = 'P'
            else:
                s = 'U'
    return s


def wexec(k):
    r = rows[k]
    e = set(r['ev'])
    if r['empty'] or k in OV and OV[k].get('status') == 'W':
        return r['empty'] or bool(e & {'oracle', 'golden', 'oracle via caller'})
    if 'oracle' in e:
        return True
    return bool(e & {'oracle via caller', 'golden'}) and kind(k) == 'play'


def kind(k):
    o = OV.get(k, {})
    if o.get('kind'):
        return o['kind']
    r = rows[k]
    if r['space'] == 'S' and r['seg'] in (0x3f1, 0x464, 0x709, 0x8ae, 0x802):
        return 'pres'
    if r['area'] == 'present':
        return 'pres'
    return 'play'


EVSHORT = {'oracle': 'oracle', 'oracle via caller': 'oracle (caller)', 'golden': 'golden', 'C# entry': 'C#',
           'C# interior': 'C# (inside)', 'spec entry': 'spec', 'spec interior': 'spec (inside)'}


def ev(k):
    # OV 'ev' replaces the cell (hand-written evidence prose), 'ev_add' appends to the generated one
    r = rows[k]
    o = OV.get(k, {})
    if o.get('ev'):
        return o['ev']
    e = [EVSHORT[x] for x in r['ev']]
    if r['q']:
        e.append('`[?]`×%d' % r['q'])
    if r['seen'] and 'oracle' not in r['ev'] and r['seen'] >= max(1, sum(cites.get(k, {}).get('entry_docs', {}).values())):
        e.append('seen only')
    if o.get('ev_add'):
        e.append(o['ev_add'])
    return ', '.join(e) or '—'


def port(k):
    # OV 'port' replaces the cell (members a lane named by hand), 'port_add' appends a member / note
    r = rows[k]
    o = OV.get(k, {})
    if o.get('port'):
        return o['port']
    ms = [m.replace('struct.', '').replace('record.', '') for m in r['members'] if not m.startswith('Msg')][:3]
    if not ms:
        ms = r['cs'][:2]
    if o.get('port_add'):
        ms = [m for m in ms if m != o['port_add'].split(' ')[0]] + [o['port_add']]
    return ', '.join(ms) or '—'


ST = {k: status(k) for k in rows}
KIND = {k: kind(k) for k in rows}

# ---- per-path closures ----
PATHS = [
    ('Start-up, title, introduction', ['S:6', 'S:4354', 'O:6fb', 'O:aa9', 'O:2ea4c', 'O:e600']),
    ('Party menu, creation, Modify, icons, roster', ['O:19ae0', 'O:19eeb', 'O:1ba1e', 'O:1d1ac', 'O:1e684', 'O:1ead2']),
    ('Area loop, 3D view, area map, overland, doors', ['O:403e', 'O:17054', 'O:17438', 'O:2dcf4', 'O:2eef4', 'O:2de38']),
    ('ECL VM and host opcodes', ['O:3c73', 'O:3eb7', 'O:7689', 'O:7814']),
    ('Encounters (monsters, close-up, menu, parley, surprise)', ['O:2c3e', 'O:3311', 'O:155d', 'O:240f', 'O:236f', 'O:1196', 'O:10fa', 'O:1eeaa', 'O:7a21']),
    ('Combat (map, menu, moves, AI, spells, effects, items, after combat)', ['O:24e1', 'O:9881', 'O:6aa5']),
    ('Camp (all menus, rest, Memorize, Scribe, Cast, Alter)', ['O:196a0', 'O:24a66']),
    ('Shops, temples, venues (PROGRAM, CALL)', ['O:7212', 'O:5280', 'O:3b56', 'O:3a15']),
    ('Treasure and loot', ['O:26f6']),
    ('Training and level up', ['O:1c32d', 'O:2aa87']),
    ('Save and load', ['O:1fd3e', 'O:1f5e8']),
    ('End game', ['O:20941', 'O:206a3']),
    ('Party killed / destroyed', ['O:34bb', 'O:5a47']),
]
callees = {k: r['callees'] for k, r in rows.items()}


ALLROOTS = set(r for _, rs in PATHS for r in rs)


def closure(roots, stop_at_other_roots=True):
    seen = set()
    work = list(roots)
    while work:
        k = work.pop()
        if k in seen or k not in rows:
            continue
        if stop_at_other_roots and k in ALLROOTS and k not in roots:
            continue
        seen.add(k)
        if ST[k] in 'LN' and k not in roots:
            continue
        work.extend(callees[k])
    return seen


PCODES = ['Su', 'Pm', 'Ex', 'Ecl', 'Enc', 'Cb', 'Cp', 'Sh', 'Tl', 'Lv', 'Sv', 'End', 'Kd']
INPATH = collections.defaultdict(list)
for code, (name, roots) in zip(PCODES, PATHS):
    for k in closure(roots):
        INPATH[k].append(code)
out = []
P = out.append
tot = collections.Counter(ST[k] for k in rows)
P('# Routine coverage ledger (Pool of Radiance, DOS v1.3)')
P('')
P('Every routine of the v1.3 program (`START.EXE` and `GAME.OVR`) that the game runs from the title to the ending, '
  'with how far the engine follows it. Built in session 47 (2026-11-22) to make the remaining [play] work finite: '
  '**P** and **U** rows are the list of known gaps; a **W** row was either run against the program (oracle, golden '
  'captures) or read and ported per the spec and C# comments, which is not the same as checked branch by branch '
  '(the two counts are split below). Addresses are GAME.OVR '
  'file offsets unless marked START (offsets in the EXEPACK-expanded `START.EXE` image), as in the other `docs/re` '
  'specs; far calls resolve through the overlay stubs (`ecl-format.md` "Program references").')
P('')
P('## Statuses')
P('')
P('| Status | Meaning | Count |')
P('|---|---|---|')
nwx = sum(1 for k in rows if ST[k] == 'W' and wexec(k))
P('| **W** ported whole | the Core / Client code follows the routine | %d |' % tot['W'])
P('| … W by execution | an oracle runs it (or runs its caller, for state routines), the ECL golden captures cover it, or it is empty | %d |' % nwx)
P('| … W by citation | the C# names its entry and the spec reads it with no open `[?]` on those lines: read and ported per the comments, not verified branch by branch | %d |' % (tot['W'] - nwx))
P('| **P** patched by symptom | partly built: the C# names only inside addresses, or the spec lines carry `[?]`, or the behaviour was built from emulator observations | %d |' % tot['P'])
P('| **U** unchecked | no evidence that the engine matches it (not cited, or read in a spec but not ported) | %d |' % tot['U'])
P('| N not ported by design | platform work the module format replaces: DAX reading (the importer), disk numbers, memory release, the code wheel | %d |' % tot['N'])
P('| L library | Turbo Pascal run-time library, overlay manager, Crt and DOS units (one line per segment below) | %d |' % tot['L'])
P('')
P('Total: %d routines (%d START, %d GAME.OVR). W + P + U = %d game routines.' % (
    len(rows), sum(1 for k in rows if k[0] == 'S'), sum(1 for k in rows if k[0] == 'O'), tot['W'] + tot['P'] + tot['U']))
P('')
P('Evidence column: `oracle` = a `tools/scripts/por_*_oracle.py` runs the routine under unicorn and a Core test '
  'compares (`oracle (caller)`: run inside a called routine, not trapped; drawing, text and sound callees excluded '
  'since the oracles compare state, not pixels); `golden` = an ECL handler or VM helper covered by the golden DOSBox '
  'captures EclVm matches; `C#` / `C# (inside)` = a C# comment names the entry / only addresses inside it; `spec` / '
  '`spec (inside)` = the same for `docs/*.md`; `[?]`×n = spec lines on the entry with an open question; `seen only` = '
  'every spec line on the entry is a DOSBox-X / FS-UAE observation. Port = the C# type.member whose comment names '
  'the entry. Kind: **play** = rules, state or branching; **pres** = drawing, text, sound, input timing.')
P('')
P('## How it was built (to rebuild after a session)')
P('')
P('The generator scripts are in `tools/scripts/ledger/` (s47). Set `GOLDBOX_POR_DIR` to a v1.3 copy, `LEDGER_WORK` '
  'to a scratch folder (with a `porgame` link to the copy and a `tools/` copy of the scripts: `render.py` imports '
  '`overrides.py` and runs `gaps.py` from there) and `PYTHONPATH` to `tools/scripts`; run them from the repo path with '
  'the unicorn + capstone venv in this order: `cg.py`, `disp.py`, `ecldisp.py`, `cite.py`, `build.py`, `render.py` '
  '(`tables.py`, `peek.py` and the rest are probes).')
P('')
P('1. Load the program with `tools/scripts/por_emu.py` `load_image` (EXEPACK expanded); the overlay stubs are the '
  'paragraphs starting `CD 3F 00 00` (dword GAME.OVR offset at +4, word code size at +8, entry count at +0x0C, '
  'entries of 5 bytes from +0x20: `CD 3F` + the routine offset in the unit).')
P('2. Recursive descent with capstone (16-bit) from the entry point (START 0x0006), every stub entry and the sound '
  'timer interrupt (START 0x9ECA, installed through INT 21h 25h): near calls stay in the segment / unit, far calls '
  'resolve through the stubs, and decoding stops at stub paragraphs. Then a sweep for routine prologues (`55 89 E5`, '
  '`55 8B EC`) not yet covered. What stays uncovered is string constants kept in the code segments.')
P('3. Indirect calls: the program has three, all through tables it fills at run time: the spell routines (DS:6B84, '
  'built by 0x2A42B, called from 0x27C38; its entry 0 is a procedure variable, the target pick 0x148BE, stored by the '
  'fight at 0x988C and by the builders at 0x11EB6 / 0x2A479 and called at 0x27D88), the effect handlers (DS:6828, built by 0x11DFD, called from 0x2AEEA) and '
  'the TP run-time. The tables were read by running the two builders (`por_spell_oracle.py` set-up). The ECL '
  'dispatcher (0x3C73) is a compare chain of near calls; its opcode → handler map is in the ECL section below.')
P('4. Citations: every `0x` number in `docs/*.md`, `docs/re/*.md` (surveys and prior art left out) and in C# comments '
  'under `src/`: after `START` / `START.EXE` (and numbers chained to it, unless no START routine holds the number) a '
  'START address, `SSSS:OOOO` resolved by '
  'segment or stub, a range `0xA–0xB` counts for every entry inside it, otherwise a GAME.OVR offset (4–5 digits, or '
  'an exact routine entry); numbers after `+`, `$`, `DS:`, `hunk n` or `Amiga` are skipped. A citation counts for the '
  'routine whose decoded instructions hold it; "entry" means the routine\'s first address. The oracles were read '
  'by hand (their `e.call` targets count, trapped routines do not).')
P('5. Rows marked with a note in the gap lists were checked by hand; the rest follow the rule in "Statuses". The '
  'count is %d START and %d GAME.OVR addresses cited, on %d routines.' % (RAW.get('S', 0), RAW.get('O', 0),
                                                                          sum(1 for k in rows if k in cites)))
P('')

# ---- paths ----
P('## Title to ending: screens and paths')
P('')
P('Each path\'s root routines and everything they call, without entering another path\'s roots (library and N '
  'rows not followed). Shared helpers (menus, text, messages, effects) count in every path that reaches them.')
P('')
P('| Code | Path | Roots | Routines | W | P | U | Play gaps (P + U, kind play) |')
P('|---|---|---|---|---|---|---|---|')
for code, (name, roots) in zip(PCODES, PATHS):
    cl = [k for k in closure(roots) if ST[k] in 'WPU']
    c = collections.Counter(ST[k] for k in cl)
    gaps = sorted((k for k in cl if ST[k] in 'PU' and KIND[k] == 'play'), key=lambda k: rows[k]['addr'])
    P('| %s | %s | %s | %d | %d | %d | %d | %s |' % (code, name, ', '.join(addr(r) for r in roots), len(cl), c['W'], c['P'],
                                                     c['U'], ', '.join(addr(k) for k in gaps) or '—'))
P('')
P('The Paths column of the unit tables below uses these codes (the paths whose closure holds the routine).')
P('')

# ---- ECL map ----
P('## ECL opcode handlers (dispatcher 0x3C73)')
P('')
P('| Handler | Opcodes | Status |')
P('|---|---|---|')
for k in sorted((k for k in rows if rows[k]['ecl']), key=lambda k: rows[k]['addr']):
    ops, names = rows[k]['ecl']
    P('| %s | %s | %s |' % (addr(k), ', '.join('0x%02X %s' % (o, n) for o, n in zip(ops, names)), ST[k]))
P('')
P('All 62 PoR opcodes have an `EclVm` case; the handlers that call into the host (COMBAT, TREASURE, ENCMENU, '
  'PARLEY, shops through PROGRAM / CALL) are W for the handler itself; the host routines they reach have their '
  'own rows below.')
P('')

# ---- per unit tables ----
P('## Ledger by program unit')
P('')


def table(keys):
    P('| Address | Role | St | Kind | Paths | Evidence | Port (C#) |')
    P('|---|---|---|---|---|---|---|')
    for k in sorted(keys, key=lambda k: rows[k]['addr']):
        P('| %s | %s | %s | %s | %s | %s | %s |' % (addr(k), role(k), ST[k], KIND[k], ' '.join(INPATH.get(k, [])) or '—',
                                                ev(k), port(k)))
    P('')


def counts(ks):
    c = collections.Counter(ST[k] for k in ks)
    wx = sum(1 for k in ks if ST[k] == 'W' and wexec(k))
    out_ = []
    for s in 'WPUNL':
        if c[s]:
            out_.append('W %d (%d by execution)' % (c[s], wx) if s == 'W' else '%s %d' % (s, c[s]))
    return ', '.join(out_)


P('### START.EXE')
P('')
P('Library segments (status L, not listed one by one):')
P('')
P('| Segment | Routines | Content |')
P('|---|---|---|')
for sg in sorted(LIB):
    ks = [k for k in rows if k[0] == 'S' and rows[k]['seg'] == sg]
    if ks:
        lo = min(rows[k]['addr'] for k in ks)
        P('| %04X (START 0x%04X…) | %d | %s |' % (sg, lo, len(ks), LIB[sg]))
P('')
for sg in sorted(set(r['seg'] for r in rows.values() if r['space'] == 'S')):
    if sg in LIB:
        continue
    ks = [k for k in rows if k[0] == 'S' and rows[k]['seg'] == sg]
    P('#### Segment %04X: %s (%d routines: %s)' % (sg, SEGNAME.get(sg, '[?]'), len(ks), counts(ks)))
    P('')
    table(ks)

P('### GAME.OVR')
P('')
units = sorted((v[0], v[1], int(p)) for p, v in cg['units'].items())
for fo, size, p in units:
    ks = [k for k in rows if k[0] == 'O' and fo <= rows[k]['addr'] < fo + size]
    P('#### Unit 0x%04X–0x%04X (stub %X): %s (%d routines: %s)' % (fo, fo + size - 1, p, UNITNAME.get(fo, '[?]'), len(ks),
                                                                   counts(ks)))
    P('')
    table(ks)

# ---- gaps ----
def score(k):
    r = rows[k]
    return (2 if ST[k] == 'U' else 1) * math.log2(r['n'] + 2) * (1 + math.log2(1 + len(r['callers'])))


onpath = set()
for name, roots in PATHS:
    onpath |= closure(roots, False)
offpath = [k for k in rows if ST[k] in 'WPU' and k not in onpath]
print('game routines not reached from any path root:', len(offpath), [addr(k) for k in offpath][:40])
gaps_play = sorted((k for k in rows if ST[k] in 'PU' and KIND[k] == 'play'), key=lambda k: -score(k))
gaps_pres = sorted((k for k in rows if ST[k] in 'PU' and KIND[k] == 'pres'), key=lambda k: -score(k))
json.dump({'play': gaps_play, 'pres': gaps_pres}, open(W + 'gaps.json', 'w'))
open(W + 'ledger_body.md', 'w').write('\n'.join(out) + '\n')
print(tot, len(gaps_play), len(gaps_pres))
for k in gaps_play:
    print('play', ST[k], addr(k), rows[k]['n'], role(k)[:70], '|', ev(k), '| on path' if k in onpath else '')
for k in gaps_pres:
    print('pres', ST[k], addr(k), rows[k]['n'], role(k)[:70], '|', ev(k))
exec(open(W + 'tools/gaps.py').read())
