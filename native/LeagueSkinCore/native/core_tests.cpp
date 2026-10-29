#define BUILD_HOST
#define main unused_host_main
#include "core.cpp"
#undef main
#include <cassert>
#include <initializer_list>
#include "object_skin_rules.h"
#include "runtime_test_support.h"
int main() {
    {
        BorderContext context{};strcpy_s(context.tier,"CHALLENGER");strcpy_s(context.mode,"rank");
        assert(!strcmp(borderCurrentId(&context),"CHALLENGER"));
        strcpy_s(context.mode,"ranked");assert(!strcmp(borderCurrentId(&context),"original"));
        strcpy_s(context.mode,"prestige");assert(!strcmp(borderCurrentId(&context),"original"));
    }
    assert(!borderDivisionValid(0) && !borderDivisionValid(-1) && !borderDivisionValid(5));
    for(int division=1;division<=4;division++)assert(borderDivisionValid(division));
    {
        auto image=(BYTE*)VirtualAlloc(nullptr,0x1f0c000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);assert(image);
        BYTE card[0x928]{};BorderCard result{};
        *(BYTE**)card=image+0x18dfdb8;card[0x10]=1;
        assert(borderCard(image,card,1,&result,false) && result.display==card+0x1f0 && result.kind==1);
        assert(!borderCard(image,card,2,&result,false));
        assert(!borderCard(image,card,1,&result,true)); // Unbound images are never a write target.
        *(BYTE**)card=image+0x18dff48;
        assert(borderCard(image,card,1,&result,false) && result.display==card+0xf8 && result.kind==2);
        *(BYTE**)card=image+0x18dff50;assert(!borderCard(image,card,1,&result,false));
        VirtualFree(image,0,MEM_RELEASE);
    }
    {
        auto resource=(BYTE*)VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);assert(resource);
        *(LONG*)(resource+0x1c)=7;assert(borderRefValid(resource));BYTE* field=nullptr;
        borderSetRef(nullptr,&field,resource);assert(field==resource && *(LONG*)(resource+0x1c)==8);
        InterlockedDecrement((LONG*)(resource+0x1c));
        DWORD previous=0;assert(VirtualProtect(resource,4096,PAGE_READONLY,&previous));assert(!borderRefValid(resource));
        assert(VirtualProtect(resource,4096,PAGE_READWRITE,&previous));*(LONG*)(resource+0x1c)=0;assert(!borderRefValid(resource));
        VirtualFree(resource,0,MEM_RELEASE);
        struct GameText {const char* data;unsigned size,capacity;};GameText text{"GOLD",4,4};char copy[32];
        assert(borderText((BYTE*)&text,copy,sizeof(copy)) && !strcmp(copy,"GOLD"));
        text.size=0xffffffff;assert(!borderText((BYTE*)&text,copy,sizeof(copy)));
    }
    assert(gameExecutable(L"C:\\Games\\League of Legends.exe"));
    assert(gameExecutable(L"LEAGUE OF LEGENDS.EXE"));
    assert(!gameExecutable(L"LeagueClient.exe") && !gameExecutable(L"League of Legends.exe.bak"));
    {
        HWND renamed=CreateWindowExW(0,L"STATIC",L"Renamed game window diagnostic fixture",WS_OVERLAPPEDWINDOW,
            0,0,640,480,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);assert(renamed);
        ShowWindow(renamed,SW_SHOWNOACTIVATE);
        GameWindowDiscovery info{};info.pids[0]=GetCurrentProcessId();info.processes=1;
        collectGameWindow(renamed,(LPARAM)&info);
        assert(selectGameWindow(info) && info.selected.window==renamed && info.selected.thread==GetCurrentThreadId());
        assert(!wcscmp(info.selected.title,L"Renamed game window diagnostic fixture"));
        assert(!wcscmp(info.selected.className,L"Static"));
        const UINT routingMessage=WM_APP+0x633;MSG routed{};
        assert(PostThreadMessageW(GetCurrentThreadId(),routingMessage,1,2));
        assert(!PeekMessageW(&routed,renamed,routingMessage,routingMessage,PM_REMOVE));
        assert(PeekMessageW(&routed,(HWND)-1,routingMessage,routingMessage,PM_REMOVE) && !routed.hwnd);
        assert(postGameRequest(renamed,GetCurrentProcessId(),GetCurrentThreadId(),routingMessage,3,4));
        assert(PeekMessageW(&routed,renamed,routingMessage,routingMessage,PM_REMOVE));
        assert(routed.hwnd==renamed && routed.wParam==3 && routed.lParam==4);
        assert(!postGameRequest(renamed,GetCurrentProcessId()+1,GetCurrentThreadId(),routingMessage,3,4));
        assert(GetLastError()==ERROR_INVALID_WINDOW_HANDLE);
        GameWindowDiscovery unrelated{};unrelated.pids[0]=0;unrelated.processes=1;
        collectGameWindow(renamed,(LPARAM)&unrelated);assert(unrelated.observed==0);
        ShowWindow(renamed,SW_HIDE);
        info={};info.pids[0]=GetCurrentProcessId();info.processes=1;
        collectGameWindow(renamed,(LPARAM)&info);assert(!selectGameWindow(info));
        assert(!strcmp(info.state,"no_usable_game_window"));DestroyWindow(renamed);
    }
    {
        WNDCLASSW gameClass{};gameClass.lpfnWndProc=DefWindowProcW;gameClass.hInstance=GetModuleHandleW(nullptr);
        gameClass.lpszClassName=L"RiotWindowClass";assert(RegisterClassW(&gameClass));
        HWND owner=CreateWindowExW(0,L"STATIC",L"Owner fixture",WS_OVERLAPPEDWINDOW,0,0,100,100,nullptr,nullptr,gameClass.hInstance,nullptr);
        assert(owner);
        HWND hiddenGame=CreateWindowExW(0,L"RiotWindowClass",L"League of Legends (TM) Client",WS_POPUP,
            0,0,1,1,owner,nullptr,gameClass.hInstance,nullptr);assert(hiddenGame);
        GameWindowDiscovery info{};info.pids[0]=GetCurrentProcessId();info.processes=1;
        collectGameWindow(hiddenGame,(LPARAM)&info);
        assert(selectGameWindow(info) && info.selected.window==hiddenGame);
        assert(!info.selected.visible && info.selected.hasOwner && info.selected.width<64);
        assert(!strcmp(info.selected.reason,"recognized_game_class"));
        GameWindowDiscovery unrelated{};unrelated.pids[0]=0;unrelated.processes=1;
        collectGameWindow(hiddenGame,(LPARAM)&unrelated);assert(!selectGameWindow(unrelated));
        DestroyWindow(hiddenGame);DestroyWindow(owner);UnregisterClassW(L"RiotWindowClass",gameClass.hInstance);
    }
    {
        GameWindowDiscovery info{};info.processes=2;info.observed=2;
        info.windows[0].pid=1;info.windows[0].area=100;
        info.windows[1].pid=2;info.windows[1].area=200;
        assert(!selectGameWindow(info) && !strcmp(info.state,"ambiguous_game_processes"));
        info.windows[1].pid=1;info.windows[1].area=100;
        assert(!selectGameWindow(info) && !strcmp(info.state,"ambiguous_game_windows"));
        info.windows[1].area=200;assert(selectGameWindow(info) && info.selected.area==200);
        info.windows[0].priority=2;assert(selectGameWindow(info) && info.selected.area==100);
        info.overflow=true;assert(!selectGameWindow(info));
    }
    // Exercise the real DLL callback only with invalid requests: never executes game functions.
    {
        wchar_t dllPath[32768];GetModuleFileNameW(nullptr,dllPath,32768);
        wchar_t* dllName=wcsrchr(dllPath,L'\\');assert(dllName);wcscpy_s(dllName+1,32768-(dllName+1-dllPath),L"league-skin-core.dll");
        HMODULE bridge=LoadLibraryW(dllPath);assert(bridge);
        auto callback=(HOOKPROC)GetProcAddress(bridge,"StudioHookProc");assert(callback);
        auto bridgeVersion=(DWORD(__cdecl*)())GetProcAddress(bridge,"StudioBridgeProtocol");assert(bridgeVersion && bridgeVersion()==Protocol);
        DWORD pid=GetCurrentProcessId(),thread=GetCurrentThreadId();uint64_t nonce=0x123456789abcdef0ULL;
        HookTraceOwner trace(pid,thread,nonce);assert(trace.trace);
        HookTraceOwner collision(pid,thread,nonce+1);assert(!collision.trace && activeHookTrace==trace.trace);
        MSG message{};message.message=WM_NULL;
        callback(HC_ACTION,PM_REMOVE,(LPARAM)&message);
        assert(trace.trace->callbacks==1 && trace.trace->messages==0 && trace.trace->stage==1);
        message.message=WM_APP+0x532;message.wParam=Magic;message.lParam=(LPARAM)nonce;
        callback(HC_ACTION,PM_REMOVE,(LPARAM)&message);
        assert(message.message==WM_APP+0x532 && trace.trace->messages==0); // Never consume another protocol's request.
        auto invoke=[&]() {message.message=BridgeMessage;message.wParam=Magic;message.lParam=(LPARAM)nonce;callback(HC_ACTION,PM_REMOVE,(LPARAM)&message);};
        invoke();assert(trace.trace->stage==3 && trace.trace->win32==ERROR_FILE_NOT_FOUND);
        wchar_t mapping[128],event[128];names(pid,nonce,mapping,event);
        HANDLE file=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Request),mapping);assert(file);
        invoke();assert(trace.trace->stage==4 && trace.trace->win32==ERROR_FILE_NOT_FOUND);
        HANDLE signal=CreateEventW(nullptr,TRUE,FALSE,event);assert(signal);
        auto request=(Request*)MapViewOfFile(file,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request));assert(request);
        request->magic=0;request->nonce=nonce;
        invoke();assert(trace.trace->stage==6 && request->phase==0 && !request->result[0]);
        assert(trace.trace->callbacks==5 && trace.trace->messages==3);
        HWND routingWindow=CreateWindowExW(0,L"STATIC",L"Hook routing fixture",WS_OVERLAPPEDWINDOW,
            0,0,640,480,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);assert(routingWindow);
        HHOOK installed=SetWindowsHookExW(WH_GETMESSAGE,callback,bridge,thread);assert(installed);
        assert(postGameRequest(routingWindow,pid,thread,BridgeMessage,Magic,(LPARAM)nonce));
        MSG delivered{};assert(PeekMessageW(&delivered,routingWindow,BridgeMessage,BridgeMessage,PM_REMOVE));
        assert(trace.trace->messages==4 && trace.trace->stage==6 && request->phase==0);
        assert(delivered.message==WM_NULL);assert(UnhookWindowsHookEx(installed));DestroyWindow(routingWindow);
        UnmapViewOfFile(request);CloseHandle(signal);CloseHandle(file);FreeLibrary(bridge);
    }
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
    runtimeTestHeaders(cameraImage,(DWORD)Game_image_size);
    memcpy(cameraImage+CameraLookupRva,CameraLookup,sizeof(CameraLookup));
    memcpy(cameraImage+CameraDefaultRva,CameraDefault,sizeof(CameraDefault));
    alignas(8) BYTE cameraConfig[0xa0]{};*(float*)(cameraConfig+0x20)=600.25f;*(float*)(cameraConfig+0x28)=2250.f;
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
    memcpy(cameraImage+CameraViewLookupRva,CameraViewLookup,sizeof(CameraViewLookup));
    memcpy(cameraImage+CameraViewInitRva,CameraViewInit,sizeof(CameraViewInit));
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
    assert(!cameraMove(currentView,*currentView,10001.f));
    cameraRequest->index=cameraBits(2250.f);cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(currentView->current==2250.f && currentView->target==2250.f && strstr(cameraRequest->result,"\"camera_default_view_distance\":2250"));
    cameraController[0x3e5]=1;cameraInvoked=false;cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"camera_view_unavailable"));cameraController[0x3e5]=0;
    cameraImage[CameraViewInitRva]^=1;cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"profile_signature_missing"));cameraImage[CameraViewInitRva]^=1;
    cameraRequest->command=16;
    cameraInvoked=false;cameraWriter.used=0;cameraRequest->session[0]='0';
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"stale_session") && *(float*)(cameraConfig+0x20)==1700.f);
    cameraImage[CameraLookupRva]^=1;cameraWriter.used=0;
    independentCamera(cameraRequest,&cameraWriter,&cameraContext,&cameraInvoked);
    assert(!cameraInvoked && strstr(cameraRequest->result,"profile_signature_missing"));
    HeapFree(GetProcessHeap(),0,cameraRequest);VirtualFree(cameraImage,0,MEM_RELEASE);
    auto request=(Request*)HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,sizeof(Request));
    request->magic=Magic;request->version=Protocol;request->pid=42;request->nonce=99;
    for(int command=0;command<6;command++){request->command=command;assert(!claim(request,42,99));}
    request->command=6;assert(!claim(request,41,99));assert(!claim(request,42,98));
    request->version=1;assert(!claim(request,42,99));request->version=Protocol;
    assert(claim(request,42,99));assert(!claim(request,42,99));
    request->phase=0;request->deadline=0;assert(claim(request,42,99));finishRequest(request);
    assert(strstr(request->result,"request_expired") && strstr(request->result,"\"bridge_protocol\":2") && request->phase==2);
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
    for(const char* hero:{"Kayn","Lux","Sona"})for(int skin:{0,6,7,100})
        assert(independentDefaultGear(hero,skin,2)==2);
    for(const char* hero:{"Morgana","Tristana"}) {
        assert(independentDefaultGear(hero,80,-1)==3);
        assert(independentDefaultGear(hero,79,3)==-1 && independentDefaultGear(hero,81,3)==-1);
    }
    struct GearCase {const char* hero;int skin;};
    for(const auto& item:std::initializer_list<GearCase>{{"Ezreal",5},{"Leona",64},{"Caitlyn",22},{"Zeri",40},
        {"Thresh",19},{"Lillia",46},{"Aatrox",33},{"Evelynn",24},{"Viego",43}}) {
        assert(independentDefaultGear(item.hero,item.skin,-1)==0);
        assert(independentDefaultGear(item.hero,item.skin-1,0)==-1);
        assert(independentDefaultGear(item.hero,item.skin+1,0)==-1);
    }
    assert(independentDefaultGear("Ashe",76,2)==-1 && independentDefaultGear("MasterYi",116,2)==-1);
    assert(independentDefaultGear("Katarina",28,2)==-1 && independentDefaultGear("Katarina",29,2)==0 &&
        independentDefaultGear("Katarina",36,2)==0 && independentDefaultGear("Katarina",37,2)==-1);
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
