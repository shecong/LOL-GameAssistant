// Independent current-build engine, using game-owned skin data and game functions.
#include "game_profile.h"
#include "variant_metadata.h"
constexpr int IndependentMaxEntries=2048;
struct IndependentEntry {int index,skin,gear;const char* stableModel;char model[64],name[1024],gearName[128];};
struct IndependentContext {BYTE* base;BYTE* player;BYTE* stack;BYTE* champion;IndependentEntry* entries;int count;char hero[64],session[32];};
static bool independentImage(BYTE* base) {
    auto dos=(IMAGE_DOS_HEADER*)base;if(dos->e_magic!=IMAGE_DOS_SIGNATURE || dos->e_lfanew<=0 || dos->e_lfanew>1024*1024)return false;
    auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    return nt->Signature==IMAGE_NT_SIGNATURE && nt->OptionalHeader.Magic==IMAGE_NT_OPTIONAL_HDR64_MAGIC &&
        nt->FileHeader.TimeDateStamp==Game_timestamp && nt->OptionalHeader.SizeOfImage==Game_image_size && gameProfileMatches(base);
}
static bool skinValue(BYTE* field,int* value) {
    if(field[20]!=1 || field[21]>1 || field[22]>4 || field[23]>3 || field[21]*4+field[22]!=4)return false;
    uint32_t decoded=*(uint32_t*)(field+4+field[23]*4),key=*(uint32_t*)field;
    if(field[21])decoded^=~key;
    for(int i=4-field[22];i<4;i++)((BYTE*)&decoded)[i]^=(BYTE)~((BYTE*)&key)[i];
    *value=(int)decoded;return true;
}
static bool independentAdd(IndependentContext* c,int index,int skin,int gear,const char* model,const char* name,const char* gearName) {
    if(c->count>=IndependentMaxEntries || index>999)return false;
    auto entry=&c->entries[c->count++];entry->index=index;entry->skin=skin;entry->gear=gear;entry->stableModel=model;
    return cstring(model,entry->model,sizeof(entry->model)) && cstring(name,entry->name,sizeof(entry->name)) &&
        cstring(gearName,entry->gearName,sizeof(entry->gearName));
}
static BYTE* independentChampion(BYTE* base,const char* hero) {
    BYTE* manager=*(BYTE**)(base+Game_champion_slot);if(!manager)return nullptr;
    BYTE** list=*(BYTE***)(manager+0x18);int size=*(int*)(manager+0x20),capacity=*(int*)(manager+0x24);
    if(!list || size<=0 || size>10000 || capacity<size || capacity>20000)return nullptr;
    for(int n=0;n<size;n++){char name[64];if(!list[n] || !cstring(*(const char**)(list[n]+8),name,sizeof(name)))return nullptr;if(!strcmp(name,hero))return list[n];}
    return nullptr;
}
static BYTE* independentChampionIndex(BYTE* base,int index,int* count) {
    BYTE* manager=*(BYTE**)(base+Game_champion_slot);if(!manager)return nullptr;
    BYTE** list=*(BYTE***)(manager+0x18);int size=*(int*)(manager+0x20),capacity=*(int*)(manager+0x24);
    if(!list || size<=0 || size>10000 || capacity<size || capacity>20000)return nullptr;
    *count=size;return index>=0 && index<size?list[index]:nullptr;
}
static bool independentCatalog(IndependentContext* c) {
    BYTE* skins=*(BYTE**)(c->champion+0xd8);int size=*(int*)(c->champion+0xe0),capacity=*(int*)(c->champion+0xe4);
    if(!skins || size<=0 || size>1000 || capacity<size || capacity>10000)return false;
    int ids[1000];for(int n=0;n<size;n++){ids[n]=*(int*)(skins+n*24);if(ids[n]<0 || ids[n]>10000)return false;}
    for(int n=1;n<size;n++){int value=ids[n],j=n;while(j>0 && ids[j-1]>value){ids[j]=ids[j-1];j--;}ids[j]=value;}
    const char* stableHero=*(const char**)(c->champion+8);int index=0;
    using Translate=const char*(__fastcall*)(const char*);
    for(int n=0;n<size;n++) {
        char key[160],name[1024];sprintf_s(key,"game_character_skin_displayname_%s_%d",c->hero,ids[n]);
        const char* translated=ids[n]?((Translate)(c->base+Game_translate))(key):stableHero;
        if(!translated || !cstring(translated,name,sizeof(name)))return false;
        if(ids[n] && !strcmp(name,key))continue;
        int duplicate=0;for(int j=0;j<c->count;j++)if(c->entries[j].gear==-1 && !strcmp(c->entries[j].model,c->hero)){
            const char* previous=c->entries[j].name;size_t length=strlen(name);
            if(!strcmp(previous,name) || (!strncmp(previous,name,length) && !strncmp(previous+length," Chroma ",8)))duplicate++;
        }
        if(duplicate){char original[1024];strcpy_s(original,name);if(sprintf_s(name,"%s Chroma %d",original,duplicate)<0)return false;}
        int baseIndex=index++;
        if(!independentAdd(c,baseIndex,ids[n],-1,stableHero,name,""))return false;
        for(const auto& variant:variants)if(!strcmp(variant.hero,c->hero) && variant.skin==ids[n] && !strcmp(variant.model,c->hero) && variant.gear>=0)
            if(!independentAdd(c,baseIndex,ids[n],variant.gear,stableHero,name,variant.gear_name))return false;
        for(const auto& variant:variants)if(!strcmp(variant.hero,c->hero) && variant.skin==ids[n] && strcmp(variant.model,c->hero) && variant.gear==-1)
            if(!independentAdd(c,index++,ids[n],-1,variant.model,variant.name,""))return false;
    }
    return c->count>0;
}
static bool independentContext(IndependentContext* c,Request* r) {
    c->base=(BYTE*)GetModuleHandleW(nullptr);
    if(!independentImage(c->base)){fail(r,"independent_game_version_not_supported");return false;}
    c->player=*(BYTE**)(c->base+Game_player_slot);
    if(!c->player){fail(r,"local_player_unavailable");return false;}
    c->stack=c->player+Game_stack_offset;
    if(!cstring(*(const char**)(c->stack+0x18),c->hero,sizeof(c->hero))){fail(r,"player_model_unavailable");return false;}
    int skin;if(!skinValue(c->player+Game_skin_offset,&skin) || skin!=*(int*)(c->stack+0x38)){fail(r,"independent_skin_field_not_verified");return false;}
    c->champion=independentChampion(c->base,c->hero);
    if(!c->champion || !independentCatalog(c)){fail(r,"independent_game_catalog_unavailable");return false;}
    FILETIME created,exited,kernel,user;if(!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user))return false;
    uint64_t token=modelHash(c->hero);token=digest(token,&created,sizeof(created));token=digest(token,&c->base,sizeof(c->base));token=digest(token,&c->player,sizeof(c->player));
    for(int n=0;n<c->count;n++){auto entry=&c->entries[n];token=digest(token,&entry->skin,sizeof(entry->skin));token=digest(token,&entry->gear,sizeof(entry->gear));token=digest(token,entry->model,strlen(entry->model)+1);token=digest(token,entry->name,strlen(entry->name)+1);}
    sprintf_s(c->session,"%016llx",token);return true;
}
// Reference DLL RVA 0x55ecd..0x55ed7 synchronizes layer +0x20 from base +0x38.
// The inspected game Update selects end-0xa0, or the base at stack+0x18.
constexpr size_t IndependentLayerStride=0xa0,IndependentLayerSkin=0x20;
static bool independentLayers(IndependentContext* c,BYTE** first,int* count) {
    BYTE* begin=*(BYTE**)c->stack,*end=*(BYTE**)(c->stack+8),*capacity=*(BYTE**)(c->stack+16);
    *first=begin;*count=0;
    if(begin==end) {
        uintptr_t a=(uintptr_t)begin,cap=(uintptr_t)capacity;
        return (!a && !cap) || (a && cap>=a && (cap-a)%IndependentLayerStride==0 && (cap-a)/IndependentLayerStride<=256);
    }
    return span(begin,end,capacity,IndependentLayerStride,256,count);
}
static bool independentActiveState(IndependentContext* c,char* model,size_t size,int* skin) {
    BYTE* first;int count;if(!independentLayers(c,&first,&count))return false;
    BYTE* active=count?first+(count-1)*IndependentLayerStride:c->stack+0x18;
    *skin=*(int*)(active+IndependentLayerSkin);
    return *skin>=0 && *skin<=10000 && cstring(*(const char**)active,model,size);
}
static bool independentActiveModel(IndependentContext* c,char* model,size_t size) {
    int skin;return independentActiveState(c,model,size,&skin);
}
static bool independentValidateLayers(IndependentContext* c,const char* target,bool replacesStack) {
    BYTE* first;int count;if(!independentLayers(c,&first,&count))return false;
    for(int n=0;n<count;n++) {
        BYTE* layer=first+n*IndependentLayerStride;char model[64];int skin=*(int*)(layer+IndependentLayerSkin);
        if(skin<0 || skin>10000 || !cstring(*(const char**)layer,model,sizeof(model)))return false;
        // Do not overwrite a borrowed/transform model with another model's skin ID.
        if(n==count-1 && !replacesStack && strcmp(model,target))return false;
    }
    return true;
}
static bool independentSyncLayers(IndependentContext* c,const char* target,int skin) {
    if(!independentValidateLayers(c,target,false))return false;
    BYTE* first;int count;if(!independentLayers(c,&first,&count))return false;
    for(int n=0;n<count;n++) {
        BYTE* layer=first+n*IndependentLayerStride;char model[64];
        if(!cstring(*(const char**)layer,model,sizeof(model)))return false;
        if(!strcmp(model,target))*(int*)(layer+IndependentLayerSkin)=skin;
    }
    return true;
}
static void independentState(Writer* writer,IndependentContext* c) {
    char active[64];int skin;bool valid=independentActiveState(c,active,sizeof(active),&skin);
    append(writer,"\"skin\":%d,\"gear\":%d,\"model\":",*(int*)(c->stack+0x38),(int)*(signed char*)(c->stack+0x9c));quoted(writer,c->hero);
    append(writer,",\"active_model\":");if(valid)quoted(writer,active);else append(writer,"null");
    append(writer,",\"active_skin\":");if(valid)append(writer,"%d",skin);else append(writer,"null");
}
static bool independentWriteSkin(IndependentContext* c,int skin) {
    BYTE* field=c->player+Game_skin_offset;int previous;if(!skinValue(field,&previous))return false;
    uint32_t key=*(uint32_t*)field,value=(uint32_t)skin;if(field[21])value^=~key;
    for(int n=4-field[22];n<4;n++)((BYTE*)&value)[n]^=(BYTE)~((BYTE*)&key)[n];
    BYTE index=(field[23]+1)&3;*(uint32_t*)(field+4+index*4)=value;field[23]=index;*(int*)(c->stack+0x38)=skin;return true;
}
static int independentDefaultGear(const char* hero,int skin,int current) {
    if(!strcmp(hero,"Kayn"))return current;
    if(!strcmp(hero,"Morgana") && skin==80)return 3;
    if((!strcmp(hero,"Katarina") && skin>=29 && skin<=36) || (!strcmp(hero,"Viego") && skin==43) ||
       (!strcmp(hero,"Jinx") && skin==60) || (!strcmp(hero,"Mordekaiser") && skin==54) ||
       (!strcmp(hero,"Jax") && skin>=14 && skin<=19) || (!strcmp(hero,"Sett") && skin==66) ||
       (!strcmp(hero,"Kayle") && skin==6) || (!strcmp(hero,"Ashe") && skin==76) ||
       (!strcmp(hero,"Kaisa") && skin==71) || (!strcmp(hero,"Renekton") && skin>=26 && skin<=32) ||
       (!strcmp(hero,"MasterYi") && skin==116) || (!strcmp(hero,"Ahri") && skin==86))return 0;
    return -1;
}
static void independentProcess(Request* r) {
    IndependentContext c{};c.entries=(IndependentEntry*)calloc(IndependentMaxEntries,sizeof(IndependentEntry));
    if(!c.entries){fail(r,"independent_allocation_failed");return;}bool invoked=false;
    __try {
        __try {
            if(!independentContext(&c,r))__leave;
            Writer writer{r->result,0,sizeof(r->result),false};
            if(r->command==10){
                int count=0;independentChampionIndex(c.base,0,&count);
                if(!count){fail(r,"independent_game_catalog_unavailable");__leave;}
                append(&writer,"{\"ok\":true,\"independent\":true,\"original_required\":false,\"catalog_only\":true,\"reference_sha256\":\"%s\"",GameProfileSha);
                if(r->index<0){
                    append(&writer,",\"heroes\":[");for(int n=0;n<count;n++){int checked=0;BYTE* champion=independentChampionIndex(c.base,n,&checked);char name[64];
                        if(!champion || !cstring(*(const char**)(champion+8),name,sizeof(name))){fail(r,"independent_game_catalog_unavailable");__leave;}
                        if(n)append(&writer,",");append(&writer,"{\"index\":%d,\"model\":",n);quoted(&writer,name);append(&writer,"}");}
                    append(&writer,"]}");if(writer.failed)fail(r,"reply_too_large");__leave;
                }
                c.champion=independentChampionIndex(c.base,r->index,&count);c.count=0;
                if(!c.champion || !cstring(*(const char**)(c.champion+8),c.hero,sizeof(c.hero)) || !independentCatalog(&c)){fail(r,"independent_game_catalog_unavailable");__leave;}
                append(&writer,",\"model\":");quoted(&writer,c.hero);
            }
            if(r->command==8 || r->command==9) {
                if(strcmp(r->session,c.session)){fail(r,"stale_session");__leave;}
                IndependentEntry* selected=nullptr;
                for(int n=0;n<c.count;n++)if((r->command==9?c.entries[n].skin==0 && c.entries[n].gear==-1 && !strcmp(c.entries[n].model,c.hero):c.entries[n].index==r->index && c.entries[n].gear==r->gear)){selected=&c.entries[n];break;}
                if(!selected){fail(r,"invalid_entry");__leave;}
                if(GetTickCount64()>r->deadline){fail(r,"request_expired");__leave;}
                bool replacesStack=!strcmp(c.hero,"Lux") || !strcmp(c.hero,"Sona");
                if(!independentValidateLayers(&c,selected->model,replacesStack)){fail(r,"independent_skin_layer_not_verified","validation",false);__leave;}
                invoked=true;if(!independentWriteSkin(&c,selected->skin)){fail(r,"independent_skin_field_not_verified","call",false);__leave;}
                using Update=__int64(__fastcall*)(uintptr_t,bool);
                bool special=(!strcmp(c.hero,"Lux") && selected->skin==7) || (!strcmp(c.hero,"Sona") && selected->skin==6);
                if(special) {
                    HMODULE pinned=nullptr;if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,(LPCWSTR)&independentProcess,&pinned)){fail(r,"independent_model_lifetime_failed","call",true);__leave;}
                    *(BYTE**)(c.stack+8)=*(BYTE**)c.stack;
                    using Push=__int64(__fastcall*)(uintptr_t,const char*,int,int,bool,bool,bool,bool,bool,bool,int8_t,const char*,int,const char*,int,bool,int,const char*);
                    ((Push)(c.base+Game_push))((uintptr_t)c.stack,selected->stableModel,selected->skin,0,false,false,false,false,true,false,-1,"",0,"",0,false,1,"");
                } else {
                    if(!strcmp(c.hero,"Lux") || !strcmp(c.hero,"Sona"))*(BYTE**)(c.stack+8)=*(BYTE**)c.stack;
                    *(signed char*)(c.stack+0x9c)=(signed char)independentDefaultGear(c.hero,selected->skin,*(signed char*)(c.stack+0x9c));
                }
                if(selected->gear>=0)*(signed char*)(c.stack+0x9c)=(signed char)selected->gear;
                if(!independentSyncLayers(&c,selected->model,selected->skin)){fail(r,"independent_skin_layer_not_verified","call",true);__leave;}
                ((Update)(c.base+Game_update))((uintptr_t)c.stack,true);
                char active[64];int skin,activeSkin;
                bool verified=skinValue(c.player+Game_skin_offset,&skin) && skin==selected->skin && *(int*)(c.stack+0x38)==skin &&
                    independentActiveState(&c,active,sizeof(active),&activeSkin) && activeSkin==skin && !strcmp(active,selected->model) && (selected->gear<0 || *(signed char*)(c.stack+0x9c)==selected->gear);
                append(&writer,"{\"ok\":true,\"invoked\":true,\"state_verified\":%s,",verified?"true":"false");
            } else if(r->command!=10)append(&writer,"{\"ok\":true,\"matched\":true,");
            if(r->command!=10){append(&writer,"\"independent\":true,\"original_required\":false,\"connected\":true,\"session\":\"%s\",\"pid\":%lu,\"request_id\":\"%016llx\",\"reference_sha256\":\"%s\",",c.session,GetCurrentProcessId(),r->nonce,GameProfileSha);independentState(&writer,&c);}
            if(r->command==7 || r->command==10){
                append(&writer,",\"entries\":[");for(int n=0;n<c.count;n++){auto entry=&c.entries[n];if(n)append(&writer,",");append(&writer,"{\"entry_id\":\"%d:%d\",\"index\":%d,\"gear\":%d,\"skin_num\":%d,\"name\":",entry->index,entry->gear,entry->index,entry->gear,entry->skin);quoted(&writer,entry->name);append(&writer,",\"model\":");quoted(&writer,entry->model);if(entry->gear>=0){append(&writer,",\"gear_name\":");quoted(&writer,entry->gearName);}append(&writer,"}");}append(&writer,"]");
            }
            append(&writer,"}");if(writer.failed)fail(r,"reply_too_large","serialization",invoked);
        } __except(EXCEPTION_EXECUTE_HANDLER){fail(r,"independent_access_or_call_failed",invoked?"call_or_readback":"read",invoked);}
    } __finally {free(c.entries);}
}
