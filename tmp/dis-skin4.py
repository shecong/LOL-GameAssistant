exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for off in [0xbe4,0xbe0,0x560]:
 ms=[m.start() for m in re.finditer(re.escape(struct.pack('<I',off)),d[:0x1800000])];print('offset',hex(off),len(ms))
 for at in ms[:8]:show(at-12,78)
pat=rb'\xe8....\x4c\x3b.\x0f\x94\xc0'
for m in re.finditer(pat,d,re.S):
 print('owner call',hex(m.start()));show(m.start()-25,60);target=m.start()+5+struct.unpack_from('<i',d,m.start()+1)[0];show(target,40)
