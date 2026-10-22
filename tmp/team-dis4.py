exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for i in [5,6,7,8,9,10,11]:print(i);show(probe['vtable_head'][i],26)
