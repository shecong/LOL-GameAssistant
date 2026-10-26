import sys,json,urllib.request,urllib.error,ssl,time,hashlib
sys.argv.append('--quiet')
from inspect_lcu import get,base,auth,root
def request(method,path,body):
    req=urllib.request.Request(base+path,data=json.dumps(body).encode(),method=method,headers={'Authorization':'Basic '+auth,'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,context=ssl._create_unverified_context(),timeout=10) as response:return response.status,response.read().decode('utf-8','replace')[:300]
    except urllib.error.HTTPError as error:return error.code,error.read().decode('utf-8','replace')[:300]
def owner():
    a,x=get('/lol-summoner/v1/current-summoner');b,y=get('/lol-rso-auth/v1/authorization')
    if a!=200 or b!=200:raise RuntimeError('Identity unavailable')
    return hashlib.sha256((x['puuid']+'\n'+y['currentPlatformId']).encode()).hexdigest()
def prefs(summary):
    ids=[int(x) for x in summary['selectedChallengesString'].split(',') if x]
    title=str(summary.get('title',{}).get('itemId',''))
    return {'challengeIds':ids,'title':'' if title in ['-1','0'] else title,'bannerAccent':summary['bannerId'],'crestBorder':summary['crestId'],'prestigeCrestBorderLevel':summary['prestigeCrestBorderLevel']}
endpoint='/lol-challenges/v1/update-player-preferences';regalia='/lol-regalia/v2/current-summoner/regalia'
identity=owner();_,summary=get('/lol-challenges/v1/summary-player-data/local-player');original=prefs(summary)
_,raw=get(regalia);original_regalia={k:raw[k] for k in ['preferredCrestType','preferredBannerType','selectedPrestigeCrest']}
_,all_titles=get('/lol-challenges/v2/titles/all');_,owned_titles=get('/lol-challenges/v2/titles/local-player');owned={x['contentId'] for x in owned_titles}
title=next(x for x in all_titles.values() if x['contentId'] not in owned and x['itemId']>0)
_,banners=get('/lol-regalia/v3/inventory/REGALIA_BANNER');banner=next(k for k,v in banners.items() if not v['isOwned'] and any(x.get('isSelectable') for x in v['items']))
_,challenges=get('/lol-challenges/v1/challenges/local-player');badge=next(v for v in challenges.values() if v.get('currentLevel') in ['NONE',''] and v.get('levelToIconPath'))
results=[]
for kind,value in [('title',str(title['itemId'])),('bannerAccent',banner),('challengeIds',[badge['id']]),('selectedPrestigeCrest',20)]:
    if owner()!=identity:raise RuntimeError('Identity changed; stopped without another write')
    body=dict(original_regalia if kind=='selectedPrestigeCrest' else original);body[kind]=value
    if kind=='selectedPrestigeCrest':body['preferredCrestType']='prestige'
    path=regalia if kind=='selectedPrestigeCrest' else endpoint;method='PUT' if kind=='selectedPrestigeCrest' else 'POST'
    try:
        status,error=request(method,path,body);time.sleep(.4)
        _,after=get(regalia if kind=='selectedPrestigeCrest' else '/lol-challenges/v1/summary-player-data/local-player')
        actual=after['selectedPrestigeCrest'] if kind=='selectedPrestigeCrest' else prefs(after)[kind]
        result={'kind':kind,'requested':value,'status':status,'actual':actual,'matched':actual==value,'error':error if status>=400 else ''}
    finally:
        if owner()!=identity:raise RuntimeError('Identity changed; original account must restore separately')
        restore_status,restore_error=request(method,path,original_regalia if kind=='selectedPrestigeCrest' else original)
        time.sleep(.3);_,restored=get(regalia if kind=='selectedPrestigeCrest' else '/lol-challenges/v1/summary-player-data/local-player')
        restored_values={k:restored[k] for k in original_regalia} if kind=='selectedPrestigeCrest' else prefs(restored)
        result['restore_verified']=restore_status<300 and restored_values==(original_regalia if kind=='selectedPrestigeCrest' else original)
    results.append(result);print(json.dumps(result,ensure_ascii=False),flush=True)
    if not result['restore_verified']:raise RuntimeError('Restore did not verify; stopped')
(root/'apply-restore-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
