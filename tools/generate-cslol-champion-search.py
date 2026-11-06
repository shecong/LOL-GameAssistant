"""Generate offline search data. Requires pypinyin only when regenerating this file.

Inputs: Tencent hero_list.js JSON and Meraki champions.json snapshots.
Sources: https://game.gtimg.cn/images/lol/act/img/js/heroList/hero_list.js
         https://cdn.merakianalytics.com/riot/lol/resources/latest/en-US/champions.json
"""
import argparse
import json
from pathlib import Path

from pypinyin import Style, lazy_pinyin


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tencent_json")
    parser.add_argument("positions_json")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    heroes = {hero["alias"]: hero for hero in read(args.tencent_json)["hero"]}
    positions = read(args.positions_json)
    names = read(root / "LOL-GameAssistant/Resources/cslol-names.zh-CN.json")["champions"]
    result = {}
    for key, name in sorted(names.items()):
        base = key.removeprefix("Jade_")
        if base == "Wukong":
            base = "MonkeyKing"
        hero = heroes.get(base, {})
        words = set(filter(None, [name, key, base, hero.get("name"), hero.get("title")]))
        words.update(filter(None, hero.get("keywords", "").split(",")))
        for word in list(words):
            words.add("".join(lazy_pinyin(word)))
            words.add("".join(lazy_pinyin(word, style=Style.FIRST_LETTER)))
        lanes = positions.get(base, {}).get("positions", [])
        # New champion missing from the position snapshot; its base role is top.
        if base == "Zaahen" and not lanes:
            lanes = ["TOP"]
        result[key] = {"keywords": sorted(words), "positions": sorted(set(lanes))}
    output = root / "LOL-GameAssistant/Resources/cslol-champion-search.json"
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(result)} champion search records to {output}")


if __name__ == "__main__":
    main()
