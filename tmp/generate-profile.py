import json,struct,hashlib
from pathlib import Path
p=json.loads(Path('tmp/skin-adaptation/probe.json').read_text(encoding='utf-8-sig')); ps={x['name']:x for x in p['signatures']};d=Path('tmp/skin-adaptation/runtime-code.bin').read_bytes()
def target(name,pos,size,second=False):
 x=ps[name];a=x['second_rva'] if second else x['rva'];return a+size+struct.unpack_from('<i',d,a+pos)[0]
assert target('public_translate',1,5)==target('public_translate',1,5,True)
profile=dict(timestamp=p['image_timestamp'],image_size=p['image_size'],player_slot=target('public_player',3,7),champion_slot=target('public_champion_manager',3,7),translate=target('public_translate',1,5),update=ps['public_update']['rva'],push=target('public_push',1,5),stack_offset=0x3638,skin_offset=0xbe4,object_list_slot=0x1c7f430,owner_offset=0x560,team_offset=0x158)
checks=[(ps[n]['rva'],160) for n in ['public_player','public_stack','public_update','public_push','public_champion_manager','public_translate']]+[(profile['push'],160),(0x28ec32,43),(0x2821d4,37),(0x290640,10),(0x28e670,12),(0x28cf10,18),(0x5d2343,49)]
profile['checks']=[dict(rva=r,hex=d[r:r+s].hex()) for r,s in checks]
sha=hashlib.sha256(json.dumps(profile,sort_keys=True).encode()).hexdigest()
lines=['// Inspected Tencent CN build 2026-10-22; unknown builds fail closed.','#pragma once',f'constexpr char GameProfileSha[]="{sha}";']
lines += [f'constexpr uintptr_t Game_{k}=0x{v:x};' for k,v in profile.items() if k!='checks']
for i,x in enumerate(profile['checks']):lines.append(f'constexpr unsigned char gameFingerprint{i}[]={{'+','.join('0x'+x['hex'][j:j+2] for j in range(0,len(x['hex']),2))+'};')
lines+=['static bool gameProfileMatches(BYTE* base){','return '+' && '.join(f'memcmp(base+0x{x["rva"]:x},gameFingerprint{i},sizeof(gameFingerprint{i}))==0' for i,x in enumerate(profile['checks']))+';}']
Path('native/LeagueSkinCore/native/game_profile.h').write_text('\n'.join(lines)+'\n')
Path('native/LeagueSkinCore/game-profile.json').write_text(json.dumps(dict(inspected_date='2026-10-22',sha256=sha,profile=profile),indent=2)+'\n')
