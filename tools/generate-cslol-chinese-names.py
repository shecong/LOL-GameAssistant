"""Build the offline name snapshot from the Chinese client's game-data JSON.

Names are indexed by full skin/chroma ID, including questSkinInfo.tiers.
This does not change the package manifest or any resource paths.
"""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--skins-json', type=Path, required=True)
parser.add_argument('--champions-json', type=Path, required=True)
parser.add_argument('--output', type=Path, default=Path('LOL-GameAssistant/Resources/cslol-names.zh-CN.json'))
args = parser.parse_args()
skins = json.loads(args.skins_json.read_text(encoding='utf-8-sig'))
champions = json.loads(args.champions_json.read_text(encoding='utf-8-sig'))
names = {}

def visit(value):
    if isinstance(value, dict):
        if isinstance(value.get('id'), int) and isinstance(value.get('name'), str):
            names[str(value['id'])] = value['name']
        for child in value.values():
            visit(child)
    elif isinstance(value, list):
        for child in value:
            visit(child)

visit(skins)
heroes = {c['alias']: c.get('description') or c['name'] for c in champions if c.get('id', 0) > 0}
# These package-only forms do not have individual client catalog records.
# Keep the official base name and translate just the package's form label.
forms = {'222061': ('222060', '皮尔特沃夫头号通缉犯')}
for number, label in enumerate(['基础武器', '战士武器', '辅助武器', '刺客武器', '射手武器', '法师武器', '坦克武器'], 234044):
    forms[str(number)] = ('234043', label)
for number, (parent, label) in forms.items():
    if number not in names and parent in names:
        names[number] = names[parent] + ' · ' + label
snapshot = {'source': 'LeagueClient rcp-be-lol-game-data zh_CN; package-only form labels translated separately',
            'champions': dict(sorted(heroes.items())), 'skins': dict(sorted(names.items(), key=lambda item: int(item[0])))}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(snapshot, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'{len(heroes)} champions; {len(names)} skin, chroma and tier names')
