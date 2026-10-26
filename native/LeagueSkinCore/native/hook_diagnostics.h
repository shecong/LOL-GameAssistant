#pragma once
// Independent diagnostic channel: leaves the request ABI unchanged. No game memory is inspected.
struct HookTrace {
    DWORD magic,version,pid,thread;
    uint64_t nonce;
    volatile LONG callbacks,messages,stage,win32;
};
static void hookTraceName(wchar_t (&name)[128],DWORD pid,DWORD thread) {
    swprintf_s(name,L"Local\\LeagueSkinCore.HookTrace.v1.%lu.%lu",pid,thread);
}
static const char* hookStage(LONG stage) {
    switch(stage) {
    case 1:return "callback_entered";case 2:return "message_received";
    case 3:return "request_open_failed";case 4:return "signal_open_failed";
    case 5:return "request_map_failed";case 6:return "claim_rejected";
    case 7:return "request_claimed";case 8:return "request_completed";
    case 9:return "signal_set_failed";default:return "not_observed";
    }
}
#ifdef BUILD_HOST
static HookTrace* activeHookTrace=nullptr;
struct HookTraceOwner {
    HANDLE file=nullptr;HookTrace* trace=nullptr;
    HookTraceOwner(DWORD pid,DWORD thread,uint64_t nonce) {
        wchar_t name[128];hookTraceName(name,pid,thread);
        file=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(HookTrace),name);
        if(!file)return;
        // Another request may own this thread's trace. Never overwrite its evidence.
        if(GetLastError()==ERROR_ALREADY_EXISTS) {CloseHandle(file);file=nullptr;return;}
        trace=(HookTrace*)MapViewOfFile(file,FILE_MAP_ALL_ACCESS,0,0,sizeof(HookTrace));
        if(!trace)return;
        trace->magic=Magic;trace->version=1;trace->pid=pid;trace->thread=thread;trace->nonce=nonce;
        activeHookTrace=trace;
    }
    ~HookTraceOwner() {
        if(activeHookTrace==trace)activeHookTrace=nullptr;
        if(trace)UnmapViewOfFile(trace);if(file)CloseHandle(file);
    }
};
#else
struct HookTraceObserver {
    HANDLE file=nullptr;HookTrace* trace=nullptr;
    HookTraceObserver() {
        DWORD previous=GetLastError();wchar_t name[128];
        hookTraceName(name,GetCurrentProcessId(),GetCurrentThreadId());
        file=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
        if(file)trace=(HookTrace*)MapViewOfFile(file,FILE_MAP_ALL_ACCESS,0,0,sizeof(HookTrace));
        if(trace && trace->magic==Magic && trace->version==1 && trace->pid==GetCurrentProcessId() && trace->thread==GetCurrentThreadId()) {
            InterlockedIncrement(&trace->callbacks);InterlockedCompareExchange(&trace->stage,1,0);
        } else if(trace) {UnmapViewOfFile(trace);trace=nullptr;}
        SetLastError(previous);
    }
    bool matches(uint64_t nonce) const {return trace && trace->nonce==nonce;}
    void mark(LONG stage,DWORD code=0) {
        if(trace) {InterlockedExchange(&trace->win32,(LONG)code);InterlockedExchange(&trace->stage,stage);}
    }
    ~HookTraceObserver() {
        DWORD previous=GetLastError();if(trace)UnmapViewOfFile(trace);if(file)CloseHandle(file);SetLastError(previous);
    }
};
#endif
