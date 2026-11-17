"""Minimal IPF (SPS/CAPS) -> ADF converter for standard AmigaDOS tracks.
Decodes DATA-record element streams (data/sync/gap), interleaves odd/even,
verifies header+data checksums. Reports any non-standard track."""
import struct, sys
def mfm_data_bits(raw):
    out=bytearray()
    bits=int.from_bytes(raw,'big'); n=len(raw)*8
    v=0
    for i in range(n//2):
        v=(v<<1)|((bits>>(n-2-2*i))&1)
    return v.to_bytes(n//16,'big')
def inter(o,e):
    out=bytearray()
    for a,b in zip(o,e):
        x=0
        for k in range(7,-1,-1): x=(x<<2)|(((a>>k)&1)<<1)|((b>>k)&1)
        out+=x.to_bytes(2,'big')
    return bytes(out)
def spread_xor(half):
    # checksum over raw longwords = XOR of 16-bit decoded words spread to 0x55555555
    acc=0
    for i in range(0,len(half),2):
        acc^=int.from_bytes(half[i:i+2],'big')
    x=0
    for k in range(15,-1,-1): x=(x<<2)|((acc>>k)&1)
    return x
def recs(b):
    p=0
    while p<len(b):
        t=b[p:p+4].decode('latin1'); ln=struct.unpack('>I',b[p+4:p+8])[0]
        yield t,p
        p+= ln+(struct.unpack('>I',b[p+12:p+16])[0] if t=='DATA' else 0)
def main(src,dst):
    b=open(src,'rb').read()
    imges=[];datas={}
    for t,p in recs(b):
        if t=='IMGE': imges.append(struct.unpack('>'+'I'*17,b[p+12:p+80]))
        elif t=='DATA':
            size,bits,crc,key=struct.unpack('>IIII',b[p+12:p+28]); datas[key]=p+28
    adf=bytearray(901120); have=set(); problems=[]
    for v in imges:
        cyl,head=v[0],v[1]; bc=v[10]
        if cyl>=80:
            if bc: problems.append(f'cyl {cyl} has {bc} blocks (extra track)')
            continue
        tr=cyl*2+head
        if bc!=11: problems.append(f'track {tr}: {bc} blocks'); 
        off=datas[v[13]]
        for i in range(bc):
            d=struct.unpack('>'+'I'*8,b[off+32*i:off+32*i+32]); flags=d[5]
            q=off+d[7]; dec=bytearray()
            while True:
                h=b[q]; q+=1; typ=h&0x1f; w=h>>5
                if typ==0: break
                cnt=int.from_bytes(b[q:q+w],'big'); q+=w
                n=(cnt+7)//8 if flags&4 else cnt
                s=b[q:q+n]; q+=n
                if typ in (2,3): dec+=s
                elif typ in (1,4): dec+=mfm_data_bits(s)
                else: problems.append(f'track {tr} blk {i}: element type {typ}')
            k=dec.find(b'\xa1\xa1')
            if k<0 or len(dec)-(k+2)<540: problems.append(f'track {tr} blk {i}: no std sector'); continue
            s=dec[k+2:k+2+540]
            info=inter(s[0:2],s[2:4]); label=inter(s[4:12],s[12:20])
            hc=int.from_bytes(inter(s[20:22],s[22:24]),'big'); dc=int.from_bytes(inter(s[24:26],s[26:28]),'big')
            do,de=s[28:284],s[284:540]
            if spread_xor(s[0:20])!=hc: problems.append(f'track {tr} blk {i}: header csum bad')
            if spread_xor(do+de)!=dc: problems.append(f'track {tr} blk {i}: data csum bad')
            if info[0]!=0xff or info[1]!=tr: problems.append(f'track {tr} blk {i}: info {info.hex()}')
            sec=info[2]
            if sec>10: problems.append(f'track {tr}: sector {sec}'); continue
            adf[(tr*11+sec)*512:(tr*11+sec+1)*512]=inter(do,de); have.add((tr,sec))
    missing=1760-len(have)
    open(dst,'wb').write(adf)
    print(f'{src}: sectors {len(have)}/1760 missing {missing}; problems {len(problems)}')
    for p in problems[:20]: print('  ',p)
main(sys.argv[1],sys.argv[2])
