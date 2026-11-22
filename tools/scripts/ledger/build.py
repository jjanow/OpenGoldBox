import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

"""Build the routine ledger rows (JSON) from cg.json, cites.json, eclh.json, disp.json."""
import json, re, os, collections, sys
ROOT = _ROOT
W = _WORK
cg = json.load(open(W + 'cg.json'))
cj = json.load(open(W + 'cites.json'))
C, ORA = cj['cites'], cj['oracle']
eclh = json.load(open(W + 'eclh.json'))
disp = json.load(open(W + 'disp.json'))
R = {('%s:%x' % (r['space'], r['addr'])): r for r in cg['routines']}

# ---- edges (direct + dispatch tables) ----
callees = collections.defaultdict(set)
callers = collections.defaultdict(set)
for k, r in R.items():
    for sp, a in r['calls']:
        t = '%s:%x' % (sp, a)
        if t in R:
            callees[k].add(t); callers[t].add(k)
for sid, t in disp['spells'].items():
    callees['O:27c38'].add(t); callers[t].add('O:27c38')
for hid, t in disp['handlers'].items():
    callees['O:2aeea'].add(t); callers[t].add('O:2aeea')
# DS:6B84 entry 0 is a procedure variable: the target pick 0x148BE, stored by the fight 0x9881 (0x988C) and the
# table builders (0x11EB6, 0x2A479), called by 0x27C38 at 0x27D88
callees['O:27c38'].add('O:148be'); callers['O:148be'].add('O:27c38')
spell_of = collections.defaultdict(list)
for sid, t in disp['spells'].items():
    spell_of[t].append(int(sid))
handler_of = collections.defaultdict(list)
for hid, t in disp['handlers'].items():
    handler_of[t].append(int(hid))
ecl_of = {'O:' + a: v for a, v in eclh.items()}

LIB_SEGS = {0xaf8: 'Turbo Pascal System unit (RTL: heap, strings, sets, real, I/O, halt)',
            0xaa2: 'Turbo Pascal overlay manager (INT 3Fh loader, overlay file reads)',
            0xa40: 'Crt-style unit (keyboard BIOS, Delay, video mode, cursor)',
            0xa31: 'unit initialisation stub', 0xa32: 'DOS helper unit (INT 21h)',
            0x3da: 'unit initialisation stub', 0x3ed: 'unit initialisation stub'}
SEG_ROLE = {0xba: 'game core helpers', 0x3f1: 'windows and frames', 0x42b: 'start-up / configuration',
            0x464: 'picture decode and draw', 0x709: 'text, input and time', 0x802: 'keyboard / file helpers',
            0x899: 'disk swap, keys while busy', 0x8ae: 'sound driver (PC speaker / Tandy, timer ISR)'}

DOC_AREA = {'camp.md': 'camp', 'combat-ai.md': 'combat', 'combat-format.md': 'combat', 'combat-map.md': 'combat',
            'combat-menu.md': 'combat', 'spells-effects.md': 'combat', 'item-format.md': 'items',
            'shop-format.md': 'shops', 'training.md': 'hall', 'start-menu.md': 'party', 'character-format.md': 'chars',
            'ecl-format.md': 'ecl', 'view3d.md': 'explore', 'geo-format.md': 'explore', 'save-format.md': 'save',
            'sound.md': 'present', 'por-art-formats.md': 'present', 'dos-graphics-survey.md': 'present'}


def src_area(f):
    n = os.path.basename(f)
    for pat, a in (('Camp', 'camp'), ('Fight', 'combat'), ('Combat', 'combat'), ('Effect', 'combat'),
                   ('MonsterAi', 'combat'), ('Sweep', 'combat'), ('TurnUndead', 'combat'), ('Flight', 'combat'),
                   ('AfterCombat', 'combat'), ('Shop', 'shops'), ('Temple', 'shops'), ('Trad', 'shops'),
                   ('Venue', 'shops'), ('Treasure', 'shops'), ('Money', 'shops'), ('Training', 'hall'),
                   ('Hall', 'hall'), ('LevelStats', 'hall'), ('CharacterCreation', 'party'),
                   ('CharacterModify', 'party'), ('IconEditor', 'party'), ('PartyMenu', 'party'),
                   ('Explore', 'explore'), ('Area', 'explore'), ('Overland', 'explore'), ('View', 'explore'),
                   ('Ecl', 'ecl'), ('ScriptRunner', 'ecl'), ('Encounter', 'enc'), ('EndGame', 'end'),
                   ('Intro', 'intro'), ('Title', 'intro'), ('Sound', 'present'), ('Audio', 'present'),
                   ('Amiga', 'present'), ('Text', 'present'), ('Art', 'present'), ('Save', 'save'),
                   ('StatRecalc', 'chars'), ('Carrying', 'chars'), ('Item', 'items')):
        if pat in n:
            return a
    return None


def area_of(k):
    r = R[k]
    if r['space'] == 'S':
        if r['seg'] in LIB_SEGS:
            return 'lib'
        if r['seg'] in (0x3f1, 0x464, 0x709, 0x8ae, 0x802, 0x899):
            return 'present' if r['seg'] != 0x899 else 'shared'
        if r['seg'] == 0x42b or r['addr'] == 6:
            return 'intro'
        return 'shared'
    if k in ecl_of:
        return 'ecl'
    if k in spell_of or k in handler_of:
        return 'combat'
    c = C.get(k)
    votes = collections.Counter()
    if c:
        for f, n in c['entry_docs'].items():
            a = DOC_AREA.get(os.path.basename(f))
            if a:
                votes[a] += 3 * n
        for f, n in c['docs'].items():
            a = DOC_AREA.get(os.path.basename(f))
            if a:
                votes[a] += n
        for f, n in c['src'].items():
            a = src_area(f)
            if a:
                votes[a] += 2 * n
    if votes:
        return votes.most_common(1)[0][0]
    return None


area = {k: area_of(k) for k in R}
# uncited: inherit from callers (iterate)
for _ in range(6):
    for k in R:
        if area[k] is None:
            v = collections.Counter(area[c] for c in callers[k] if area[c])
            if v:
                area[k] = v.most_common(1)[0][0]
            else:
                v = collections.Counter(area[c] for c in callees[k] if area[c] and area[c] not in ('lib', 'shared', 'present'))
                if v:
                    area[k] = v.most_common(1)[0][0]
for k in R:
    if area[k] is None:
        area[k] = 'shared'

# ---- evidence & status ----
CSHARP_ID = {}
for k, (ops, names) in ecl_of.items():
    CSHARP_ID[k] = 'EclVm (opcode %s)' % '/'.join(names)


def evidence(k):
    r = R[k]
    c = C.get(k, {})
    ev = []
    if k in ORA:
        ev.append('oracle')
    if k in ecl_of or k in golden_unit:
        ev.append('golden')
    if k in via_oracle and k not in ORA:
        ev.append('oracle via caller')
    esrc = c.get('entry_src', {})
    if esrc:
        ev.append('C# entry')
    elif c.get('src'):
        ev.append('C# interior')
    if c.get('entry_docs'):
        ev.append('spec entry')
    elif c.get('docs'):
        ev.append('spec interior')
    return ev


# ---- oracle closure: callees the oracle ran (not trapped) were exercised and compared through their caller ----
def S_(a): return 'S:%x' % a
def O_(a): return 'O:%x' % a
ALLOC = [S_(0xB1BF), S_(0xB1D4)]
ORACLES = {
    'ai': ([O_(x) for x in (0x143F3, 0x148BE, 0xAEAB, 0x22AC4, 0x22827, 0x228D7)],
           [O_(x) for x in (0x2AEEA, 0x30A0F, 0x27C38, 0x239D6)] + [S_(x) for x in (0xFE2, 0x228C, 0x3728, 0x7E4E, 0x2383, 0x2233)] + ALLOC),
    'chargen': ([O_(0x19EEB)], [O_(x) for x in (0x2D669, 0x2D9A0, 0x1CD66, 0x1D1AC, 0x1E684, 0x2E5C7)]
                + [S_(x) for x in (0x7A73, 0x76B6, 0x7D3B, 0x3F10, 0x4047, 0x41F8)] + ALLOC),
    'combat': ([O_(x) for x in (0xCDE1, 0xD785, 0xCE75, 0xDFC6, 0x30ADA, 0x313F3, 0x300CD, 0x3045B, 0x14E38)] + [S_(0x2FD3), S_(0x30FB)],
               [O_(0x1997F), S_(0x8AB2)] + ALLOC),
    'icon': ([O_(0x1D1AC)], [O_(x) for x in (0x2CC8C, 0x2D9A0, 0x1E468, 0x1CF3D, 0x1CE5E, 0x3081D, 0x3083F, 0x2CFCA, 0x31BBA, 0x31C34)]
             + [S_(0x41F8), S_(0x76B6)] + ALLOC),
    'level': ([O_(0x2AA87), S_(0x3AF7)], ALLOC),
    'modify': ([O_(x) for x in (0x1BA1E, 0x1D932, 0x1DA0B)], [O_(0x2CC8C), S_(0x7DF8)] + ALLOC),
    'restore': ([O_(0x29C5B)], [S_(0x228C)] + ALLOC),
    'spell': ([O_(x) for x in (0x11DFD, 0x2A42B, 0x2AEEA, 0x2AF10)] + [k for k in ORA if ORA[k] == 'spell'],
              [S_(x) for x in (0x228C, 0x2B8B, 0x2233, 0x257E, 0x25C9, 0x7E4E, 0x2383, 0x1758, 0x3AF7)]
              + [O_(x) for x in (0x310D0, 0x32123, 0x2AA87, 0x3154B, 0x31321, 0x31349, 0x317E1)] + ALLOC),
    'trade': ([O_(x) for x in (0x250D5, 0x2276E, 0x6F0D)] + [S_(0x1758)], [S_(x) for x in (0x2233, 0x3379, 0x37E8)] + ALLOC),
    'sound': ([S_(0x9E5C), S_(0x9ECA), S_(0xA222), S_(0xA28E)], []),
    'train': ([O_(0x1C32D)], [S_(x) for x in (0x7DF8, 0x76B6, 0x4047, 0x23B6, 0x2233, 0x41F8, 0xBA8B, 0xB062)]
              + [O_(0x2D9A0), O_(0x239D6)] + ALLOC),
}
# s49 oracles; their base classes (por_spell_oracle.Oracle, por_ai_oracle.AiOracle) keep their own traps
SPELL_TRAPS = ORACLES['spell'][1]
AI_TRAPS = [O_(0x2AEEA), O_(0x30A0F)]
ORACLES.update({
    'venue': ([O_(x) for x in (0x23819, 0x4864, 0x22F8C, 0x47C0, 0x4978, 0x4CC8, 0x4A87, 0x4DB1, 0x4E9F, 0x644B, 0x25C07)],
              [O_(x) for x in (0x458A, 0x4636, 0x2D9A0, 0x2CC8C, 0x21AED, 0x62B6, 0x25191, 0x252C3, 0x8B57)]
              + [S_(x) for x in (0xFE2, 0x77F8, 0x4047, 0x2233, 0x7E4E, 0x7E63, 0x3379, 0x1307)] + SPELL_TRAPS),  # s51: every temple entry, the loot menu
    'round': ([O_(x) for x in (0x127FB, 0x9938, 0x12F0D, 0x2B9D1)],
              [O_(0x30C65), S_(0x8BBC), O_(0x1407F)] + SPELL_TRAPS),
    'handler': ([O_(x) for x in (0xF91E, 0x10AD2, 0x2A0F1, 0x11134, 0x112E5, 0x2BC4D)],
                [O_(0x27AA9)] + SPELL_TRAPS),  # DS:6B84 entry 0 (the pick) scripted
    'spellcast': ([O_(0x27C38), O_(0x275B4)],
                  [O_(x) for x in (0x2D9A0, 0x2A332, 0x2BEBC, 0x31321, 0x31349, 0x300CD, 0x31232, 0x30E54)]
                  + [S_(x) for x in (0x76B6, 0x2233, 0x2383, 0x4047, 0x257E, 0x8C85, 0x8BBC, 0x25C9)] + AI_TRAPS + SPELL_TRAPS),
    'cast': ([O_(x) for x in (0x14C0F, 0x275B4, 0x24331)],
             [O_(x) for x in (0x239D6, 0x27C38, 0x319D2)] + [S_(x) for x in (0x2233, 0x228C, 0x3728, 0x1583)] + AI_TRAPS + SPELL_TRAPS),
    # s51: por_altx_oracle.py (ParamStr START 0xC63C trapped)
    'altx': ([O_(0x1638E)], [S_(0xC63C)] + SPELL_TRAPS),
    # s51: por_items_oracle.py (the Items loop whole; list, yes/no, handler, naming and drawing trapped)
    'items': ([O_(0x21F32)], [O_(x) for x in (0x2D669, 0x2D9A0, 0x2AEEA)]
              + [S_(x) for x in (0x2233, 0xFE2, 0x7D3B, 0x76B6, 0x77F8, 0x3F10, 0x4047, 0x41F8, 0x23B6)] + ALLOC),
})
via_oracle = {}
for name, (roots, traps) in ORACLES.items():
    traps = set(traps)
    seen = set()
    work = [r for r in roots if r in R]
    while work:
        k = work.pop()
        if k in seen or k in traps:
            continue
        seen.add(k)
        for c in callees[k]:
            # handlers / spell routines reached only through the tables: only the cases the oracle runs
            if k in ('O:27c38', 'O:2aeea'):
                continue
            work.append(c)
    for k in seen:
        r = R[k]
        if name == 'sound':
            via_oracle.setdefault(k, set()).add(name)
            continue
        if r['space'] == 'S' and r['seg'] in (0x3f1, 0x464, 0x709, 0x8ae, 0x802):
            continue  # drawing, text, keys, sound: run but their output (pixels, sound) is not compared
        if area.get(k) == 'present':
            continue
        if k not in roots:
            via_oracle.setdefault(k, set()).add(name)
# the VM helper unit (GAME.OVR 0x74D3-0x96ED) and the opcode handlers' unit: golden captures through the handlers
golden_unit = set(k for k, r in R.items() if r['space'] == 'O' and 0x74D3 <= r['addr'] < 0x96ED)

EMPTY = set(k for k, r in R.items() if r['n'] <= 4 and not r['calls'])


def status(k):
    r = R[k]
    if area[k] == 'lib':
        return 'L'
    if k in EMPTY:
        return 'W'  # an empty routine (push bp / mov bp,sp / pop bp / retf): nothing to port
    ev = evidence(k)
    c = C.get(k, {})
    if 'oracle' in ev or 'golden' in ev or 'oracle via caller' in ev:
        return 'W'
    if 'C# entry' in ev and (k in spell_of or k in handler_of) and c.get('q_lines', 0) == 0:
        return 'W'  # spell routines and effect handlers: the spec lists them by number
    if 'C# entry' in ev and 'spec entry' in ev:
        dl = sum(c['entry_docs'].values())
        if c.get('q_lines', 0) == 0 and c.get('seen_lines', 0) < dl:
            return 'W'
        return 'P'
    if 'C# entry' in ev or 'C# interior' in ev:
        return 'P'
    return 'U'


ST = {k: status(k) for k in R}


def short_names(c):
    out = []
    for f in sorted(c.get('entry_src', {}), key=lambda f: -c['entry_src'][f]):
        n = os.path.splitext(os.path.basename(f))[0]
        if n not in out:
            out.append(n)
    if not out:
        for f in sorted(c.get('src', {}), key=lambda f: -c['src'][f]):
            n = os.path.splitext(os.path.basename(f))[0]
            if n not in out:
                out.append(n)
    return out[:3]


# ---- role text (from the spec line that cites the entry) ----
lines_cache = {}


def getline(ref):
    f, ln = ref.rsplit(':', 1)
    if f not in lines_cache:
        lines_cache[f] = open(ROOT + f, encoding='utf-8').read().split('\n')
    return lines_cache[f][int(ln) - 1]


def role(k):
    r = R[k]
    sp, a = k.split(':')
    a = int(a, 16)
    c = C.get(k)
    if not c:
        return ''
    pat = re.compile(r'(0x0*%X\b|%04X:[0-9A-F]+)' % (a, a), re.I)
    best = None
    for ref in c.get('elines', []):
        if not ref.startswith('docs/'):
            continue
        line = getline(ref)
        if line.startswith('|'):
            cells = [x.strip() for x in line.strip('|').split('|')]
            for i, cell in enumerate(cells):
                if pat.search(cell) and i + 1 < len(cells) and len(cells[i + 1]) > 3:
                    cand = cells[i + 1]
                    break
            else:
                continue
        else:
            m = pat.search(line)
            if not m:
                continue
            s, e = m.start(), m.end()
            left = line[:s]
            right = line[e:]
            # clause: back to the previous separator, forward to the next
            lb = max(left.rfind('. '), left.rfind('; '), left.rfind(': '), left.rfind('('), left.rfind('**'))
            rb_candidates = [x for x in (right.find('. '), right.find('; '), right.find(')'), right.find(', ')) if x >= 0]
            rb = min(rb_candidates) if rb_candidates else len(right)
            cand = (left[lb + 1:] + ' @ ' + right[:rb]).strip()
        cand = re.sub(r'"[^"]*"|“[^”]*”|‘[^’]*’', '…', cand)
        cand = re.sub(r'\(?(START(\.EXE)? )?0x[0-9A-Fa-f]+\)?|\b[0-9A-F]{2,4}:[0-9A-F]{2,4}\b', '', cand)
        cand = re.sub(r'\*\*|`|@', '', cand)
        cand = re.sub(r'\s+', ' ', cand).strip(' ,:;–-=()')
        if len(cand) < 6:
            continue
        if best is None or abs(len(cand) - 45) < abs(len(best) - 45):
            best = cand
    if best and len(best) > 90:
        best = best[:88].rsplit(' ', 1)[0] + ' …'
    return best or ''


DECL = re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|internal|protected|static|sealed|readonly|override|virtual|abstract|partial|async|const|unsafe|new)\s+)+[\w<>\[\],.?() ]*?\b(\w+)\s*(?:\(|<[^>]*>\s*\(|\{|=>|=|;|:)')
TYPE = re.compile(r'^\s*(?:(?:public|private|internal|protected|static|sealed|partial|abstract|readonly|file|ref)\s+)*(?:class|struct|record|interface|enum)\s+(\w+)')


def member_at(ref):
    f, ln = ref.rsplit(':', 1)
    ln = int(ln)
    if f not in lines_cache:
        lines_cache[f] = open(ROOT + f, encoding='utf-8').read().split('\n')
    L = lines_cache[f]
    line = L[ln - 1]
    name = None
    if line.strip().startswith('///'):
        for j in range(ln, min(len(L), ln + 25)):
            t = L[j]
            if t.strip().startswith('///') or t.strip().startswith('['):
                continue
            m = DECL.match(t)
            if m:
                name = m.group(1)
            else:
                m = TYPE.match(t)
                if m:
                    name = m.group(1)
            break
    else:
        for j in range(ln - 1, max(-1, ln - 300), -1):
            m = DECL.match(L[j])
            if m and not L[j].strip().startswith('//'):
                name = m.group(1)
                break
    typ = None
    for j in range(ln - 1, -1, -1):
        m = TYPE.match(L[j])
        if m:
            typ = m.group(1)
            break
    if name and typ and name != typ:
        return typ + '.' + name
    return typ or os.path.splitext(os.path.basename(f))[0]


def members(k):
    c = C.get(k, {})
    out = []
    for ref in c.get('elines', []):
        if ref.startswith('src/') and not '/Tests/' in ref and '.Tests/' not in ref:
            m = member_at(ref)
            if m and m not in out:
                out.append(m)
    return out


rows = []
for k, r in R.items():
    c = C.get(k, {})
    rows.append({'key': k, 'space': r['space'], 'addr': r['addr'], 'seg': r['seg'], 'n': r['n'], 'area': area[k],
                 'status': ST[k], 'ev': evidence(k), 'cs': short_names(c), 'members': members(k), 'role': role(k),
                 'ecl': ecl_of.get(k), 'spells': sorted(spell_of.get(k, [])), 'handlers': sorted(handler_of.get(k, [])),
                 'callers': sorted(callers[k]), 'callees': sorted(callees[k]), 'q': c.get('q_lines', 0),
                 'seen': c.get('seen_lines', 0), 'main': r['main'], 'empty': k in EMPTY,
                 'docs': sorted(set(os.path.basename(f) for f in c.get('docs', {}))),
                 'ints': r['ints']})
json.dump(rows, open(W + 'rows.json', 'w'), indent=0)
cnt = collections.Counter((x['area'], x['status']) for x in rows)
tot = collections.Counter(x['status'] for x in rows)
print(tot)
for a in sorted(set(x['area'] for x in rows)):
    print(a, {s: cnt[(a, s)] for s in 'WPUL'})
