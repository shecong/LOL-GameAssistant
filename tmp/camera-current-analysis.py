from pathlib import Path
import sys, struct
sys.path.insert(0, 'E:/tools/lol/output/pete-analysis/packages')
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
b=Path('tmp/skin-adaptation/runtime-code.bin').read_bytes()
md=Cs(CS_ARCH_X86,CS_MODE_64)
def dump(a,n=180):
 print('\nAT',hex(a))
 for i in md.disasm(b[a:a+n],a): print(hex(i.address),i.mnemonic,i.op_str)
refs=[]
for j in range(len(b)-7):
 if b[j:j+2]==b'\x48\x8b' and b[j+2]&0xc7==5 and j+7+struct.unpack_from('<i',b,j+3)[0]==0x1c85220: refs.append(j)
print('CONFIG REFS', [hex(a) for a in refs])
for a in refs[:25]: dump(max(0,a-20),200)
dump(0x35ea20,150)
