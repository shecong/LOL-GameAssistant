// Independent core extracted from League Skin Studio 1.3.0.
#include <windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdarg>
#include <cstdlib>
constexpr DWORD Protocol=1, Magic=0x4c53434b;
constexpr UINT BridgeMessage=WM_APP+0x532;
constexpr size_t ReplySize=1024*1024;
struct Request {
    DWORD magic,version,command,pid;
    uint64_t nonce,deadline;
    volatile LONG phase; // queued=0, running=1, done=2, cancelled=3
    int index,gear;
    char session[32],result[ReplySize];
};
struct Writer {char* buffer;size_t used,capacity;bool failed;};
static void append(Writer* w,const char* format,...) {
    if(w->failed) return;
    va_list args;va_start(args,format);
    int n=vsnprintf(w->buffer+w->used,w->capacity-w->used,format,args);va_end(args);
    if(n<0 || (size_t)n>=w->capacity-w->used) {w->failed=true;return;}w->used+=n;
}
static void quoted(Writer* w,const char* value) {
    append(w,"\"");
    for(const unsigned char* c=(const unsigned char*)value;*c;c++) {
        if(*c=='"'||*c=='\\') append(w,"\\%c",*c);
        else if(*c<32) append(w,"\\u%04x",*c);
        else append(w,"%c",*c);
    }
    append(w,"\"");
}
static void fail(Request* r,const char* error,const char* stage="validation",bool invoked=false) {
    sprintf_s(r->result,"{\"ok\":false,\"error\":\"%s\",\"stage\":\"%s\",\"invoked\":%s,\"request_id\":\"%016llx\"}",error,stage,invoked?"true":"false",r->nonce);
}
static void names(DWORD pid,uint64_t nonce,wchar_t* mapping,wchar_t* event) {
    swprintf_s(mapping,128,L"Local\\LeagueSkinCore.v1.Request.%lu.%016llx",pid,nonce);
    swprintf_s(event,128,L"Local\\LeagueSkinCore.v1.Result.%lu.%016llx",pid,nonce);
}
static uint64_t digest(uint64_t h,const void* data,size_t size) {
    for(size_t i=0;i<size;i++) {h^=((const unsigned char*)data)[i];h*=0x100000001b3ULL;}return h;
}
static uint64_t modelHash(const char* name) {return digest(0xcbf29ce484222325ULL,name,strlen(name)+1);}
static bool readText(const char* data,size_t size,char* out,size_t capacity) {
    if(!data || size>=capacity) return false;
    if(size && memchr(data,0,size)) return false;
    if(size && !MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,data,(int)size,nullptr,0)) return false;
    memcpy(out,data,size);out[size]=0;return true;
}
static bool cstring(const char* data,char* out,size_t capacity) {
    if(!data) return false;size_t size=0;while(size<capacity && data[size]) size++;
    return readText(data,size,out,capacity);
}
static bool stlString(BYTE* object,char* out,size_t capacity) {
    size_t size=*(size_t*)(object+16),reserved=*(size_t*)(object+24);
    if(size>reserved || reserved>1024*1024) return false;
    return readText(reserved<16?(char*)object:*(const char**)object,size,out,capacity);
}
static bool span(BYTE* first,BYTE* end,BYTE* capacity,size_t stride,size_t max,int* count) {
    uintptr_t a=(uintptr_t)first,b=(uintptr_t)end,c=(uintptr_t)capacity;
    if(!a || b<a || c<b || (b-a)%stride || (c-a)%stride || (c-a)/stride>max) return false;
    *count=(int)((b-a)/stride);return *count>0;
}
#include "standalone_core.h"
static bool claim(Request* r,DWORD pid,uint64_t nonce) {
    return r->magic==Magic && r->version==Protocol && r->pid==pid && r->nonce==nonce &&
        (r->command>=6 && r->command<=10) && memchr(r->session,0,sizeof(r->session)) && InterlockedCompareExchange(&r->phase,1,0)==0;
}
static void process(Request* r) { independentProcess(r); }
static void finishRequest(Request* r) {
    if(GetTickCount64()>r->deadline) fail(r,"request_expired");else process(r);
    InterlockedExchange(&r->phase,2);
}
#ifndef BUILD_HOST
extern "C" __declspec(dllexport) LRESULT CALLBACK StudioHookProc(int code,WPARAM removed,LPARAM data) {
    if(code>=0 && removed==PM_REMOVE) {
        MSG* message=(MSG*)data;
        if(message->message==BridgeMessage && message->wParam==Magic) {
            uint64_t nonce=(uint64_t)message->lParam;wchar_t mapping[128],event[128];names(GetCurrentProcessId(),nonce,mapping,event);
            HANDLE file=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mapping),signal=OpenEventW(EVENT_MODIFY_STATE,FALSE,event);
            if(file && signal) {
                auto r=(Request*)MapViewOfFile(file,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request));
                if(r) {
                    if(claim(r,GetCurrentProcessId(),nonce)) {
                        finishRequest(r);SetEvent(signal);
                    }
                    UnmapViewOfFile(r);
                }
            }
            if(file) CloseHandle(file);if(signal) CloseHandle(signal);
            message->message=WM_NULL;message->wParam=0;message->lParam=0;
        }
    }
    return CallNextHookEx(nullptr,code,removed,data);
}
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID) {return TRUE;}
#else
static int error(const char* stage,DWORD code) {printf("{\"ok\":false,\"error\":\"%s\",\"stage\":\"transport\",\"win32\":%lu}\n",stage,code);return 1;}
static bool integer(const char* text,int minimum,int maximum,int* result) {
    char* end=nullptr;long value=strtol(text,&end,10);
    if(end==text || *end || value<minimum || value>maximum) return false;*result=(int)value;return true;
}
int main(int argc,char** argv) {
    DWORD command=99;int index=-1,gear=-1;const char* session="";
    if(argc==2 && !strcmp(argv[1],"status")) command=6;
    if(argc==2 && !strcmp(argv[1],"catalog")) command=7;
    if(argc==2 && !strcmp(argv[1],"inventory")) command=10;
    if(argc==3 && !strcmp(argv[1],"inventory")){command=10;if(!integer(argv[2],0,9999,&index))return error("invalid_entry",ERROR_INVALID_PARAMETER);}
    if(argc==5 && !strcmp(argv[1],"entry")){command=8;session=argv[2];if(!integer(argv[3],0,999,&index)||!integer(argv[4],-1,127,&gear))return error("invalid_entry",ERROR_INVALID_PARAMETER);}
    if(argc==3 && !strcmp(argv[1],"restore")){command=9;session=argv[2];}
    if(command==99 || strlen(session)>=32) return error("invalid_request",ERROR_INVALID_PARAMETER);
    if(command==8 || command==9) {
        if(strlen(session)!=16) return error("invalid_session",ERROR_INVALID_PARAMETER);
        for(const char* p=session;*p;p++) if(!((*p>='0'&&*p<='9')||(*p>='a'&&*p<='f'))) return error("invalid_session",ERROR_INVALID_PARAMETER);
    }
    HWND window=FindWindowW(nullptr,L"League of Legends (TM) Client");if(!window) return error("game_window_not_found",ERROR_NOT_FOUND);
    DWORD pid=0;DWORD thread=GetWindowThreadProcessId(window,&pid);uint64_t nonce=0;
    if(BCryptGenRandom(nullptr,(PUCHAR)&nonce,sizeof(nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0) return error("nonce_failed",ERROR_INVALID_DATA);
    wchar_t mapping[128],event[128];names(pid,nonce,mapping,event);
    HANDLE file=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Request),mapping);
    if(!file) return error("create_mapping_failed",GetLastError());
    if(GetLastError()==ERROR_ALREADY_EXISTS) {CloseHandle(file);return error("channel_collision",ERROR_ALREADY_EXISTS);}
    HANDLE signal=CreateEventW(nullptr,TRUE,FALSE,event);auto r=(Request*)MapViewOfFile(file,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request));
    if(!signal || !r) {DWORD code=GetLastError();if(signal) CloseHandle(signal);CloseHandle(file);return error("create_channel_failed",code);}
    r->magic=Magic;r->version=Protocol;r->command=command;r->pid=pid;r->nonce=nonce;r->deadline=GetTickCount64()+5000;
    r->index=index;r->gear=gear;strcpy_s(r->session,session);
    wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);wchar_t* slash=wcsrchr(path,L'\\');HMODULE dll=nullptr;HHOOK hook=nullptr;int result=1;
    if(slash) {wcscpy_s(slash+1,32768-(slash+1-path),L"league-skin-core.dll");dll=LoadLibraryW(path);}
    if(!dll) result=error("load_own_bridge_failed",GetLastError());
    else {
        auto callback=(HOOKPROC)GetProcAddress(dll,"StudioHookProc");hook=callback?SetWindowsHookExW(WH_GETMESSAGE,callback,dll,thread):nullptr;
        if(!hook) result=error("standard_windows_hook_rejected",GetLastError());
        else {
            bool sent=PostThreadMessageW(thread,BridgeMessage,Magic,(LPARAM)nonce)!=FALSE;DWORD wait=sent?WaitForSingleObject(signal,5500):WAIT_FAILED;
            if(wait==WAIT_OBJECT_0 && r->phase==2 && r->result[0]) {printf("%s\n",r->result);result=0;}
            else {LONG phase=InterlockedCompareExchange(&r->phase,3,0);result=error(phase==1?"request_outcome_unknown":"request_expired",ERROR_TIMEOUT);}
        }
    }
    if(hook) UnhookWindowsHookEx(hook);if(dll) FreeLibrary(dll);UnmapViewOfFile(r);CloseHandle(signal);CloseHandle(file);return result;
}
#endif
