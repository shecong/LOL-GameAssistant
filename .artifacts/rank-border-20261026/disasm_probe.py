import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent / 'python-deps'))
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
md = Cs(CS_ARCH_X86, CS_MODE_64)
out=[]
for name in sys.argv[1:]:
    p=json.loads((Path(__file__).parent / ('function-'+name+'.json')).read_text(encoding='utf-8-sig'))
    out.append('FILE '+name)
    f=p.get('function')
    if f:
        out.extend(f'{i.address:x}: {i.mnemonic} {i.op_str}' for i in md.disasm(bytes.fromhex(f['bytes']), f['begin']))
(Path(__file__).parent / 'regalia-callers.asm').write_text('\n'.join(out))
print('\n'.join(out))
