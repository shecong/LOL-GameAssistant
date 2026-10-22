"""Observe the isolated native read-only probe and public local API together."""
import json
from pathlib import Path
import ssl
import subprocess
import time
import urllib.request

root = Path(__file__).resolve().parent
exe = root / 'v3/cooldown-readonly-probe.exe'
samples = []
ctx = ssl._create_unverified_context()
last = None
for n in range(90):
    started = time.monotonic()
    raw = subprocess.run([str(exe), 'timers'], capture_output=True, text=True, timeout=6)
    try:
        sample = json.loads(raw.stdout)
    except Exception:
        sample = {'ok': False, 'error': 'invalid_probe_response', 'stdout': raw.stdout[:1000]}
    sample['observed_at_unix'] = time.time()
    try:
        with urllib.request.urlopen('https://127.0.0.1:2999/liveclientdata/allgamedata', context=ctx, timeout=2) as response:
            live = json.load(response)
        sample['api_game_time'] = live['gameData']['gameTime']
        sample['mode'] = live['gameData']['gameMode']
        sample['api_spells'] = [p['summonerSpells'] for p in live['allPlayers'] if p.get('riotId') == live['activePlayer'].get('riotId')]
    except Exception as error:
        sample['api_error'] = str(error)
    samples.append(sample)
    (root / 'observations.json').write_text(json.dumps(samples, indent=2), encoding='utf-8')
    if sample.get('ok'):
        slot = next(s for s in sample['slots'] if s['index'] == 5)
        value = (sample['pid'], slot['cooldown_end'], slot['duration_candidate'])
        if value != last or n % 10 == 0:
            print(json.dumps({'sample': n, 'pid': sample['pid'], 'game_time': sample['game_time'],
                              'end': slot['cooldown_end'], 'duration': slot['duration_candidate'],
                              'remaining': slot['remaining_primary']}), flush=True)
        last = value
    else:
        print(json.dumps({'sample': n, 'error': sample}), flush=True)
    time.sleep(max(0, 1 - (time.monotonic() - started)))
print('Observation complete.', flush=True)
