#pragma once
static void updateProbe(Request* r) {
    // Only PE headers and the first executable section, never game heap/state.
    __try {
        BYTE* base=(BYTE*)GetModuleHandleW(nullptr);
        auto dos=(IMAGE_DOS_HEADER*)base;
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE || dos->e_lfanew<=0 || dos->e_lfanew>1048576){fail(r,"probe_invalid_pe");return;}
        auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE || nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64){fail(r,"probe_invalid_pe");return;}
        auto sections=IMAGE_FIRST_SECTION(nt);IMAGE_SECTION_HEADER* text=nullptr;
        for(int n=0;n<nt->FileHeader.NumberOfSections;n++)if(sections[n].Characteristics&IMAGE_SCN_MEM_EXECUTE){text=&sections[n];break;}
        if(!text || !text->Misc.VirtualSize || text->Misc.VirtualSize>64*1024*1024 ||
            (uint64_t)text->VirtualAddress+text->Misc.VirtualSize>nt->OptionalHeader.SizeOfImage){fail(r,"probe_invalid_text");return;}
        Writer w{r->result,0,sizeof(r->result),false};
        append(&w,"{\"ok\":true,\"independent\":true,\"original_required\":false,\"read_only\":true,\"pid\":%lu,\"timestamp\":%lu,\"image_size\":%lu,\"code_rva\":%lu,\"code_size\":%lu",
            GetCurrentProcessId(),nt->FileHeader.TimeDateStamp,nt->OptionalHeader.SizeOfImage,text->VirtualAddress,text->Misc.VirtualSize);
        if(r->command==10) {
            constexpr DWORD chunk=65536;
            uint64_t offset=(uint64_t)(unsigned)r->index*chunk;
            if(offset>=text->Misc.VirtualSize){fail(r,"probe_invalid_page");return;}
            DWORD size=(DWORD)((text->Misc.VirtualSize-offset)<chunk?(text->Misc.VirtualSize-offset):chunk);
            append(&w,",\"page\":%d,\"hex\":\"",r->index);
            const char* digits="0123456789abcdef";const BYTE* data=base+text->VirtualAddress+offset;
            if(w.used+size*2+4>=w.capacity){fail(r,"probe_page_too_large");return;}
            for(DWORD n=0;n<size;n++){BYTE value=data[n];w.buffer[w.used++]=digits[value>>4];w.buffer[w.used++]=digits[value&15];}
            w.buffer[w.used]=0;append(&w,"\"");
        }
        append(&w,"}");if(w.failed)fail(r,"probe_reply_too_large");
    } __except(EXCEPTION_EXECUTE_HANDLER) {fail(r,"probe_code_read_denied");}
}
