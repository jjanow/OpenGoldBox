#!/usr/bin/env python3
"""The game clock, the effect timeout, the camp's rest time and the rest loop run by the program itself: the v1.3
routines of GAME.OVR unit 0x8C (0x23DCC–0x24C30) under unicorn (por_emu.py), for GoldBox.Core's ClockProgramTests
(docs/re/camp.md "Rest", "Rest-time arithmetic"; GameClock, CampTime, RestTimeMenu, CampRest).

Usage: python3 -I por_clock_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seeds, so the output is the same every run):
- advance: the clock routine 0x241A1(count, unit) with its carry 0x240BE and the effect timeout 0x23DCC run, in camp
  (DS:49F3 = 2, the flags DS:471E and the party size $6E3E as given) or outside; the effect removal 0x2AF10 is trapped
  (logged, the record unlinked);
- resttime: the rest-time carry 0x2416F and borrow 0x24246(count, unit) on DS:6DC2;
- menu: the rest-time menu 0x244ED with the word menu 0x2CC8C answering a scripted key list (the rest time drawn by
  0x243BC is trapped);
- rest: the rest loop 0x24A66 whole (ticks, heal count 0x2463D, preparation 0x248D6 / 0x2497E with the memorising
  0x246F1 and scribing 0x247B3 and the scroll removal 0x2A2A2, the interruption roll), with the rest-time menu trapped
  (rest or leave), the key poll scripted, the heal 0x2C647 trapped (+1 HP below the maximum, logged), 0x2AF10 trapped
  as above and the item removal START 0x1509 trapped (the item unlinked). Drawing, text and key waits are trapped.
The records are made up here; the output holds spell levels and base item slots read from the program and ITEMS, so
it belongs in tests/fixtures-private only.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

REC, ITEM = 0x11D, 63
ADVANCE, CARRY, BORROW, MENU, REST = 0x241A1, 0x2416F, 0x24246, 0x244ED, 0x24A66
REMOVE_EFFECT, HEAL, SHOW_TIME, WORD_MENU, YES_NO, CLEAR_ROW, SPELL_NAME = (
    0x2AF10, 0x2C647, 0x243BC, 0x2CC8C, 0x2D9A0, 0x2CFCA, 0x2A332)
CLEAR_BOX, PRINT, PARTY_LIST, KEY_WAIT, CLEAR_TEXT, KEY_PRESSED, ITEM_REMOVE = (
    (0x709, 0xDD3), (0x709, 0x626), (0xBA, 0x767), (0x709, 0xDBE), (0xBA, 0x17E3), (0xA40, 0x2FD), (0xBA, 0x1509))
CLOCK_OFF = (0x49C6 * 2 + 0x6E00) & 0xFFFF       # the clock cells in zone 0 (DS:49D2), word per cell
UNIT_BASE = 0x23DCC


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def ptr_bytes(p):
    return struct.pack('<HH', p[1], p[0]) if p else b'\0\0\0\0'


class ClockOracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        with open(find(gamedir, 'ITEMS'), 'rb') as f:
            self.base = f.read()[2:2 + 16 * 128]
        self.log = []
        self.keys = []
        self.answers = []
        self.menu_keys = []
        self.rest_choice = 1
        e.trap_ovr(REMOVE_EFFECT, self._remove_effect)
        e.trap_ovr(HEAL, self._heal)
        e.trap_ovr(SHOW_TIME, Emu.ret(2))
        e.trap_ovr(WORD_MENU, self._word_menu)
        e.trap_ovr(YES_NO, lambda em: em.pop_ret(0xA, self.answers.pop(0) if self.answers else ord('N')))
        e.trap_ovr(CLEAR_ROW, Emu.ret(0))
        e.trap_ovr(SPELL_NAME, self._spell_name)
        e.trap_start(*CLEAR_BOX, Emu.ret(8))
        e.trap_start(*PRINT, Emu.ret(0xA))
        e.trap_start(*PARTY_LIST, Emu.ret(4))
        e.trap_start(*KEY_WAIT, Emu.ret(0))
        e.trap_start(*CLEAR_TEXT, Emu.ret(0))
        e.trap_start(*KEY_PRESSED, lambda em: em.pop_ret(0, self.keys.pop(0) if self.keys else 0))
        e.trap_start(*ITEM_REMOVE, self._item_remove)

    # ---- set-up ----
    def fresh(self):
        e = self.e
        e.reset_heap()
        self.zone0 = e.alloc(0x400)
        e.set_dsptr(0x49D2, self.zone0)
        self.zone1 = e.alloc(0x800)
        e.set_dsptr(0x49D6, self.zone1)
        e.wb(e.ds(0x5582), self.base)
        e.wb(e.ds(0x471E), bytes(72))
        self.log = []

    def lin(self, p):
        return p[0] * 16 + p[1]

    def set_clock(self, t):
        self.e.wb(self.lin(self.zone0) + CLOCK_OFF, struct.pack('<7H', *t))

    def clock(self):
        return list(struct.unpack('<7H', self.e.rb(self.lin(self.zone0) + CLOCK_OFF, 14)))

    def set_zone1(self, off, v):
        self.e.wb(self.lin(self.zone1) + off, struct.pack('<H', v & 0xFFFF))

    def rest_time(self):
        return list(struct.unpack('<7H', self.e.rb(self.e.ds(0x6DC2), 14)))

    def set_rest_time(self, t):
        self.e.wb(self.e.ds(0x6DC2), struct.pack('<7H', *t))

    def make_party(self, specs):
        """specs: dicts with record bytes 'bytes' {offset: value}, 'age', 'effects' [(id, minutes)], 'items' [bytes]."""
        e = self.e
        self.ptrs = []
        for s in specs:
            r = e.alloc(REC)
            rl = self.lin(r)
            for off, v in s.get('bytes', {}).items():
                e.wb(rl + int(off), [v])
            e.wb(rl + 0x30, struct.pack('<H', s.get('age', 0)))
            prev = None
            for eid, mins in reversed(s.get('effects', [])):
                p = e.alloc(9)
                e.wb(self.lin(p), bytes([eid, mins & 0xFF, mins >> 8, 0, 0]) + ptr_bytes(prev))
                prev = p
            e.wb(rl + 0x7F, ptr_bytes(prev))
            prev = None
            for it in reversed(s.get('items', [])):
                p = e.alloc(ITEM)
                b = bytearray(it)
                b[0x2A:0x2E] = ptr_bytes(prev)
                e.wb(self.lin(p), bytes(b))
                prev = p
            e.wb(rl + 0xC8, ptr_bytes(prev))
            self.ptrs.append(r)
        for a, b in zip(self.ptrs, self.ptrs[1:] + [None]):
            e.wb(self.lin(a) + 0x104, ptr_bytes(b))
        e.set_dsptr(0x5D96, self.ptrs[0] if self.ptrs else (0, 0))
        e.set_dsptr(0x5D92, self.ptrs[0] if self.ptrs else (0, 0))

    def index(self, p):
        return self.ptrs.index(p) if p in self.ptrs else -1

    def walk(self, p, nxt):
        out = []
        o, s = struct.unpack('<HH', self.e.rb(p, 4))
        while (s, o) != (0, 0) and len(out) < 64:
            out.append((s, o))
            o, s = struct.unpack('<HH', self.e.rb(self.lin((s, o)) + nxt, 4))
        return out

    def effects(self, r):
        return [[b[0], b[1] | b[2] << 8] for b in (self.e.rb(self.lin(p), 3) for p in self.walk(self.lin(r) + 0x7F, 5))]

    def items(self, r):
        return [self.e.rb(self.lin(p) + 0x2E, ITEM - 0x2E).hex() for p in self.walk(self.lin(r) + 0xC8, 0x2A)]

    def detach(self, head_lin, nxt, target):
        e = self.e
        cur = head_lin
        while True:
            o, s = struct.unpack('<HH', e.rb(cur, 4))
            if (s, o) == (0, 0):
                raise RuntimeError('record not in its list')
            if (s, o) == target:
                e.wb(cur, e.rb(self.lin(target) + nxt, 4))
                return
            cur = self.lin((s, o)) + nxt

    # ---- traps ----
    def _remove_effect(self, e):
        # 0x2AF10(creature, id, record): words above the return address: record (off, seg), id, creature (off, seg)
        rec = (e.arg(2), e.arg(0))
        who = (e.arg(8), e.arg(6))
        k = self.walk(self.lin(who) + 0x7F, 5).index(rec)
        self.log.append(['remove', self.index(who), k, e.arg(4) & 0xFF])
        self.detach(self.lin(who) + 0x7F, 5, rec)
        e.pop_ret(0xA)

    def _heal(self, e):
        # 0x2C647(creature, 1, 0): +1 HP below the maximum (the test's rule; the routine itself is checked elsewhere)
        who = (e.arg(6), e.arg(4))
        rl = self.lin(who)
        hp, mx = e.rb(rl + 0x11B)[0], e.rb(rl + 0x32)[0]
        ok = hp < mx
        if ok:
            e.wb(rl + 0x11B, [hp + 1])
        self.log.append(['heal', self.index(who), int(ok)])
        e.pop_ret(8, int(ok))

    def _spell_name(self, e):
        # 0x2A332(creature, text, spell); the return address tells memorising (0x2478B) from scribing (0x248A6)
        ip = struct.unpack('<H', e.rb(e.stack(), 2))[0]
        kind = {0x2478B - UNIT_BASE: 'memorised', 0x248A6 - UNIT_BASE: 'scribed'}.get(ip, 'other')
        self.log.append([kind, self.index((e.arg(8), e.arg(6))), e.arg(0) & 0xFF])
        e.pop_ret(0xA)

    def _item_remove(self, e):
        # START 0x1509(creature, item): the item leaves the creature's list (freeing it is not modelled)
        item = (e.arg(2), e.arg(0))
        who = (e.arg(6), e.arg(4))
        self.detach(self.lin(who) + 0xC8, 0x2A, item)
        self.log.append(['dropped', self.index(who)])
        e.pop_ret(8)

    def _word_menu(self, e):
        # 0x2CC8C(..., var extended): words above the return address: extended (off, seg), then the menu's arguments
        key, ext = self.menu_keys.pop(0) if self.menu_keys else (ord('E'), 0)
        e.wb(e.arg(2) * 16 + e.arg(0), [ext])
        e.pop_ret(0x16, key)


def rnd_time(rnd):
    return [rnd.randrange(10), rnd.choice([0, 1, 5, 9, 10, 15]), rnd.choice([0, 3, 5, 6, 7]), rnd.choice([0, 5, 23, 24, 30]),
            rnd.choice([0, 1, 29, 30, 50, 99, 100]), rnd.choice([0, 0, 1, 11, 12]), rnd.choice([0, 1, 255, 256])]


def advance_cases(o, rnd, n):
    e = o.e
    out = []
    for _ in range(n):
        o.fresh()
        size = rnd.randint(1, 8)
        specs = []
        for _k in range(size):
            effs = [(rnd.choice([1, 9, 0x15, 0x2D, 0x40, 0x4A]),
                     rnd.choice([0, 0, 1, 2, 5, 9, 10, 11, 20, 59, 60, 600, 1440, 0xFFFF]))
                    for _j in range(rnd.choice([0, 0, 1, 2, 3, 4, 6]))]
            specs.append({'age': rnd.choice([0, 20, 0xFFFF]), 'effects': effs})
        o.make_party(specs)
        clock = rnd_time(rnd)
        o.set_clock(clock)
        camping = rnd.random() < 0.4
        e.set_dsb(0x49F3, 2 if camping else rnd.choice([0, 1, 3, 5]))
        flags = [rnd.choice([0, 0, 1]) for _ in range(72)]
        e.wb(e.ds(0x471E), bytes(flags))
        party_size = rnd.choice([size, size, 0, 1, 12])
        o.set_zone1(0x67C, party_size)
        unit = rnd.choice([1, 1, 1, 2, 3, 4, 5, 6])
        count = rnd.choice([0, 1, 1, 5, 10, 12, 59, 100, 255])
        e.call(ADVANCE, [count, unit])
        out.append({'party': specs, 'clock': clock, 'camping': int(camping), 'flags': flags, 'partySize': party_size,
                    'unit': unit, 'count': count, 'clockAfter': o.clock(),
                    'ages': [struct.unpack('<H', e.rb(o.lin(p) + 0x30, 2))[0] for p in o.ptrs],
                    'effectsAfter': [o.effects(p) for p in o.ptrs], 'flagsAfter': list(e.rb(e.ds(0x471E), 72)),
                    'log': o.log})
    return out


def resttime_cases(o, rnd, n):
    e = o.e
    out = []
    for i in range(n):
        o.fresh()
        age0 = rnd.choice([0, 7])
        o.make_party([{'age': age0}])
        t = rnd_time(rnd)
        if rnd.random() < 0.3:
            t[4], t[5] = rnd.choice([0, 1, 3, 98]), 0
        if rnd.random() < 0.2:
            t = [0] * 7
            t[rnd.choice([1, 2, 3, 4])] = rnd.choice([0, 1, 2])
        o.set_rest_time(t)
        op = 'carry' if i % 3 == 0 else 'borrow'
        unit = rnd.choice([1, 2, 3, 4])
        count = rnd.choice([0, 1, 1, 5, 5, 10, 60, 255])
        if op == 'carry':
            e.call(CARRY)
        else:
            e.call(BORROW, [count, unit])
        out.append({'op': op, 'time': t, 'unit': unit, 'count': count, 'ageBefore': age0, 'after': o.rest_time(),
                    'age': struct.unpack('<H', e.rb(o.lin(o.ptrs[0]) + 0x30, 2))[0]})
    return out


MENU_KEYS = [(ord(c), 0) for c in 'YHMIIDDXQ'] + [(0x48, 1), (0x50, 1), (0x4B, 1), (0x4D, 1), (0x47, 1), (0x3B, 1)]


def menu_cases(o, rnd, n):
    out = []
    for _ in range(n):
        o.fresh()
        o.make_party([{'age': 5}])
        t = rnd_time(rnd)
        t[5], t[6] = rnd.choice([0, 0, 1]), 0
        o.set_rest_time(t)
        keys = [rnd.choice(MENU_KEYS) for _k in range(rnd.randint(0, 25))]
        keys.append(rnd.choice([(ord('R'), 0), (13, 0), (ord('E'), 0), (0, 0)]))
        o.menu_keys = list(keys)
        r = o.e.call(MENU)[0] & 0xFF
        out.append({'time': t, 'keys': keys, 'result': r, 'after': o.rest_time(), 'left': len(o.menu_keys)})
    return out


def scroll_item(o, rnd, spells):
    scroll_types = [t for t in range(128) if 11 <= o.base[16 * t] <= 13]
    other_types = [t for t in range(128) if not 11 <= o.base[16 * t] <= 13]
    b = bytearray(ITEM)
    readable = rnd.random() < 0.8
    b[0x2E] = rnd.choice(scroll_types if readable else other_types)
    sp = [rnd.choice(spells) | (0x80 if rnd.random() < 0.6 else 0) if rnd.random() < 0.8 else 0 for _ in range(3)]
    b[0x3C:0x3F] = bytes(sp)
    b[0x30] = 0xD1 + sum(1 for s in sp if s)
    return bytes(b)


def rest_cases(o, rnd, n):
    e = o.e
    spells = [s for s in range(1, 60)]
    e.trap_ovr(MENU, lambda em: em.pop_ret(0, o.rest_choice))   # the rest-time menu: rest or leave (run in 'menu')
    out = []
    for _ in range(n):
        o.fresh()
        size = rnd.randint(1, 6)
        specs = []
        for _k in range(size):
            mx = rnd.randint(1, 60)
            bts = {0x32: mx, 0x11B: rnd.randint(0, mx), 0x2C: rnd.choice([0, 0, 0, 1, 2, 4, 6, 0xFF])}
            for i in range(21):
                if rnd.random() < 0.25:
                    bts[0x17 + i] = rnd.choice(spells) | (0x80 if rnd.random() < 0.6 else 0)
            effs = [(rnd.choice([1, 9, 0x40]), rnd.choice([0, 5, 10, 60, 300, 2000])) for _j in range(rnd.choice([0, 0, 1, 2]))]
            items = [scroll_item(o, rnd, spells) for _j in range(rnd.choice([0, 0, 1, 2, 3]))]
            specs.append({'bytes': {str(k): v for k, v in bts.items()}, 'effects': effs, 'items': [i.hex() for i in items]})
        o.make_party([dict(s, items=[bytes.fromhex(i) for i in s['items']]) for s in specs])
        clock = rnd_time(rnd)
        clock[6] = rnd.choice([0, 1])
        o.set_clock(clock)
        e.set_dsb(0x49F3, 2)
        rt = [0, rnd.randrange(10), rnd.randrange(6), rnd.choice([0, 1, 4, 8, 23]), rnd.choice([0, 0, 0, 1, 2]), 0, 0]
        o.set_rest_time(rt)
        heal_ticks = rnd.choice([0, 100, 280, 287, 0x11F])
        e.set_dsw(0x6DD8, heal_ticks)
        interval = rnd.choice([0, 3, 50])
        e.set_dsw(0x49FB, interval)
        safe = rnd.choice([0, 0, 6, 12, 48])
        pct = rnd.choice([0, 10, 50, 100])
        o.set_zone1(0x5A4, safe)
        o.set_zone1(0x5A6, pct)
        o.set_zone1(0x67C, size)
        o.rest_choice = 1 if rnd.random() < 0.9 else 0
        keys, answers = [], []
        for _t in range(600):
            k = 1 if rnd.random() < 0.01 else 0
            keys.append(k)
            if k:
                answers.append(rnd.choice([ord('Y'), ord('N')]))
        o.keys, o.answers = list(keys), list(answers)
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        r = e.call(REST)[0] & 0xFF
        polled = len(keys) - len(o.keys)
        out.append({'party': specs, 'clock': clock, 'restTime': rt, 'healTicks': heal_ticks, 'interval': interval,
                    'safe': safe, 'percent': pct, 'rest': o.rest_choice, 'keys': keys[:polled],
                    'answers': answers[:len(answers) - len(o.answers)], 'seed': seed,
                    'result': r, 'clockAfter': o.clock(), 'restTimeAfter': o.rest_time(),
                    'healTicksAfter': e.dsw(0x6DD8), 'intervalAfter': e.dsw(0x49FB),
                    'prepTimers': list(e.rb(e.ds(0x6DD0), size)), 'seedAfter': e.randseed(),
                    'records': [e.rb(o.lin(p), REC).hex() for p in o.ptrs],
                    'effectsAfter': [o.effects(p) for p in o.ptrs], 'itemsAfter': [o.items(p) for p in o.ptrs],
                    'log': o.log})
    return out


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = ClockOracle(argv[1])
    rnd = random.Random(20261125)
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_clock_oracle.py)'}
    out['advance'] = advance_cases(o, rnd, 400)
    out['resttime'] = resttime_cases(o, rnd, 400)
    out['menu'] = menu_cases(o, rnd, 200)
    out['rest'] = rest_cases(o, rnd, 150)
    with open(argv[2], 'w') as f:
        json.dump(out, f, separators=(',', ':'))
    print(' '.join('%s %d' % (k, len(v)) for k, v in out.items() if isinstance(v, list)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
