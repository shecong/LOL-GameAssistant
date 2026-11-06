#pragma once
static bool probeText(const char* p,char* out) {
    __try {
        if(!cstring(p,out,160) || strlen(out)<4)return false;
        for(const unsigned char* t=(const unsigned char*)out;*t;t++)if(*t<32 || *t>126)return false;
        return true;
    } __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
static void updateProbe(Request* r) {
    IndependentContext c{};c.entries=(IndependentEntry*)calloc(IndependentMaxEntries,sizeof(IndependentEntry));
    if(!c.entries){fail(r,"allocation_failed");return;}
    __try { __try {
        if(!independentContext(&c,r))__leave;
        Writer w{r->result,0,sizeof(r->result),false};
        append(&w,"{\"ok\":true,\"read_only\":true,\"session\":\"%s\",",c.session);independentState(&w,&c);
        append(&w,",\"resources\":[");
        for(int slot=0;slot<5;slot++) {
            int off=0xb8+slot*8;BYTE* resource=*(BYTE**)(c.stack+off);
            if(slot)append(&w,",");append(&w,"{\"offset\":%d,\"names\":[",off);
            bool comma=false;
            if(objectReadable(resource,0x180))for(int i=0;i<0x180;i+=8) {
                char name[160];const char* candidates[2]={(const char*)(resource+i),*(const char**)(resource+i)};
                for(auto candidate:candidates)if(probeText(candidate,name)){
                    if(comma)append(&w,",");comma=true;quoted(&w,name);
                }
            }
            append(&w,"]}");
        }
        append(&w,"]}");if(w.failed)fail(r,"reply_too_large");
    } __except(EXCEPTION_EXECUTE_HANDLER){fail(r,"read_failed");}
    } __finally {free(c.entries);}
}
