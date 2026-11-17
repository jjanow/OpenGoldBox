"""Minimal Pasti .stx -> .st sector image (for file inspection only). Reports protection anomalies."""
import struct,sys
b=open(sys.argv[1],'rb').read()
assert b[:4]==b'RSY\0'
ntr=b[10]; p=16; secs={}; notes=[]
maxtrk=0;maxside=0;maxsec=0
for t in range(ntr):
    rs,fz,sc,fl,tl,tn,tt=struct.unpack('<IIHHHBB',b[p:p+16])
    trk=tn&0x7f; side=tn>>7; maxtrk=max(maxtrk,trk); maxside=max(maxside,side)
    q=p+16
    if fl&1:
        descs=[struct.unpack('<IHHBBBBHBB',b[q+16*i:q+16*i+16]) for i in range(sc)]
        q+=16*sc+fz; base=q
        for (off,bp,rt,it,ih,isec,isz,crc,fdc,_) in descs:
            size=128<<(isz&3)
            if fdc&0x10: notes.append(f't{trk}s{side} sec{isec}: RNF'); continue
            if fdc&0x08: notes.append(f't{trk}s{side} sec{isec}: CRC error flag')
            if it!=trk or size!=512 or isec>11: notes.append(f't{trk}s{side}: odd ID trk{it} sec{isec} size{size}'); 
            if size==512 and 1<=isec<=11 and it==trk:
                secs.setdefault((trk,side,isec),b[base+off:base+off+512]); maxsec=max(maxsec,isec)
    else:
        for i in range(sc):
            secs.setdefault((trk,side,i+1),b[q+512*i:q+512*i+512]); maxsec=max(maxsec,i+1)
    p+=rs
spt=maxsec; sides=maxside+1; tracks=maxtrk+1
img=bytearray(tracks*sides*spt*512); miss=0
for t in range(tracks):
    for s in range(sides):
        for k in range(1,spt+1):
            d=secs.get((t,s,k))
            if d is None: miss+=1; continue
            o=((t*sides+s)*spt+k-1)*512; img[o:o+512]=d
open(sys.argv[2],'wb').write(img)
print(f'{sys.argv[1].split("/")[-1]}: {tracks} trk x {sides} sides x {spt} spt, missing {miss}, anomalies {len(notes)}')
for n in notes[:15]: print('  ',n)
