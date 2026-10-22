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
    // The camera adapter changes one float, requires the observed baseline,
    // and refuses NaN, out-of-range values, stale state and read-only pages.
    alignas(4) float cameraFields[]={123.f,600.f,456.f};
    assert(cameraReplace(&cameraFields[1],600.f,1700.f));
    assert(cameraFields[0]==123.f && cameraFields[1]==1700.f && cameraFields[2]==456.f);
    assert(!cameraReplace(&cameraFields[1],600.f,1000.f) && cameraFields[1]==1700.f);
    assert(!cameraReplace(&cameraFields[1],1700.f,1701.f));
    assert(!cameraReplace(&cameraFields[1],1700.f,NAN));
    assert(!cameraReplace(&cameraFields[1],1700.f,599.f));
    assert(cameraReplace(&cameraFields[1],1700.f,600.f));
    auto readonlyCamera=(float*)VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    *readonlyCamera=600.f;DWORD cameraProtection;
    assert(VirtualProtect(readonlyCamera,4096,PAGE_READONLY,&cameraProtection));
    assert(!cameraReplace(readonlyCamera,600.f,1700.f) && *readonlyCamera==600.f);
    VirtualFree(readonlyCamera,0,MEM_RELEASE);
    auto cameraImage=(BYTE*)VirtualAlloc(nullptr,Game_image_size,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    assert(cameraImage);
    memcpy(cameraImage+0x35ea31,CameraLookup,sizeof(CameraLookup));
    memcpy(cameraImage+0x346dfa,CameraDefault,sizeof(CameraDefault));
    alignas(8) BYTE cameraConfig[0xa0]{};*(float*)(cameraConfig+0x20)=600.25f;
    *(BYTE**)(cameraImage+CameraSlot)=cameraConfig;
    IndependentContext cameraContext{};cameraContext.base=cameraImage;strcpy_s(cameraContext.session,"1234567890abcdef");
    auto cameraRequest=(Request*)HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,sizeof(Request));
    bool cameraInvoked=false;Writer cameraWriter{cameraRequest->result,0,sizeof(cameraRequest->result),false};
    cameraRequest->command=15;independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"\"camera_distance\":600.25"));
    cameraRequest->command=16;strcpy_s(cameraRequest->session,cameraContext.session);
    cameraRequest->index=cameraBits(1700.f);cameraRequest->gear=cameraBits(600.25f);cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(cameraInvoked && *(float*)(cameraConfig+0x20)==1700.f && strstr(cameraRequest->result,"\"state_verified\":true"));
    memcpy(cameraImage+0x65ae96,CameraViewLookup,sizeof(CameraViewLookup));
    memcpy(cameraImage+0x98a028,CameraViewInit,sizeof(CameraViewInit));
    alignas(8) BYTE cameraManager[0x20]{},cameraController[0x480]{};
    *(BYTE**)(cameraImage+CameraManagerSlot)=cameraManager;*(BYTE**)(cameraManager+0x18)=cameraController;
    *(BYTE**)(cameraController+0x478)=cameraConfig;*(BYTE**)(cameraController+0x3b0)=cameraConfig;
    auto currentView=(CameraViewPair*)(cameraController+0x3c0);*currentView={2250.f,1800.f};
    cameraController[0x3bf]=0x75;cameraController[0x3c8]=0x26;
    cameraRequest->command=17;cameraRequest->index=cameraBits(600.f);cameraInvoked=false;cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(cameraInvoked && currentView->current==600.f && currentView->target==600.f);
    assert(*(float*)(cameraConfig+0x20)==1700.f && cameraController[0x3bf]==0x75 && cameraController[0x3c8]==0x26);
    assert(strstr(cameraRequest->result,"\"camera_view_distance\":600") && strstr(cameraRequest->result,"\"state_verified\":true"));
    CameraViewPair stale{2250.f,1800.f};assert(!cameraMove(currentView,stale,700.f));
    assert(!cameraMove(currentView,*currentView,NAN));
    assert(!cameraMove(currentView,*currentView,1701.f));
    cameraController[0x3e5]=1;cameraInvoked=false;cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"camera_view_unavailable"));cameraController[0x3e5]=0;
    cameraImage[0x98a028]^=1;cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"camera_view_profile_not_supported"));cameraImage[0x98a028]^=1;
    cameraRequest->command=16;
    cameraInvoked=false;cameraWriter.used=0;cameraRequest->session[0]='0';
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"stale_session") && *(float*)(cameraConfig+0x20)==1700.f);
    cameraImage[0x35ea31]^=1;cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"camera_profile_not_supported"));
    HeapFree(GetProcessHeap(),0,cameraRequest);VirtualFree(cameraImage,0,MEM_RELEASE);
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
    // Captured constructor default and independent game encoder/decoder vectors.
    *(uint32_t*)encrypted=0x6427b939u;
    int skin=-1;assert(skinValue(encrypted,&skin) && skin==0);
    IndependentContext context{};context.player=player;context.stack=stack;
    assert(objectLaneModel("SRU_OrderMinionMelee") && objectLaneModel("SRU_ChaosMinionSuper"));
    assert(!objectLaneModel("SRU_Blue") && !objectLaneModel("YellowTrinket") && !objectLaneModel("Ashe"));
    assert(objectWardModel("YellowTrinket") && !objectWardModel("TestCubeRender10Vision"));
    BYTE wardObject[0x700]{},ownerWrapper[64]{};
    *(BYTE**)(wardObject+Game_owner_offset)=ownerWrapper;*(BYTE**)ownerWrapper=player;
    assert(objectLocalWard(&context,wardObject));
    *(BYTE**)ownerWrapper=stack;assert(!objectLocalWard(&context,wardObject));
    *(BYTE**)(wardObject+Game_owner_offset)=nullptr;assert(!objectLocalWard(&context,wardObject));
    assert(!objectReadable((void*)1,64));
    assert(independentWriteSkin(&context,104));assert(skinValue(encrypted,&skin) && skin==104);
    assert(*(int*)(stack+0x38)==104);
    assert(independentWriteSkin(&context,0));
    assert(skinValue(encrypted,&skin) && skin==0);
    for(int value=0;value<=10000;value++){
        memset(encrypted+4,0xa5,20);
        assert(independentWriteSkin(&context,value));assert(skinValue(encrypted,&skin) && skin==value);
        for(int n=4;n<24;n++)assert(encrypted[n]==0xa5); // Adjacent game fields must survive.
    }
    assert(!independentWriteSkin(&context,-1) && !independentWriteSkin(&context,10001));
    *(uint32_t*)encrypted=skinEncoded(10001);assert(!skinValue(encrypted,&skin));
    assert(!independentWriteSkin(&context,0)); // Refuse an invalid previous value.
    *(uint32_t*)encrypted=0x6427b939u;assert(independentWriteSkin(&context,0));
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
