"""A2R2 flux -> sectors via woz.decode_track. Usage: python3 -I a2r.py file.a2r out.phys"""
import sys, os, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import woz

def captures(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'A2R2' and b[4:8] == b'\xff\x0a\x0d\x0a', b[:8]
    off = 8; caps = {}
    while off + 8 <= len(b):
        cid = b[off:off+4]; ln = struct.unpack('<I', b[off+4:off+8])[0]; o = off + 8
        if cid == b'STRM':
            e = o + ln
            while o < e and b[o] != 0xff:
                loc, typ = b[o], b[o+1]; dl = struct.unpack('<I', b[o+2:o+6])[0]
                data = b[o+10:o+10+dl]
                if typ in (1, 3) and loc not in caps: caps[loc] = data
                o += 10 + dl
        off += 8 + ln
    return caps

def nibs_from_flux(data):
    out = []; reg = 0; acc = 0
    for v in data:
        acc += v
        if v == 255: continue
        cells = max(1, round(acc / 32.0)); acc = 0
        for i in range(cells):
            bit = 1 if i == cells - 1 else 0
            reg = ((reg << 1) | bit) & 0xff
            if reg & 0x80: out.append(reg); reg = 0
    return out

if __name__ == '__main__':
    caps = captures(sys.argv[1]); img = bytearray(); per = []
    for t in range(35):
        secs = woz.decode_track(nibs_from_flux(caps[t*4]), t) if t*4 in caps else {}
        per.append(len(secs))
        for s in range(16): img += secs.get(s, b'\0' * 256)
    open(sys.argv[2], 'wb').write(img)
    print(sum(per), per)
