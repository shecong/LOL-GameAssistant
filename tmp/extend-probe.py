from pathlib import Path
p=Path('tmp/skin-adaptation/native/standalone_probe.h');s=p.read_text();needle='BYTE* stack=player+offset;char model[64];'
s=s.replace(needle,needle+'''
            append(&writer,",\\"player_head_hex\\":\\"");for(int n=0;n<0x400;n++)append(&writer,"%02x",player[n]);append(&writer,"\\"");
            uint32_t v=*(uint32_t*)(player+0xbe4);auto ror=[](uint32_t v,int b){return (v>>b)|(v<<(32-b));};
            append(&writer,",\\"skin_candidate\\":%u",ror(ror(v,21)^0x3dc9cb21u,19));
            BYTE* manager=*(BYTE**)(base+0x1c7f430);BYTE** objs=*(BYTE***)(manager+8);int count=*(int*)(manager+16);
            append(&writer,",\\"objects\\":[");int emitted=0;
            if(count>=0 && count<=10000)for(int n=0;n<count;n++){BYTE* obj=objs[n];if(!obj)continue;char name[64];if(!cstring(*(const char**)(obj+offset+0x18),name,sizeof(name)))continue;
                if(emitted++)append(&writer,",");append(&writer,"{\\"model\\":");quoted(&writer,name);append(&writer,",\\"skin\\":%d",*(int*)(obj+offset+0x38));
                if(objectWardModel(name)){BYTE* wrapper=*(BYTE**)(obj+0x560);append(&writer,",\\"owner_present\\":%s,\\"own\\":%s",wrapper?"true":"false",objectReadable(wrapper,64)&&*(BYTE**)wrapper==player?"true":"false");}append(&writer,"}");}
            append(&writer,"]");
''');p.write_text(s)
