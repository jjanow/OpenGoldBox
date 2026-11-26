#!/usr/bin/env python3
"""Spell routines and effect handlers run by the program itself: the v1.3 routines (por_emu.py, unicorn) on generated
creatures, for GoldBox.Core's FightSpellsProgramTests (docs/re/spells-effects.md "Checked under emulation").

Usage: python3 -I por_spell_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Outside a fight (DS:49F3 = 0), no combat map. Each case sets the creature list (DS:5D96), the current character
(DS:5D92), the spell target list (DS:6C94/6C95) and DS:681B, seeds RandSeed, then calls the spell routine
(DS:6B84 + 4·id, the table built by 0x2A42B) or the handler (0x2AEEA) and records the creatures afterwards (record,
effect list, items) and RandSeed. Text, sound and drawing routines are trapped; GetMem/FreeMem are served from the
emulator's arena; START 0x3AF7 (race level limit) answers "no limit", 0x2AA87 (level stats) and START 0x1758 (stat
recalculation) are skipped, so the tests leave the bytes those write out. The output holds creature bytes the script
made up (no game data), but it is kept with the private fixtures.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
from unicorn.x86_const import UC_X86_REG_AX, UC_X86_REG_DX  # noqa: E402

REC, COMBAT, EFFECT, ITEM = 0x11D, 0x16, 9, 0x3F
BUILD_HANDLERS, BUILD_ROUTINES, CALL_HANDLER = 0x11DFD, 0x2A42B, 0x2AEEA
# START.EXE far routines trapped (linear = seg*16 + off as the far calls name them) and their argument bytes.
START_TRAPS = {0x228C: 12, 0x2B8B: 10, 0x2233: 4, 0x257E: 2, 0x25C9: 12, 0x7E4E: 0, 0x2383: 0, 0x1758: 4,
               0x8C85: 2, 0x8BBC: 2,  # (s52) sound cue and driver start: Hold Person's flights
               0x1583: 4}  # (s52) the creature panel: turn undead
OVR_TRAPS = {0x310D0: 8, 0x32123: 10, 0x2AA87: 4, 0x3154B: 4,
             0x2CFCA: 0, 0x319D2: 8}  # (s52) turn undead: menu clear, view scroll
# Map routines answered only in the cases without a combat map: square x / y, put on a square.
MAP_TRAPS = {0x31321: (4, 0), 0x31349: (4, 0), 0x317E1: (10, 1)}
BUILD_CELLS, REMOVE_EFFECT = 0x30ADA, 0x2AF10
TURN_UNDEAD, MORALE_EFFECTS = 0x13965, 0x2BF2B
GETMEM, FREEMEM, RACE_LIMIT = 0xB1BF, 0xB1D4, 0x3AF7


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


class Oracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        for lin, n in START_TRAPS.items():
            e.trap_start(0, lin, Emu.ret(n))
        self.real_remove = False
        for off, n in OVR_TRAPS.items():
            e.trap_ovr(off, self._remove_trap(n) if off == 0x3154B else Emu.ret(n))
        e.trap_start(0, GETMEM, self._getmem)
        e.trap_start(0, FREEMEM, Emu.ret(6))
        e.trap_start(0, RACE_LIMIT, Emu.ret(6, 0))
        self.map_mode = False
        for off, (n, ax) in MAP_TRAPS.items():
            e.trap_ovr(off, self._map_trap(n, ax))
        e.call(BUILD_HANDLERS)
        e.call(BUILD_ROUTINES)
        self.routines = {}
        for i in range(0, 68):
            seg, off = e.dsptr(0x6B84 + 4 * i)
            self.routines[i] = (seg, off)

    def _remove_trap(self, n):
        # 0x3154B (off the map: sound, body icon, cells) is skipped; in the turn-undead cases (s52) the creature's slot
        # size DS:5F2A + 4k is zeroed first, as 0x2BDF0 takes a creature off, so the target search no longer finds it
        def f(e):
            if self.real_remove:
                ptr = (e.arg(2), e.arg(0))
                for k in range(1, 64):
                    o, sg = struct.unpack('<HH', e.rb(e.ds(0x65B9 + 4 * k), 4))
                    if (sg, o) == ptr:
                        e.set_dsb(0x5F2A + 4 * k, 0)
                        break
            e.pop_ret(n)
        return f

    def _map_trap(self, n, ax):
        def f(e):
            if not self.map_mode:
                e.pop_ret(n, ax)
        return f

    def _getmem(self, e):
        size = e.arg(0)
        seg, off = e.alloc(max(size, 1))
        e.pop_ret(2, off)
        e.uc.reg_write(UC_X86_REG_DX, seg)

    def lin(self, ptr):
        return ptr[0] * 16 + ptr[1]

    def build(self, creatures):
        e = self.e
        e.reset_heap()
        ptrs = []
        for c in creatures:
            r = e.alloc(REC)
            rl = self.lin(r)
            e.wb(rl, bytes.fromhex(c['record']))
            cb = e.alloc(COMBAT)
            e.wb(rl + 0x108, struct.pack('<HH', cb[1], cb[0]))
            e.wb(rl + 0x7F, b'\0\0\0\0')
            prev = None
            for eff in c['effects']:
                n = e.alloc(EFFECT)
                e.wb(self.lin(n), bytes.fromhex(eff)[:5] + b'\0\0\0\0')
                if prev is None:
                    e.wb(rl + 0x7F, struct.pack('<HH', n[1], n[0]))
                else:
                    e.wb(self.lin(prev) + 5, struct.pack('<HH', n[1], n[0]))
                prev = n
            e.wb(rl + 0xC8, b'\0\0\0\0')
            prev = None
            for it in c['items']:
                n = e.alloc(ITEM)
                b = bytearray(bytes.fromhex(it))
                b[0x2A:0x2E] = b'\0\0\0\0'
                e.wb(self.lin(n), bytes(b))
                if prev is None:
                    e.wb(rl + 0xC8, struct.pack('<HH', n[1], n[0]))
                else:
                    e.wb(self.lin(prev) + 0x2A, struct.pack('<HH', n[1], n[0]))
                prev = n
            ptrs.append(r)
        for a, b in zip(ptrs, ptrs[1:] + [None]):
            e.wb(self.lin(a) + 0x104, struct.pack('<HH', b[1], b[0]) if b else b'\0\0\0\0')
        e.set_dsptr(0x5D96, ptrs[0] if ptrs else (0, 0))
        return ptrs

    def read_creature(self, ptr):
        e = self.e
        rl = self.lin(ptr)
        rec = e.rb(rl, REC)
        effects = []
        o, s = struct.unpack('<HH', rec[0x7F:0x83])
        while (o, s) != (0, 0) and len(effects) < 64:
            b = e.rb(s * 16 + o, EFFECT)
            effects.append(b[:5].hex())
            o, s = struct.unpack('<HH', b[5:9])
        items = []
        o, s = struct.unpack('<HH', rec[0xC8:0xCC])
        while (o, s) != (0, 0) and len(items) < 64:
            b = e.rb(s * 16 + o, ITEM)
            items.append(b.hex())
            o, s = struct.unpack('<HH', b[0x2A:0x2E])
        return {'record': rec.hex(), 'effects': effects, 'items': items}

    def setup_map(self, case, ptrs):
        e = self.e
        arena = e.alloc(0x4E9)
        e.set_dsptr(0x6716, arena)
        e.wb(self.lin(arena) + 7, bytes.fromhex(case['cells']))
        zone0 = e.alloc(0x800)
        e.set_dsptr(0x49D2, zone0)
        e.wb(self.lin(zone0) + 0x1CC, struct.pack('<H', case.get('cell49e6', 0)))
        for k, (p, (x, y, size)) in enumerate(zip(ptrs, case['places']), 1):
            e.wb(e.ds(0x5F27 + 4 * k), [x & 0xFF, y & 0xFF, k, size])
            e.wb(e.ds(0x65B9 + 4 * k), struct.pack('<HH', p[1], p[0]))
        e.set_dsb(0x5F2A, len(ptrs) + 1)
        e.wb(e.ds(0x5F27 + 4 * (len(ptrs) + 1)), [0, 0, 0, 0])
        e.wb(e.ds(0x65B9 + 4 * (len(ptrs) + 1)), b'\0\0\0\0')
        e.set_dsptr(0x6DBB, (0, 0))
        e.set_dsb(0x6715, 0)
        e.call(BUILD_CELLS)
        return arena

    def run(self, case):
        e = self.e
        ptrs = self.build(case['creatures'])
        self.map_mode = 'cells' in case
        arena = self.setup_map(case, ptrs) if self.map_mode else None
        e.set_dsb(0x49F3, 5 if case.get('fight') else 0)
        if 'ds4845' in case:
            e.set_dsb(0x4844, case['ds4844'])
            e.set_dsb(0x4845, case['ds4845'])
            e.set_dsb(0x681C, 0)
        e.set_dsb(0x681F, 0)
        e.set_dsb(0x6DBF, 0)
        e.set_dsb(0x6819, 0)
        e.set_dsb(0x6823, case.get('hits1', 0))
        e.set_dsptr(0x5D92, ptrs[case['current']])
        e.set_randseed(case['seed'])
        if case['kind'] == 'cloud':
            for cur, (ax, ay) in case['casts']:
                e.set_dsptr(0x5D92, ptrs[cur])
                e.set_dsb(0x681B, 0x22)
                e.set_dsb(0x6DB9, ax)
                e.set_dsb(0x6DBA, ay)
                e.call(self.routines[0x22])
            if 'removeFrom' in case:
                c = ptrs[case['removeFrom']]
                e.call(REMOVE_EFFECT, [c[0], c[1], 0x28, 0, 0])
        elif case['kind'] == 'morale':  # s52: 0x2BF2B(creature 0)
            c = ptrs[case['current']]
            e.call(MORALE_EFFECTS, [c[0], c[1]])
        elif case['kind'] == 'turn':  # s52: turn undead 0x13965 by creature 0 (combat record bytes preset)
            for ci, off, v in case.get('combatSet', []):
                cb = struct.unpack('<HH', e.rb(self.lin(ptrs[ci]) + 0x108, 4))
                e.wb(cb[1] * 16 + cb[0] + off, [v])
            c = ptrs[case['current']]
            self.real_remove = True
            try:
                e.call(TURN_UNDEAD, [c[0], c[1]])
            finally:
                self.real_remove = False
        elif case['kind'] == 'spell':
            if 'aim' in case:
                e.set_dsb(0x6DB9, case['aim'][0])
                e.set_dsb(0x6DBA, case['aim'][1])
            e.set_dsb(0x681B, case['id'])
            e.set_dsb(0x6820, 0)
            e.set_dsb(0x6C94, len(case['targets']))
            for i, t in enumerate(case['targets']):
                e.set_dsptr(0x6C95 + 4 * i, ptrs[t])
            e.call(self.routines[case['id']])
        else:
            c = ptrs[case['creature']]
            # the first record with the id on that creature
            rl = self.lin(c)
            o, s = struct.unpack('<HH', e.rb(rl + 0x7F, 4))
            node = (0, 0)
            while (o, s) != (0, 0):
                if e.rb(s * 16 + o, 1)[0] == case['id']:
                    node = (s, o)
                    break
                o, s = struct.unpack('<HH', e.rb(s * 16 + o + 5, 4))
            if 'other' in case:
                cb = struct.unpack('<HH', e.rb(rl + 0x108, 4))
                tp = ptrs[case['other']]
                e.wb(cb[1] * 16 + cb[0] + 0x0A, struct.pack('<HH', tp[1], tp[0]))
            e.set_dsb(0x681B, 0)
            e.call(CALL_HANDLER, [case['id'], c[0], c[1], node[0], node[1], 1 if case.get('removed') else 0])
        res = {'after': [self.read_creature(p) for p in ptrs], 'seedAfter': e.randseed(),
               'spellCell': e.dsb(0x681B), 'ds4845': e.dsb(0x4845)}
        if 'ds4845' in case:
            res['ds4844'] = e.dsb(0x4844)
            res['ds6819'] = e.dsb(0x6819)
            res['ds681c'] = e.dsb(0x681C)
            res['ds6820'] = e.dsb(0x6820)
        if case['kind'] == 'turn':
            res['combat'] = []
            for p in ptrs:
                cb = struct.unpack('<HH', e.rb(self.lin(p) + 0x108, 4))
                res['combat'].append(e.rb(cb[1] * 16 + cb[0], COMBAT).hex())
            res['ds6e31'] = e.dsb(0x6E31)
        if arena is not None and case['kind'] != 'turn':
            res['cells'] = e.rb(self.lin(arena) + 7, 1250).hex()
            res['aim'] = [e.dsb(0x6DB9), e.dsb(0x6DBA)]
        return res


def creature(rnd, **kw):
    r = bytearray(REC)
    name = b'C%d' % rnd.randrange(100)
    r[0] = len(name)
    r[1:1 + len(name)] = name
    r[0x10] = kw.get('str', rnd.randrange(3, 19))
    r[0x16] = kw.get('exc', rnd.choice([0, 0, 50, 99]))
    r[0x14] = rnd.randrange(3, 19)
    r[0x15] = rnd.randrange(3, 19)
    r[0x2E] = kw.get('race', rnd.randrange(0, 8))
    hpmax = kw.get('hpmax', rnd.randrange(4, 60))
    r[0x32] = hpmax
    r[0xB1] = rnd.randrange(0, hpmax + 1)
    r[0x11B] = kw.get('hp', rnd.randrange(1, hpmax + 1))
    for i in range(5):
        r[0x6D + i] = rnd.randrange(4, 18)
    r[0x6C] = kw.get('size', rnd.choice([1, 1, 2]))
    r[0x73] = kw.get('hd', rnd.randrange(0, 8))
    r[0x74] = kw.get('drained', 0)
    r[0x75] = kw.get('drainedhp', 0)
    levels = kw.get('levels')
    if levels is None:
        levels = [0] * 8
        for k in rnd.sample(range(8), rnd.choice([1, 1, 2])):
            levels[k] = rnd.randrange(1, 10)
    for i, v in enumerate(levels):
        r[0x96 + i] = v
    xp = kw.get('xp', rnd.randrange(0, 200000))
    r[0xAC:0xB0] = struct.pack('<I', xp)
    r[0x9F] = kw.get('type', rnd.choice([0, 0, 1, 4]))
    r[0x84] = kw.get('npc', rnd.choice([0, 0x80, 0xB3]))
    r[0x10C] = kw.get('status', 0)
    r[0x10D] = kw.get('inplay', 1)
    r[0x10E] = kw.get('hostile', rnd.randrange(2))
    r[0x10F] = 0
    for i in range(21):
        r[0x17 + i] = rnd.choice([0, 0, 0x15, 0x2F])
    return r


def effect(eid, minutes=0, param=0xFF, flag=0):
    return bytes([eid, minutes & 0xFF, minutes >> 8, param, flag]).hex()


def item(rnd, cursed=False, readied=False, words=(0, 0, 0)):
    b = bytearray(ITEM)
    b[0x2E] = rnd.randrange(1, 0x60)
    b[0x2F], b[0x30], b[0x31] = words
    b[0x34] = 1 if readied else 0
    b[0x36] = 1 if cursed else 0
    return b.hex()


def make_cases(rnd):
    cases = []

    def add(kind, cid, creatures, current, **kw):
        c = {'kind': kind, 'id': cid, 'creatures': [{'record': bytes(r).hex(), 'effects': ef, 'items': it}
                                                     for r, ef, it in creatures],
             'current': current, 'seed': rnd.getrandbits(32)}
        c.update(kw)
        cases.append(c)

    for _ in range(40):  # 35 Strength
        lv = [0] * 8
        for k in rnd.sample([0, 2, 5, 6], rnd.choice([1, 1, 2, 3])):
            lv[k] = rnd.randrange(1, 9)
        t = creature(rnd, levels=lv, str=rnd.randrange(10, 19))
        cases_c = [(creature(rnd, levels=[0, 0, 0, 0, 0, rnd.randrange(3, 10), 0, 0]), [], []), (t, [], [])]
        add('spell', 35, cases_c, 0, targets=[1])
    for _ in range(30):  # 36 Animate Dead
        cs = [(creature(rnd, levels=[rnd.randrange(5, 12), 0, 0, 0, 0, 0, 0, 0], hostile=0), [], [])]
        for _ in range(rnd.randrange(1, 7)):
            cs.append((creature(rnd, status=rnd.choice([6, 6, 0, 4]), type=rnd.choice([0, 0, 1]),
                                inplay=0), [], []))
        add('spell', 36, cs, 0, targets=[])
    for sid in (37, 39, 58):  # cure blindness, cure disease, 58
        for _ in range(15):
            pool = [0x21, 0x22, 0x2B, 0x2C, 0x1F, 0x32, 0x39, 0x37, 0x16, 0x01]
            ef = [effect(x, rnd.randrange(0, 50)) for x in rnd.sample(pool, rnd.randrange(0, 5))]
            t = creature(rnd)
            add('spell', sid, [(creature(rnd), [], []), (t, ef, [])], 0, targets=[1])
    for sid in (41, 46):  # Dispel Magic
        for _ in range(25):
            ef = [effect(rnd.choice([0x01, 0x02, 0x08, 0x09, 0x0E, 0x11, 0x21, 0x24]), rnd.randrange(1, 90),
                         rnd.choice([0xFF, rnd.randrange(0, 0x40), rnd.randrange(0, 0x100)])) for _ in range(rnd.randrange(1, 5))]
            caster = creature(rnd, levels=[rnd.randrange(1, 12), 0, 0, 0, 0, rnd.randrange(1, 12), 0, 0])
            n = rnd.randrange(1, 4)
            cs = [(caster, [], [])] + [(creature(rnd), ef if i == 0 else [], []) for i in range(n)]
            add('spell', sid, cs, 0, targets=list(range(1, n + 1)))
    for _ in range(15):  # 42 Prayer (resolution), 44 Bestow Curse
        cs = [(creature(rnd, levels=[rnd.randrange(5, 12), 0, 0, 0, 0, 0, 0, 0]), [], [])] + \
             [(creature(rnd), [], []) for _ in range(3)]
        add('spell', rnd.choice([42, 44]), cs, 0, targets=[1, 2, 3])
    for _ in range(20):  # 43 Remove Curse
        ef = [effect(0x24, 10)] if rnd.random() < 0.4 else []
        its = [item(rnd, cursed=rnd.random() < 0.4) for _ in range(rnd.randrange(0, 4))]
        add('spell', 43, [(creature(rnd), [], []), (creature(rnd), ef, its)], 0, targets=[1])
    for sid, cancel in ((48, 0x2A), (55, 0x27)):  # Haste, Slow
        for _ in range(20):
            caster = creature(rnd, levels=[0, 0, 0, 0, 0, rnd.randrange(1, 6), 0, 0], hostile=0)
            n = rnd.randrange(1, 7)
            cs = [(caster, [], [])] + [(creature(rnd), [effect(cancel, 5)] if rnd.random() < 0.3 else [], [])
                                       for _ in range(n)]
            add('spell', sid, cs, 0, targets=list(range(1, n + 1)))
    for _ in range(20):  # 56 Restoration
        lv = [0] * 8
        for k in rnd.sample([0, 2, 5, 6], rnd.choice([1, 2])):
            lv[k] = rnd.randrange(1, 12)
        d = rnd.randrange(0, 3)
        t = creature(rnd, levels=lv, drained=d, drainedhp=rnd.randrange(0, 20) if d else 0)
        add('spell', 56, [(creature(rnd), [], []), (t, [], [])], 0, targets=[1])
    for sid in (57, 59, 62, 65, 67):
        for _ in range(10):
            ef = [effect(0x2A, 5)] if sid == 57 and rnd.random() < 0.4 else []
            t = creature(rnd)
            add('spell', sid, [(creature(rnd), [], []), (t, ef, [])], 0, targets=[1])
    # handlers
    for hid in (81, 82):
        for _ in range(15):
            carrier = creature(rnd, hostile=1)
            cs = [(carrier, [effect(hid, 0, 0x0C, 0)], [])]
            for _ in range(rnd.randrange(1, 7)):
                cs.append((creature(rnd, hostile=rnd.choice([0, 0, 1]), inplay=rnd.choice([1, 1, 0]),
                                    hd=rnd.randrange(0, 8)), [], []))
            add('handler', hid, cs, 0, creature=0)
    for _ in range(20):  # 83 gaze
        gazer = creature(rnd, hostile=1)
        ef = [effect(83, 0, 0xFF)] + ([effect(0x7F, 0, 0xFF)] if rnd.random() < 0.6 else [])
        its = [item(rnd, readied=rnd.random() < 0.7, words=(0, rnd.choice([0, 0x76]), 0)) for _ in range(rnd.randrange(0, 3))]
        add('handler', 83, [(gazer, ef, []), (creature(rnd, hostile=0), [], its)], 0, creature=0, other=1)
    for hid in (85, 86):
        for _ in range(20):
            lv = [0] * 8
            for k in rnd.sample([0, 2, 5, 6], rnd.choice([1, 2])):
                lv[k] = rnd.randrange(1, 12)
            v = creature(rnd, levels=lv, hd=max(lv), status=rnd.choice([0, 0, 0, 1]))
            add('handler', hid, [(creature(rnd), [effect(hid, 0, 0xFF)], []), (v, [], [])], 0, creature=0, other=1)
    # ---- on a combat map ----
    def arena():
        return bytes(1 if rnd.random() < 0.08 else 23 for _ in range(1250))

    def placed(n, hostile0=0):
        cs, places, used = [], [], set()
        while len(places) < n:
            x, y = rnd.randrange(2, 48), rnd.randrange(2, 23)
            if (x, y) in used:
                continue
            used.add((x, y))
            places.append((x, y, 1))
            lv = [0, 0, 0, 0, 0, rnd.randrange(3, 13), 0, 0]
            cs.append((creature(rnd, levels=lv, hostile=hostile0 if not cs else rnd.randrange(2), hp=rnd.randrange(5, 40)),
                       [], []))
        return cs, places

    for sid in (51, 51, 51, 60):  # Lightning Bolt, 60
        for _ in range(12):
            cs, places = placed(rnd.randrange(3, 9))
            cells = bytearray(arena())
            for x, y, _s in places:
                cells[y * 50 + x] = 23
            if rnd.random() < 0.5:
                a = places[rnd.randrange(1, len(places))][:2]
            else:
                a = (rnd.randrange(1, 49), rnd.randrange(1, 24))
            if a == places[0][:2]:
                continue
            add('spell', sid, cs, 0, targets=[], cells=bytes(cells).hex(), places=places, aim=list(a))
    for sid in (47, 64):  # Fireball
        for _ in range(12):
            cs, places = placed(rnd.randrange(3, 9))
            cells = bytearray(arena())
            a = places[rnd.randrange(1, len(places))][:2]
            add('spell', sid, cs, 0, targets=list(range(1, len(places))), cells=bytes(cells).hex(), places=places,
                aim=list(a), cell49e6=rnd.choice([0, 0, 1]))
    for _ in range(15):  # Stinking Cloud twice, then the first one's effect removed (handler 40)
        cs, places = placed(rnd.randrange(3, 8))
        cells = bytearray(arena())
        a1 = places[rnd.randrange(1, len(places))][:2]
        a2 = (a1[0] + rnd.choice([-1, 0, 1]), a1[1] + rnd.choice([-1, 0, 1]))
        casts = [(0, list(a1)), (rnd.choice([0, 1]), list(a2))]
        c = {'cells': bytes(cells).hex(), 'places': places, 'casts': casts, 'targets': []}
        if rnd.random() < 0.8:
            c['removeFrom'] = 0
        add('cloud', 34, cs, 0, **c)
    more_cases(rnd, add, placed, arena)
    return cases


def more_cases(rnd, add, placed, arena):
    """s52: spell routines 1-33, 38, 40, 45, 61, 63 and 66 (read whole in s52); DS:4844 / 4845 set and compared."""
    pool = [0x0C, 0x35, 0x37, 0x21, 0x2A, 0x27, 0x0B, 0x01, 0x16, 0x24]

    def lv_any():
        lv = [0] * 8
        for k in rnd.sample(range(8), rnd.choice([1, 1, 2, 3])):
            lv[k] = rnd.randrange(1, 14)
        return lv

    def target(**kw):
        ef = [effect(x, rnd.randrange(0, 60), rnd.choice([0xFF, rnd.randrange(0, 0x40)]))
              for x in rnd.sample(pool, rnd.choice([0, 0, 1, 2]))]
        return creature(rnd, **kw), ef, []

    def g():
        return dict(ds4844=rnd.randrange(256), ds4845=rnd.randrange(256))

    plain = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 24, 25, 28, 29, 30, 31, 32,
             33, 38, 40, 45, 52, 53, 54, 61, 63, 66, 50]
    for sid in plain:
        for _ in range(12):
            caster = (creature(rnd, levels=lv_any()), [], [])
            n = rnd.choice([1, 1, 1, 2, 3, 4])
            ts = []
            for _i in range(n):
                kw = {}
                if sid == 10:
                    kw = dict(type=rnd.choice([0, 1, 2, 4]), size=rnd.choice([1, 1, 2]))
                if sid in (3, 4, 9, 15, 20):
                    kw = dict(hpmax=rnd.randrange(10, 60))
                ts.append(target(**kw))
            if sid == 13 and rnd.random() < 0.6:
                ts[0][1].insert(rnd.randrange(len(ts[0][1]) + 1), effect(0x0C, 30, rnd.randrange(0, 256)))
            if sid == 10 and rnd.random() < 0.3:
                ts[0][1].append(effect(0x0B, 30, rnd.randrange(0, 256)))
            add('spell', sid, [caster] + ts, 0, targets=list(range(1, n + 1)), **g())
    for _ in range(12):  # 12 Enlarge: casting levels 1-6 and outside
        lv = [0] * 8
        lv[rnd.choice([0, 1, 2, 3, 4, 5, 6, 7])] = rnd.choice([1, 2, 3, 4, 5, 6, 7, 9, 12])
        add('spell', 12, [(creature(rnd, levels=lv), [], []), target(str=rnd.randrange(3, 19))], 0, targets=[1], **g())
    for _ in range(30):  # 21 Sleep: hit dice, race, status, 0x35, the 4d4 budget
        cs = [(creature(rnd, levels=lv_any()), [], [])]
        n = rnd.randrange(1, 9)
        for _i in range(n):
            c, ef, it = target(hd=rnd.choice([0, 1, 2, 3, 4, 5, 6, 9, 0xFE]), race=rnd.choice([0, 0, 1, 3]),
                               status=rnd.choice([0, 0, 0, 1, 4]))
            if rnd.random() < 0.2:
                ef.append(effect(0x35, 5))
            cs.append((c, ef, it))
        add('spell', 21, cs, 0, targets=list(range(1, n + 1)), **g())
    for sid in (23, 49):  # Hold Person: one to four targets
        for _ in range(20):
            n = rnd.randrange(1, 5)
            cs = [(creature(rnd, levels=lv_any()), [], [])] + \
                [target(type=rnd.choice([0, 1, 1, 2]), size=rnd.choice([1, 1, 2])) for _i in range(n)]
            add('spell', sid, cs, 0, targets=list(range(1, n + 1)), **g())
    for _ in range(25):  # 26 Slow Poison
        c, ef, it = target(status=rnd.choice([0, 0, 1, 4]), hp=rnd.choice([0, 0, 5]))
        if rnd.random() < 0.7:
            ef.insert(rnd.randrange(len(ef) + 1), effect(0x37, 0, 0xFF))
        add('spell', 26, [(creature(rnd, levels=lv_any()), [], []), (c, ef, it)], 0, targets=[1], **g())
    for _ in range(20):  # 27 Snake Charm: the whole creature list
        cs = [(creature(rnd, levels=lv_any(), hp=rnd.randrange(1, 60)), [], [])]
        for _i in range(rnd.randrange(1, 8)):
            cs.append(target(type=rnd.choice([0x0E, 0x0E, 0, 1]), hp=rnd.randrange(1, 30)))
        add('spell', 27, cs, 0, targets=[], **g())
    for v in [1, 2, 3, 4, 5, 6, 7, 9] * 3:  # 12 Enlarge: every class at the same level = the casting level
        add('spell', 12, [(creature(rnd, levels=[v] * 8), [], []), target(str=rnd.randrange(3, 19))], 0, targets=[1],
            **g())
    for _ in range(12):  # 14 Friends: Charisma near the cap of 25
        c, ef, it = target()
        c[0x15] = rnd.randrange(16, 26)
        add('spell', 14, [(creature(rnd, levels=lv_any()), [], []), (c, ef, it)], 0, targets=[1], **g())
    for sid in (3, 21, 23, 49, 13, 26):  # no target (62 heals the stale DS:6C95 then: not run)
        add('spell', sid, [(creature(rnd, levels=lv_any()), [], [])], 0, targets=[], **g())
    for _ in range(60):  # turn undead 0x13965 with the target search 0x13B4C (0x3154B: only the slot size zeroed, cells not compared)
        cs, places = placed(rnd.randrange(2, 10))
        lvl = rnd.choice([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 13, 14, 20, 0xF0])
        cs[0][0][0x96] = lvl
        sets = []
        for k in range(1, len(cs)):
            cs[k][0][0x76] = rnd.choice([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xFE])  # PoR rows are 1-10
            cs[k][0][0x10E] = rnd.choice([1, 1, 1, 0])
            if rnd.random() < 0.15:
                sets.append([k, 0x10, 1])
        add('turn', 0, cs, 0, targets=[], cells=bytes(arena()).hex(), places=places, fight=1, combatSet=sets)
    for _ in range(30):  # a high cleric among many weak undead: the 1d12 runs out while destroying (count back to 1)
        cs, places = placed(rnd.randrange(7, 12))
        cs[0][0][0x96] = rnd.choice([9, 11, 13, 14])
        for k in range(1, len(cs)):
            cs[k][0][0x76] = rnd.choice([1, 1, 2, 3])
            cs[k][0][0x10E] = 1
        add('turn', 0, cs, 0, targets=[], cells=bytes(arena()).hex(), places=places, fight=1)
    for _ in range(12):  # 1 Bless in a fight: targets with an enemy beside them leave the list
        cs, places = placed(rnd.randrange(3, 9))
        add('spell', 1, cs, 0, targets=list(range(len(cs))), cells=bytes(arena()).hex(), places=places,
            fight=1, aim=list(places[0][:2]), **g())
    for _ in range(30):  # 0x2BF2B: the morale check's removal of 0x4A then 0x4B (their removal handlers 74 / 75 run)
        cs, places = placed(rnd.randrange(2, 5))
        k = rnd.randrange(1, len(cs))
        ef = [effect(rnd.choice([0x4A, 0x4B, 0x4A, 0x4B, 0x16, 0x01]), 0, k + 1, 1) for _i in range(rnd.randrange(0, 5))]
        cs[0] = (cs[0][0], ef, cs[0][2])
        cs[k] = (cs[k][0], [effect(0x3A, 0, 0xFF)] if rnd.random() < 0.7 else [], cs[k][2])
        add('morale', 0, cs, 0, targets=[], cells=bytes(arena()).hex(), places=places, fight=1)


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = Oracle(argv[1])
    rnd = random.Random(20261117)
    cases = make_cases(rnd)
    for c in cases:
        try:
            c['result'] = o.run(c)
        except RuntimeError as x:
            raise RuntimeError('%s %d: %s' % (c['kind'], c['id'], x)) from x
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_spell_oracle.py)', 'cases': cases}
    with open(argv[2], 'w') as f:
        json.dump(out, f, indent=0)
    print('%d cases' % len(cases))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
