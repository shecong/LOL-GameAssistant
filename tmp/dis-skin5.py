exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
show(0x2821b0,85)
show(0x2221c0,75)
pat=rb'\x48\x8b\x0d....\xe8....\x48\x8b\x0d....\xe8....\x48\x8b\x0d....\xe8....\x48\x8b\x0d....\x48\x8b\x01'
for m in re.finditer(pat,d,re.S): print('list',hex(m.start()),'slot',hex(m.start()+7+struct.unpack_from('<i',d,m.start()+3)[0]));show(m.start(),70)
