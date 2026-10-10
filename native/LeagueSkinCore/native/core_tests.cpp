#define BUILD_HOST
#define main unused_host_main
#include "core.cpp"
#undef main
#include <cassert>
int main() {
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
    puts("Independent core: ownership, expiry, skin field, layer synchronization, restore and bounds passed.");
}
