import json
from pathlib import Path
root=Path('tmp/skin-adaptation')
def read(name):return json.loads((root/name).read_text(encoding='utf-8-sig'))
hero=read('final-status.json');objects=read('final-objects.json')
assert hero['ok'] and hero['skin']==0 and hero['active_skin']==0
assert objects['ok'] and objects['baseline_minion_skin']==0 and objects['baseline_ward_skin']==107
keys=['ok','invoked','state_verified','reference_sha256','model','skin','active_skin','lane_count','own_ward_count','object_target_count','object_changed_count','baseline_minion_skin','baseline_ward_skin','transport_hook_released']
evidence={'date':'2026-10-22','game_timestamp':hex(read('probe.json')['image_timestamp']),'hero':'Lissandra','tests':{'native_core':'passed','SkinCoreServiceTests':44,'SkinToolsTests':26},'visual_confirmation':'pending','steps':{}}
for name in ['hero-apply','hero-status','hero-restore','objects-apply','objects-restore','final-status','final-objects']:
 p=read(name+'.json');evidence['steps'][name]={k:p[k] for k in keys if k in p}
Path('native/LeagueSkinCore/adaptation-2026-10-22.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n')
print('Final hero/minion/ward baseline verified:',hero['skin'],objects['baseline_minion_skin'],objects['baseline_ward_skin'])
