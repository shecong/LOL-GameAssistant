from pathlib import Path
p=Path('tmp/skin-adaptation/native/standalone_probe.h');s=p.read_text();s=s.replace('(*(BYTE***)player)[n]-base','(*(BYTE***)(player+0x1b0))[n]-base');p.write_text(s)
