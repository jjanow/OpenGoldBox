import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

import sys, json, re
sys.path.insert(0, _ROOT + 'tools/scripts')
import ecl
from capstone import Cs, CS_ARCH_X86, CS_MODE_16
O = open(_WORK + 'porgame/GAME.OVR', 'rb').read()
md = Cs(CS_ARCH_X86, CS_MODE_16)
base = 0xd46
pending = []
handler = {}
for ins in md.disasm(O[0x3c76:0x3eb5], 0x3c76 - base):
    if ins.mnemonic == 'cmp' and ins.op_str.startswith('al,'):
        pending.append(int(ins.op_str.split(',')[1], 16))
    elif ins.mnemonic == 'jne':
        pass
    elif ins.mnemonic == 'call':
        t = base + int(ins.op_str, 16)
        # the opcodes collected since the last call: all 'je' ones share; last compared is jne
        for op in pending:
            handler.setdefault(t, []).append(op)
        pending = []
    elif ins.mnemonic == 'jmp':
        pass
res = {}
for t, ops in sorted(handler.items()):
    names = [ecl.OPS.get(o, ('?',))[0] for o in ops]
    res['%x' % t] = [ops, names]
    print(hex(t), ops, names)
json.dump(res, open(_WORK + 'eclh.json', 'w'))
