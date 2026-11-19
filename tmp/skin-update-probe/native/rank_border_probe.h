#pragma once
// Read-only investigation of this verified game image. Never reads player names;
// asset resolution uses the game's existing lookup functions.
static bool borderReadable(BYTE* address,size_t bytes) {
    MEMORY_BASIC_INFORMATION region{};
    return VirtualQuery(address,&region,sizeof(region)) && region.State==MEM_COMMIT &&
        !(region.Protect&(PAGE_NOACCESS|PAGE_GUARD)) &&
        (uintptr_t)address+bytes>=(uintptr_t)address &&
        (uintptr_t)address+bytes<=(uintptr_t)region.BaseAddress+region.RegionSize;
}
static bool borderFunction(BYTE* base,uintptr_t rva,uintptr_t* begin,uintptr_t* end) {
    auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    auto directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    if(!directory.Size || directory.Size%sizeof(RUNTIME_FUNCTION) || directory.VirtualAddress+directory.Size>runtimeGame.image_size)return false;
    auto functions=(RUNTIME_FUNCTION*)(base+directory.VirtualAddress);size_t low=0,high=directory.Size/sizeof(RUNTIME_FUNCTION);
    while(low<high) {size_t middle=low+(high-low)/2;if(functions[middle].BeginAddress<=rva)low=middle+1;else high=middle;}
    if(!low)return false;auto& function=functions[low-1];
    if(rva>=function.EndAddress || function.EndAddress>runtimeGame.image_size || function.BeginAddress>=function.EndAddress)return false;
    *begin=function.BeginAddress;*end=function.EndAddress;
    DWORD unwind=function.UnwindData;
    for(int depth=0;depth<16;depth++) {
        if(unwind+4>runtimeGame.image_size || !borderReadable(base+unwind,4))break;
        BYTE* info=base+unwind;if(!(info[0]>>3&UNW_FLAG_CHAININFO))break;
        uintptr_t chained=unwind+4+((info[2]+1)/2)*4;
        if(chained+sizeof(RUNTIME_FUNCTION)>runtimeGame.image_size || !borderReadable(base+chained,sizeof(RUNTIME_FUNCTION)))break;
        auto parent=(RUNTIME_FUNCTION*)(base+chained);
        if(parent->BeginAddress>=parent->EndAddress || parent->EndAddress>runtimeGame.image_size)break;
        if(parent->BeginAddress<*begin)*begin=parent->BeginAddress;
        if(parent->EndAddress>*end)*end=parent->EndAddress;
        unwind=parent->UnwindData;
    }
    return true;
}
static void borderCode(Writer* writer,BYTE* base,uintptr_t rva,size_t limit) {
    uintptr_t begin=0,end=0;
    if(!borderFunction(base,rva,&begin,&end)) {
        auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);auto sections=IMAGE_FIRST_SECTION(nt);bool executable=false;
        for(int s=0;s<nt->FileHeader.NumberOfSections;s++)if((sections[s].Characteristics&IMAGE_SCN_MEM_EXECUTE) && rva>=sections[s].VirtualAddress && rva+256<=sections[s].VirtualAddress+sections[s].Misc.VirtualSize)executable=true;
        if(!executable){append(writer,"null");return;}begin=rva;end=rva+256;
    }
    size_t bytes=(size_t)(end-begin);if(bytes>limit)bytes=limit;
    if(!borderReadable(base+begin,bytes)) {append(writer,"null");return;}
    append(writer,"{\"begin\":%llu,\"end\":%llu,\"bytes\":",(uint64_t)begin,(uint64_t)end);
    objectHex(writer,base+begin,bytes);append(writer,"}");
}
static void borderProbeText(Writer* writer,const char* text) {
    char value[65]{};size_t size=0;
    while(size<64 && text && borderReadable((BYTE*)text+size,1)) {
        unsigned char character=(unsigned char)text[size];if(!character){quoted(writer,value);return;}
        if(character<32 || character>126)break;value[size++]=(char)character;
    }
    append(writer,"null");
}
static void borderProbeDisplay(Writer* writer,BYTE* base) {
    // Diagnostic scan only: verified card vtables, bounded to the game's low
    // address allocations and writable private regions; never follows user text.
    append(writer,",\"display_cards\":[");int found=0;size_t scanned=0;uintptr_t at=0;
    const ULONGLONG deadline=GetTickCount64()+700;
    while(at<0x100000000ull && scanned<1024ull*1024*1024 && GetTickCount64()<deadline) {
        MEMORY_BASIC_INFORMATION region{};if(!VirtualQuery((void*)at,&region,sizeof(region)))break;
        uintptr_t end=(uintptr_t)region.BaseAddress+region.RegionSize;if(end<=at)break;
        if(region.State==MEM_COMMIT && region.Type==MEM_PRIVATE && !(region.Protect&(PAGE_NOACCESS|PAGE_GUARD)) &&
            (region.Protect&(PAGE_READWRITE|PAGE_WRITECOPY))) {
            uintptr_t lo=(at+7)&~7ull,hi=end<0x100000000ull?end:0x100000000ull;
            scanned+=hi-lo;
            for(uintptr_t current=lo;current+0x2c0<=hi && found<16;current+=8) {
                BYTE* object=(BYTE*)current;BYTE* vt=*(BYTE**)object;
                int kind=vt==base+0x18dfdb8?1:vt==base+0x18dff48?2:0;if(!kind || object[0x10]<1 || object[0x10]>57)continue;
                size_t extent=kind==1?0x928:0x2b8;if(!borderReadable(object,extent))continue;
                if(found++)append(writer,",");
                BYTE* display=object+(kind==1?0x1f0:0xf8);
                append(writer,"{\"kind\":%d,\"slot\":%u,\"object\":\"%016llx\",\"owner\":\"%016llx\",\"flags\":%u,\"bindings\":[",kind,(unsigned)object[0x10],(uint64_t)object,*(uint64_t*)(object+(kind==1?0x60:0x58)),(unsigned)display[0xe8]);
                for(int j=0;j<2;j++){if(j)append(writer,",");append(writer,"{\"wrapper_vtable\":\"%016llx\",\"value\":\"%016llx\"}",*(uint64_t*)(display+j*0x18),*(uint64_t*)(display+j*0x18+8));}
                append(writer,"]}");
            }
        }
        at=end;
    }
    append(writer,"],\"display_scan_bytes\":%llu,\"display_scan_end\":\"%016llx\",\"display_scan_complete\":%s",(uint64_t)scanned,(uint64_t)at,at>=0x100000000ull?"true":"false");
}
static void borderProbeSlots(Writer* writer,BYTE* base) {
    const char* path="Loadouts/Regalia/Crests/Ranked/RankedCrestLookup";
    using Hash=uint32_t(__fastcall*)(const char*,size_t);
    using Find=BYTE*(__fastcall*)(BYTE*,uint32_t*,int);
    using Cast=BYTE*(__fastcall*)(BYTE*,BYTE*,BYTE*);
    uint32_t key=((Hash)(base+0xf69970))(path,strlen(path));
    BYTE* database=*(BYTE**)(base+0x1c85248);
    BYTE* resource=database?((Find)(base+0xf66970))(database,&key,0):nullptr;
    BYTE* lookup=resource?((Cast)(base+0xf61f00))(*(BYTE**)(resource+8),*(BYTE**)resource,base+0x1c93590):nullptr;
    using Rules=BYTE*(__fastcall*)();using Override=BYTE*(__fastcall*)(BYTE*,BYTE*);
    BYTE* rules=((Rules)(base+0x495fc0))();
    int overrideCount=-1;bool overrideMatched=false;
    if(rules && borderReadable(rules+0x30,16)) {
        BYTE* entries=*(BYTE**)(rules+0x30);int count=*(int*)(rules+0x38);uint32_t type=*(uint32_t*)(base+0x1c932c8);
        overrideCount=count;
        if(count>=0 && count<=1024 && entries && borderReadable(entries,count*16))for(int n=0;n<count;n++)if(*(uint32_t*)(entries+n*16)==type) {
            overrideMatched=true;
            BYTE* value=*(BYTE**)(entries+n*16+8);if(value && borderReadable(value+8,16))lookup=((Override)(base+0xf72e80))(value+8,base+0x1c93590);break;
        }
    }
    append(writer,",\"rank_asset_probe\":{\"key\":%u,\"database\":%s,\"resource\":%s,\"lookup\":%s,\"override_count\":%d,\"override_matched\":%s,\"bootstrap\":[%s,%s,%s,%s]},\"rank_asset_catalog\":[",key,database?"true":"false",resource?"true":"false",lookup?"true":"false",overrideCount,overrideMatched?"true":"false",
        *(BYTE**)(base+0x1c93220)?"true":"false",*(BYTE**)(base+0x1c93228)?"true":"false",*(BYTE**)(base+0x1c93230)?"true":"false",*(BYTE**)(base+0x1c93238)?"true":"false");
    if(lookup && borderReadable(lookup,16)) {
        BYTE* entries=*(BYTE**)lookup;int count=*(int*)(lookup+8);
        if(count>=0 && count<=32 && entries && borderReadable(entries,count*0x38))for(int n=0;n<count;n++) {
            BYTE* entry=entries+n*0x38;if(n)append(writer,",");append(writer,"{\"tier\":");borderProbeText(writer,*(const char**)entry);
            append(writer,",\"divisions\":%u,\"ornaments\":%u,\"division_keys\":[",*(unsigned*)(entry+0x20),*(unsigned*)(entry+0x30));
            BYTE* divisions=*(BYTE**)(entry+0x18);int divisionCount=*(int*)(entry+0x20);
            if(divisionCount>0 && divisionCount<=8 && borderReadable(divisions,divisionCount*16))for(int j=0;j<divisionCount;j++) {if(j)append(writer,",");append(writer,"%d",*(int*)(divisions+j*16));}
            append(writer,"]}");
        }
    }
    append(writer,"]");
    append(writer,",\"asset_resolution\":[");
    const char* checks[]={"Loadouts/Regalia/Crests/Prestige/PrestigeCrestList","Loadouts/Regalia/Banners/Ranked/RankedBannerLookup","Loadouts/Regalia/Crests/Ranked/RankedCrestLookup"};
    for(int i=0;i<3;i++) {if(i)append(writer,",");uint32_t check=((Hash)(base+0xf69970))(checks[i],strlen(checks[i]));
        BYTE* result=database?((Find)(base+0xf66970))(database,&check,0):nullptr;
        append(writer,"{\"key\":%u,\"present\":%s}",check,result?"true":"false");}
    append(writer,"],\"override_type\":%u",*(uint32_t*)(base+0x1c932c8));
    BYTE* records=*(BYTE**)(base+0x1c7c038);
    append(writer,",\"rank_source_present\":%s,\"rank_slots\":[",records?"true":"false");
    for(int slot=0;records && slot<10;slot++) {
        BYTE* record=records+slot*0x1d8;if(!borderReadable(record+0xf0,0x120))break;
        if(slot)append(writer,",");append(writer,"{\"slot\":%d,\"crest_mode\":",slot);borderProbeText(writer,*(const char**)(record+0xf0));
        append(writer,",\"rank\":");borderProbeText(writer,*(const char**)(record+0x1b8));
        append(writer,",\"division\":");borderProbeText(writer,*(const char**)(record+0x1c8));
        append(writer,",\"ornament\":%d,\"prestige\":%u}",*(int*)(record+0x1d8),(unsigned)*(uint16_t*)(record+0x204));
    }
    append(writer,"]");
    BYTE* local=*(BYTE**)(base+0x1c7be80);
    if(local && borderReadable(local+0x423,1)) {
        unsigned slot=local[0x423];append(writer,",\"local_regalia_slot\":%u",slot);
        if(slot<32) {
            BYTE* entity=base+0x1cc0990+slot*0x120;
            BYTE** table=*(BYTE***)entity;
            if(table && borderReadable((BYTE*)(table+2),sizeof(void*)) && table[2]>=base && table[2]<base+runtimeGame.image_size) {
                append(writer,",\"component_map_getter\":");borderCode(writer,base,table[2]-base,256);
                const BYTE getter[]={0x48,0x8d,0x41,0x08,0xc3};
                if(!memcmp(table[2],getter,sizeof(getter))) {
                    BYTE* entries=*(BYTE**)(entity+8);int count=*(int*)(entity+0x10);
                    append(writer,",\"regalia_component_count\":%d",count);
                    if(entries && count>=0 && count<=128 && borderReadable(entries,count*24))for(int n=0;n<count;n++) {
                        BYTE* entry=entries+n*24;
                        if(*(BYTE**)entry!=base+0x1c932b0 || !*(uint64_t*)(entry+0x10))continue;
                        BYTE* handle=*(BYTE**)(entry+8);if(!handle || !borderReadable(handle,8))continue;
                        BYTE* box=*(BYTE**)handle;if(!box || !borderReadable(box+0x10,8))continue;
                        BYTE* component=*(BYTE**)(box+0x10);if(!component || !borderReadable(component,0xa0))continue;
                        append(writer,",\"local_regalia_component\":{\"vtable_rva\":%lld,\"rank\":",(long long)(*(BYTE**)component-base));
                        borderProbeText(writer,*(const char**)(component+0x48));append(writer,",\"division\":");borderProbeText(writer,*(const char**)(component+0x58));
                        append(writer,",\"mode\":");borderProbeText(writer,*(const char**)(component+0x78));
                        append(writer,",\"resources\":[");for(int j=0;j<7;j++){if(j)append(writer,",");append(writer,"\"%016llx\"",*(uint64_t*)(component+0x10+j*8));}append(writer,"]}");
                    }
                }
            }
        }
    }
}
static void independentBorderProbe(Request* request,Writer* writer,BYTE* base) {
    append(writer,"{\"ok\":true,\"independent\":true,\"original_required\":false,\"read_only\":true,\"reference_sha256\":\"%s\",\"pid\":%lu,\"request_id\":\"%016llx\"",
        runtimeGame.sha,GetCurrentProcessId(),request->nonce);
    if(request->index==-3) {borderProbeDisplay(writer,base);append(writer,"}");return;}
    if(request->index==-2) {borderProbeSlots(writer,base);append(writer,"}");return;}
    if(request->index>=0) {
        append(writer,",\"function\":");borderCode(writer,base,(uintptr_t)request->index,16384);
        append(writer,",\"callers\":[");int count=0;
        auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);auto sections=IMAGE_FIRST_SECTION(nt);
        for(int s=0;s<nt->FileHeader.NumberOfSections;s++) {
            auto section=&sections[s];if(!(section->Characteristics&IMAGE_SCN_MEM_EXECUTE))continue;
            uintptr_t lo=section->VirtualAddress,hi=lo+section->Misc.VirtualSize;if(hi>runtimeGame.image_size || hi<lo)continue;
            uintptr_t checked=0;
            for(uintptr_t at=lo;at+5<=hi && count<12;at++) {
                if(at+5>checked) {
                    MEMORY_BASIC_INFORMATION region{};if(!VirtualQuery(base+at,&region,sizeof(region)))break;
                    checked=(BYTE*)region.BaseAddress+region.RegionSize-base;
                    if(region.State!=MEM_COMMIT || region.Protect&(PAGE_NOACCESS|PAGE_GUARD)){at=checked-1;continue;}
                    if(at+5>checked)continue;
                }
                if(base[at]!=0xe8)continue;int32_t displacement=0;memcpy(&displacement,base+at+1,4);
                if((int64_t)at+5+displacement!=request->index)continue;
                if(count++)append(writer,",");append(writer,"{\"at\":%llu,\"function\":",(uint64_t)at);borderCode(writer,base,at,4096);append(writer,"}");
            }
        }
        append(writer,"]}");return;
    }
    const char* wanted[]={"Loadouts/Regalia/Crests/Ranked/RankedCrestLookup","Loadouts/Regalia/Banners/Ranked/RankedBannerLookup",
        "Loadouts/Regalia/Crests/Prestige/PrestigeCrestList","regalia_ranked_division_%i","REGALIA_CREST","REGALIA_BANNER","LoadingScreen","LocalPlayerId","OnLocalPlayerLoadoutBound",
        "$loading_card_front_vtable","$loading_card_back_vtable"};
    auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);auto sections=IMAGE_FIRST_SECTION(nt);
    append(writer,",\"strings\":[");bool first=true;
    for(auto name:wanted) {
        uintptr_t stringRva=0;size_t length=strlen(name)+1;
        if(!strcmp(name,"$loading_card_front_vtable"))stringRva=0x18dfdb8;
        if(!strcmp(name,"$loading_card_back_vtable"))stringRva=0x18dff48;
        for(int s=0;s<nt->FileHeader.NumberOfSections && !stringRva;s++) {
            auto section=&sections[s];if(!(section->Characteristics&IMAGE_SCN_MEM_READ) || section->Characteristics&(IMAGE_SCN_MEM_EXECUTE|IMAGE_SCN_MEM_WRITE))continue;
            uintptr_t low=section->VirtualAddress,high=low+section->Misc.VirtualSize;
            if(high>runtimeGame.image_size || high<low)continue;
            uintptr_t checked=0;
            for(uintptr_t at=low;at+length<=high;at++) {
                if(at+length>checked) {
                    MEMORY_BASIC_INFORMATION region{};if(!VirtualQuery(base+at,&region,sizeof(region)))break;
                    checked=(BYTE*)region.BaseAddress+region.RegionSize-base;
                    if(region.State!=MEM_COMMIT || region.Protect&(PAGE_NOACCESS|PAGE_GUARD)){at=checked-1;continue;}
                    if(at+length>checked)continue;
                }
                if(!memcmp(base+at,name,length)){stringRva=at;break;}
            }
        }
        if(!first)append(writer,",");first=false;append(writer,"{\"name\":");quoted(writer,name);
        append(writer,",\"rva\":%llu,\"xrefs\":[",(uint64_t)stringRva);int count=0;
        if(stringRva)for(int s=0;s<nt->FileHeader.NumberOfSections;s++) {
            auto section=&sections[s];if(!(section->Characteristics&IMAGE_SCN_MEM_EXECUTE))continue;
            uintptr_t low=section->VirtualAddress,high=low+section->Misc.VirtualSize;if(high>runtimeGame.image_size || high<low)continue;
            uintptr_t checked=0;
            for(uintptr_t at=low;at+7<=high && count<8;at++) {
                if(at+7>checked) {
                    MEMORY_BASIC_INFORMATION region{};if(!VirtualQuery(base+at,&region,sizeof(region)))break;
                    checked=(BYTE*)region.BaseAddress+region.RegionSize-base;
                    if(region.State!=MEM_COMMIT || region.Protect&(PAGE_NOACCESS|PAGE_GUARD)){at=checked-1;continue;}
                    if(at+7>checked)continue;
                }
                BYTE* instruction=base+at;
                if(instruction[0]<0x48 || instruction[0]>0x4f || (instruction[1]!=0x8d && instruction[1]!=0x8b) || (instruction[2]&0xc7)!=5)continue;
                int32_t displacement=0;memcpy(&displacement,instruction+3,4);
                if((int64_t)at+7+displacement!=(int64_t)stringRva)continue;
                if(count++)append(writer,",");append(writer,"{\"at\":%llu,\"function\":",(uint64_t)at);borderCode(writer,base,at,2048);append(writer,"}");
            }
        }
        append(writer,"]}");
    }
    append(writer,"]");borderProbeSlots(writer,base);append(writer,"}");
}
