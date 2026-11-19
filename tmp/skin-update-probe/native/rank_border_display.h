#pragma once
struct BorderCard {BYTE* object{};BYTE* display{};BYTE* images[2]{};int kind{};};
static BorderCard borderCards[16];
static uintptr_t borderCardScanCursor=0;
static ULONGLONG borderCardScanAgain=0;
static bool borderCard(BYTE* base,BYTE* object,int slot,BorderCard* result,bool bound) {
    if(!borderReadable(object,0x2c0))return false;
    BYTE* vt=*(BYTE**)object;int kind=vt==base+0x18dfdb8?1:vt==base+0x18dff48?2:0;
    if(!kind || object[0x10]!=slot || !borderReadable(object,kind==1?0x928:0x2b8))return false;
    result->object=object;result->kind=kind;result->display=object+(kind==1?0x1f0:0xf8);
    if(!bound)return true;
    BYTE* display=result->display;
    for(int j=0;j<2;j++) {
        BYTE* wrapper=display+j*0x18;
        if(*(BYTE**)wrapper!=base+0x186efe0)return false;
        BYTE* handle=*(BYTE**)(wrapper+8);if(!handle || !borderReadable(handle,0x18))return false;
        auto unwrap=(BYTE*(__fastcall*)(BYTE*))(base+0x1182e00);
        BYTE* image=unwrap(wrapper);
        if(!image || !borderReadable(image,0x108) || !(*(unsigned*)(image+0x20)&0x1000))return false;
        result->images[j]=image;
    }
    return true;
}
static bool borderDisplaySignatures(BYTE* base) {
    const BYTE binding[]={0x48,0x89,0x5c,0x24,0x18,0x55,0x56,0x57,0x48,0x8b,0xec,0x48,0x83,0xec,0x50};
    const BYTE unwrap[]={0x48,0x83,0xec,0x38,0x48,0x8b,0x41,0x08,0x83,0x78,0x14,0x00};
    return !memcmp(base+0x9edd80,binding,sizeof(binding)) && !memcmp(base+0x1182e00,unwrap,sizeof(unwrap));
}
static int borderFindCards(BYTE* base,int slot,BorderCard* ready) {
    int count=0;
    for(auto& cached:borderCards) {
        BorderCard current{};
        if(cached.object && !borderCard(base,cached.object,slot,&current,false))cached={};
        if(cached.object && borderCard(base,cached.object,slot,&current,true))ready[count++]=current;
    }
    if(count || GetTickCount64()<borderCardScanAgain)return count;
    const ULONGLONG deadline=GetTickCount64()+180;size_t scanned=0;
    while(borderCardScanCursor<0x100000000ull && scanned<128ull*1024*1024 && GetTickCount64()<deadline) {
        MEMORY_BASIC_INFORMATION region{};if(!VirtualQuery((void*)borderCardScanCursor,&region,sizeof(region)))break;
        uintptr_t end=(uintptr_t)region.BaseAddress+region.RegionSize;if(end<=borderCardScanCursor)break;
        if(region.State==MEM_COMMIT && region.Type==MEM_PRIVATE && !(region.Protect&(PAGE_NOACCESS|PAGE_GUARD)) && (region.Protect&(PAGE_READWRITE|PAGE_WRITECOPY))) {
            uintptr_t lo=(borderCardScanCursor+7)&~7ull,hi=end<0x100000000ull?end:0x100000000ull;
            for(uintptr_t at=lo;at+8<=hi;at+=8) {
                BYTE* object=(BYTE*)at;BYTE* vt=*(BYTE**)object;
                if(vt==base+0x18dfdb8 || vt==base+0x18dff48) {
                    BorderCard candidate{};
                    if(borderCard(base,object,slot,&candidate,false))for(auto& cached:borderCards) {
                        if(cached.object==object)break;
                        if(!cached.object){cached=candidate;break;}
                    }
                }
                scanned+=8;
                if((scanned&0xfffff)==0 && (GetTickCount64()>=deadline || scanned>=128ull*1024*1024)) {
                    borderCardScanCursor=at+8;goto scanned_cards;
                }
            }
        }
        borderCardScanCursor=end;
    }
scanned_cards:
    if(borderCardScanCursor>=0x100000000ull){borderCardScanCursor=0;borderCardScanAgain=GetTickCount64()+500;}
    for(auto& cached:borderCards) {BorderCard current{};if(cached.object && borderCard(base,cached.object,slot,&current,true))ready[count++]=current;}
    return count;
}
static bool borderEmptySlot(BYTE* base) {
    BYTE* entity=base+0x1cc0990;BYTE* entries=*(BYTE**)(entity+8);int count=*(int*)(entity+0x10);
    if(count<0 || count>128 || (count && !borderReadable(entries,count*24)))return false;
    for(int n=0;n<count;n++)if(*(BYTE**)(entries+n*24)==base+0x1c932b0 && *(uint64_t*)(entries+n*24+0x10))return false;
    return true;
}
static bool borderRefreshCards(BYTE* base,int slot,BorderCard* cards,int count,BYTE* expected,bool ranked) {
    using Bind=void(__fastcall*)(BYTE*,BYTE);auto bind=(Bind)(base+0x9edd80);
    // The game's empty-component branch clears both ranked/prestige textures,
    // then the actual slot binds exactly the selected mode. This also removes
    // the previously active crest when switching between the two modes.
    bool verified=count>0;
    for(int n=0;n<count;n++) {
        BorderCard current{};if(!borderCard(base,cards[n].object,slot,&current,true))return false;
        bind(current.display,0);bind(current.display,(BYTE)slot);
        BorderCard after{};if(!borderCard(base,current.object,slot,&after,true))return false;
        int active=ranked?0:1;
        if(*(BYTE**)(after.images[active]+0x100)!=expected)verified=false;
        auto blank=(BYTE*(__fastcall*)())(base+0x110a390);BYTE* empty=*(BYTE**)blank();
        if(*(BYTE**)(after.images[1-active]+0x100)!=empty)verified=false;
    }
    return verified;
}
