#!/usr/bin/env python3
"""Make a fight test copy of a scratch game folder (Pool of Radiance DOS v1.3; dosbox-trace-method.md "s30").

    python3 -I fight_save.py BASE OUT CODEHEX [options]

BASE is a scratch game copy (por_dosbox_game.py; never a master); OUT a new folder (copied from BASE). Save A's script
area gets vectors 1-4 -> EXIT and vector 0 (one step forward) -> the code at $9920. No game data is stored here.


CODEHEX: the vector-0 code (hex bytes) at $9920; EXIT (00) appended. Options:
  --mon D          copy MON<D>CHA/ITM/SPC and CPIC<D> over the disk-2 names
  --hp N           every member's HP (0x32, 0xB1, 0x11B)
  --spells I:a,b   member I (1-6) memorised spells at 0x17 (rest cleared)
  --monpatch ID:OFF=VAL[,OFF=VAL]  patch monster record ID in MON2CHA.DAX (after --mon) and repack the file
  --data ADDR:HEX  extra bytes in the script area at $ADDR (>= $9920 + code)
  --char I:OFF=VAL member record byte
  --vm ADDR=VAL    a word of the save's $6B00 area (e.g. 6DC6=0, the morale threshold)
  --item I:FILE:BLOCK:REC[:OFF=VAL,...]  append record REC of block BLOCK of FILE (ITEM<d>.DAX, MON<d>ITM.DAX) to
                   member I's .ITM (item count 0xC7 + 1), with bytes changed (e.g. 0x34=0 not readied, 0x35=0 identified)
"""
import shutil
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import dax  # noqa

SCRIPT = 5121


def pack_rle(raw):
    out = bytearray()
    i = 0
    n = len(raw)
    while i < n:
        j = i
        while j < n and j - i < 128 and raw[j] == raw[i]:
            j += 1
        if j - i >= 3:
            out += bytes([256 - (j - i), raw[i]])
            i = j
            continue
        j = i
        while j < n and j - i < 128:
            if j + 2 < n and raw[j] == raw[j + 1] == raw[j + 2]:
                break
            j += 1
        out += bytes([j - i - 1]) + raw[i:j]
        i = j
    return bytes(out)


def rewrite_dax(path, patches):
    f = dax.DaxFile(str(path))
    blocks = [(e.id, bytearray(f.block(e))) for e in f.entries]
    for bid, b in blocks:
        for off, val in patches.get(bid, []):
            b[off] = val
    index = bytearray()
    data = bytearray()
    for bid, b in blocks:
        comp = pack_rle(bytes(b))
        assert dax.unrle(comp, len(b)) == bytes(b)
        index += struct.pack('<BiHH', bid, len(data), len(b), len(comp))
        data += comp
    Path(path).write_bytes(struct.pack('<H', len(index)) + index + data)


def main(a):
    base, out, code = Path(a[0]), Path(a[1]), bytes.fromhex(a[2])
    if out.exists():
        raise SystemExit('exists: %s' % out)
    shutil.copytree(base, out)
    opts = a[3:]
    monpatch = {}
    data = []
    vm = []
    i = 0
    while i < len(opts):
        k, v = opts[i], opts[i + 1]
        i += 2
        if k == '--mon':
            for kind in ('CHA', 'ITM', 'SPC'):
                src = out / ('MON%s%s.DAX' % (v, kind))
                if src.exists():
                    shutil.copyfile(src, out / ('MON2%s.DAX' % kind))
            shutil.copyfile(out / ('CPIC%s.DAX' % v), out / 'CPIC2.DAX')
        elif k == '--hp':
            for m in range(1, 7):
                p = out / ('CHRDATA%d.SAV' % m)
                r = bytearray(p.read_bytes())
                for o in (0x32, 0xB1, 0x11B):
                    r[o] = int(v)
                p.write_bytes(r)
        elif k == '--spells':
            m, _, ids = v.partition(':')
            p = out / ('CHRDATA%s.SAV' % m)
            r = bytearray(p.read_bytes())
            sp = [int(x) for x in ids.split(',') if x]
            r[0x17:0x2C] = bytes(sp + [0] * (21 - len(sp)))
            p.write_bytes(r)
        elif k == '--char':
            m, _, rest = v.partition(':')
            p = out / ('CHRDATA%s.SAV' % m)
            r = bytearray(p.read_bytes())
            for kv in rest.split(','):
                o, _, val = kv.partition('=')
                r[int(o, 0)] = int(val, 0)
            p.write_bytes(r)
        elif k == '--item':
            parts = v.split(':')
            m, src, block, rec = parts[0], parts[1], int(parts[2]), int(parts[3])
            f = dax.DaxFile(str(out / src))
            raw = next(f.block(e) for e in f.entries if e.id == block)
            item = bytearray(raw[rec * 63:rec * 63 + 63])
            assert len(item) == 63, 'no record %d in block %d' % (rec, block)
            for kv in (parts[4].split(',') if len(parts) > 4 and parts[4] else []):
                o, _, val = kv.partition('=')
                item[int(o, 0)] = int(val, 0)
            p = out / ('CHRDATA%s.ITM' % m)
            p.write_bytes((p.read_bytes() if p.exists() else b'') + bytes(item))
            c = out / ('CHRDATA%s.SAV' % m)
            r = bytearray(c.read_bytes())
            r[0xC7] += 1
            c.write_bytes(r)
        elif k == '--monpatch':
            mid, _, rest = v.partition(':')
            for kv in rest.split(','):
                o, _, val = kv.partition('=')
                monpatch.setdefault(int(mid), []).append((int(o, 0), int(val, 0)))
        elif k == '--data':
            addr, _, hx = v.partition(':')
            data.append((int(addr, 16), bytes.fromhex(hx)))
        elif k == '--vm':
            addr, _, val = v.partition('=')
            vm.append((int(addr, 16), int(val, 0)))
        else:
            raise SystemExit('unknown option ' + k)
    if monpatch:
        rewrite_dax(out / 'MON2CHA.DAX', monpatch)
    d = bytearray((out / 'SAVGAMA.DAT').read_bytes())
    vectors = b''.join(b'\x01\x01' + struct.pack('<H', v) for v in (0x9920, 0x991F, 0x991F, 0x991F, 0x991F))
    area = bytearray(vectors + bytes(0x1F - 0x14) + b'\x00' + code + b'\x00')
    area += bytes(7680 - len(area))
    for addr, hx in data:
        o = addr - 0x9900
        assert o >= 0x20 + len(code) + 1
        area[o:o + len(hx)] = hx
    d[SCRIPT:SCRIPT + 7680] = area
    for addr, val in vm:  # a $6B00-area cell straight into the save (a script SAVE to $6DC6 hung DOSBox-X, s30)
        assert 0x6B00 <= addr < 0x6F00
        struct.pack_into('<H', d, 2049 + 2 * (addr - 0x6B00), val)
    (out / 'SAVGAMA.DAT').write_bytes(d)
    print('ready', out)


if __name__ == '__main__':
    main(sys.argv[1:])
