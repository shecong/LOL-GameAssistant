exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for i,f in enumerate(probe.get('vtable_head',[])):
 print('slot',hex(i*8));show(f,85)
