import sys,json
sys.argv.append('--quiet')
from inspect_lcu import get,root
status,data=get('/lol-game-data/assets/v1/generic-assets.json');print(status)
if status==200:
    (root/'generic-assets.json').write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
    def visit(value,path=''):
        if isinstance(value,dict):
            for key,item in value.items():
                if any(word in key.lower() for word in ['crest','prestige','border']):print(path+'/'+key,str(item)[:600])
                else:visit(item,path+'/'+key)
        elif isinstance(value,list):
            for i,item in enumerate(value):visit(item,path+'/'+str(i))
    visit(data)
