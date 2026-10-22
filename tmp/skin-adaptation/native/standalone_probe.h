// Read-only feasibility probe for an independent engine. No original module calls.
// Signatures are from MIT R3nzSkin source and the user-owned reference binary.
struct ProbePattern {const char* name;const char* text;int bytes[128];size_t length;int matches;uintptr_t first,second;};
static bool parseProbePattern(ProbePattern* p) {
    const char* s=p->text;p->length=0;p->matches=0;p->first=0;
    while(*s) {
        while(*s==' ')s++;if(!*s)break;if(p->length>=128)return false;
        if(*s=='?') {p->bytes[p->length++]=-1;s++;if(*s=='?')s++;}
        else {char* end=nullptr;long value=strtol(s,&end,16);if(end==s || value<0 || value>255)return false;p->bytes[p->length++]=(int)value;s=end;}
        if(*s && *s!=' ')return false;
    }
    return p->length>=5;
}
static bool probeReadable(DWORD protect) {
    if(protect&(PAGE_GUARD|PAGE_NOACCESS))return false;
    DWORD kind=protect&0xff;
    return kind==PAGE_EXECUTE_READ || kind==PAGE_EXECUTE_READWRITE || kind==PAGE_EXECUTE_WRITECOPY;
}
static bool standaloneProbe(Request* request) {
    ProbePattern patterns[]={
        {"public_player","48 8B 3D ? ? ? ? 48 85 FF 74 15 48 81 C7"},
        {"public_stack","48 8D 8D ? ? 00 00 44 8B 8C 24 ? ? 00 00"},
        {"public_update","88 54 24 10 55 53 56 57 41 54 41 55 41 56 41"},
        {"public_push","E8 ? ? ? ? 48 8D 8D ? ? 00 00 E8 ? ? ? ? 48 85 C0 74 ? 48 85 ED"},
        {"public_champion_manager","48 8B 0D ? ? ? ? 48 69 D0 ? ? 00 00 48 8B 05"},
        {"public_translate","E8 ? ? ? ? 45 33 C0 48 8B D0 48 8B CB E8 ? ? ? ? 48"},
        {"reference_player_candidate","48 8B ? ? ? ? ? 48 85 C0 75 ? 48 8B 1D ? ? ? ? 48 8D"},
        {"reference_skin_field","0F B6 ? ? 88 86 ? ? ? ? 0F B6 ? ? 88 86 ? ? ? ? 0F B6 ? ? 88 86 ? ? ? ? 40"},
        {"reference_push","E8 ? ? ? ? 48 8D 8D ? ? ? ? E8 ? ? ? ? 48 85 C0"},
        {"reference_update_candidate","E9 ? ? ? ? 48 8B 42 08 4C 8B C1 33 C9"},
        {"reference_hero_list","48 8B 05 ? ? ? ? 48 8B 78 08 8B 40 10 4C 8D 3C C7 49"},
        {"reference_champion_manager_candidate","48 89 0D ? ? ? ? 48 8D 05 ? ? ? ? 48 89 01 4C 8B F9"},
        {"reference_translate_candidate","E8 ? ? ? ? 48 8D 15 ? ? ? ? 48 8B D8 48 8B 08 4C 8B 81"}
        ,{"reference_complete_skin_field","88 86 ? ? ? ? 48 ? ? ?  0F B6 ? ? 88 86 ? ? ? ? 0F B6 ? ? 88 86 ? ? ? ? 0F B6 ? ? 88 86 ? ? ? ? 40"}
    };
    for(auto& p:patterns)if(!parseProbePattern(&p)){fail(request,"invalid_probe_pattern");return false;}
    BYTE* base=(BYTE*)GetModuleHandleW(nullptr);auto dos=(IMAGE_DOS_HEADER*)base;
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE || dos->e_lfanew<=0 || dos->e_lfanew>1024*1024){fail(request,"game_image_header_unavailable");return false;}
    auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC || nt->OptionalHeader.SizeOfImage>0x80000000){fail(request,"game_image_header_unavailable");return false;}
    HANDLE dump=CreateFileW(L"E:\\code\\weather-app\\tmp\\skin-adaptation\\runtime-code.bin",GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    DWORD written=0;if(dump!=INVALID_HANDLE_VALUE)WriteFile(dump,base,4096,&written,nullptr);
    uintptr_t at=(uintptr_t)base,limit=at+nt->OptionalHeader.SizeOfImage;size_t scanned=0;bool complete=true;
    while(at<limit) {
        if(GetTickCount64()+500>=request->deadline || scanned>=128*1024*1024){complete=false;break;}
        MEMORY_BASIC_INFORMATION region{};if(!VirtualQuery((void*)at,&region,sizeof(region))){complete=false;break;}
        uintptr_t end=(uintptr_t)region.BaseAddress+region.RegionSize;if(end<=at){complete=false;break;}if(end>limit)end=limit;
        if(region.State==MEM_COMMIT && probeReadable(region.Protect)) {
            size_t length=end-at;
            if(dump!=INVALID_HANDLE_VALUE){SetFilePointer(dump,(LONG)(at-(uintptr_t)base),nullptr,FILE_BEGIN);WriteFile(dump,(void*)at,(DWORD)length,&written,nullptr);}
            for(auto& p:patterns) {
                if(p.matches>=2)continue;
                for(size_t i=0;i+p.length<=length;i++) {
                    if((i&0x3ffff)==0 && GetTickCount64()+500>=request->deadline){complete=false;break;}
                    BYTE* candidate=(BYTE*)at+i;bool match=true;
                    for(size_t j=0;j<p.length;j++)if(p.bytes[j]>=0 && candidate[j]!=p.bytes[j]){match=false;break;}
                    if(match){if(!p.matches)p.first=(uintptr_t)candidate;else p.second=(uintptr_t)candidate;p.matches++;if(p.matches>=2)break;}
                }
                if(!complete)break;
            }
            scanned+=length;
        }
        if(!complete)break;at=end;
    }
    if(dump!=INVALID_HANDLE_VALUE)CloseHandle(dump);
    Writer writer{request->result,0,sizeof(request->result),false};
    append(&writer,"{\"ok\":true,\"probe_only\":true,\"original_required\":false,\"independent_skin_apply_available\":false,\"pid\":%lu,\"scan_complete\":%s,\"image_timestamp\":%lu,\"image_size\":%lu,\"executable_bytes_scanned\":%zu,\"signatures\":[",GetCurrentProcessId(),complete?"true":"false",nt->FileHeader.TimeDateStamp,nt->OptionalHeader.SizeOfImage,scanned);
    bool comma=false;for(const auto& p:patterns){
        if(comma)append(&writer,",");comma=true;append(&writer,"{\"name\":");quoted(&writer,p.name);
        append(&writer,",\"matches_capped_at_2\":%d,\"rva\":%llu,\"second_rva\":%llu,\"context_hex\":\"",p.matches,p.first?p.first-(uintptr_t)base:0,p.second?p.second-(uintptr_t)base:0);
        if(p.first)for(int n=0;n<160;n++)append(&writer,"%02x",((BYTE*)p.first)[n]);append(&writer,"\",\"second_context_hex\":\"");
        if(p.second)for(int n=0;n<160;n++)append(&writer,"%02x",((BYTE*)p.second)[n]);append(&writer,"\"}");
    }
    append(&writer,"]");
    if(patterns[0].matches==1 && patterns[1].matches==1){
        BYTE* instruction=(BYTE*)patterns[0].first;BYTE** playerSlot=(BYTE**)(instruction+7+*(int*)(instruction+3));
        int offset=*(int*)(patterns[1].first+3);BYTE* player=*playerSlot;
        if(player && offset>0 && offset<0x10000){
            BYTE* stack=player+offset;char model[64];
            append(&writer,",\"player_head_hex\":\"");for(int n=0;n<0x400;n++)append(&writer,"%02x",player[n]);append(&writer,"\"");
            append(&writer,",\"vtable_head\":[");for(int n=0;n<12;n++){if(n)append(&writer,",");append(&writer,"%llu",(uint64_t)((*(BYTE***)(player+0x1a8))[n]-base));}append(&writer,"]");
            BYTE* teams=*(BYTE**)(base+0x1c7be58);append(&writer,",\"team_table\":[");for(int n=0;n<16;n++){if(n)append(&writer,",");append(&writer,"%d",*(int*)(teams+0x20+n*4));}append(&writer,"]");
            uint32_t v=*(uint32_t*)(player+0xbe4);auto ror=[](uint32_t v,int b){return (v>>b)|(v<<(32-b));};
            append(&writer,",\"skin_candidate\":%u",ror(ror(v,21)^0x3dc9cb21u,19));
            BYTE* manager=*(BYTE**)(base+0x1c7f430);BYTE** objs=*(BYTE***)(manager+8);int count=*(int*)(manager+16);
            append(&writer,",\"objects\":[");int emitted=0;
            if(count>=0 && count<=10000)for(int n=0;n<count;n++){BYTE* obj=objs[n];if(!obj)continue;char name[64];if(!cstring(*(const char**)(obj+offset+0x18),name,sizeof(name)))continue;
                if(emitted++)append(&writer,",");append(&writer,"{\"model\":");quoted(&writer,name);append(&writer,",\"skin\":%d",*(int*)(obj+offset+0x38));
                if(objectWardModel(name)){BYTE* wrapper=*(BYTE**)(obj+0x560);append(&writer,",\"owner_present\":%s,\"own\":%s",wrapper?"true":"false",objectReadable(wrapper,64)&&*(BYTE**)wrapper==player?"true":"false");}append(&writer,"}");}
            append(&writer,"]");

            if(cstring(*(const char**)(stack+0x18),model,sizeof(model))){append(&writer,",\"readonly_player\":{\"model\":");quoted(&writer,model);append(&writer,",\"stack_offset\":%d,\"skin\":%d,\"gear\":%d}",offset,*(int*)(stack+0x38),(int)*(signed char*)(stack+0x9c));}
            int skinOffset=patterns[13].first?*(int*)(patterns[13].first+2):0;
            if(skinOffset>0 && skinOffset<0x10000){append(&writer,",\"skin_field_candidate\":{\"offset\":%d,\"bytes\":\"",skinOffset);for(int n=0;n<24;n++)append(&writer,"%02x",player[skinOffset+n]);append(&writer,"\"}");}
        }
    }
    if(patterns[4].first){
        BYTE* instruction=(BYTE*)patterns[4].first;BYTE** slot=(BYTE**)(instruction+7+*(int*)(instruction+3));BYTE* manager=*slot;
        if(manager){
            BYTE** list=*(BYTE***)(manager+0x18);int count=*(int*)(manager+0x20),capacity=*(int*)(manager+0x24);
            append(&writer,",\"champion_manager_candidate\":{\"count\":%d,\"capacity\":%d,\"examples\":[",count,capacity);
            if(list && count>0 && count<=10000 && count<=capacity)for(int n=0;n<count && n<5;n++){
                BYTE* champion=list[n];char model[64];if(n)append(&writer,",");
                if(cstring(*(const char**)(champion+8),model,sizeof(model))){append(&writer,"{\"model\":");quoted(&writer,model);append(&writer,",\"skins\":%d}",*(int*)(champion+0xe0));}else append(&writer,"null");
            }
            append(&writer,"]}");
        }
    }
    if(patterns[3].first){BYTE* call=(BYTE*)patterns[3].first;BYTE* target=call+5+*(int*)(call+1);append(&writer,",\"push_function_hex\":\"");for(int n=0;n<160;n++)append(&writer,"%02x",target[n]);append(&writer,"\"");}
    append(&writer,"}");return !writer.failed;
}




