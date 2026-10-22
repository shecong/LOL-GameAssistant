exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for i in [3,4,5,6]:print(i);show(probe['vtable_head'][i],45)
