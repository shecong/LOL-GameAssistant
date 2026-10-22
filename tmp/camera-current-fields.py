exec(open('tmp/camera-current-analysis.py',encoding='utf-8-sig').read().split('refs=[]')[0])
for a,n in [(0x989e80,800),(0x9b4c00,300),(0x346de0,100)]:dump(a,n)
print('global',hex(0x65ae9d+struct.unpack_from('<i',b,0x65ae99)[0]))
