from pathlib import Path
p=Path('tmp/skin-effects/native/core.cpp');s=p.read_text()
custom=r'''static void process(Request* r) {
 IndependentContext c{};c.entries=(IndependentEntry*)calloc(IndependentMaxEntries,sizeof(IndependentEntry));
 if(!c.entries){fail(r,"allocation_failed");return;}
 __try { __try {
  if(!independentContext(&c,r))__leave;
  Writer w{r->result,0,sizeof(r->result),false};append(&w,"{\"ok\":true,\"model\":");quoted(&w,c.hero);
  append(&w,",\"skin\":%d,\"resources\":[",*(int*)(c.stack+0x38));
  for(int n=0;n<5;n++){
   if(n)append(&w,",");size_t off=0xb8+n*8;BYTE* resource=*(BYTE**)(c.stack+off);
   append(&w,"{\"stack_offset\":%zu,\"address\":%llu,\"head\":\"",off,(uint64_t)resource);
   if(objectReadable(resource,256))for(int j=0;j<256;j++)append(&w,"%02x",resource[j]);
   append(&w,"\",\"strings\":[");bool comma=false;
   if(objectReadable(resource,256))for(int j=0;j<256;j+=8){const char* str=*(const char**)(resource+j);char txt[128];
    if(!objectReadable(str,128) || !cstring(str,txt,sizeof(txt)) || strlen(txt)<4)continue;
    bool valid=true;for(const char* at=txt;*at;at++)if(*at<32 || *at>126)valid=false;
    if(valid){if(comma)append(&w,",");quoted(&w,txt);comma=true;}}
   append(&w,"]}");}
  append(&w,"]}");
 }__except(EXCEPTION_EXECUTE_HANDLER){fail(r,"read_exception");} }__finally {free(c.entries);}
}'''
s=s.replace('static void process(Request* r) { independentProcess(r); }',custom);p.write_text(s)
