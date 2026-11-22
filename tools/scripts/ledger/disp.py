import os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))) + '/'
_WORK = _os.environ.get('LEDGER_WORK', '/tmp/goldbox-ledger').rstrip('/') + '/'
_GAME = _os.environ.get('GOLDBOX_POR_DIR', '').rstrip('/') + '/'

"""Build the spell-routine (DS:6B84) and effect-handler (DS:6828) tables with the program's own builders."""
import sys, json
sys.path.insert(0, _ROOT + 'tools/scripts')
import por_spell_oracle
o = por_spell_oracle.Oracle(_WORK + 'porgame')
e = o.e


def to_ovr(seg, off):
    if seg in e.stubs:
        import struct
        w = struct.unpack('<H', e.rb(seg * 16 + off + 2, 2))[0]
        return 'O:%x' % (e.stubs[seg][0] + w)
    return 'S:%x' % (seg * 16 + off)


spells = {i: to_ovr(*o.routines[i]) for i in range(68) if o.routines[i] != (0, 0)}
handlers = {}
for i in range(0, 160):
    seg, off = e.dsptr(0x6828 + 4 * i)
    if (seg, off) != (0, 0):
        handlers[i] = to_ovr(seg, off)
json.dump({'spells': spells, 'handlers': handlers}, open(_WORK + 'disp.json', 'w'), indent=0)
print(len(spells), len(handlers))
print(spells)
print(handlers)
