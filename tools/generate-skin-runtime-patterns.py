"""Compile local game-code references into version-independent locator rules.

No original assistant, remote DLL, network or process access is used. Relocation
operands and explicitly extracted layout displacements become wildcards; opcode,
register, encoding constants and layer-layout bytes remain constrained.
"""
import copy, hashlib, json, re
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_GRP_JUMP, CS_GRP_CALL
from capstone.x86 import X86_OP_MEM, X86_REG_RIP

root = Path(__file__).resolve().parents[1]
native = root/'native/LeagueSkinCore/native'
profile = json.loads((native.parent/'game-profile.json').read_text(encoding='utf-8-sig'))['profile']
old = json.loads((native.parent/'profiles/2026-10-22.json').read_text(encoding='utf-8-sig'))['profile']
names = ['player','stack','update','push_call','champions','translate','push_body','skin_decode','skin_encode',
         'object_descriptor','object_owner','object_accessor','objects','camera_config','camera_defaults','camera_view','camera_init']
md = Cs(CS_ARCH_X86,CS_MODE_64); md.detail=True
rules=[]
def rule(key, raw, rva):
    mask=bytearray([1]*len(raw)); used=0
    for ins in md.disasm(raw,rva):
        off=ins.address-rva;used=off+ins.size
        if ins.group(CS_GRP_JUMP) or ins.group(CS_GRP_CALL):
            mask[off+ins.imm_offset:off+ins.imm_offset+ins.imm_size]=bytes(ins.imm_size)
        if any(o.type==X86_OP_MEM and o.mem.base==X86_REG_RIP for o in ins.operands):
            mask[off+ins.disp_offset:off+ins.disp_offset+ins.disp_size]=bytes(ins.disp_size)
    for lo,hi in {1:[(3,7)],7:[(2,6)],8:[(33,37)]}.get(key,[]):mask[lo:hi]=bytes(hi-lo)
    raw=raw[:used];mask=mask[:used]
    assert used>=10 and sum(mask)>=8, (key,used,sum(mask))
    # Wildcard bytes have no influence on the compiled rule identity.
    normalized=bytes(b if mask[n] else 0 for n,b in enumerate(raw))
    candidate=(key,normalized,bytes(mask))
    if candidate not in rules:rules.append(candidate)
for key,c in enumerate(profile['checks']):rule(key,bytes.fromhex(c['hex']),c['rva'])
# The two known player call sites use different instruction shapes. Keep both;
# ambiguity between different addresses is rejected at runtime.
rule(0,bytes.fromhex(old['checks'][0]['hex']),old['checks'][0]['rva'])
camera=(native/'camera_core.h').read_text(encoding='utf8')
for key,name in enumerate(['CameraLookup','CameraDefault','CameraViewLookup','CameraViewInit'],13):
    raw=bytes(int(v.strip(),16) for v in re.search(r'unsigned char '+name+r'\[\]=\{([^}]+)',camera)[1].split(','))
    rva=int(re.search(r'uintptr_t '+name+r'Rva=0x([0-9a-f]+)',camera)[1],16)
    rule(key,raw,rva)
schema=hashlib.sha256(b''.join(bytes([key])+raw+mask for key,raw,mask in rules)).hexdigest()
lines=['// Generated from local game-code references. No external assistant is required.','#pragma once',
       f'constexpr char RuntimeRulesSha[]="{schema}";','constexpr unsigned RuntimeRuleCount=17;']
for n,(key,raw,mask) in enumerate(rules):
    lines.append(f'constexpr BYTE runtimeBytes{n}[]={{'+','.join(f'0x{b:02x}' for b in raw)+'};')
    lines.append(f'constexpr BYTE runtimeMask{n}[]={{'+','.join(str(v) for v in mask)+'};')
lines.append('static const RuntimePattern RuntimePatterns[]={')
for n,(key,raw,mask) in enumerate(rules):lines.append(f'{{"{names[key]}",{key},runtimeBytes{n},runtimeMask{n},sizeof(runtimeBytes{n})}},')
lines.append('};')
(native/'runtime_patterns.h').write_text('\n'.join(lines)+'\n',encoding='utf8')
(native.parent/'runtime-patterns.json').write_text(json.dumps({'schema_sha256':schema,'rules':[
    {'key':key,'name':names[key],'bytes':raw.hex(),'mask':mask.hex()} for key,raw,mask in rules]},indent=2)+'\n')
print(json.dumps({'rules':len(rules),'schema_sha256':schema}))

# Immutable inputs for cross-build and simulated-update tests. These are code
# excerpts from local references, not a second implementation of the resolver.
future=copy.deepcopy(profile)
code_delta=0x10000;data_delta=0x2000
future['timestamp']=0x71001234;future['image_size']+=0x30000
for field in ['player_slot','champion_slot','object_list_slot']:future[field]+=data_delta
for field in ['translate','update','push']:future[field]+=code_delta
future['stack_offset']=0x3700;future['skin_offset']=0xc00
for key,c in enumerate(future['checks']):
    raw=bytearray.fromhex(c['hex'])
    for ins in md.disasm(bytes(raw),c['rva']):
        if any(o.type==X86_OP_MEM and o.mem.base==X86_REG_RIP for o in ins.operands):
            pos=ins.address-c['rva']+ins.disp_offset
            value=int.from_bytes(raw[pos:pos+4],'little',signed=True)+data_delta-code_delta
            raw[pos:pos+4]=value.to_bytes(4,'little',signed=True)
    if key==1:raw[3:7]=future['stack_offset'].to_bytes(4,'little')
    if key==7:raw[2:6]=future['skin_offset'].to_bytes(4,'little')
    if key==8:raw[33:37]=future['skin_offset'].to_bytes(4,'little')
    c['rva']+=code_delta;c['hex']=raw.hex()
fixtures=[old,profile,future]
test=['// Local reference fixtures and one synthetic relocated/layout-shifted update; no game execution.','#pragma once',
      'struct RuntimeFixtureBlock {uintptr_t rva;const BYTE* bytes;size_t size;};',
      'struct RuntimeFixture {DWORD timestamp,size;uintptr_t player,champions,translate,update,push,stack,skin;const RuntimeFixtureBlock* blocks;size_t count;};']
for family,source in enumerate(fixtures):
    for n,c in enumerate(source['checks']):test.append(f'constexpr BYTE fixture{family}Bytes{n}[]={{'+','.join('0x'+c['hex'][j:j+2] for j in range(0,len(c['hex']),2))+'};')
    test.append(f'static const RuntimeFixtureBlock fixture{family}Blocks[]={{')
    for n,c in enumerate(source['checks']):test.append(f'{{0x{c["rva"]:x},fixture{family}Bytes{n},sizeof(fixture{family}Bytes{n})}},')
    test.append('};')
test.append('static const RuntimeFixture RuntimeFixtures[]={')
for family,source in enumerate(fixtures):
    values=[source[k] for k in ['timestamp','image_size','player_slot','champion_slot','translate','update','push','stack_offset','skin_offset']]
    test.append('{'+','.join(hex(v) for v in values)+f',fixture{family}Blocks,sizeof(fixture{family}Blocks)/sizeof(RuntimeFixtureBlock)'+'},')
test.append('};')
(native/'runtime_profile_test_fixtures.h').write_text('\n'.join(test)+'\n',encoding='utf8')
