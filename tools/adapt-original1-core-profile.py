"""Generate a fingerprint-bound profile from verified code and original1 locators.

No process access, code execution or game writes. Run before runtime validation;
this script alone does not certify a working skin application.
"""
import argparse, hashlib, json, re, shutil, struct
from pathlib import Path
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_GRP_JUMP, CS_GRP_CALL
from capstone.x86 import X86_OP_MEM, X86_REG_RIP

p = argparse.ArgumentParser()
p.add_argument('--capture', type=Path, required=True)
p.add_argument('--original1', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[1] / 'native/LeagueSkinCore'
archive = root / 'profiles/2026-10-22.json'
if not archive.exists():
    archive.parent.mkdir(exist_ok=True)
    shutil.copy2(root/'game-profile.json', archive)
old = json.loads(archive.read_text(encoding='utf-8-sig'))['profile']
manifest = json.loads((a.capture/'capture.json').read_text(encoding='utf8'))
data = (a.capture/'runtime-code.bin').read_bytes()
assert manifest['complete'] and manifest['read_only']
assert len(data) == manifest['image_size']
assert hashlib.sha256(data[manifest['code_rva']:manifest['code_rva']+manifest['code_size']]).hexdigest() == manifest['code_sha256']
raw = a.original1.read_bytes()
assert hashlib.sha256(raw).hexdigest() == 'cf4ed878813631efaf4018ad7cfa8354ab10b3b395a29ca64411e13590a6d3b2'
pe = pefile.PE(data=raw)
md = Cs(CS_ARCH_X86,CS_MODE_64); md.detail = True

def unique(pattern):
    matches = [m.start() for m in re.finditer(pattern,data,re.S)]
    if len(matches) != 1: raise RuntimeError(f'nonunique_locator:{matches[:5]}')
    return matches[0]

def locate_reference(reference, address):
    mask = bytearray([1]*len(reference)); end = 0
    for ins in md.disasm(reference,address):
        offset = ins.address-address; end = offset+ins.size
        if ins.group(CS_GRP_JUMP) or ins.group(CS_GRP_CALL):
            mask[offset+ins.imm_offset:offset+ins.imm_offset+ins.imm_size] = bytes(ins.imm_size)
        if any(op.type==X86_OP_MEM and op.mem.base==X86_REG_RIP for op in ins.operands):
            mask[offset+ins.disp_offset:offset+ins.disp_offset+ins.disp_size] = bytes(ins.disp_size)
    return unique(b''.join(re.escape(bytes([v])) if mask[n] else b'.' for n,v in enumerate(reference[:end])))

def signature(rva):
    text = pe.get_data(rva,250).split(b'\0')[0].decode('ascii')
    return unique(b''.join(b'.' if '?' in s else re.escape(bytes([int(s,16)])) for s in text.split()))

def relative(at, offset=3, size=7): return at+size+struct.unpack_from('<i',data,at+offset)[0]

checks = []
for n,check in enumerate(old['checks']):
    previous = bytes.fromhex(check['hex'])
    current = signature(0xa13b8) if n==0 else locate_reference(previous,check['rva'])
    checks.append(dict(rva=current,hex=data[current:current+len(previous)].hex()))
profile = {key:value for key,value in old.items() if key!='checks'}
profile.update(timestamp=manifest['timestamp'],image_size=manifest['image_size'],
    player_slot=relative(checks[0]['rva']),champion_slot=relative(checks[4]['rva']),
    translate=relative(checks[5]['rva'],1,5),update=checks[2]['rva'],
    push=relative(checks[3]['rva'],1,5),object_list_slot=relative(checks[12]['rva']),
    stack_offset=struct.unpack_from('<i',data,checks[1]['rva']+3)[0],
    skin_offset=struct.unpack_from('<i',data,checks[7]['rva']+2)[0],
    owner_offset=struct.unpack_from('<i',data,checks[10]['rva']+3)[0],checks=checks)
assert profile['skin_offset']==0xbe4 and profile['stack_offset']==0x3638 and profile['owner_offset']==0x560
# Encoder and decoder must match the existing implementation, not just a menu signature.
assert checks[7]['hex']==old['checks'][7]['hex'] and checks[8]['hex']==old['checks'][8]['hex']
sha = hashlib.sha256(json.dumps(profile,sort_keys=True).encode()).hexdigest()
lines = ['// Verified code capture 2026-10-29; original1 locators; unknown builds fail closed.','#pragma once',f'constexpr char GameProfileSha[]="{sha}";']
lines += [f'constexpr uintptr_t Game_{k}=0x{v:x};' for k,v in profile.items() if k!='checks']
for n,c in enumerate(checks):
    lines.append(f'constexpr unsigned char gameFingerprint{n}[]={{'+','.join('0x'+c['hex'][j:j+2] for j in range(0,len(c['hex']),2))+'};')
lines += ['static bool gameProfileMatches(BYTE* base){','return '+' && '.join(f'memcmp(base+0x{c["rva"]:x},gameFingerprint{n},sizeof(gameFingerprint{n}))==0' for n,c in enumerate(checks))+';}']
(root/'native/game_profile.h').write_text('\n'.join(lines)+'\n',encoding='utf8')
(root/'game-profile.json').write_text(json.dumps(dict(inspected_date='2026-10-29',sha256=sha,
    source_dll_sha256=hashlib.sha256(raw).hexdigest(),code_sha256=manifest['code_sha256'],profile=profile),indent=2)+'\n',encoding='utf8')

# Camera's independent guards must move with the common profile. Structural bytes
# remain constrained, and each reference must uniquely identify the saved code.
camera = root/'native/camera_core.h'
text = camera.read_text(encoding='utf-8-sig')
camera_results = []
for name,old_rva in [('CameraLookup',0x35ea31),('CameraDefault',0x346dfa),('CameraViewLookup',0x65ae96),('CameraViewInit',0x98a028)]:
    match = re.search(r'constexpr unsigned char '+name+r'\[\]=\{([^}]+)\};',text)
    reference = bytes(int(v.strip(),16) for v in match[1].split(','))
    current = locate_reference(reference,old_rva)
    text = text.replace(match[0],f'constexpr unsigned char {name}[]={{'+','.join(f'0x{b:02x}' for b in data[current:current+len(reference)])+'};')
    constant = name+'Rva'
    if f'constexpr uintptr_t {constant}=' in text:
        text = re.sub(r'constexpr uintptr_t '+constant+r'=0x[0-9a-f]+;',f'constexpr uintptr_t {constant}=0x{current:x};',text)
    else:
        text = text.replace('#include <cmath>',f'#include <cmath>\nconstexpr uintptr_t {constant}=0x{current:x};')
    text = text.replace(f'base+0x{old_rva:x}',f'base+{constant}')
    if name=='CameraLookup':text=re.sub(r'constexpr uintptr_t CameraSlot=0x[0-9a-f]+;',f'constexpr uintptr_t CameraSlot=0x{relative(current):x};',text)
    if name=='CameraViewLookup':text=re.sub(r'constexpr uintptr_t CameraManagerSlot=0x[0-9a-f]+;',f'constexpr uintptr_t CameraManagerSlot=0x{relative(current):x};',text)
    camera_results.append(dict(name=name,rva=current,hex=data[current:current+len(reference)].hex()))
camera.write_text(text,encoding='utf8')
(root/'original1-profile-locators-2026-10-29.json').write_text(json.dumps(dict(profile_sha256=sha,camera=camera_results),indent=2)+'\n')
print(json.dumps(dict(sha256=sha,profile={k:hex(v) for k,v in profile.items() if k!='checks'},camera=camera_results),indent=2))
