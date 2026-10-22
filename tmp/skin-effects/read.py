import json
from pathlib import Path
p=json.loads(Path('tmp/skin-effects/current.json').read_text(encoding='utf-8-sig'));print({k:p[k] for k in ['ok','model','skin'] if k in p});print([(hex(r['stack_offset']),r['strings']) for r in p.get('resources',[])])
