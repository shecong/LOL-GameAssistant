import sys,json,ssl,urllib.request,urllib.error,time
sys.argv.append('--quiet')
from inspect_lcu import get,base,auth,root
def send(body):
    req=urllib.request.Request(base+'/lol-challenges/v1/update-player-preferences',data=json.dumps(body).encode(),method='POST',headers={'Authorization':'Basic '+auth,'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,context=ssl._create_unverified_context(),timeout=10) as response:return response.status
    except urllib.error.HTTPError as error:return error.code
_,user=get('/lol-summoner/v1/current-summoner');identity=user['puuid']
_,summary=get('/lol-challenges/v1/summary-player-data/local-player')
original={'challengeIds':[int(x) for x in summary['selectedChallengesString'].split(',') if x],'title':str(summary['title']['itemId']),'bannerAccent':summary['bannerId'],'crestBorder':summary['crestId'],'prestigeCrestBorderLevel':summary['prestigeCrestBorderLevel']}
body=dict(original);body['prestigeCrestBorderLevel']=500
try:
    status=send(body);time.sleep(.4);_,after=get('/lol-challenges/v1/summary-player-data/local-player');_,regalia=get('/lol-regalia/v2/current-summoner/regalia')
    print('direct_prestige_level',status,'actual_level',after['prestigeCrestBorderLevel'],'actual_crest',regalia['selectedPrestigeCrest'])
finally:
    _,user=get('/lol-summoner/v1/current-summoner')
    if user['puuid']!=identity:raise RuntimeError('Identity changed; stopped')
    status=send(original);time.sleep(.3);_,after=get('/lol-challenges/v1/summary-player-data/local-player');print('restored',status,after['prestigeCrestBorderLevel']==original['prestigeCrestBorderLevel'])
path='/fe/lol-static-assets/images/uikit/themed-borders/theme-21-border.png'
req=urllib.request.Request(base+path,headers={'Authorization':'Basic '+auth})
try:
    with urllib.request.urlopen(req,context=ssl._create_unverified_context(),timeout=8) as response:print('border_preview',response.status,response.headers.get('Content-Type'),len(response.read()))
except urllib.error.HTTPError as error:print('border_preview',error.code)
