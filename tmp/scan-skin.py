import sys,re
from pathlib import Path
sys.path.insert(0,'tmp/skin-adapter-deps')
import pefile
p=pefile.PE(r'E:/WeGameApps/英雄联盟（含经典模式）/Game/League of Legends.exe'); data=p.get_memory_mapped_image()
s=Path('E:/tools/LeagueSkinStudio/native-bridge/standalone_probe.h').read_text()
for name,pat in re.findall(r'\{"([^"\n]+)","([0-9A-F? ]+)"\}',s):
 regex=b''.join(b'.' if '?' in x else re.escape(bytes.fromhex(x)) for x in pat.split())
 matches=list(re.finditer(regex,data,re.S))
 print(name, [hex(m.start()) for m in matches[:8]])
