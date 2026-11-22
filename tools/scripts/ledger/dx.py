import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

"""dis.py S|O addr [n] [seg] — print n instructions."""
import sys
sys.path.insert(0, _ROOT + 'tools/scripts')
import por_emu
from capstone import Cs, CS_ARCH_X86, CS_MODE_16
G = _GAME
S, cs0, ip0 = por_emu.load_image(open(G + 'START.EXE', 'rb').read())
O = open(G + 'GAME.OVR', 'rb').read()
sp = sys.argv[1]
a = int(sys.argv[2], 16)
n = int(sys.argv[3]) if len(sys.argv) > 3 else 30
m = S if sp == 'S' else O
md = Cs(CS_ARCH_X86, CS_MODE_16)
if len(sys.argv) > 4 and sys.argv[4] == 'hex':
    for i in range(0, n, 16):
        print(hex(a + i), m[a + i:a + i + 16].hex(' '), ''.join(chr(c) if 32 <= c < 127 else '.' for c in m[a + i:a + i + 16]))
    sys.exit()
for i, ins in enumerate(md.disasm(m[a:a + n * 8], a)):
    if i >= n:
        break
    print(hex(ins.address), ins.bytes.hex(), ins.mnemonic, ins.op_str)
