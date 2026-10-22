exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for label,pat in [('decode',rb'\x48\xf7\xd0\x48\x31'),('byte_decode',rb'\xf6\xd0\x30'),('owner',rb'\x48\x81\xc1..\x00\x00\xe9')]:
 ms=list(re.finditer(pat,d,re.S));print(label,len(ms))
 for m in ms:
  at=m.start()
  if label!='owner' and 0x200000<at<0x400000:show(at-90,240)
  elif label=='owner' and 0x270000<at<0x2a0000:print(hex(at),d[at:at+12].hex())
