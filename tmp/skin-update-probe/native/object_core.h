// Current-build object adapter. Model allowlists replace the obsolete F0 slot.
#include "object_skin_rules.h"
static bool objectLaneModel(const char* model) {
    const char* names[]={"SRU_OrderMinionMelee","SRU_OrderMinionRanged","SRU_OrderMinionSiege","SRU_OrderMinionSuper",
        "SRU_ChaosMinionMelee","SRU_ChaosMinionRanged","SRU_ChaosMinionSiege","SRU_ChaosMinionSuper"};
    for(auto name:names)if(!strcmp(name,model))return true;return false;
}
static bool objectWardModel(const char* model) {
    return !strcmp(model,"YellowTrinket") || !strcmp(model,"BlueTrinket") || !strcmp(model,"SightWard") ||
        !strcmp(model,"VisionWard") || !strcmp(model,"JammerDevice");
}
static bool objectReadable(const void* p,size_t length) {
    MEMORY_BASIC_INFORMATION region{};uintptr_t address=(uintptr_t)p;
    return p && address+length>=address && VirtualQuery(p,&region,sizeof(region)) && region.State==MEM_COMMIT &&
        !(region.Protect&(PAGE_NOACCESS|PAGE_GUARD)) && address+length<=(uintptr_t)region.BaseAddress+region.RegionSize;
}
static bool objectProfile(BYTE* base) {
    bool resolved=runtimeResolveObjects(base);if(resolved)runtimeCacheSave(runtimeGame);return resolved;
}
struct ObjectTarget {BYTE* object;int skin;bool ward;char model[64];};
static bool objectList(IndependentContext* c,BYTE*** objects,int* count) {
    BYTE* manager=*(BYTE**)(c->base+runtimeGame.object_list_slot);
    if(!objectReadable(manager,24))return false;
    *objects=*(BYTE***)(manager+8);*count=*(int*)(manager+16);
    return *count>=0 && *count<=10000 && (!*count || objectReadable(*objects,*count*sizeof(BYTE*)));
}
static bool objectLocalWard(IndependentContext* c,BYTE* object) {
    // The current GoldRedirect candidate returns a wrapper, not the root object.
    // Only accept the observed wrapper layout when its first field is our player.
    BYTE* wrapper=*(BYTE**)(object+runtimeGame.owner_offset);
    return objectReadable(wrapper,64) && *(BYTE**)wrapper==c->player;
}
static bool objectWardName(IndependentContext* c,int skin,char* name,size_t size) {
    if(skin<0 || skin>1000)return false;
    char key[128];sprintf_s(key,"game_character_skin_displayname_SightWard_%d",skin);
    using Translate=const char*(__fastcall*)(const char*);
    const char* translated=((Translate)(c->base+runtimeGame.translate))(key);
    return translated && cstring(translated,name,size) && name[0] && strcmp(name,key);
}
static void independentObjects(Request* r,Writer* writer,IndependentContext* c,bool* invoked) {
    if(!objectProfile(c->base)){fail(r,runtimeGame.error,runtimeGame.stage);return;}
    BYTE** objects;int count;if(!objectList(c,&objects,&count)){fail(r,"object_list_not_verified");return;}
    if(r->command!=12 && strcmp(r->session,c->session)){fail(r,"stale_session");return;}
    if(r->command!=12 && (r->index < -1 || r->index>(r->command==13?10:10000) || r->gear < -1 || r->gear>1000)){fail(r,"invalid_object_selection");return;}
    int playerTeam=c->player[Game_team_offset];
    if(r->command==13 && r->index>=0 && playerTeam!=1 && playerTeam!=2){fail(r,"object_team_not_verified");return;}
    char wardName[1024];
    if(r->command!=12 && r->gear>=0 && !objectWardName(c,r->gear,wardName,sizeof(wardName))){fail(r,"ward_skin_not_in_catalog");return;}
    int lanes=0,wards=0,baselineMinion=-1,baselineWard=-1;bool mixedMinion=false,mixedWard=false;
    ObjectTarget* targets=(ObjectTarget*)calloc(count?count:1,sizeof(ObjectTarget));
    if(!targets){fail(r,"independent_allocation_failed");return;}
    __try {
        int targetCount=0;bool invalid=false;
        for(int n=0;n<count;n++) {
            BYTE* object=objects[n];if(!objectReadable(object,runtimeGame.stack_offset+0xa0))continue;
            char model[64];if(!cstring(*(const char**)(object+runtimeGame.stack_offset+0x18),model,sizeof(model))){invalid=true;break;}
            bool lane=objectLaneModel(model),ward=objectWardModel(model) && objectLocalWard(c,object);
            if(!lane && !ward)continue;
            int skin=*(int*)(object+runtimeGame.stack_offset+0x38);if(skin<0 || skin>10000){invalid=true;break;}
            if(lane){lanes++;if(baselineMinion<0)baselineMinion=skin;else if(baselineMinion!=skin)mixedMinion=true;}
            else {wards++;if(baselineWard<0)baselineWard=skin;else if(baselineWard!=skin)mixedWard=true;}
            if(r->command==12)continue;
            int selected=-1;
            if(r->command==14)selected=lane?r->index:r->gear;
            else {ObjectSkinObservation observation{lane,ward,ward,playerTeam,skin,0,model,c->hero};
                selected=selectObjectSkin(observation,{r->index,r->gear}).skin;}
            if(selected<0)continue;
            IndependentContext objectContext=*c;objectContext.player=object;objectContext.stack=object+runtimeGame.stack_offset;
            if(!independentValidateLayers(&objectContext,model,false)){invalid=true;break;}
            auto target=&targets[targetCount++];target->object=object;target->skin=selected;target->ward=ward;strcpy_s(target->model,model);
        }
        if(invalid){fail(r,"object_skin_layer_not_verified");__leave;}
        if(r->command!=12 && GetTickCount64()>r->deadline){fail(r,"request_expired");__leave;}
        bool verified=true;int changed=0;
        for(int n=0;n<targetCount;n++) {
            if(GetTickCount64()>r->deadline){fail(r,"request_expired",*invoked?"call":"validation",*invoked);__leave;}
            auto target=&targets[n];IndependentContext objectContext=*c;objectContext.player=target->object;objectContext.stack=target->object+runtimeGame.stack_offset;
            // The game rewrites base IDs between requests while rendered resources
            // can retain the last selection. A manual apply/restore must refresh
            // even when the sampled base ID already equals the requested ID.
            {
                *invoked=true;*(int*)(objectContext.stack+0x38)=target->skin;
                if(!independentSyncLayers(&objectContext,target->model,target->skin)){fail(r,"object_skin_layer_not_verified","call",true);__leave;}
                using Update=__int64(__fastcall*)(uintptr_t,bool);((Update)(c->base+runtimeGame.update))((uintptr_t)objectContext.stack,true);changed++;
            }
            char active[64];int skin;
            if(!independentActiveState(&objectContext,active,sizeof(active),&skin) || skin!=target->skin || strcmp(active,target->model))verified=false;
        }
        append(writer,"{\"ok\":true,\"independent\":true,\"original_required\":false,\"session\":\"%s\",\"reference_sha256\":\"%s\",\"request_id\":\"%016llx\",\"model\":",c->session,runtimeGame.sha,r->nonce);quoted(writer,c->hero);
        append(writer,",\"invoked\":%s,\"state_verified\":%s,\"object_target_count\":%d,\"object_changed_count\":%d,\"lane_count\":%d,\"own_ward_count\":%d,\"baseline_minion_skin\":%d,\"baseline_ward_skin\":%d",*invoked?"true":"false",r->command!=12 && targetCount && verified?"true":"false",targetCount,changed,lanes,wards,mixedMinion?-1:baselineMinion,mixedWard?-1:baselineWard);
        if(r->command==12) {
            append(writer,",\"ward_entries\":[");int emitted=0;
            for(int n=0;n<=1000;n++)if(objectWardName(c,n,wardName,sizeof(wardName))){if(emitted++)append(writer,",");append(writer,"{\"skin_num\":%d,\"name\":",n);quoted(writer,wardName);append(writer,"}");}
            append(writer,"]");
        }
        append(writer,"}");
    } __finally {free(targets);}
}
