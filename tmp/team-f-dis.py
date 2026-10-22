exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for i in [4,6]: print(i);show(probe['vtable_head'][i],70)
