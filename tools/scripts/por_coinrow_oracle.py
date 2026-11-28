#!/usr/bin/env python3
"""The coin row parser (GAME.OVR 0x2581C) run by the program itself, for GoldBox.Core's CoinRowProgramTests
(docs/re/shop-format.md "The coin row parser 0x2581C").

Usage: python3 -I por_coinrow_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

The money Trade (0x23190), Drop (0x234E1) and Take (0x25974) hand it the text of the row the list routine returned
(the DS:0FFA name, left-padded by Trade and Drop, then the amount) and an output string; it returns the coin kind and
copies that kind's prompt label into the output. The cases are every DS:0FFA name as the callers build the rows
(padded, unpadded), in other cases of letters, and rows that match nothing. The routine calls only the library
(string copy, UpCase), so nothing is trapped. A row that matches nothing returns the uninitialised local [bp-1]: under
Emu.call that is the stack filler 0xEE, and the output string stays as it was. Rows that are empty or only spaces are
left out: the space skip then reads past the string into whatever the stack held. The output holds the program's
labels, so it is kept with the private fixtures.
"""
import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

PARSER = 0x2581C
NAMES_DS = 0xFFA            # the coin names: 7 Pascal strings of 11 bytes (Copper … Jewelry)


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def pstr(b):
    return b[1:1 + b[0]].decode('latin-1')


def rows(rnd, names):
    out = []
    width = max(len(n) for n in names)
    for n in names:
        amount = str(rnd.randrange(1, 65536))
        out += [n, n + ' ' + amount, n.rjust(width) + ' ' + amount, '  ' + n, n.upper() + ' 7', n.lower() + ' 7',
                n[0] + n[1:].upper(), n[0].lower() + n[1:], n[:1], n[:2]]
    # 'G' with every second letter, both cases, and the other first letters alone or with odd tails.
    for c in 'GEPSCJ':
        for d in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ 0':
            out.append(c + d)
    # No match: any other first character after the spaces.
    for ch in 'abcdefghijklmnopqrstuvwxyzABDFHIKLMNOQRTUVWXYZ0123456789!-.':
        out.append(' ' * rnd.randrange(3) + ch + 'xyz')
    for _ in range(60):
        n = rnd.randrange(1, 30)
        out.append(''.join(rnd.choice('GgEePpSsCcJjxyz 019') for _ in range(n)).rstrip() or 'Q')
    return [r for r in out if r.strip()]


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    gamedir, out = sys.argv[1], sys.argv[2]
    e = Emu(open(find(gamedir, 'START.EXE'), 'rb').read(), open(find(gamedir, 'GAME.OVR'), 'rb').read())
    names = [pstr(e.rb(e.ds(NAMES_DS + 11 * k), 11)) for k in range(7)]
    rnd = random.Random(20261128)
    cases = []
    for row in rows(rnd, names):
        e.reset_heap()
        rs, ro = e.alloc(256)
        os_, oo = e.alloc(256)
        b = row.encode('latin-1')
        e.wb(rs * 16 + ro, bytes([len(b)]) + b)
        e.call(PARSER, (rs, ro, os_, oo))
        al = e.uc.reg_read(__import__('unicorn').x86_const.UC_X86_REG_AX) & 0xFF
        label = pstr(e.rb(os_ * 16 + oo, 256))
        cases.append({'row': row, 'kind': al, 'label': label})
    with open(out, 'w') as f:
        json.dump({'source': 'GAME.OVR v1.3 0x2581C run under unicorn (tools/scripts/por_coinrow_oracle.py)',
                   'residue': 0xEE, 'names': names, 'cases': cases}, f, indent=0)
    print('%d cases' % len(cases))
    return 0


if __name__ == '__main__':
    sys.exit(main())
