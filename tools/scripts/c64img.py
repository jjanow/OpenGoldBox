#!/usr/bin/env python3
"""Read D64 / G64 / NIB images: decode to 1541 sectors, list directory, hash files.
Usage: c64img.py IMAGE [--json]
"""
import sys, hashlib, json, struct

SPT = [0] + [21]*17 + [19]*7 + [18]*6 + [17]*5 + [17]*5  # tracks 1..40

GCR_DEC = {0x0a:0,0x0b:1,0x12:2,0x13:3,0x0e:4,0x0f:5,0x16:6,0x17:7,
           0x09:8,0x19:9,0x1a:10,0x1b:11,0x0d:12,0x1d:13,0x1e:14,0x15:15}

def bits_of(data):
    n = int.from_bytes(data, 'big')
    return n, len(data)*8

def decode_gcr_bytes(bitstr):
    """bitstr: str of '0'/'1', length multiple of 10 -> bytes (None on bad gcr)"""
    out = bytearray()
    for i in range(0, len(bitstr) - 9, 10):
        hi = GCR_DEC.get(int(bitstr[i:i+5], 2)); lo = GCR_DEC.get(int(bitstr[i+5:i+10], 2))
        if hi is None or lo is None: return None
        out.append(hi << 4 | lo)
    return bytes(out)

def decode_track(raw):
    """raw GCR track bytes -> dict sector -> (track_in_header, 256 data bytes, ok)"""
    b = ''.join(format(x, '08b') for x in raw)
    b2 = b + b[:4000]  # wrap around
    res = {}
    i = 0
    L = len(b)
    hdr = None
    while i < L:
        j = b2.find('1111111111', i)
        if j < 0 or j >= L: break
        k = j
        while k < len(b2) and b2[k] == '1': k += 1
        # first byte after sync
        first = decode_gcr_bytes(b2[k:k+10])
        if not first: i = k + 1; continue
        if first[0] == 0x08:
            h = decode_gcr_bytes(b2[k:k+80])
            if h: hdr = h  # 08 cksum sector track id2 id1 0f 0f
        elif first[0] == 0x07 and hdr is not None:
            d = decode_gcr_bytes(b2[k:k+2580])
            if d and len(d) >= 258:
                data = d[1:257]; ck = 0
                for x in data: ck ^= x
                sec = hdr[2]
                ok = (ck == d[257])
                if sec not in res or (ok and not res[sec][2]):
                    res[sec] = (hdr[3], data, ok)
            hdr = None
        i = k
    return res

def load(path):
    raw = open(path, 'rb').read()
    sectors = {}  # (t,s)->bytes
    info = {}
    if raw[:8] == b'GCR-1541':
        ntr = raw[9]; info['fmt'] = 'G64'; info['halftracks'] = ntr
        bad = 0
        for ht in range(ntr):
            off = struct.unpack_from('<I', raw, 12 + 4*ht)[0]
            if not off or ht % 2: continue
            tlen = struct.unpack_from('<H', raw, off)[0]
            t = ht//2 + 1
            if t > 40: continue
            dec = decode_track(raw[off+2:off+2+tlen])
            for s, (th, data, ok) in dec.items():
                if s < SPT[t]:
                    sectors[(t, s)] = data
                    if not ok: bad += 1
        info['bad_cksum'] = bad
    elif raw[:13] == b'MNIB-1541-RAW':
        info['fmt'] = 'NIB'
        ent = []
        p = 0x10
        while p < 0x100 and raw[p]:
            ent.append(raw[p]); p += 2
        bad = 0
        for n, ht in enumerate(ent):
            if ht % 2: continue
            t = ht//2
            data = raw[0x100 + n*0x2000: 0x100 + (n+1)*0x2000]
            if t < 1 or t > 40 or not data: continue
            dec = decode_track(data)
            for s, (th, d, ok) in dec.items():
                if s < SPT[t] and ((t, s) not in sectors):
                    sectors[(t, s)] = d
                    if not ok: bad += 1
        info['bad_cksum'] = bad
    else:
        info['fmt'] = 'D64'; info['size'] = len(raw)
        ntr = 40 if len(raw) >= 196608 else 35
        off = 0
        for t in range(1, ntr+1):
            for s in range(SPT[t]):
                sectors[(t, s)] = raw[off:off+256]; off += 256
        nsec = off // 256
        if len(raw) == off + nsec:
            errs = raw[off:]
            info['error_bytes_nonok'] = sum(1 for e in errs if e not in (0, 1))
    info['sectors_decoded'] = len(sectors)
    return sectors, info

def petscii(b):
    return ''.join(chr(c) if 32 <= c < 127 else ('' if c == 0xa0 else '?') for c in b)

def directory(sectors):
    bam = sectors.get((18, 0))
    out = {'name': None, 'id': None, 'files': []}
    if not bam: return out
    out['name'] = petscii(bam[0x90:0xa0]); out['id'] = petscii(bam[0xa2:0xa7])
    t, s = bam[0], bam[1]; seen = set()
    types = ['DEL', 'SEQ', 'PRG', 'USR', 'REL']
    while t and (t, s) not in seen and (t, s) in sectors:
        seen.add((t, s)); d = sectors[(t, s)]
        for e in range(8):
            ent = d[e*32:(e+1)*32]
            ft = ent[2]
            if ft == 0: continue
            name = petscii(ent[5:21]); blocks = ent[30] | ent[31] << 8
            # follow chain
            ft_t, ft_s = ent[3], ent[4]; buf = bytearray(); chain = set(); ok = True
            while ft_t:
                if (ft_t, ft_s) in chain or (ft_t, ft_s) not in sectors: ok = False; break
                chain.add((ft_t, ft_s)); sd = sectors[(ft_t, ft_s)]
                if sd[0] == 0: buf += sd[2:sd[1]+1]; break
                buf += sd[2:]; ft_t, ft_s = sd[0], sd[1]
            out['files'].append({'name': name, 'type': types[ft & 7] if (ft & 7) < 5 else '?',
                                 'closed': bool(ft & 0x80), 'blocks': blocks, 'start': [ent[3], ent[4]],
                                 'len': len(buf), 'sha1': hashlib.sha1(bytes(buf)).hexdigest()[:12],
                                 'chain_ok': ok})
        t, s = d[0], d[1]
    return out

def main():
    p = sys.argv[1]
    sectors, info = load(p)
    d = directory(sectors)
    allsec = hashlib.sha1(b''.join(sectors.get((t, s), b'\0'*256) for t in range(1, 36) for s in range(SPT[t]))).hexdigest()
    info['sectors_sha1_35trk'] = allsec
    if '--json' in sys.argv:
        print(json.dumps({'info': info, 'dir': d, 'sec': {f'{t},{s}': hashlib.sha1(v).hexdigest()[:10] for (t, s), v in sectors.items()}}))
        return
    print(info)
    print('"%s" %s' % (d['name'], d['id']))
    for f in d['files']:
        print('%4d %-18s %s%s len=%d sha1=%s %s' % (f['blocks'], '"%s"' % f['name'], f['type'], '' if f['closed'] else '*', f['len'], f['sha1'], '' if f['chain_ok'] else 'CHAIN-BROKEN'))

if __name__ == '__main__':
    main()
