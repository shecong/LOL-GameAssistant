import json,collections
from pathlib import Path
p=json.loads(Path('tmp/skin-adaptation/probe.json').read_text(encoding='utf-8-sig'));print('skin',p.get('skin_candidate'));print('head',p.get('player_head_hex'));print(collections.Counter(o['model'] for o in p.get('objects',[])));print([o for o in p.get('objects',[]) if 'own' in o])
