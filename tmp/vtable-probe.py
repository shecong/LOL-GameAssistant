from pathlib import Path
p=Path('tmp/skin-adaptation/native/standalone_probe.h');s=p.read_text();needle='uint32_t v=*(uint32_t*)(player+0xbe4);'
s=s.replace(needle,'''append(&writer,",\\"vtable_head\\":[");for(int n=0;n<12;n++){if(n)append(&writer,",");append(&writer,"%llu",(uint64_t)((*(BYTE***)player)[n]-base));}append(&writer,"]");
            '''+needle);p.write_text(s)
