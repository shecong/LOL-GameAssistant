from pathlib import Path
p=Path('tmp/skin-adaptation/native/standalone_probe.h');s=p.read_text();needle='uint32_t v=*(uint32_t*)(player+0xbe4);';s=s.replace(needle,'''BYTE* teams=*(BYTE**)(base+0x1c7be58);append(&writer,",\\"team_table\\":[");for(int n=0;n<16;n++){if(n)append(&writer,",");append(&writer,"%d",*(int*)(teams+0x20+n*4));}append(&writer,"]");
            '''+needle);p.write_text(s)
