#pragma once
// Resolve the game's own executable code. No external DLL, service or game write.
struct RuntimePattern {const char* name;unsigned key;const BYTE* bytes;const BYTE* mask;size_t size;};
#include "runtime_patterns.h"
struct RuntimeMatch {uintptr_t rva=0;int pattern=-1;};
struct RuntimeImage {BYTE* base=nullptr;DWORD timestamp=0,size=0;IMAGE_SECTION_HEADER* sections=nullptr;unsigned count=0;};
struct RuntimeProfile {
    BYTE* base=nullptr;DWORD timestamp=0,image_size=(DWORD)Game_image_size;
    uintptr_t player_slot=Game_player_slot,champion_slot=Game_champion_slot,translate=Game_translate,update=Game_update,push=Game_push;
    uintptr_t stack_offset=Game_stack_offset,skin_offset=Game_skin_offset,object_list_slot=Game_object_list_slot,owner_offset=Game_owner_offset;
    uintptr_t camera_slot=0,camera_manager=0;
    RuntimeMatch matches[RuntimeRuleCount]{};
    bool ready=false,objects=false,camera=false,view=false,cacheHit=false;
    char sha[65]{};const char* error="profile_not_resolved";char stage[96]{};ULONGLONG elapsed=0;
};
static RuntimeProfile runtimeGame;
static bool runtimeReadable(const void* pointer,size_t size) {
    uintptr_t start=(uintptr_t)pointer,end=start+size;if(!start || end<start)return false;
    while(start<end) {
        MEMORY_BASIC_INFORMATION region{};
        if(!VirtualQuery((const void*)start,&region,sizeof(region)) || region.State!=MEM_COMMIT || (region.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return false;
        uintptr_t next=(uintptr_t)region.BaseAddress+region.RegionSize;if(next<=start)return false;
        start=next<end?next:end;
    }
    return true;
}
static bool runtimeImage(BYTE* base,RuntimeImage* image) {
    if(!runtimeReadable(base,sizeof(IMAGE_DOS_HEADER)))return false;
    auto dos=(IMAGE_DOS_HEADER*)base;
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE || dos->e_lfanew<=0 || dos->e_lfanew>1048576 || !runtimeReadable(base+dos->e_lfanew,sizeof(IMAGE_NT_HEADERS64)))return false;
    auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE || nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64 ||
        nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC || nt->FileHeader.NumberOfSections<1 || nt->FileHeader.NumberOfSections>64 ||
        nt->FileHeader.SizeOfOptionalHeader<sizeof(IMAGE_OPTIONAL_HEADER64) || nt->OptionalHeader.SizeOfImage<4096 || nt->OptionalHeader.SizeOfImage>256*1024*1024)return false;
    auto sections=IMAGE_FIRST_SECTION(nt);
    if((BYTE*)(sections+nt->FileHeader.NumberOfSections)>base+nt->OptionalHeader.SizeOfImage ||
        !runtimeReadable(sections,sizeof(*sections)*nt->FileHeader.NumberOfSections))return false;
    *image={base,nt->FileHeader.TimeDateStamp,nt->OptionalHeader.SizeOfImage,sections,nt->FileHeader.NumberOfSections};
    for(unsigned n=0;n<image->count;n++) {
        auto s=&sections[n];if((uint64_t)s->VirtualAddress+s->Misc.VirtualSize>image->size)return false;
    }
    return true;
}
static bool runtimeSpan(const RuntimeImage& image,uintptr_t rva,size_t bytes,bool executable) {
    if(!rva || rva>=image.size || bytes>image.size-rva)return false;
    for(unsigned n=0;n<image.count;n++) {
        auto s=&image.sections[n];uintptr_t lo=s->VirtualAddress,hi=lo+s->Misc.VirtualSize;
        if(rva>=lo && rva+bytes<=hi && ((s->Characteristics&IMAGE_SCN_MEM_EXECUTE)!=0)==executable)
            return runtimeReadable(image.base+rva,bytes);
    }
    return false;
}
static bool runtimePatternAt(const RuntimeImage& image,const RuntimePattern& pattern,uintptr_t rva) {
    if(!runtimeSpan(image,rva,pattern.size,true))return false;
    for(size_t n=0;n<pattern.size;n++)if(pattern.mask[n] && image.base[rva+n]!=pattern.bytes[n])return false;
    return true;
}
static int runtimeFind(const RuntimeImage& image,const RuntimePattern& pattern,uintptr_t* found) {
    size_t anchor=0,length=0;
    for(size_t n=0;n<pattern.size;) {
        if(!pattern.mask[n]){n++;continue;}size_t first=n;while(n<pattern.size && pattern.mask[n])n++;
        if(n-first>length){anchor=first;length=n-first;}
    }
    if(length<4)return -1;
    int count=0;
    for(unsigned s=0;s<image.count;s++) {
        auto section=&image.sections[s];if(!(section->Characteristics&IMAGE_SCN_MEM_EXECUTE))continue;
        uintptr_t low=section->VirtualAddress,high=low+section->Misc.VirtualSize;
        if(high-low<pattern.size)continue;
        uintptr_t cursor=low;
        while(cursor<high) {
            MEMORY_BASIC_INFORMATION region{};
            if(!VirtualQuery(image.base+cursor,&region,sizeof(region)) || region.State!=MEM_COMMIT || (region.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return -1;
            uintptr_t limit=(BYTE*)region.BaseAddress+region.RegionSize-image.base;if(limit>high)limit=high;if(limit<=cursor)return -1;
            while(cursor<limit) {
                auto hit=(const BYTE*)memchr(image.base+cursor,pattern.bytes[anchor],limit-cursor);
                if(!hit)break;uintptr_t position=hit-image.base;cursor=position+1;
                if(position<low+anchor)continue;
                uintptr_t candidate=position-anchor;
                if(candidate+pattern.size>high)continue;
                uintptr_t regionLow=(BYTE*)region.BaseAddress-image.base;
                bool withinRegion=candidate>=regionLow && candidate+pattern.size<=limit;
                if(!withinRegion && !runtimeReadable(image.base+candidate,pattern.size))continue;
                // The entire current region was already checked. Query memory
                // again only across a region boundary or after a full anchor hit.
                if(memcmp(hit,pattern.bytes+anchor,length)==0 && runtimePatternAt(image,pattern,candidate)) {
                    if(!count)*found=candidate;if(++count==2)return count;
                }
            }
            cursor=limit;
        }
    }
    return count;
}
static bool runtimeGroup(const RuntimeImage& image,RuntimeProfile* profile,unsigned first,unsigned last) {
    for(unsigned key=first;key<=last;key++) {
        auto& selected=profile->matches[key];
        if(selected.pattern>=0 && runtimePatternAt(image,RuntimePatterns[selected.pattern],selected.rva))continue;
        selected={};int candidates=0;
        for(unsigned n=0;n<sizeof(RuntimePatterns)/sizeof(RuntimePatterns[0]);n++) {
            auto& rule=RuntimePatterns[n];if(rule.key!=key)continue;uintptr_t found=0;
            int count=runtimeFind(image,rule,&found);
            if(count<0){profile->error="profile_code_not_ready";sprintf_s(profile->stage,"locator:%s",rule.name);return false;}
            if(count>1 || (count==1 && candidates && found!=selected.rva)) {
                profile->error="profile_signature_ambiguous";sprintf_s(profile->stage,"locator:%s",rule.name);return false;
            }
            if(count==1){candidates=1;selected={found,(int)n};}
        }
        if(!candidates) {
            profile->error="profile_signature_missing";
            const char* name="unknown";for(const auto& p:RuntimePatterns)if(p.key==key){name=p.name;break;}
            sprintf_s(profile->stage,"locator:%s",name);return false;
        }
    }
    return true;
}
static bool runtimeRelative(const RuntimeImage& image,uintptr_t at,unsigned displacement,unsigned size,bool executable,uintptr_t* result) {
    if(!runtimeSpan(image,at,size,true) || displacement+4>size)return false;
    int32_t delta;memcpy(&delta,image.base+at+displacement,4);
    int64_t target=(int64_t)at+size+delta;
    if(target<=0 || !runtimeSpan(image,(uintptr_t)target,executable?1:8,executable))return false;
    if(!executable && target%8)return false;
    *result=(uintptr_t)target;return true;
}
static bool runtimeHash(RuntimeProfile* p) {
    uint64_t values[]={p->timestamp,p->image_size,p->player_slot,p->champion_slot,p->translate,p->update,p->push,p->stack_offset,p->skin_offset};
    BYTE input[sizeof(values)+64],hash[32];memcpy(input,values,sizeof(values));memcpy(input+sizeof(values),RuntimeRulesSha,64);
    BCRYPT_ALG_HANDLE algorithm=nullptr;
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
    NTSTATUS status=BCryptHash(algorithm,nullptr,0,input,sizeof(input),hash,sizeof(hash));BCryptCloseAlgorithmProvider(algorithm,0);
    if(status<0)return false;
    for(unsigned n=0;n<sizeof(hash);n++)sprintf_s(p->sha+n*2,3,"%02x",hash[n]);return true;
}
static bool runtimeResolve(BYTE* base,RuntimeProfile* result) {
    ULONGLONG start=GetTickCount64();RuntimeImage image{};
    if(!runtimeImage(base,&image)){result->ready=false;result->error="profile_invalid_image";strcpy_s(result->stage,"image_headers");return false;}
    if(result->base!=base || result->timestamp!=image.timestamp || result->image_size!=image.size)*result=RuntimeProfile{};
    result->base=base;result->timestamp=image.timestamp;result->image_size=image.size;result->ready=false;
    if(!runtimeGroup(image,result,0,8)){result->elapsed=GetTickCount64()-start;return false;}
    auto at=[&](unsigned key){return result->matches[key].rva;};
    uintptr_t pushTarget=0,translatedTwice=0;
    bool valid=runtimeRelative(image,at(0),3,7,false,&result->player_slot) &&
        runtimeRelative(image,at(4),3,7,false,&result->champion_slot) &&
        runtimeRelative(image,at(5),1,5,true,&result->translate) &&
        runtimeRelative(image,at(5)+0x31,1,5,true,&translatedTwice) && translatedTwice==result->translate &&
        runtimeRelative(image,at(3),1,5,true,&pushTarget) && pushTarget==at(6);
    result->update=at(2);result->push=pushTarget;
    uint32_t stack=0,decode=0,encode=0;
    memcpy(&stack,base+at(1)+3,4);memcpy(&decode,base+at(7)+2,4);memcpy(&encode,base+at(8)+33,4);
    // Supported schema: layer +0x18/+0x38 and 0xa0 stride remain constrained
    // in the update/push patterns; only the containing object offsets can move.
    valid=valid && stack>=0x100 && stack<=0x10000 && stack%8==0 && decode==encode && decode>=0x100 && decode<=0x10000 && decode%4==0;
    if(!valid){result->error="profile_schema_not_verified";strcpy_s(result->stage,"relative_targets_and_layout");return false;}
    result->stack_offset=stack;result->skin_offset=decode;
    if(!runtimeHash(result)){result->error="profile_hash_failed";strcpy_s(result->stage,"identity");return false;}
    result->ready=true;result->error="";result->stage[0]=0;result->elapsed=GetTickCount64()-start;return true;
}
static bool runtimeResolveObjects(BYTE* base) {
    RuntimeImage image{};if(!runtimeImage(base,&image) || !runtimeGame.ready || runtimeGame.base!=base)return false;
    runtimeGame.objects=false;
    if(!runtimeGroup(image,&runtimeGame,9,12))return false;
    if(!runtimeRelative(image,runtimeGame.matches[12].rva,3,7,false,&runtimeGame.object_list_slot)) {
        runtimeGame.error="profile_schema_not_verified";strcpy_s(runtimeGame.stage,"object_list_slot");return false;
    }
    uint32_t owner;memcpy(&owner,base+runtimeGame.matches[10].rva+3,4);runtimeGame.owner_offset=owner;
    runtimeGame.objects=true;return true;
}
static bool runtimeResolveCamera(BYTE* base,bool view) {
    RuntimeImage image{};if(!runtimeImage(base,&image))return false;
    if(runtimeGame.base!=base){runtimeGame=RuntimeProfile{};runtimeGame.base=base;runtimeGame.timestamp=image.timestamp;runtimeGame.image_size=image.size;}
    runtimeGame.camera=false;if(!runtimeGroup(image,&runtimeGame,13,14))return false;
    if(!runtimeRelative(image,runtimeGame.matches[13].rva,3,7,false,&runtimeGame.camera_slot)) {
        runtimeGame.error="profile_schema_not_verified";strcpy_s(runtimeGame.stage,"camera_config_slot");return false;
    }
    runtimeGame.camera=true;
    if(view) {
        runtimeGame.view=false;if(!runtimeGroup(image,&runtimeGame,15,16))return false;
        if(!runtimeRelative(image,runtimeGame.matches[15].rva,3,7,false,&runtimeGame.camera_manager)) {
            runtimeGame.error="profile_schema_not_verified";strcpy_s(runtimeGame.stage,"camera_manager_slot");return false;
        }
        runtimeGame.view=true;
    }
    return true;
}
static void runtimeMetadata(Writer* w) {
    append(w,"\"profile_resolution\":{\"mode\":\"runtime_signatures\",\"rules_sha256\":\"%s\",\"ready\":%s,\"cache_hit\":%s,\"timestamp\":%lu,\"image_size\":%lu,\"elapsed_ms\":%llu,\"stage\":",
        RuntimeRulesSha,runtimeGame.ready?"true":"false",runtimeGame.cacheHit?"true":"false",runtimeGame.timestamp,runtimeGame.image_size,runtimeGame.elapsed);
    quoted(w,runtimeGame.stage);append(w,"}");
}
#include "runtime_profile_cache.h"
