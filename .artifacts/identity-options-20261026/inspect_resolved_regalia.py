import sys,json
sys.argv.append('--quiet')
from inspect_lcu import get
_,user=get('/lol-summoner/v1/current-summoner')
status,data=get('/lol-regalia/v3/summoners/'+str(user['summonerId'])+'/regalia')
print('resolved_status',status)
if isinstance(data,dict):
    print('keys',list(data))
    for key,value in data.items():
        if any(x in key.lower() for x in ['crest','banner','level','loadout']):print(key,json.dumps(value,ensure_ascii=False)[:2200])
