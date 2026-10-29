// Read-only in-process evidence collection. No object methods are invoked.
static int objectFind(BYTE* base,const BYTE* bytes,const char* mask,size_t size,uintptr_t* first) {
    auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    auto sections=IMAGE_FIRST_SECTION(nt);int count=0;
    for(int s=0;s<nt->FileHeader.NumberOfSections;s++) {
        auto section=&sections[s];if(!(section->Characteristics&IMAGE_SCN_MEM_EXECUTE))continue;
        uintptr_t lo=section->VirtualAddress,hi=lo+section->Misc.VirtualSize;
        if(hi>runtimeGame.image_size || hi<lo)return -1;
        uintptr_t checked=0;bool readable=false;
        for(uintptr_t n=lo;n+size<=hi;n++) {
            if(n+size>checked) {
                MEMORY_BASIC_INFORMATION region{};
                if(!VirtualQuery(base+n,&region,sizeof(region)))return -1;
                checked=(BYTE*)region.BaseAddress+region.RegionSize-base;
                readable=region.State==MEM_COMMIT && !(region.Protect&(PAGE_NOACCESS|PAGE_GUARD));
                if(!readable){n=checked-1;continue;}
            }
            if(!readable || n+size>checked)continue;
            bool match=true;for(size_t j=0;j<size;j++)if(mask[j]=='x' && base[n+j]!=bytes[j]){match=false;break;}
            if(match){if(!count)*first=n;if(++count==2)return count;}
        }
    }
    return count;
}
static void objectHex(Writer* w,BYTE* pointer,size_t size) {
    append(w,"\"");for(size_t n=0;n<size;n++)append(w,"%02x",pointer[n]);append(w,"\"");
}
static void independentObjectProbe(Writer* w,IndependentContext* c) {
    const BYTE listPattern[]={0x48,0x8b,0x0d,0,0,0,0,0xe8,0,0,0,0,0x48,0x8b,0x0d,0,0,0,0,0xe8,0,0,0,0,0x48,0x8b,0x0d,0,0,0,0,0xe8,0,0,0,0,0x48,0x8b,0x0d,0,0,0,0,0x48,0x8b,0x01};
    const BYTE ownerPattern[]={0xe8,0,0,0,0,0x4c,0x3b,0,0x0f,0x94,0xc0};
    uintptr_t list=0,owner=0;
    int lists=objectFind(c->base,listPattern,"xxx????x????xxx????x????xxx????x????xxx????xxx",sizeof(listPattern),&list);
    int owners=objectFind(c->base,ownerPattern,"x????xx?xxx",sizeof(ownerPattern),&owner);
    append(w,"{\"ok\":true,\"probe_only\":true,\"independent\":true,\"original_required\":false,\"object_apply_available\":false,\"pid\":%lu,\"list_matches\":%d,\"owner_matches\":%d",GetCurrentProcessId(),lists,owners);
    if(owners==1) {
        append(w,",\"owner_call_context_rva\":%llu,\"owner_call_context\":",(uint64_t)(owner-64));objectHex(w,c->base+owner-64,128);
        intptr_t target=(intptr_t)owner+5+*(int32_t*)(c->base+owner+1);
        if(target>0 && target+128<(intptr_t)runtimeGame.image_size){
            append(w,",\"owner_candidate_rva\":%llu,\"owner_code\":",(uint64_t)target);objectHex(w,c->base+target,128);
            if(c->base[target+7]==0xe9){intptr_t next=target+12+*(int32_t*)(c->base+target+8);
                if(next>0 && next+256<(intptr_t)runtimeGame.image_size){append(w,",\"owner_accessor_rva\":%llu,\"owner_accessor_code\":",(uint64_t)next);objectHex(w,c->base+next,256);}}
        }
    }
    if(lists==1) {
        intptr_t slot=(intptr_t)list+7+*(int32_t*)(c->base+list+3);
        if(slot>0 && slot+8<(intptr_t)runtimeGame.image_size) {
            BYTE* manager=*(BYTE**)(c->base+slot);
            if(manager) {
                BYTE** objects=*(BYTE***)(manager+8);int count=*(int*)(manager+0x10);
                if(objects && count>=0 && count<=10000) {
                    append(w,",\"list_candidate_slot\":%llu,\"list_count\":%d,\"objects\":[",(uint64_t)slot,count);int emitted=0;
                    for(int n=0;n<count && n<256;n++) {
                        BYTE* object=objects[n];char model[64];if(!object || !cstring(*(const char**)(object+runtimeGame.stack_offset+0x18),model,sizeof(model)))continue;
                        if(emitted++)append(w,",");append(w,"{\"model\":");quoted(w,model);
                        append(w,",\"base_skin\":%d,\"classifier_candidates\":[",*(int*)(object+runtimeGame.stack_offset+0x38));
                        BYTE** table=*(BYTE***)object;
                        for(int j=0;j<2;j++) {
                            if(j)append(w,",");BYTE* function=table[j?0xf1:0xf0];intptr_t rva=function-c->base;
                            append(w,"{\"slot\":%d,\"rva\":%lld",j?0xf1:0xf0,(long long)rva);
                            if(rva>0 && rva+64<(intptr_t)runtimeGame.image_size){append(w,",\"code\":");objectHex(w,function,64);}append(w,"}");
                        }
                        append(w,"],\"descriptor_getter_rva\":%lld",(long long)(table[0x3e]-c->base));
                        intptr_t getter=table[0x3e]-c->base;
                        if(getter>0 && getter+64<(intptr_t)runtimeGame.image_size){append(w,",\"descriptor_getter_code\":");objectHex(w,table[0x3e],64);}
                        const BYTE getterBytes[]={0x48,0x8b,0x81,0xf0,0x35,0,0,0x48,0x85,0xc0,0x74,0x05,0x48,0x8b,0x40,0x28,0xc3,0xc3};
                        if(getter>0 && getter+sizeof(getterBytes)<runtimeGame.image_size && !memcmp(table[0x3e],getterBytes,sizeof(getterBytes))) {
                            BYTE* descriptor=*(BYTE**)(object+0x35f0);if(descriptor)descriptor=*(BYTE**)(descriptor+0x28);
                            if(descriptor)append(w,",\"type_flags_candidate\":%u,\"slot_f0_result_candidate\":%s",*(unsigned*)(descriptor+0x60),(descriptor[0x60]&2)?"true":"false");
                        }
                        BYTE* candidateOwner=*(BYTE**)(object+runtimeGame.owner_offset);
                        append(w,",\"owner_present_candidate\":%s,\"local_owner_candidate\":%s",candidateOwner?"true":"false",candidateOwner==c->player?"true":"false");
                        if(!strcmp(model,"YellowTrinket")) {
                            append(w,",\"ownership_field\":");objectHex(w,object+runtimeGame.owner_offset,64);
                            MEMORY_BASIC_INFORMATION region{};
                            if(candidateOwner && VirtualQuery(candidateOwner,&region,sizeof(region)) && region.State==MEM_COMMIT && !(region.Protect&(PAGE_GUARD|PAGE_NOACCESS)) && (uintptr_t)candidateOwner+64<=(uintptr_t)region.BaseAddress+region.RegionSize){append(w,",\"owner_head\":");objectHex(w,candidateOwner,64);append(w,",\"owner_wrapper_local_candidate\":%s",*(BYTE**)candidateOwner==c->player?"true":"false");}
                        }
                        append(w,"}");
                    }
                    append(w,"]");
                }
            }
        }
    }
    append(w,",\"player_model\":");quoted(w,c->hero);
    append(w,",\"classification_methods\":[");BYTE** playerTable=*(BYTE***)c->player;
    if(lists==1){intptr_t slot=list+7+*(int32_t*)(c->base+list+3);BYTE* manager=*(BYTE**)(c->base+slot);
        if(manager){BYTE** objects=*(BYTE***)(manager+8);int count=*(int*)(manager+0x10);
            for(int n=0;objects && n<count && count<=10000;n++){char model[64];
                if(objects[n] && cstring(*(const char**)(objects[n]+runtimeGame.stack_offset+0x18),model,sizeof(model)) && !strcmp(model,"SRU_OrderMinionMelee")){playerTable=*(BYTE***)objects[n];break;}}}}
    for(int j=0xe0;j<0x110;j++) {
        if(j!=0xe0)append(w,",");intptr_t method=playerTable[j]-c->base;
        append(w,"{\"slot\":%d,\"rva\":%lld",j,(long long)method);
        if(method>0 && method+64<(intptr_t)runtimeGame.image_size){append(w,",\"code\":");objectHex(w,playerTable[j],64);}append(w,"}");
    }
    append(w,"]");
    append(w,",\"player_address\":%llu,\"team_index\":%u}",(uint64_t)c->player,c->player[Game_team_offset]);
}
