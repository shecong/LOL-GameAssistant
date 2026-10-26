#pragma once
constexpr char BorderProfile[]="cn16.21.regalia.v2";
struct BorderTier {const char* id;const char* name;};
static const BorderTier BorderTiers[]={{"IRON","黑铁"},{"BRONZE","青铜"},{"SILVER","白银"},{"GOLD","黄金"},{"PLATINUM","铂金"},{"EMERALD","翡翠"},{"DIAMOND","钻石"},{"MASTER","超凡大师"},{"GRANDMASTER","傲世宗师"},{"CHALLENGER","最强王者"}};
struct BorderContext {BYTE* base;BYTE* component;int slot;char session[32],tier[32],division[16],mode[32];};
struct BorderOriginal {bool captured=false;char session[32]{},tier[32]{},division[16]{},mode[32]{},caption[128]{};BYTE* resources[7]{};};
static BorderOriginal borderOriginal;
static bool borderDivisionValid(int value) {return value>=1 && value<=4;}
static bool borderText(BYTE* object,char* text,size_t capacity) {
    if(!borderReadable(object,16))return false;
    const char* data=*(const char**)object;unsigned size=*(unsigned*)(object+8);
    if(size>=capacity || !data || !borderReadable((BYTE*)data,size+1) || data[size])return false;
    return readText(data,size,text,capacity);
}
static bool borderContext(BorderContext* context,Request* request) {
    auto base=(BYTE*)GetModuleHandleW(nullptr);context->base=base;
    if(!independentImage(base)){fail(request,"independent_game_version_not_supported");return false;}
    const BYTE mapGetter[]={0x48,0x8d,0x41,0x08,0xc3};
    const BYTE buildPrefix[]={0x40,0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x81,0xec,0x60,0x01,0x00,0x00};
    if(memcmp(base+0x21c9f0,mapGetter,sizeof(mapGetter)) || memcmp(base+0x413260,buildPrefix,sizeof(buildPrefix))) {
        fail(request,"rank_border_profile_mismatch","border_function_signatures");return false;
    }
    BYTE* local=*(BYTE**)(base+0x1c7be80);if(!local || !borderReadable(local+0x423,1)){fail(request,"rank_border_not_ready");return false;}
    int slot=local[0x423];if(slot<1 || slot>31){fail(request,"rank_border_not_ready");return false;}context->slot=slot;
    BYTE* entity=base+0x1cc0990+slot*0x120;
    BYTE** table=*(BYTE***)entity;
    if(!table || !borderReadable((BYTE*)(table+2),8) || table[2]!=base+0x21c9f0){fail(request,"rank_border_profile_mismatch","border_entity_getter");return false;}
    BYTE* entries=*(BYTE**)(entity+8);int count=*(int*)(entity+0x10);
    if(!entries || count<0 || count>128 || !borderReadable(entries,count*24)){fail(request,"rank_border_not_ready");return false;}
    for(int n=0;n<count;n++) {
        BYTE* entry=entries+n*24;if(*(BYTE**)entry!=base+0x1c932b0 || !*(uint64_t*)(entry+0x10))continue;
        BYTE* handle=*(BYTE**)(entry+8);if(!handle || !borderReadable(handle,8))continue;
        BYTE* box=*(BYTE**)handle;if(!box || !borderReadable(box+0x10,8))continue;
        context->component=*(BYTE**)(box+0x10);break;
    }
    BYTE* component=context->component;
    if(!component || !borderReadable(component,0xa0) || *(BYTE**)component!=base+0x18a4380 ||
        !borderText(component+0x48,context->tier,sizeof(context->tier)) ||
        !borderText(component+0x58,context->division,sizeof(context->division)) ||
        !borderText(component+0x78,context->mode,sizeof(context->mode)) || !context->mode[0]) {fail(request,"rank_border_not_ready");return false;}
    FILETIME created,exited,kernel,user;if(!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user)){fail(request,"rank_border_not_ready");return false;}
    uint64_t session=modelHash(BorderProfile);session=digest(session,&created,sizeof(created));session=digest(session,&component,sizeof(component));session=digest(session,&slot,sizeof(slot));
    sprintf_s(context->session,"%016llx",session);return true;
}
static BYTE* borderLookup(BYTE* base) {
    using Hash=uint32_t(__fastcall*)(const char*,size_t);using Find=BYTE*(__fastcall*)(BYTE*,uint32_t*,int);using Cast=BYTE*(__fastcall*)(BYTE*,BYTE*,BYTE*);
    const char* path="Loadouts/Regalia/Crests/Ranked/RankedCrestLookup";uint32_t key=((Hash)(base+0xf69970))(path,strlen(path));
    BYTE* database=*(BYTE**)(base+0x1c85248);BYTE* resource=database?((Find)(base+0xf66970))(database,&key,0):nullptr;
    BYTE* lookup=resource?((Cast)(base+0xf61f00))(*(BYTE**)(resource+8),*(BYTE**)resource,base+0x1c93590):nullptr;
    using Rules=BYTE*(__fastcall*)();using Override=BYTE*(__fastcall*)(BYTE*,BYTE*);
    BYTE* rules=((Rules)(base+0x495fc0))();
    if(rules && borderReadable(rules+0x30,16)) {
        BYTE* entries=*(BYTE**)(rules+0x30);int count=*(int*)(rules+0x38);uint32_t type=*(uint32_t*)(base+0x1c932c8);
        if(count>=0 && count<=1024 && entries && borderReadable(entries,count*16))for(int n=0;n<count;n++)if(*(uint32_t*)(entries+n*16)==type) {
            BYTE* value=*(BYTE**)(entries+n*16+8);if(value && borderReadable(value+8,16))lookup=((Override)(base+0xf72e80))(value+8,base+0x1c93590);break;
        }
    }
    return lookup;
}
static const char* borderCurrentId(BorderContext* context) {
    if(!strcmp(context->mode,"rank"))for(auto& tier:BorderTiers)if(!strcmp(tier.id,context->tier))return tier.id;
    return "original";
}
static void borderReply(Request* request,Writer* writer,BorderContext* context,bool invoked,bool verified,bool available,bool restored=false,int cards=0,bool displayVerified=false) {
    append(writer,"{\"ok\":true,\"independent\":true,\"original_required\":false,\"invoked\":%s,\"state_verified\":%s,\"rank_border_supported\":true,\"rank_border_loading_ready\":%s,\"rank_border_profile\":\"%s\",\"rank_border_id\":\"%s\",\"session\":\"%s\",\"pid\":%lu,\"request_id\":\"%016llx\",\"reference_sha256\":\"%s\",\"rank_border_entries\":[{\"id\":\"original\",\"name\":\"原始边框 / 当前等级装饰\"}",
        invoked?"true":"false",verified?"true":"false",available && cards>0?"true":"false",BorderProfile,borderCurrentId(context),context->session,GetCurrentProcessId(),request->nonce,GameProfileSha);
    for(auto& tier:BorderTiers) {append(writer,",{\"id\":\"%s\",\"name\":",tier.id);quoted(writer,tier.name);append(writer,"}");}
    const char* baseline=borderCurrentId(context);
    if(borderOriginal.captured && !strcmp(borderOriginal.session,context->session) && !strcmp(borderOriginal.mode,"rank"))for(auto& tier:BorderTiers)if(!strcmp(tier.id,borderOriginal.tier))baseline=tier.id;
    if(borderOriginal.captured && !strcmp(borderOriginal.session,context->session) && strcmp(borderOriginal.mode,"rank"))baseline="original";
    append(writer,"],\"rank_border_baseline_id\":\"%s\",\"rank_border_baseline_available\":%s,\"rank_border_restore_verified\":%s,\"rank_border_display_count\":%d,\"rank_border_display_verified\":%s,\"rank_border_display_state\":\"%s\"}",baseline,borderOriginal.captured && !strcmp(borderOriginal.session,context->session)?"true":"false",restored?(verified?"true":"false"):"null",cards,cards?(displayVerified?"true":"false"):"null",cards?(displayVerified?"resources_bound":"not_verified"):(available?"waiting_for_card":"not_present"));
}
static bool borderRefValid(BYTE* value) {
    if(!value)return true;
    MEMORY_BASIC_INFORMATION info{};
    return borderReadable(value+0x1c,4) && VirtualQuery(value+0x1c,&info,sizeof(info)) &&
        (info.Protect&(PAGE_READWRITE|PAGE_WRITECOPY|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY)) &&
        *(unsigned*)(value+0x1c)>0 && *(unsigned*)(value+0x1c)<0x1000000;
}
static void borderSetRef(BYTE* base,BYTE** field,BYTE* value) {
    if(value)InterlockedIncrement((LONG*)(value+0x1c));
    BYTE* previous=*field;if(previous)((void(__fastcall*)(BYTE*))(base+0x11050c0))(previous);*field=value;
}
static bool borderCapture(BorderContext* context) {
    if(borderOriginal.captured && !strcmp(borderOriginal.session,context->session))return true;
    if(borderOriginal.captured)return false;
    char caption[128];if(!borderText(context->component+0x68,caption,sizeof(caption)))return false;
    for(int j=0;j<7;j++)if(!borderRefValid(*(BYTE**)(context->component+0x10+j*8)))return false;
    strcpy_s(borderOriginal.session,context->session);strcpy_s(borderOriginal.tier,context->tier);strcpy_s(borderOriginal.division,context->division);strcpy_s(borderOriginal.mode,context->mode);strcpy_s(borderOriginal.caption,caption);
    for(int j=0;j<7;j++){BYTE* value=*(BYTE**)(context->component+0x10+j*8);if(value)InterlockedIncrement((LONG*)(value+0x1c));borderOriginal.resources[j]=value;}
    borderOriginal.captured=true;return true;
}
static void independentRankBorder(Request* request,Writer* writer,bool* invoked) {
    BorderContext context{};if(!borderContext(&context,request))return;
    BYTE* lookup=borderLookup(context.base);
    BorderCard cards[16]{};int cardCount=0;
    if(lookup) {
        if(!borderDisplaySignatures(context.base) || !borderEmptySlot(context.base)){fail(request,"rank_border_profile_mismatch","border_display_signatures");return;}
        HMODULE pinned=nullptr;if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,(LPCWSTR)&independentRankBorder,&pinned)){fail(request,"rank_border_lifetime_failed");return;}
        cardCount=borderFindCards(context.base,context.slot,cards);
    }
    if(request->command==19) {borderReply(request,writer,&context,false,false,lookup!=nullptr,false,cardCount);return;}
    if(request->command!=21 && strcmp(request->session,context.session)){fail(request,"stale_session");return;}
    if(GetTickCount64()>request->deadline){fail(request,"request_expired");return;}
    using Text=void(__fastcall*)(BYTE*,const char*);auto assign=(Text)(context.base+0xf61760);
    if(request->index<0) {
        if(!borderOriginal.captured || strcmp(borderOriginal.session,context.session)){fail(request,"rank_border_baseline_unavailable");return;}
        if(lookup && !cardCount){fail(request,"rank_border_not_ready","waiting_for_loading_card");return;}
        for(int j=0;j<7;j++)if(!borderRefValid(borderOriginal.resources[j]) || !borderRefValid(*(BYTE**)(context.component+0x10+j*8))){fail(request,"rank_border_profile_mismatch");return;}
        *invoked=true;
        assign(context.component+0x48,borderOriginal.tier);assign(context.component+0x58,borderOriginal.division);
        assign(context.component+0x78,borderOriginal.mode);assign(context.component+0x68,borderOriginal.caption);
        for(int j=0;j<7;j++)borderSetRef(context.base,(BYTE**)(context.component+0x10+j*8),borderOriginal.resources[j]);
        BorderContext after{};bool verified=borderContext(&after,request) && !strcmp(after.tier,borderOriginal.tier) && !strcmp(after.division,borderOriginal.division) && !strcmp(after.mode,borderOriginal.mode);
        for(int j=0;verified && j<7;j++)verified=*(BYTE**)(after.component+0x10+j*8)==borderOriginal.resources[j];
        bool displayVerified=cardCount && borderRefreshCards(context.base,context.slot,cards,cardCount,*(BYTE**)(context.component+0x20),!strcmp(after.mode,"rank"));
        if(cardCount)verified=verified && displayVerified;
        borderReply(request,writer,&after,true,verified,lookup!=nullptr,true,cardCount,displayVerified);return;
    }
    if(request->index>=sizeof(BorderTiers)/sizeof(BorderTiers[0])){fail(request,"invalid_rank_border_entry");return;}
    if(!lookup || !borderReadable(lookup,16)){fail(request,"rank_border_loading_assets_unavailable");return;}
    if(!cardCount){fail(request,"rank_border_not_ready","waiting_for_loading_card");return;}
    BYTE* entries=*(BYTE**)lookup;int count=*(int*)(lookup+8),division=-1;bool found=false;BYTE* expectedCrest=nullptr;BYTE* expectedDivision=nullptr;
    if(entries && count>0 && count<=32 && borderReadable(entries,count*0x38))for(int n=0;n<count;n++) {
        BYTE* entry=entries+n*0x38;char name[32];const char* tierName=*(const char**)entry;
        if(!tierName || !borderReadable((BYTE*)tierName,strlen(BorderTiers[request->index].id)+1) || !cstring(tierName,name,sizeof(name)) || strcmp(name,BorderTiers[request->index].id))continue;
        BYTE* divisions=*(BYTE**)(entry+0x18);int size=*(int*)(entry+0x20);
        if(!divisions || size<1 || size>8 || !borderReadable(divisions,size*16))break;
        BYTE* crest=*(BYTE**)(entry+0x10);
        if(!crest || !borderReadable(crest+0x98,8))break;expectedCrest=*(BYTE**)(crest+0x98);
        for(int j=0;j<size;j++) {
            int value=*(int*)(divisions+j*16);
            // The game's ranked-display predicate requires a non-empty division.
            // Key 0 is a placeholder and would select the prestige display branch.
            if(borderDivisionValid(value) && (division<0 || value<division)) {
                BYTE* asset=*(BYTE**)(divisions+j*16+8);if(!asset || !borderReadable(asset+0x98,8))continue;
                division=value;expectedDivision=*(BYTE**)(asset+0x98);
            }
        }
        found=division>=0 && expectedCrest && expectedDivision && borderRefValid(expectedCrest) && borderRefValid(expectedDivision);break;
    }
    if(!found){fail(request,"invalid_rank_border_entry");return;}
    HMODULE pinned=nullptr;if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,(LPCWSTR)&independentRankBorder,&pinned)){fail(request,"rank_border_lifetime_failed");return;}
    if(!borderCapture(&context)){fail(request,"rank_border_baseline_unavailable");return;}
    const char* roman[]={"","I","II","III","IV"};
    *invoked=true;assign(context.component+0x48,BorderTiers[request->index].id);assign(context.component+0x58,roman[division]);assign(context.component+0x78,"rank");
    for(int j=0;j<3;j++)borderSetRef(context.base,(BYTE**)(context.component+0x30+j*8),nullptr);
    // The game's builder retains the selected assets and releases the old crest references.
    using Build=void(__fastcall*)(BYTE*,BYTE*,int,int);((Build)(context.base+0x413260))(context.component,context.component+0x48,division,0);
    BorderContext after{};bool verified=borderContext(&after,request) && !strcmp(after.tier,BorderTiers[request->index].id) && !strcmp(after.mode,"rank") && !strcmp(after.division,roman[division]) &&
        *(BYTE**)(context.component+0x20)==expectedCrest && *(BYTE**)(context.component+0x28)==expectedDivision;
    bool displayVerified=borderRefreshCards(context.base,context.slot,cards,cardCount,expectedCrest,true);
    borderReply(request,writer,&after,true,verified && displayVerified,true,false,cardCount,displayVerified);
}
