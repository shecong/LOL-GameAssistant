exec(open('tmp/analyze-skin.py').read().split("for p in probe['signatures']")[0])
for m in list(re.finditer(re.escape(struct.pack('<I',0x3638+0x38)),d)):
 if m.start()<0x1800000: print('stack_skin',hex(m.start()));show(m.start()-30,100)
