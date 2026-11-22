#!/usr/bin/env python3
"""Test saves for screen checks in DOSBox-X and the Client (Pool of Radiance DOS v1.3).

    python3 -I por_test_saves.py GAMEDIR [SLOT=NAME ...]

Writes SAVGAMB..I.DAT into a scratch game copy (never a master; existing files of those slots are replaced), each
being SAVGAMA.DAT with its script area (save offset 5121 = $9900) replaced: vectors 1-4 point at an EXIT at $991F,
vector 0 (one step forward) at the test code at $9920. Vector 4 does not run LOADPIECES, so both the program and the
Client (since s23) show the saved wall sets. Slots (docs/re/start-menu.md §8,
training.md, combat-format.md, ecl-format.md "DAMAGE"):

    B  DAMAGE 0xE0, 1, 1, 200, 0: the whole party killed by the script (six lines, the wipe window)
    C  DAMAGE 0xE0, 1, 1, 0, 0 (1 point to everyone), then PROGRAM 0 (the training hall)
    D  a 1-HP copy of save A's party (CHRDATD1-6) against eight goblin guards: the party destroyed after a fight
    E  ADDNPC 73 (a gnoll, share 7), TREASURE 100 copper, COMBAT with no monsters: the NPC share window, the summary
    F  PICTURE 2, CLRMON, TREASURE (no items), SAVE 1 -> $6E6C, COMBAT: a shop
    G  CLRMON, SAVE 1 -> $6DE2, COMBAT: a temple
    H  PROGRAM 8: the end game (FINAL5.DAX is copied to FINAL2.DAX when missing)
    I  PRINT, PROGRAM 0, PRINT, a one-word HMENU: the text box and cursor after the hall

SLOT=NAME writes the named save into slot B..I instead (the program loads A..J; A and J are the release's own):

    loot-shop    CLRMON, TREASURE 100 copper, COMBAT (a loot), then TREASURE (no items), SAVE 1 -> $6E6C, COMBAT: a shop
    loot-temple  the same loot, then SAVE 1 -> $6DE2, COMBAT: a temple
    inputstr     PRINT a question, INPUTSTR into $9F00, PRINT, a one-word HMENU: the text input's screen
    long-text    PRINTCLR of a 255-character text, then a one-word HMENU: the text routine's 255 case (START 0x77F8)

The texts written by I are this tool's own. Nothing from the game is stored in this file.
The folder can stay GOLDBOX_POR_DIR afterwards: the tests that count the release's files read a copy without these.
"""
import shutil
import struct
import sys
from pathlib import Path

SCRIPT = 5121      # save offset of $9900
STEMS = 12809      # 8 x string[40]: the members' file stems
DAMAGE, PROGRAM, CLRMON, LOADMON, COMBAT, TREASURE, ADDNPC, SAVE, PICTURE, PRINT, HMENU = (
    0x2E, 0x38, 0x1C, 0x0B, 0x24, 0x27, 0x36, 0x09, 0x0E, 0x11, 0x2B)
INPUTSTR, PRINTCLR = 0x10, 0x12


def imm(*vals):
    """Type-0 (8-bit immediate) operands."""
    return b''.join(b'\x00' + bytes([v]) for v in vals)


def text(s):
    """A type-0x80 inline text: 6-bit codes, most significant bit first (docs/re/ecl-format.md)."""
    codes = [ord(c) - 64 if ord(c) >= 0x40 else ord(c) for c in s]
    bits = ''.join(format(c, '06b') for c in codes)
    bits += '0' * (-len(bits) % 8)
    b = bytes(int(bits[i:i + 8], 2) for i in range(0, len(bits), 8))
    return b'\x80' + bytes([len(b)]) + b


LOOT = bytes([CLRMON, TREASURE]) + imm(100, 0, 0, 0, 0, 0, 0, 255) + bytes([COMBAT])
NAMED = {
    'loot-shop': LOOT + bytes([TREASURE]) + imm(0, 0, 0, 0, 0, 0, 0, 255) + bytes([SAVE]) + imm(1) + b'\x01\x6c\x6e'
    + bytes([COMBAT]),
    'loot-temple': LOOT + bytes([SAVE]) + imm(1) + b'\x01\xe2\x6d' + bytes([COMBAT]),
    'inputstr': bytes([PRINT]) + text('A QUESTION ABOVE THE TEXT INPUT.') + bytes([INPUTSTR]) + imm(40) + b'\x01\x00\x9f'
    + bytes([PRINT]) + text('DONE.') + bytes([HMENU]) + b'\x01\x82\x6e' + imm(1) + text('WAIT'),
    'long-text': bytes([PRINTCLR]) + text(('LONG TEST TEXT FOR THE PRINT ' * 9)[:255]) + bytes([HMENU]) + b'\x01\x82\x6e'
    + imm(1) + text('WAIT'),
}


def main(argv):
    named = [a.split('=', 1) for a in argv[2:]]
    if len(argv) < 2 or any(len(n) != 2 or len(n[0]) != 1 or n[0] not in 'BCDEFGHI' or n[1] not in NAMED
                            for n in named):
        print(__doc__, file=sys.stderr)
        return 2
    game = Path(argv[1])
    base = (game / 'SAVGAMA.DAT').read_bytes()

    def save(slot, code, chars=None):
        d = bytearray(base)
        vectors = b''.join(b'\x01\x01' + struct.pack('<H', v) for v in (0x9920, 0x991F, 0x991F, 0x991F, 0x991F))
        area = vectors + bytes(0x1F - 0x14) + b'\x00' + code + b'\x00'
        d[SCRIPT:SCRIPT + 7680] = area + bytes(7680 - len(area))
        if chars:
            for i in range(6):
                stem = ('CHRDAT%s%d' % (chars, i + 1)).encode()
                o = STEMS + 41 * i
                d[o:o + 41] = bytes([len(stem)]) + stem + bytes(40 - len(stem))
        (game / ('SAVGAM%s.DAT' % slot)).write_bytes(d)

    save('B', bytes([DAMAGE]) + imm(0xE0, 1, 1, 200, 0))
    save('C', bytes([DAMAGE]) + imm(0xE0, 1, 1, 0, 0) + bytes([PROGRAM]) + imm(0))
    for i in range(1, 7):
        for ext in ('SAV', 'ITM', 'SPC'):
            src = game / ('CHRDATA%d.%s' % (i, ext))
            if src.exists():
                shutil.copyfile(src, game / ('CHRDATD%d.%s' % (i, ext)))
        rec = bytearray((game / ('CHRDATD%d.SAV' % i)).read_bytes())
        rec[0x11B] = 1  # current HP
        (game / ('CHRDATD%d.SAV' % i)).write_bytes(rec)
    save('D', bytes([CLRMON, LOADMON]) + imm(2, 8, 2) + bytes([COMBAT]), chars='D')
    save('E', bytes([CLRMON, ADDNPC]) + imm(73, 100) + bytes([TREASURE]) + imm(100, 0, 0, 0, 0, 0, 0, 255)
         + bytes([COMBAT]))
    save('F', bytes([PICTURE]) + imm(2) + bytes([CLRMON, TREASURE]) + imm(0, 0, 0, 0, 0, 0, 0, 255) + bytes([SAVE])
         + imm(1) + b'\x01\x6c\x6e' + bytes([COMBAT]))
    save('G', bytes([CLRMON, SAVE]) + imm(1) + b'\x01\xe2\x6d' + bytes([COMBAT]))
    save('H', bytes([PROGRAM]) + imm(8))
    if not (game / 'FINAL2.DAX').exists():
        shutil.copyfile(game / 'FINAL5.DAX', game / 'FINAL2.DAX')
    save('I', bytes([PRINT]) + text('FIRST TEXT BEFORE THE HALL.') + bytes([PROGRAM]) + imm(0) + bytes([PRINT])
         + text('SECOND.') + bytes([HMENU]) + b'\x01\x82\x6e' + imm(1) + text('WAIT'))
    for slot, name in named:
        save(slot, NAMED[name])
        print(name, 'in slot', slot)
    print('saves B-I written to', game)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
