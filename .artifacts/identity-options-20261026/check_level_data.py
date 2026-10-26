import sys,json
sys.argv.append('--quiet')
from inspect_lcu import get,root
for name in ['summoner-banners','summoner-level-rewards','summoner-levels','lol-regalia']:
    status,data=get('/lol-game-data/assets/v1/'+name+'.json');print(name,status)
    if status==200:
        print(json.dumps(data,ensure_ascii=False)[:3000]);(root/(name+'.json')).write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
