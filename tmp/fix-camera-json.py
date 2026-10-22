from pathlib import Path
p=Path('native/LeagueSkinCore/native/camera_core.h');s=p.read_text(encoding='utf-8');x='\\"camera_default_view_distance\\":%.9g,';s=s.replace(x+x,x);p.write_text(s,encoding='utf-8')
