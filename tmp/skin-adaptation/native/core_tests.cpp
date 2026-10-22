#define BUILD_HOST
#define main unused_host_main
#include "core.cpp"
#undef main
#include <cassert>
#include <initializer_list>
#include "object_skin_rules.h"
int main() {
    // Module verification must reject this test process rather than load or hook it.
    assert(moduleState(GetCurrentProcessId())==1);
    auto request=(Request*)HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,sizeof(Request));
    request->magic=Magic;request->version=Protocol;request->pid=42;request->nonce=99;
    for(int command=0;command<6;command++){request->command=command;assert(!claim(request,42,99));}
    request->command=6;assert(!claim(request,41,99));assert(!claim(request,42,98));
    assert(claim(request,42,99));assert(!claim(request,42,99));
    request->phase=0;request->deadline=0;assert(claim(request,42,99));finishRequest(request);
    assert(strstr(request->result,"request_expired") && request->phase==2);
    HeapFree(GetProcessHeap(),0,request);
    BYTE player[Game_skin_offset+24]{},stack[0xb0]{};
    BYTE* encrypted=player+Game_skin_offset;
    *(uint32_t*)encrypted=0xabcdef12;*(uint32_t*)(encrypted+4)=~0xabcdef12u;
    encrypted[20]=1;encrypted[21]=1;encrypted[22]=0;encrypted[23]=0;
    int skin=-1;assert(skinValue(encrypted,&skin) && skin==0);
    IndependentContext context{};context.player=player;context.stack=stack;
    assert(objectLaneModel("SRU_OrderMinionMelee") && objectLaneModel("SRU_ChaosMinionSuper"));
    assert(!objectLaneModel("SRU_Blue") && !objectLaneModel("YellowTrinket") && !objectLaneModel("Ashe"));
    assert(objectWardModel("YellowTrinket") && !objectWardModel("TestCubeRender10Vision"));
    BYTE wardObject[0x700]{},ownerWrapper[64]{};
    *(BYTE**)(wardObject+0x6f8)=ownerWrapper;*(BYTE**)ownerWrapper=player;
    assert(objectLocalWard(&context,wardObject));
    *(BYTE**)ownerWrapper=stack;assert(!objectLocalWard(&context,wardObject));
    *(BYTE**)(wardObject+0x6f8)=nullptr;assert(!objectLocalWard(&context,wardObject));
    assert(!objectReadable((void*)1,64));
    assert(independentWriteSkin(&context,104));assert(skinValue(encrypted,&skin) && skin==104);
    assert(*(int*)(stack+0x38)==104);
    encrypted[21]=0;encrypted[22]=4;assert(independentWriteSkin(&context,0));
    assert(skinValue(encrypted,&skin) && skin==0);
    encrypted[21]=1;assert(!skinValue(encrypted,&skin));
    encrypted[22]=0;encrypted[23]=4;assert(!skinValue(encrypted,&skin));
    *(const char**)(stack+0x18)="Lux";
    char model[64];int activeSkin=-1;
    assert(independentActiveState(&context,model,sizeof(model),&activeSkin) && activeSkin==0);
    BYTE frames[0x140]{};*(BYTE**)stack=frames;*(BYTE**)(stack+8)=frames+sizeof(frames);*(BYTE**)(stack+16)=frames+sizeof(frames);
    *(const char**)frames="Lux";*(const char**)(frames+0xa0)="LuxFire";
    *(int*)(frames+0x20)=1;*(int*)(frames+0xa0+0x20)=2;
    assert(independentActiveState(&context,model,sizeof(model),&activeSkin)&&!strcmp(model,"LuxFire")&&activeSkin==2);
    // A different active model rejects the request without modifying either layer.
    assert(!independentSyncLayers(&context,"Lux",7));
    assert(*(int*)(frames+0x20)==1 && *(int*)(frames+0xa0+0x20)==2);
    assert(independentSyncLayers(&context,"LuxFire",7));
    assert(*(int*)(frames+0x20)==1 && *(int*)(frames+0xa0+0x20)==7);
    // Same model, stale skin: synchronize both layers, including the one Update consumes.
    *(const char**)(frames+0xa0)="Lux";
    assert(independentSyncLayers(&context,"Lux",7));
    assert(*(int*)(frames+0x20)==7 && *(int*)(frames+0xa0+0x20)==7);
    assert(independentActiveState(&context,model,sizeof(model),&activeSkin)&&activeSkin==7);
    assert(independentSyncLayers(&context,"Lux",0));
    assert(*(int*)(frames+0x20)==0 && *(int*)(frames+0xa0+0x20)==0);
    // Prevalidation prevents partial writes if a later layer has an invalid skin/layout.
    *(int*)(frames+0xa0+0x20)=-1;assert(!independentSyncLayers(&context,"Lux",7));
    assert(*(int*)(frames+0x20)==0);
    *(int*)(frames+0xa0+0x20)=0;
    *(BYTE**)(stack+8)=frames+sizeof(frames)-1;assert(!independentActiveModel(&context,model,sizeof(model)));
    assert(!independentSyncLayers(&context,"Lux",7));assert(*(int*)(frames+0x20)==0);
    *(BYTE**)(stack+8)=frames;*(BYTE**)(stack+16)=frames-1;
    assert(!independentActiveModel(&context,model,sizeof(model)));
    assert(independentDefaultGear("Morgana",80,-1)==3);
    assert(independentDefaultGear("Ahri",86,-1)==0);
    assert(independentDefaultGear("Kayn",0,2)==2);
    ObjectSkinObservation object{true,false,false,1,0,0,"SRU_OrderMinionMelee","Ashe"};
    ObjectSkinSelection selection{4,12};
    auto decision=selectObjectSkin(object,selection);
    assert(decision.action==ObjectSkinAction::LaneMinion && decision.skin==8 && decision.refresh && decision.legacyBaseReset);
    object.playerTeam=2;assert(selectObjectSkin(object,selection).skin==9);
    selection.minionIndex=-1;assert(selectObjectSkin(object,selection).action==ObjectSkinAction::Skip);
    object.laneMinion=false;object.hasOwner=true;object.ownedByLocal=true;object.model="SightWard";
    decision=selectObjectSkin(object,selection);assert(decision.action==ObjectSkinAction::Ward && decision.skin==12 && decision.refresh);
    object.baseSkin=12;assert(!selectObjectSkin(object,selection).refresh);
    for(const char* model:{"JammerDevice","YellowTrinket","VisionWard","BlueTrinket"}) {
        object.model=model;assert(selectObjectSkin(object,selection).action==ObjectSkinAction::Ward);
    }
    object.ownedByLocal=false;assert(selectObjectSkin(object,selection).action==ObjectSkinAction::Skip);
    object.ownedByLocal=true;selection.wardSkin=-1;assert(selectObjectSkin(object,selection).action==ObjectSkinAction::Skip);
    object.model="TestCubeRender10Vision";object.playerModel="Yone";object.ownerSkin=93;
    decision=selectObjectSkin(object,selection);assert(decision.action==ObjectSkinAction::OwnerShape && decision.skin==93 && !decision.legacyBaseReset);
    object.playerModel="Ashe";assert(selectObjectSkin(object,selection).skin==0);
    object.model="SRU_Jungle_Companions";assert(selectObjectSkin(object,selection).action==ObjectSkinAction::Skip);
    object.model="SightWard";selection.wardSkin=10001;assert(selectObjectSkin(object,selection).action==ObjectSkinAction::Skip);
    puts("Independent core: ownership, expiry, skin field, layer synchronization, restore and bounds passed.");
}
