exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
show(probe['vtable_head'][6],100)
print([o for o in probe.get('objects',[]) if 'own' in o])
