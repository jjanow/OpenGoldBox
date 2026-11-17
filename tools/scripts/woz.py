"""WOZ1/WOZ2 parser + 6&2 sector decoder. Usage: python3 -I woz.py info|dump <file.woz> [outdsk]"""
import sys, struct, zlib, json

def load(path):
    b = open(path, 'rb').read()
    magic = b[:4]
    if magic not in (b'WOZ1', b'WOZ2') or b[4:8] != b'\xff\x0a\x0d\x0a':
        raise ValueError('bad magic')
    crc = struct.unpack('<I', b[8:12])[0]
    crc_ok = (crc == 0) or (zlib.crc32(b[12:]) & 0xffffffff) == crc
    chunks = {}
    off = 12
    while off + 8 <= len(b):
        cid = b[off:off+4].decode('latin1'); ln = struct.unpack('<I', b[off+4:off+8])[0]
        chunks[cid] = (off + 8, ln); off += 8 + ln
    return b, magic.decode(), crc_ok, chunks

def info(path):
    b, ver, crc_ok, ch = load(path)
    o, l = ch['INFO']; i = b[o:o+l]
    r = {'format': ver, 'crc_ok': crc_ok, 'info_version': i[0], 'disk_type': i[1], 'write_protected': i[2],
         'synchronized': i[3], 'cleaned': i[4], 'creator': i[5:37].decode('latin1').strip()}
    if i[0] >= 2:
        r.update(sides=i[37], boot_sector_format=i[38], optimal_bit_timing=i[39],
                 compatible_hw=struct.unpack('<H', i[40:42])[0], required_ram_kb=struct.unpack('<H', i[42:44])[0])
    if 'META' in ch:
        o, l = ch['META']
        r['meta'] = dict(line.split('\t', 1) for line in b[o:o+l].decode('utf-8', 'replace').splitlines() if '\t' in line)
    tm = b[ch['TMAP'][0]:ch['TMAP'][0]+160]
    r['tracks'] = len({t for t in tm if t != 0xff})
    r['chunks'] = list(ch)
    return r

def track_bits(b, ver, ch, qt):
    tm = b[ch['TMAP'][0]:ch['TMAP'][0]+160]
    t = tm[qt]
    if t == 0xff: return None
    o = ch['TRKS'][0]
    if ver == 'WOZ2':
        sb, bc, bits = struct.unpack('<HHI', b[o+t*8:o+t*8+8])
        data = b[sb*512: sb*512 + bc*512]
    else:
        e = o + t*6656
        data = b[e:e+6646]; bits = struct.unpack('<H', b[e+6648:e+6650])[0]
    return data, bits

def nibbles(data, bits):
    out = []; reg = 0
    for n in range(bits * 2):  # wrap twice
        k = n % bits
        bit = (data[k >> 3] >> (7 - (k & 7))) & 1
        reg = ((reg << 1) | bit) & 0xff
        if reg & 0x80:
            out.append(reg); reg = 0
    return out

DEC = {}
WT = [0x96,0x97,0x9a,0x9b,0x9d,0x9e,0x9f,0xa6,0xa7,0xab,0xac,0xad,0xae,0xaf,0xb2,0xb3,0xb4,0xb5,0xb6,0xb7,0xb9,0xba,0xbb,0xbc,0xbd,0xbe,0xbf,0xcb,0xcd,0xce,0xcf,0xd3,0xd6,0xd7,0xd9,0xda,0xdb,0xdc,0xdd,0xde,0xdf,0xe5,0xe6,0xe7,0xe9,0xea,0xeb,0xec,0xed,0xee,0xef,0xf2,0xf3,0xf4,0xf5,0xf6,0xf7,0xf9,0xfa,0xfb,0xfc,0xfd,0xfe,0xff]
for i, v in enumerate(WT): DEC[v] = i

def decode_track(nb, want_track):
    secs = {}; n = len(nb); i = 0
    while i < n - 400:
        if nb[i] == 0xd5 and nb[i+1] == 0xaa and nb[i+2] == 0x96:
            f = nb[i+3:i+11]
            vol = ((f[0] << 1) | 1) & f[1]; trk = ((f[2] << 1) | 1) & f[3]
            sec = ((f[4] << 1) | 1) & f[5]; ck = ((f[6] << 1) | 1) & f[7]
            if vol ^ trk ^ sec != ck: i += 1; continue
            j = i + 11
            while j < i + 60 and not (nb[j] == 0xd5 and nb[j+1] == 0xaa and nb[j+2] == 0xad): j += 1
            if j >= i + 60: i += 1; continue
            raw = nb[j+3:j+3+343]
            try: vals = [DEC[x] for x in raw]
            except KeyError: i += 1; continue
            buf = []; last = 0
            for v in vals[:342]: last ^= v; buf.append(last)
            if last ^ vals[342] != 0: i += 1; continue
            out = bytearray(256)
            for k in range(256):
                lo = buf[k % 86] >> (2 * (k // 86))
                lo = ((lo & 1) << 1) | ((lo >> 1) & 1)
                out[k] = ((buf[86 + k] << 2) | lo) & 0xff
            if sec not in secs and trk == want_track: secs[sec] = bytes(out)
            i = j + 346
        else:
            i += 1
    return secs

DOS_SKEW = [0x0,0x7,0xe,0x6,0xd,0x5,0xc,0x4,0xb,0x3,0xa,0x2,0x9,0x1,0x8,0xf]  # logical->physical for .dsk (DOS order)

def dump(path):
    b, ver, crc_ok, ch = load(path)
    tracks = {}
    for t in range(35):
        tb = track_bits(b, ver, ch, t * 4)
        if tb is None: tracks[t] = {}; continue
        tracks[t] = decode_track(nibbles(*tb), t)
    return tracks

if __name__ == '__main__':
    cmd, p = sys.argv[1], sys.argv[2]
    if cmd == 'info':
        print(json.dumps(info(p)))
    elif cmd == 'dump':
        tr = dump(p)
        good = sum(len(v) for v in tr.values())
        print(json.dumps({'sectors_decoded': good, 'per_track': [len(tr[t]) for t in range(35)]}))
        if len(sys.argv) > 3:  # write physical-order sector image (.po-like physical order; missing = zeros)
            img = bytearray()
            for t in range(35):
                for s in range(16):
                    img += tr[t].get(s, b'\0' * 256)
            open(sys.argv[3], 'wb').write(img)
