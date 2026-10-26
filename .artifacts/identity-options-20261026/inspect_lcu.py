import base64,json,ssl,urllib.request,urllib.error,subprocess,re,sys
sys.stdout.reconfigure(encoding='utf-8')
from pathlib import Path
root=Path(__file__).parent
parts=Path('E:/WeGameApps/英雄联盟（含经典模式）/LeagueClient/lockfile').read_text().split(':')
if len(parts)>=5:
    port,secret=int(parts[2]),parts[3]
else:
    command="Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUx.exe'\" | Select-Object -ExpandProperty CommandLine | ConvertTo-Json -Compress"
    raw=subprocess.run(['powershell','-NoProfile','-Command',command],capture_output=True,check=True).stdout.decode('utf-8-sig',errors='replace')
    lines=json.loads(raw)
    if isinstance(lines,str):lines=[lines]
    for line in lines or []:
        p=re.search(r'--app-port=(\d+)',line);s=re.search(r'--remoting-auth-token=([^\s"]+)',line)
        if p and s:port,secret=int(p[1]),s[1];break
    else:raise SystemExit('No usable local client connection.')
base=f'https://127.0.0.1:{port}'
auth=base64.b64encode(('riot:'+secret).encode()).decode()
def get(path):
    request=urllib.request.Request(base+path,headers={'Authorization':'Basic '+auth})
    try:
        with urllib.request.urlopen(request,context=ssl._create_unverified_context(),timeout=8) as response:
            return response.status,json.load(response)
    except urllib.error.HTTPError as error:
        return error.code,None
for path in ([] if '--quiet' in sys.argv else ['/lol-game-data/assets/v1/regalia.json','/lol-regalia/v2/current-summoner/regalia','/lol-regalia/v3/inventory/REGALIA_BANNER','/lol-challenges/v2/titles/all','/lol-challenges/v2/titles/local-player']):
    status,data=get(path)
    print(path,status)
    if path=='/help' and isinstance(data,dict):
        (root/'swagger.json').write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
        print('help keys',list(data))
    elif isinstance(data,dict):
        print('keys',list(data)[:15] if '/titles/' not in path else '(catalog content ids omitted)')
        if '/titles/' in path:print('total_titles',len(data))
        if path.endswith('/regalia'):
            print({key:data.get(key) for key in ['preferredCrestType','preferredBannerType','selectedPrestigeCrest']})
        elif path.startswith('/lol-regalia/v3/inventory'):
            print('groups',len(data))
            print('ownership_counts',{'owned':sum(bool(v.get('isOwned')) for v in data.values()),'unowned':sum(not bool(v.get('isOwned')) for v in data.values())})
            for key,value in list(data.items())[:2]:print('sample',key,json.dumps(value,ensure_ascii=False)[:1400])
            (root/('inventory-'+path.rsplit('/',1)[1]+'.json')).write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
        elif path.endswith('/local-player'):
            if 'title' in data:print('title_keys',list(data['title']))
            print('field_types',{key:type(value).__name__ for key,value in data.items()})
    elif isinstance(data,list):
        print('count',len(data),'first_keys',list(data[0]) if data and isinstance(data[0],dict) else [])
        if data and isinstance(data[0],dict):print('first_nonpersonal',{key:data[0].get(key) for key in ['itemId','isPermanentTitle','titleAcquisitionType','challengeTitleData']})
