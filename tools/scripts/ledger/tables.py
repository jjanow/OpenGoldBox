import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

import struct, sys
sys.path.insert(0, _ROOT + 'tools/scripts')
import por_emu
G = _GAME
S, cs0, ip0 = por_emu.load_image(open(G + 'START.EXE', 'rb').read())
DS = por_emu.data_segment(S, cs0, ip0) * 16
print('S len', hex(len(S)), 'DS lin', hex(DS))
for base, n in ((0x6828, 140), (0x6B84, 70)):
    vals = [struct.unpack_from('<HH', S, DS + base + 4 * k) for k in range(n)]
    print(hex(base), vals[:12])
