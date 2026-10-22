import json
from pathlib import Path
p=json.loads(Path('tmp/skin-effects/current.json').read_text(encoding='utf-8-sig'))
evidence={'date':'2026-10-22','game_profile_sha256':'36c4715121fc8af987c63c61ae5fc85b496004ec801bea2d76cdf8b3e8682e28','model':p.get('model'),'skin':p.get('skin'),'resource_slots':[{'offset':hex(r['stack_offset']),'resource_names':r['strings']} for r in p.get('resources',[])],'inspection':'read-only; all five cached resource slots identify LuxSkin07','skill_visual_confirmation':'pending','recall_visual_confirmation':'pending','limitation':'These resource identities do not prove that skill particles or the recall animation were rendered correctly.'}
Path('native/LeagueSkinCore/effects-verification-2026-10-22.json').write_text(json.dumps(evidence,indent=2)+'\n')
