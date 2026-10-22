exec(open('tmp/camera-current-analysis.py',encoding='utf-8-sig').read().split('refs=[]')[0])
for p in (0x9b737e+0xeac112,0x9b7387+0xeac112):print(hex(p),struct.unpack_from('<f',b,p)[0])
dump(0x995e80,290)
dump(0x9c3d7d,340)
