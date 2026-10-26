import sys,json,collections
sys.argv.append('--quiet')
from inspect_lcu import get,root
for path in ['/lol-game-data/assets/v1/regalia.json','/lol-game-data/assets/v1/challenges.json','/lol-challenges/v1/challenges/local-player','/lol-challenges/v2/titles/all','/lol-challenges/v1/summary-player-data/local-player']:
    status,data=get(path);print(path,status)
    if status!=200:continue
    if path.endswith('regalia.json'):
        print('types',dict(collections.Counter(x.get('regaliaType') for x in data)))
        for kind in set(x.get('regaliaType') for x in data):print(kind,next(x for x in data if x.get('regaliaType')==kind))
    elif path.endswith('summary-player-data/local-player'):
        print('preferences',{k:data.get(k) for k in ['selectedChallengesString','bannerId','crestId','prestigeCrestBorderLevel']},'title_id',data.get('title',{}).get('itemId'))
    else:
        entries=list(data.items()) if isinstance(data,dict) else list(enumerate(data))
        print('count',len(entries),'sample',json.dumps(entries[:1],ensure_ascii=False)[:4500])
    if 'game-data' in path or path.endswith('titles/all'):(root/('catalog-'+path.rsplit('/',1)[1].replace('.json','')+'.json')).write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
