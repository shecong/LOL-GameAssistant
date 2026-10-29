"""Offline compatibility audit. Does not execute, inject or patch game code."""
import argparse
import hashlib
import json
from pathlib import Path

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_GRP_JUMP, CS_GRP_CALL
from capstone.x86 import X86_REG_RIP

parser = argparse.ArgumentParser()
parser.add_argument('game', type=Path)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--scan-stored-code', action='store_true', help='Search stored executable bytes even in protected files; never treat hits as verified runtime addresses')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
saved = json.loads((root / 'native/LeagueSkinCore/game-profile.json').read_text(encoding='utf-8-sig'))
profile = saved['profile']
pe = pefile.PE(str(args.game))
section = next(s for s in pe.sections if s.Characteristics & 0x20000000)
protected_image = any(s.Name.rstrip(b'\0') == b'.ace' for s in pe.sections)
code = section.get_data()
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True
checks = []
for check in profile['checks']:
    reference = bytes.fromhex(check['hex'])
    mask = bytearray([255] * len(reference))
    consumed = 0
    for instruction in md.disasm(reference, check['rva']):
        offset = instruction.address - check['rva']
        consumed = offset + instruction.size
        # Only relocation-dependent operands are masked; structure offsets,
        # integer constants and register selection remain constrained.
        if instruction.group(CS_GRP_JUMP) or instruction.group(CS_GRP_CALL):
            for n in range(instruction.imm_offset, instruction.imm_offset + instruction.imm_size):
                mask[offset+n] = 0
        if any(op.mem.base == X86_REG_RIP for op in instruction.operands if op.type == 3):
            for n in range(instruction.disp_offset, instruction.disp_offset + instruction.disp_size):
                mask[offset+n] = 0
    reference = reference[:consumed]
    mask = mask[:consumed]
    # Search using the longest fully constrained run, then validate all bytes.
    runs = []
    start = 0
    while start < len(mask):
        end = start
        while end < len(mask) and mask[end]: end += 1
        if end > start: runs.append((start, end))
        start = end + 1
    candidates = []
    if (not protected_image or args.scan_stored_code) and runs and sum(bool(b) for b in mask) >= 8:
        a, b = max(runs, key=lambda pair: pair[1]-pair[0])
        cursor = 0
        while True:
            hit = code.find(reference[a:b], cursor)
            if hit < 0: break
            cursor = hit + 1
            begin = hit - a
            if begin >= 0 and begin + len(reference) <= len(code) and all(not m or code[begin+n] == reference[n] for n, m in enumerate(mask)):
                candidates.append(section.VirtualAddress + begin)
    checks.append({'old_rva': check['rva'], 'exact_match': pe.get_data(check['rva'],len(bytes.fromhex(check['hex']))) == bytes.fromhex(check['hex']),
        'relocation_masked_candidates': candidates, 'constrained_bytes': sum(bool(b) for b in mask),
        'candidate_is_verified': False})
result = {'game_path': str(args.game), 'game_file_sha256': hashlib.sha256(args.game.read_bytes()).hexdigest(),
    'timestamp': pe.FILE_HEADER.TimeDateStamp, 'image_size': pe.OPTIONAL_HEADER.SizeOfImage,
    'expected_timestamp': profile['timestamp'], 'expected_image_size': profile['image_size'], 'checks': checks,
    'compatible_with_existing_profile': pe.FILE_HEADER.TimeDateStamp == profile['timestamp'] and pe.OPTIONAL_HEADER.SizeOfImage == profile['image_size'] and all(c['exact_match'] for c in checks),
    'profile_updated': False, 'game_runtime_verified': False, 'protected_disk_image': protected_image,
    'candidate_search_skipped': protected_image and not args.scan_stored_code,
    'note': 'Protected disk images require an authorized loaded-code reference before adaptation; disk signature mismatches do not prove corresponding runtime functions changed. Candidates are navigation hints only and do not validate structures, globals, ABI or runtime effects.'}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'compatible': result['compatible_with_existing_profile'], 'exact_matches': sum(c['exact_match'] for c in checks),
    'unique_candidates': sum(len(c['relocation_masked_candidates']) == 1 for c in checks), 'checks': len(checks), 'report': str(args.output)}))
