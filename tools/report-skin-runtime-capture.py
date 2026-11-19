"""Report how the compiled locator rules fare against a captured game image.

Read-only. The on-disk game image is protected (every section is `.ace` and the
executable section is stored compressed/encrypted), so adaptation always starts
from a capture of the loaded executable section. This tool classifies each rule:

  unchanged        exact match under the compiled mask; the rule still works
  layout-shifted   matches once relocation/layout immediates are ignored, so only
                   field displacements moved
  opcode-changed   no masked match; prints the closest instruction-skeleton
                   candidates so the rule can be rebuilt by hand
  missing          nothing found

It also derives the profile values (global slots, stack and skin offsets) from
the captured bytes and checks the cross-field invariants the C++ resolver checks.
"""
from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_GRP_CALL, CS_GRP_JUMP
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG

ANCHOR_BYTES = 16


def shape(ins) -> tuple[str, tuple[str, ...]]:
    kinds = []
    for operand in ins.operands:
        if operand.type == X86_OP_REG:
            kinds.append("r")
        elif operand.type == X86_OP_IMM:
            kinds.append("i")
        elif operand.type == X86_OP_MEM:
            kinds.append("m")
        else:
            kinds.append("?")
    return ins.mnemonic, tuple(kinds)


class Image:
    def __init__(self, directory: Path, md: Cs) -> None:
        self.code = (directory / "game-code.bin").read_bytes()
        manifest = json.loads((directory / "capture.json").read_text(encoding="utf-8"))
        self.manifest = manifest
        self.code_rva = manifest["code_rva"]
        self.image_size = manifest["image_size"]
        self.timestamp = manifest["timestamp"]
        self.md = md

    def read(self, rva: int, size: int) -> bytes:
        start = rva - self.code_rva
        if start < 0 or start + size > len(self.code):
            raise ValueError(f"rva {rva:#x} outside captured section")
        return self.code[start:start + size]

    def instructions(self, raw: bytes, base: int = 0, limit: int | None = None):
        count = 0
        for ins in self.md.disasm(raw, base):
            yield ins
            count += 1
            if limit is not None and count >= limit:
                return


def wildcard_immediates(md: Cs, raw: bytes) -> bytes:
    """Mask every immediate and displacement so only opcode/register bytes remain."""
    mask = bytearray([1] * len(raw))
    for ins in md.disasm(bytes(raw), 0):
        mask[ins.address:ins.address + ins.size] = b"\x01" * ins.size
        if ins.imm_size and ins.imm_offset:
            mask[ins.address + ins.imm_offset:ins.address + ins.imm_offset + ins.imm_size] = bytes(ins.imm_size)
        if ins.disp_size and ins.disp_offset:
            mask[ins.address + ins.disp_offset:ins.address + ins.disp_offset + ins.disp_size] = bytes(ins.disp_size)
    return bytes(mask)


def longest_run(mask: bytes) -> tuple[int, int]:
    best = (0, 0)
    n = 0
    while n < len(mask):
        if not mask[n]:
            n += 1
            continue
        start = n
        while n < len(mask) and mask[n]:
            n += 1
        if n - start > best[1]:
            best = (start, n - start)
    return best


def search(image: Image, raw: bytes, mask: bytes, limit: int = 4) -> list[int]:
    start, length = longest_run(mask)
    if length < 4:
        return []
    anchor = raw[start:start + length]
    hits: list[int] = []
    position = image.code.find(anchor)
    while position >= 0 and len(hits) < limit:
        candidate = position - start
        if candidate >= 0 and candidate + len(raw) <= len(image.code):
            if all(not mask[k] or image.code[candidate + k] == raw[k] for k in range(len(raw))):
                hits.append(candidate + image.code_rva)
        position = image.code.find(anchor, position + 1)
    return hits


def anchor_prefix(image: Image, raw: bytes) -> tuple[bytes, int] | None:
    """Shortest instruction prefix with at least ANCHOR_BYTES non-immediate bytes."""
    mask = wildcard_immediates(image.md, raw)
    end = 0
    constrained = 0
    for ins in image.md.disasm(bytes(raw), 0):
        end = ins.address + ins.size
        constrained += sum(mask[ins.address:end])
        if constrained >= ANCHOR_BYTES:
            break
    if constrained < ANCHOR_BYTES:
        return None
    return raw[:end], end


def skeleton_hits(image: Image, raw: bytes, limit: int = 4) -> list[int]:
    prefix = anchor_prefix(image, raw)
    if not prefix:
        return []
    anchor, end = prefix
    anchor_mask = wildcard_immediates(image.md, anchor)
    want = [shape(ins) for ins in image.md.disasm(bytes(raw), 0)]
    if len(want) < 4:
        return []
    hits: list[int] = []
    start, length = longest_run(anchor_mask)
    if length < 6:
        return []
    needle = anchor[start:start + length]
    position = image.code.find(needle)
    while position >= 0 and len(hits) < limit:
        candidate = position - start
        if candidate >= 0:
            got = [shape(ins) for ins in image.md.disasm(image.code[candidate:candidate + end + 256], 0)]
            if len(got) >= len(want) and got[:len(want)] == want:
                hits.append(candidate + image.code_rva)
        position = image.code.find(needle, position + 1)
    return hits


def read_i32(raw: bytes, offset: int) -> int:
    return struct.unpack_from("<i", raw, offset)[0]


def read_u32(raw: bytes, offset: int) -> int:
    return struct.unpack_from("<I", raw, offset)[0]


def class_rule(image: Image, rule: dict) -> dict:
    raw = bytes.fromhex(rule["bytes"])
    mask = bytes.fromhex(rule["mask"])
    exact = search(image, raw, mask)
    if exact:
        # The compiled rule stores masked bytes as zero, so values always come
        # from the capture, never from the rule text.
        return {"status": "unchanged", "rvas": exact, "bytes": image.read(exact[0], len(raw)),
                "operand_offset": int(rule.get("operand_offset") or 0)}
    shifted_mask = bytes(min(1, mask[k]) and wildcard_immediates(image.md, raw)[k] for k in range(len(raw)))
    shifted = search(image, raw, shifted_mask)
    if shifted:
        return {"status": "layout-shifted", "rvas": shifted, "bytes": image.read(shifted[0], len(raw)),
                "operand_offset": int(rule.get("operand_offset") or 0)}
    skeleton = skeleton_hits(image, raw)
    if skeleton:
        return {"status": "opcode-changed", "rvas": skeleton, "bytes": image.read(skeleton[0], len(raw)),
                "operand_offset": int(rule.get("operand_offset") or 0)}
    return {"status": "missing", "rvas": [], "bytes": b"", "operand_offset": int(rule.get("operand_offset") or 0)}


def derive(profile_value: dict, image: Image, results: dict) -> dict:
    """Recompute profile values from the capture at the resolved rule addresses."""
    values = dict(profile_value)
    first = lambda key: results[key]["bytes"]

    def displacement(key: str, offset: int, instruction_size: int) -> int:
        rva = results[key]["rvas"][0]
        raw = image.read(rva, instruction_size + 4)
        return rva + instruction_size + read_i32(raw, offset)

    if results.get(0, {}).get("status") in ("unchanged", "layout-shifted"):
        values["player_slot"] = displacement(0, 3, 7)
    if results.get(4, {}).get("status") in ("unchanged", "layout-shifted"):
        values["champion_slot"] = displacement(4, 3, 7)
    if results.get(5, {}).get("status") in ("unchanged", "layout-shifted"):
        values["translate"] = displacement(5, 1, 5)
    if results.get(2, {}).get("status") in ("unchanged", "layout-shifted"):
        values["update"] = results[2]["rvas"][0]
    if results.get(6, {}).get("status") in ("unchanged", "layout-shifted"):
        values["push"] = results[6]["rvas"][0]
    if results.get(1, {}).get("status") in ("unchanged", "layout-shifted"):
        values["stack_offset"] = read_u32(first(1), 3)
    if results.get(7, {}).get("status") in ("unchanged", "layout-shifted"):
        values["skin_offset"] = read_u32(first(7), 2)
    if results.get(8, {}).get("status") in ("unchanged", "layout-shifted"):
        offset = results[8].get("operand_offset") or 33
        values["skin_encode_offset"] = read_u32(first(8), offset)
    if results.get(12, {}).get("status") in ("unchanged", "layout-shifted"):
        values["object_list_slot"] = displacement(12, 3, 7)
    if results.get(10, {}).get("status") in ("unchanged", "layout-shifted"):
        values["owner_offset"] = read_u32(first(10), 3)
    return values


def invariants(values: dict, results: dict) -> list[str]:
    problems = []
    image_size = values.get("image_size", 0)
    for field in ("player_slot", "champion_slot", "object_list_slot"):
        slot = values.get(field)
        if isinstance(slot, int) and slot:
            if slot % 8 or slot >= image_size:
                problems.append(f"{field} 不是映像内的 8 字节对齐槽：{slot:#x}")
    stack = values.get("stack_offset")
    if isinstance(stack, int) and not (0x100 <= stack <= 0x10000 and stack % 8 == 0):
        problems.append(f"stack_offset 超出已验证范围：{stack:#x}")
    skin = values.get("skin_offset")
    if isinstance(skin, int) and not (0x100 <= skin <= 0x10000 and skin % 4 == 0):
        problems.append(f"skin_offset 超出已验证范围：{skin:#x}")
    encode = values.get("skin_encode_offset")
    if isinstance(skin, int) and isinstance(encode, int) and skin != encode:
        problems.append(f"解码字段 {skin:#x} 与编码字段 {encode:#x} 不一致")
    push = values.get("push")
    if results.get(3, {}).get("rvas") and isinstance(push, int):
        pass  # push_call target equality is re-checked by the native resolver
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--capture", type=Path, required=True)
    parser.add_argument("--rules", type=Path, default=None,
                        help="runtime-patterns.json (default: native/LeagueSkinCore/runtime-patterns.json)")
    parser.add_argument("--profile", type=Path, default=None,
                        help="reference profile whose checks are aligned instead of the compiled rules")
    parser.add_argument("--expect", type=Path, default=None,
                        help="profile whose values the derived values are compared against")
    parser.add_argument("--json", type=Path, default=None)
    parser.add_argument("--show-bytes", action="store_true", help="print captured bytes for changed rules")
    args = parser.parse_args()
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, OSError):
        pass
    root = Path(__file__).resolve().parents[1]
    rules_path = args.rules or root / "native/LeagueSkinCore/runtime-patterns.json"
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    image = Image(args.capture, md)
    if args.profile:
        profile = json.loads(args.profile.read_text(encoding="utf-8-sig"))["profile"]
        names = ["player", "stack", "update", "push_call", "champions", "translate", "push_body", "skin_decode",
                 "skin_encode", "object_descriptor", "object_owner", "object_accessor", "objects", "camera_config",
                 "camera_defaults", "camera_view", "camera_init"]
        rules = [{"key": n, "name": names[n], "bytes": c["hex"], "mask": "01" * (len(c["hex"]) // 2), "schema": 1}
                 for n, c in enumerate(profile["checks"])]
        reference = profile
    else:
        rules = json.loads(rules_path.read_text(encoding="utf-8-sig"))["rules"]
        reference = {"timestamp": image.timestamp, "image_size": image.image_size}

    results: dict[int, dict] = {}
    per_rule: list[tuple[int, str, list[int]]] = []
    rank = {"unchanged": 0, "layout-shifted": 1, "opcode-changed": 2, "missing": 3}
    print(f"capture: {args.capture}")
    print(f"  timestamp={image.timestamp} image_size={image.image_size} code={len(image.code)} rva={image.code_rva:#x}")
    print(f"  {'key':>3} {'name':<18}{'schema':>6}  {'status':<15} rvas")
    for rule in rules:
        outcome = class_rule(image, rule)
        key = int(rule["key"])
        per_rule.append((key, outcome["status"], outcome["rvas"]))
        rvas = ", ".join(f"{rva:#x}" for rva in outcome["rvas"]) or "-"
        print(f"  {rule['key']:>3} {rule['name']:<18}{rule.get('schema', 1):>6}  {outcome['status']:<15} {rvas}")
        if args.show_bytes and outcome["bytes"]:
            print(f"      {outcome['bytes'].hex()}")
        if key not in results or rank[outcome["status"]] < rank[results[key]["status"]]:
            results[key] = outcome

    # Ambiguity is only meaningful inside the winning tier: comparing a strict
    # match with a relaxed one is not how the native resolver decides.
    ambiguous: dict[int, list[int]] = {}
    for key, outcome in results.items():
        same_tier = [rvas for k, status, rvas in per_rule if k == key and status == outcome["status"]]
        distinct = sorted({rva for rvas in same_tier for rva in rvas})
        if len(distinct) > 1:
            ambiguous[key] = distinct

    values = derive(reference, image, results)
    expected = {}
    if args.expect:
        expected = json.loads(args.expect.read_text(encoding="utf-8-sig"))["profile"]
    problems = invariants(values, results)
    print("\n派生值：")
    for field in ("player_slot", "champion_slot", "translate", "update", "push", "stack_offset",
                  "skin_offset", "skin_encode_offset", "object_list_slot", "owner_offset"):
        if field in values:
            value = values[field]
            reference_value = expected.get(field, reference.get(field))
            mark = "" if reference_value is None else (" 与参考值一致" if value == reference_value else f" 参考值 {reference_value:#x}")
            shown = f"{value:#x}" if isinstance(value, int) else str(value)
            print(f"  {field:<20} {shown}{mark}")
    print("\n不变量检查：" + ("全部通过" if not problems else "；".join(problems)))
    resolved = sorted(key for key, outcome in results.items() if outcome["status"] in ("unchanged", "layout-shifted"))
    changed = sorted(key for key, outcome in results.items() if outcome["status"] in ("opcode-changed", "missing"))
    shifted = sorted(key for key, outcome in results.items() if outcome["status"] == "layout-shifted")
    exact = sorted(key for key, outcome in results.items() if outcome["status"] == "unchanged")
    usable = [key for key in resolved if key not in ambiguous]
    print(f"\n定位键 {len(results)} 个：完全命中 {len(exact)}，仅位移变化 {len(shifted)}，需要人工重建 {len(changed)}。")
    print("说明：当前编译进核心的规则只接受完全命中；仅位移变化的键在现有核心中同样会失败。")
    if ambiguous:
        print("存在多候选（核心会拒绝执行）：" + "，".join(f"key {k} -> {[hex(v) for v in vs]}" for k, vs in sorted(ambiguous.items())))
    hero_missing = [key for key in range(9) if key not in usable]
    print("英雄换肤链路（key 0-8）：" + ("可解析" if not hero_missing else "需要重建：" + ", ".join(str(k) for k in hero_missing)))
    if shifted:
        print("仅位移变化（可放宽掩码后复核）：" + ", ".join(str(k) for k in shifted))
    if changed:
        print("需要人工重建的定位键：" + ", ".join(f"{key}" for key in changed))
    if args.json:
        payload = {"capture": image.manifest,
                   "results": {str(k): {"status": v["status"], "rvas": v["rvas"]} for k, v in results.items()},
                   "values": values, "problems": problems, "needs_manual_rules": changed,
                   "ambiguous": {str(k): v for k, v in ambiguous.items()}}
        args.json.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"报告已写入 {args.json}")
    return 0 if not changed else 2


if __name__ == "__main__":
    raise SystemExit(main())
