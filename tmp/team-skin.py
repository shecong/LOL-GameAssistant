exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
head=bytes.fromhex(probe['player_head_hex']);print('team candidates',[(hex(i),v) for i,v in enumerate(head[:0x280]) if v in (1,2) and (i==0 or head[i-1]==0) and head[i+1]==0]);show(0x2821d0,50)
# references to the virtual Team interface: identify its root getter using vtable and disk image data.
