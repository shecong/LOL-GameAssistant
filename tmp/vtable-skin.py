exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for j in [0x20//8,0x30//8,0x3e]:
 f=struct.unpack_from('<Q',d,0x18555d8+j*8)[0]-0x140000000;print('vtable',hex(j*8),hex(f));show(f,160)
# start at known boundary preceding skin encoder
show(0x282190,110)
