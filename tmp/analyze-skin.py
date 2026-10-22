import sys,re,json,struct
from pathlib import Path
sys.path.insert(0,'tmp/skin-adapter-deps')
from capstone import *
d=Path('tmp/skin-adaptation/runtime-code.bin').read_bytes()
c=Cs(CS_ARCH_X86,CS_MODE_64)
def show(at,size=200):
 for i in c.disasm(d[at:at+size],at):print(hex(i.address),i.mnemonic,i.op_str)
probe=json.loads(Path('tmp/skin-adaptation/probe.json').read_text(encoding='utf-8-sig'))
for p in probe['signatures'][:6]:
 if p['rva']:
  print(p['name']);show(p['rva'],32)
for label,pat in [('oldget',rb'\x48\x8b\x81..\x00\x00\x48\x85\xc0\x74\x05\x48\x8b\x40\x28\xc3\xc3'),('skininit',rb'\xc6\x86..\x00\x00\x01\x0f\xb6\x86'),('skinwrite',rb'\x88\x86..\x00\x00\x48\x89')]:
 print(label)
 for m in list(re.finditer(pat,d,re.S))[:30]:
  print(hex(m.start()),d[m.start():m.start()+35].hex())
  if label=='skininit':show(m.start()-65,260)
