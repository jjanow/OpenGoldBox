#!/usr/bin/env python3
"""Combat cast and camp text fixtures from the program itself: runs the v1.3 routines (por_emu.py, unicorn) on
generated inputs and writes what they produce, for GoldBox.Core's CombatCastProgramTests (docs/re/spells-effects.md
"Combat cast", docs/re/camp.md "Rest").

Usage: python3 -I por_cast_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seed, so the output is the same every run):
- cast: the combat cast 0x14C0F(spell, ai, var done) for every spell id 0..67 and some above, ai 0 and 1, with the
  initiative around the casting delay. Trapped and recorded in call order: the spell list 0x239D6 (its arguments;
  returns a scripted id), the cast 0x27C38 (spell, ai, third argument, DS:6DBF; writes a scripted done), the
  messages START 0x2233 / 0x228C (their text), the redraws START 0x3728, 0x319D2 and START 0x1583. START 0x31D7 and
  the string library run. Recorded: the done byte and combat record bytes 0..7 before and after.
- list: the spell list builder 0x275B4(0) (the memorised list) on random slot bytes: the entries (DS:4766 chain,
  +0x29 heading flag) and the id table DS:476A; GetMem served from the emulator's arena.
- two: the rest-time digits 0x24331(v) for v = 0..255 (the Pascal string it returns).
The output holds the program's message texts besides bytes made up here; it goes in tests/fixtures-private.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_ai_oracle import AiOracle, ptr_bytes  # noqa: E402
from por_emu import Emu  # noqa: E402

COMBAT_CAST, SPELL_LIST, CAST, BUILD_LIST, TWO = 0x14C0F, 0x239D6, 0x27C38, 0x275B4, 0x24331
REDRAW_MAP = 0x319D2
MESSAGE_TEXT, MESSAGE_NAMED, CLEAR_TEXT, SHOW_SUMMARY = (0xBA, 0x1693), (0xBA, 0x16EC), (0xBA, 0x2B88), (0xBA, 0x9E3)


def pstring(e, lin):
    n = e.rb(lin, 1)[0]
    return e.rb(lin + 1, n).decode('latin-1')


def cast_cases(o, rnd):
    e = o.e
    calls = []
    script = {}

    def far_str(em, k):
        o_, s_ = em.arg(k), em.arg(k + 2)
        return pstring(em, s_ * 16 + o_)

    def on_list(em):
        # 0x239D6(kind, action, var position, var flag): kind at +0xA, action +8, position ptr +4, flag ptr +0
        calls.append({'op': 'list', 'kind': em.arg(0xA) & 0xFF, 'action': em.arg(8) & 0xFF,
                      'position': struct.unpack('<h', em.rb(em.arg(6) * 16 + em.arg(4), 2))[0],
                      'flag': em.rb(em.arg(2) * 16 + em.arg(0), 1)[0]})
        em.pop_ret(12, script['pick'])

    def on_cast(em):
        calls.append({'op': 'cast', 'spell': em.arg(8) & 0xFF, 'ai': em.arg(6) & 0xFF, 'b': em.arg(4) & 0xFF,
                      'flag': em.dsb(0x6DBF)})
        em.wb(em.arg(2) * 16 + em.arg(0), [script['done']])
        em.pop_ret(10)

    def on_text(em):
        calls.append({'op': 'text', 'text': far_str(em, 0)})
        em.pop_ret(4)

    def on_named(em):
        # START 0x228C(creature, text, colour, pause)
        calls.append({'op': 'named', 'text': far_str(em, 4), 'colour': em.arg(2) & 0xFF, 'pause': em.arg(0) & 0xFF})
        em.pop_ret(12)

    def simple(name, n):
        def f(em):
            calls.append({'op': name})
            em.pop_ret(n)
        return f

    def on_map(em):
        calls.append({'op': 'map', 'a': em.arg(2) & 0xFF, 'b': em.arg(0) & 0xFF})
        em.pop_ret(8)

    e.trap_ovr(SPELL_LIST, on_list)
    e.trap_ovr(CAST, on_cast)
    e.trap_ovr(REDRAW_MAP, on_map)
    e.trap_start(*MESSAGE_TEXT, on_text)
    e.trap_start(*MESSAGE_NAMED, on_named)
    e.trap_start(*CLEAR_TEXT, simple('clear', 0))
    e.trap_start(*SHOW_SUMMARY, simple('summary', 4))
    out = []
    spells = list(range(0, 68)) + [68, 70, 0x7F]
    for spell in spells:
        for ai in (0, 1):
            delay = o.spell(spell)[12] // 3 if spell <= 67 else None
            inits = [-128, -1, 0, 1, 127]
            if delay is not None:
                inits += [delay, delay + 1, delay - 1]
            for init in sorted(set(i for i in inits if -128 <= i <= 127)):
                o.fresh()
                ptrs = o.creatures([{'size': 1, 'inplay': 1, 'hostile': 0}])
                cur = ptrs[0]
                e.set_dsptr(0x5D92, cur)
                e.set_dsb(0x49F3, 5)
                e.set_dsb(0x6DBF, rnd.randrange(2))
                cr = o.combat_rec(cur)
                before = bytes([rnd.randrange(256), rnd.randrange(2), rnd.randrange(2), init & 0xFF] +
                               [rnd.randrange(256) for _ in range(4)])
                e.wb(cr, before)
                script['pick'] = rnd.choice([0, 3, 7, 21, 22, 23, 40, 47, 51, rnd.randrange(1, 68)])
                script['done'] = rnd.randrange(2)
                calls.clear()
                done = e.alloc(2)
                e.wb(o.lin(done), b'\xee')
                e.call(COMBAT_CAST, [spell, ai, *done])
                out.append({'spell': spell, 'ai': ai, 'pick': script['pick'], 'scripted': script['done'],
                            'item': e.dsb(0x6DBF), 'before': before.hex(), 'after': e.rb(cr, 8).hex(),
                            'done': e.rb(o.lin(done), 1)[0], 'calls': list(calls)})
    return out


def list_cases(o, rnd, n):
    e = o.e
    out = []
    for i in range(n):
        o.fresh()
        ptrs = o.creatures([{'size': 1, 'inplay': 1, 'hostile': 0}])
        cur = ptrs[0]
        e.set_dsptr(0x5D92, cur)
        pool = [0, 0, 0, rnd.randrange(1, 68), rnd.randrange(1, 68), 0x80 | rnd.randrange(1, 68), 0xFF, 0x7F, 0x45]
        slots = bytes(rnd.choice(pool) if rnd.random() < 0.6 else rnd.randrange(1, 68) for _ in range(21))
        if i % 4 == 0:  # sorted by level now and then, as Memorize leaves them, with duplicates
            slots = bytes(sorted(slots, key=lambda b: (b == 0, b)))
        e.wb(o.lin(cur) + 0x17, slots)
        e.set_dsptr(0x4766, (0, 0))
        any_, _ = e.call(BUILD_LIST, [0])
        entries = []
        p = e.dsptr(0x4766)
        while p != (0, 0) and len(entries) < 64:
            lin = o.lin(p)
            entries.append({'heading': e.rb(lin + 0x29, 1)[0], 'text': pstring(e, lin)})
            o_, s_ = struct.unpack('<HH', e.rb(lin + 0x2A, 4))
            p = (s_, o_)
        ids = list(e.rb(e.ds(0x476A), 21))
        out.append({'slots': slots.hex(), 'any': any_ & 0xFF, 'entries': entries, 'ids': ids})
    return out


def two_cases(o):
    e = o.e
    out = []
    for v in range(256):
        o.fresh()
        res = e.alloc(0x30)
        e.wb(o.lin(res), b'\xee' * 0x30)
        e.call(TWO, [res[0], res[1], v])
        out.append({'v': v, 'text': pstring(e, o.lin(res))})
    return out


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = AiOracle(argv[1])
    rnd = random.Random(20261122)
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_cast_oracle.py)'}
    out['two'] = two_cases(o)
    out['list'] = list_cases(o, rnd, 200)
    out['cast'] = cast_cases(o, rnd)
    with open(argv[2], 'w') as f:
        json.dump(out, f, separators=(',', ':'))
    print('two %d, list %d, cast %d' % (len(out['two']), len(out['list']), len(out['cast'])))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
