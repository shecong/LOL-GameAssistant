import sys
from pathlib import Path
sys.path.insert(0,'tmp/skin-adapter-deps')
from capstone import *
d=Path('tmp/skin-adaptation/runtime-code.bin').read_bytes();cs=Cs(CS_ARCH_X86,CS_MODE_64)
lines=[]
for i in cs.disasm(d[0x21c020:0x21d020],0x21c020):
 lines.append(f'{i.address:x} {i.mnemonic} {i.op_str}')
 if i.mnemonic=='ret':break
Path('tmp/skin-adaptation/update-function.asm').write_text('\n'.join(lines));print('\n'.join(lines))
