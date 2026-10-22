exec(open('tmp/camera-current-analysis.py',encoding='utf-8-sig').read().split('refs=[]')[0])
for a,n in [(0x9c3920,750),(0x9a7a70,230),(0x9b7340,250)]:dump(a,n)
