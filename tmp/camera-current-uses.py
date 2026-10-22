exec(open('tmp/camera-current-analysis.py',encoding='utf-8-sig').read().split('refs=[]')[0])
md.skipdata=True
xs=list(md.disasm(b[0x980000:0x9c0000],0x980000))
for k,i in enumerate(xs):
 if any(t in i.op_str for t in ('+ 0x3c0]','+ 0x3c4]','+ 0x3b0]')):
  print('\nFIELD')
  for x in xs[max(0,k-4):k+7]:print(hex(x.address),x.mnemonic,x.op_str)
