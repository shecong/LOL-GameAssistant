exec(open('tmp/camera-current-analysis.py',encoding='utf-8-sig').read().split('refs=[]')[0])
for target in (0x363790,0x347320,0x347000):
 refs=[]; start=0
 while True:
  j=b.find(b'\xe8',start)
  if j<0 or j+5>len(b):break
  if j+5+struct.unpack_from('<i',b,j+1)[0]==target:refs.append(j)
  start=j+1
 print('CALLS',hex(target),[hex(x) for x in refs])
 for a in refs[:15]:dump(a,160)
dump(0x35ea31,130)
dump(0x347320,360)
