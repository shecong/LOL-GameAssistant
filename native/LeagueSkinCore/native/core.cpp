// Independent core extracted from League Skin Studio 1.3.0.
#include <windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdarg>
#include <cstdlib>
constexpr DWORD Protocol=2, Magic=0x4c53434b;
// Older hook DLLs consume the v1 message even when they cannot open its request.
// A separate message and namespace prevent a resident older app claiming new work.
constexpr UINT BridgeMessage=WM_APP+0x533;
constexpr size_t ReplySize=1024*1024;
#include "hook_diagnostics.h"
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
    swprintf_s(mapping,128,L"Local\\LeagueSkinCore.v2.Request.%lu.%016llx",pid,nonce);
    swprintf_s(event,128,L"Local\\LeagueSkinCore.v2.Result.%lu.%016llx",pid,nonce);
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
        (r->command>=6 && r->command<=21) && memchr(r->session,0,sizeof(r->session)) && InterlockedCompareExchange(&r->phase,1,0)==0;
}
static void process(Request* r) { independentProcess(r); }
static void finishRequest(Request* r) {
    if(GetTickCount64()>r->deadline) fail(r,"request_expired","dispatch_deadline");else process(r);
    size_t length=strnlen_s(r->result,sizeof(r->result));
    if(length && length<sizeof(r->result) && r->result[length-1]=='}') {
        Writer writer{r->result,length-1,sizeof(r->result),false};append(&writer,",\"bridge_protocol\":%lu}",Protocol);
        if(writer.failed)fail(r,"reply_too_large","protocol_metadata");
    }
    InterlockedExchange(&r->phase,2);
}
#ifndef BUILD_HOST
extern "C" __declspec(dllexport) DWORD StudioBridgeProtocol() {return Protocol;}
extern "C" __declspec(dllexport) LRESULT CALLBACK StudioHookProc(int code,WPARAM removed,LPARAM data) {
    if(code<0)return CallNextHookEx(nullptr,code,removed,data);
    HookTraceObserver trace;
    if(code>=0 && removed==PM_REMOVE) {
        MSG* message=(MSG*)data;
        if(message->message==BridgeMessage && message->wParam==Magic) {
            uint64_t nonce=(uint64_t)message->lParam;wchar_t mapping[128],event[128];names(GetCurrentProcessId(),nonce,mapping,event);
            bool tracing=trace.matches(nonce);
            if(tracing) {InterlockedIncrement(&trace.trace->messages);trace.mark(2);}
            HANDLE file=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mapping);
            if(!file && tracing)trace.mark(3,GetLastError());
            HANDLE signal=OpenEventW(EVENT_MODIFY_STATE,FALSE,event);
            if(file && !signal && tracing)trace.mark(4,GetLastError());
            if(file && signal) {
                auto r=(Request*)MapViewOfFile(file,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request));
                if(!r && tracing)trace.mark(5,GetLastError());
                if(r) {
                    if(claim(r,GetCurrentProcessId(),nonce)) {
                        if(tracing)trace.mark(7);
                        finishRequest(r);
                        if(tracing)trace.mark(8);
                        if(!SetEvent(signal) && tracing)trace.mark(9,GetLastError());
                    } else if(tracing)trace.mark(6,ERROR_INVALID_DATA);
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
#include <tlhelp32.h>
#include <climits>
#include "game_window.h"
static DWORD diagnosticPid=0,diagnosticThread=0;
static uint64_t diagnosticNonce=0;
static ULONGLONG diagnosticStart=GetTickCount64();
static int error(const char* name,DWORD code,const char* stage="transport",LONG phase=-1) {
#ifdef _WIN64
    const char* architecture="x64";
#else
    const char* architecture="x86";
#endif
    printf("{\"ok\":false,\"error\":\"%s\",\"stage\":\"%s\",\"win32\":%lu,\"pid\":%lu,\"thread_id\":%lu,\"request_id\":\"%016llx\",\"phase\":%ld,\"host_architecture\":\"%s\",\"elapsed_ms\":%llu",
        name,stage,code,diagnosticPid,diagnosticThread,diagnosticNonce,phase,architecture,GetTickCount64()-diagnosticStart);
    if(activeHookTrace)printf(",\"hook_trace_available\":true,\"hook_callbacks\":%ld,\"hook_messages\":%ld,\"hook_stage\":\"%s\",\"hook_win32\":%ld",
        activeHookTrace->callbacks,activeHookTrace->messages,hookStage(activeHookTrace->stage),activeHookTrace->win32);
    printf(",\"message_route\":\"window\"");printWindowDiscovery();printf("}\n");return 1;
}
static bool integer(const char* text,int minimum,int maximum,int* result) {
    char* end=nullptr;long value=strtol(text,&end,10);
    if(end==text || *end || value<minimum || value>maximum) return false;*result=(int)value;return true;
}
// This path only reads process/module metadata; it never loads the bridge or installs a hook.
static int moduleState(DWORD pid) {
    HANDLE process=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);
    if(!process) {
        DWORD code=GetLastError();
        if(code!=ERROR_INVALID_PARAMETER)return error("module_check_process_failed",code);
        printf("{\"ok\":true,\"independent\":true,\"original_required\":false,\"pid\":%lu,\"game_running\":false,\"module_loaded\":false,\"module_unload_verified\":true}\n",pid);return 0;
    }
    if(WaitForSingleObject(process,0)==WAIT_OBJECT_0) {
        CloseHandle(process);printf("{\"ok\":true,\"independent\":true,\"original_required\":false,\"pid\":%lu,\"game_running\":false,\"module_loaded\":false,\"module_unload_verified\":true}\n",pid);return 0;
    }
    wchar_t image[32768];DWORD length=32768;
    if(!QueryFullProcessImageNameW(process,0,image,&length)){DWORD code=GetLastError();CloseHandle(process);return error("module_check_image_failed",code);}
    const wchar_t* name=wcsrchr(image,L'\\');name=name?name+1:image;
    if(_wcsicmp(name,L"League of Legends.exe")){CloseHandle(process);return error("module_check_target_mismatch",ERROR_INVALID_PARAMETER);}
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE|TH32CS_SNAPMODULE32,pid);
    if(snapshot==INVALID_HANDLE_VALUE){DWORD code=GetLastError();CloseHandle(process);return error("module_check_snapshot_failed",code);}
    MODULEENTRY32W entry{};entry.dwSize=sizeof(entry);bool loaded=false;
    BOOL more=Module32FirstW(snapshot,&entry);
    while(more) {if(!_wcsicmp(entry.szModule,L"league-skin-core.dll")){loaded=true;break;}more=Module32NextW(snapshot,&entry);}
    DWORD code=loaded?ERROR_SUCCESS:GetLastError();CloseHandle(snapshot);
    bool running=WaitForSingleObject(process,0)!=WAIT_OBJECT_0;CloseHandle(process);
    if(!loaded && code!=ERROR_NO_MORE_FILES)return error("module_check_enumeration_failed",code);
    loaded=loaded && running;
    printf("{\"ok\":true,\"independent\":true,\"original_required\":false,\"pid\":%lu,\"game_running\":%s,\"module_loaded\":%s,\"module_unload_verified\":%s}\n",pid,running?"true":"false",loaded?"true":"false",loaded?"false":"true");return 0;
}
int main(int argc,char** argv) {
    if(argc==3 && !strcmp(argv[1],"module-state")) {
        int pid=0;if(!integer(argv[2],1,2147483647,&pid))return error("invalid_process",ERROR_INVALID_PARAMETER);
        return moduleState((DWORD)pid);
    }
    DWORD command=99;int index=-1,gear=-1;const char* session="";
    if(argc==2 && !strcmp(argv[1],"status")) command=6;
    if(argc==2 && !strcmp(argv[1],"catalog")) command=7;
    if(argc==2 && !strcmp(argv[1],"inventory")) command=10;
    if(argc==2 && !strcmp(argv[1],"object-probe")) command=11;
    if(argc==2 && !strcmp(argv[1],"rank-border-probe")) command=18;
    if(argc==2 && !strcmp(argv[1],"rank-border-catalog")) command=19;
    if(argc==4 && !strcmp(argv[1],"rank-border-auto")) {
        command=21;index=-1;
        if(strcmp(argv[2],BorderProfile))return error("rank_border_profile_mismatch",ERROR_INVALID_PARAMETER);
        for(int n=0;n<(int)(sizeof(BorderTiers)/sizeof(BorderTiers[0]));n++)if(!strcmp(argv[3],BorderTiers[n].id))index=n;
        if(index<0)return error("invalid_rank_border_entry",ERROR_INVALID_PARAMETER);
    }
    if(argc==5 && !strcmp(argv[1],"rank-border")) {
        command=20;session=argv[2];index=-1;
        if(strcmp(argv[3],BorderProfile))return error("rank_border_profile_mismatch",ERROR_INVALID_PARAMETER);
        if(strcmp(argv[4],"original")) {
            bool found=false;for(int n=0;n<(int)(sizeof(BorderTiers)/sizeof(BorderTiers[0]));n++)if(!strcmp(argv[4],BorderTiers[n].id)){index=n;found=true;break;}
            if(!found)return error("invalid_rank_border_entry",ERROR_INVALID_PARAMETER);
        }
    }
    if(argc==3 && !strcmp(argv[1],"rank-border-probe")) {command=18;if(!strcmp(argv[2],"state"))index=-2;else if(!strcmp(argv[2],"display"))index=-3;else if(!integer(argv[2],0,(int)Game_image_size-1,&index))return error("invalid_probe_rva",ERROR_INVALID_PARAMETER);}
    if(argc==2 && !strcmp(argv[1],"object-catalog")) command=12;
    if(argc==2 && !strcmp(argv[1],"camera-status")) command=15;
    if(argc==4 && !strcmp(argv[1],"camera-view")) {
        command=17;session=argv[2];char* end=nullptr;float selected=strtof(argv[3],&end);
        if(end==argv[3] || *end || !validCameraView(selected))return error("invalid_camera_distance",ERROR_INVALID_PARAMETER);
        index=cameraBits(selected);
    }
    if(argc==5 && !strcmp(argv[1],"camera")) {
        command=16;session=argv[2];char *end1=nullptr,*end2=nullptr;
        float selected=strtof(argv[3],&end1),expected=strtof(argv[4],&end2);
        if(end1==argv[3] || *end1 || end2==argv[4] || *end2 || !cameraDistance(selected) || !cameraDistance(expected))return error("invalid_camera_distance",ERROR_INVALID_PARAMETER);
        index=cameraBits(selected);gear=cameraBits(expected);
    }
    if(argc==5 && (!strcmp(argv[1],"objects") || !strcmp(argv[1],"objects-restore"))){command=!strcmp(argv[1],"objects")?13:14;session=argv[2];if(!integer(argv[3],-1,command==13?10:10000,&index)||!integer(argv[4],-1,1000,&gear))return error("invalid_object_selection",ERROR_INVALID_PARAMETER);}
    if(argc==3 && !strcmp(argv[1],"inventory")){command=10;if(!integer(argv[2],0,9999,&index))return error("invalid_entry",ERROR_INVALID_PARAMETER);}
    if(argc==5 && !strcmp(argv[1],"entry")){command=8;session=argv[2];if(!integer(argv[3],0,999,&index)||!integer(argv[4],-1,127,&gear))return error("invalid_entry",ERROR_INVALID_PARAMETER);}
    if(argc==3 && !strcmp(argv[1],"restore")){command=9;session=argv[2];}
    if(command==99 || strlen(session)>=32) return error("invalid_request",ERROR_INVALID_PARAMETER);
    if(command==8 || command==9 || command==13 || command==14 || command==16 || command==17 || command==20) {
        if(strlen(session)!=16) return error("invalid_session",ERROR_INVALID_PARAMETER);
        for(const char* p=session;*p;p++) if(!((*p>='0'&&*p<='9')||(*p>='a'&&*p<='f'))) return error("invalid_session",ERROR_INVALID_PARAMETER);
    }
    if(!discoverGameWindow())return error("game_window_not_found",ERROR_NOT_FOUND,"window_discovery");
    DWORD pid=windowDiscovery.selected.pid,thread=windowDiscovery.selected.thread;uint64_t nonce=0;
    if(BCryptGenRandom(nullptr,(PUCHAR)&nonce,sizeof(nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0) return error("nonce_failed",ERROR_INVALID_DATA);
    diagnosticPid=pid;diagnosticThread=thread;diagnosticNonce=nonce;
    HookTraceOwner traceOwner(pid,thread,nonce);
    wchar_t mapping[128],event[128];names(pid,nonce,mapping,event);
    HANDLE file=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Request),mapping);
    if(!file) return error("create_mapping_failed",GetLastError());
    if(GetLastError()==ERROR_ALREADY_EXISTS) {CloseHandle(file);return error("channel_collision",ERROR_ALREADY_EXISTS);}
    HANDLE signal=CreateEventW(nullptr,TRUE,FALSE,event);auto r=(Request*)MapViewOfFile(file,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request));
    if(!signal || !r) {DWORD code=GetLastError();if(signal) CloseHandle(signal);CloseHandle(file);return error("create_channel_failed",code);}
    r->magic=Magic;r->version=Protocol;r->command=command;r->pid=pid;r->nonce=nonce;r->deadline=GetTickCount64()+5000;
    r->index=index;r->gear=gear;strcpy_s(r->session,session);
    wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);wchar_t* slash=wcsrchr(path,L'\\');HMODULE dll=nullptr;HHOOK hook=nullptr;int result=1;bool completed=false;
    if(slash) {wcscpy_s(slash+1,32768-(slash+1-path),L"league-skin-core.dll");dll=LoadLibraryW(path);}
    if(!dll) result=error("load_own_bridge_failed",GetLastError(),"bridge_load");
    else {
        auto version=(DWORD(__cdecl*)())GetProcAddress(dll,"StudioBridgeProtocol");
        if(!version || version()!=Protocol) {FreeLibrary(dll);dll=nullptr;UnmapViewOfFile(r);CloseHandle(signal);CloseHandle(file);return error("bridge_version_mismatch",ERROR_REVISION_MISMATCH,"bridge_load");}
        auto callback=(HOOKPROC)GetProcAddress(dll,"StudioHookProc");hook=callback?SetWindowsHookExW(WH_GETMESSAGE,callback,dll,thread):nullptr;
        if(!hook) result=error("standard_windows_hook_rejected",GetLastError(),"hook_install");
        else {
            if(!postGameRequest(windowDiscovery.selected.window,pid,thread,BridgeMessage,Magic,(LPARAM)nonce)) {
                DWORD code=GetLastError();InterlockedCompareExchange(&r->phase,3,0);
                result=error("message_post_failed",code,"message_post");
            } else {
                DWORD wait=WaitForSingleObject(signal,5500);
                DWORD code=wait==WAIT_FAILED?GetLastError():ERROR_TIMEOUT;
                if(wait==WAIT_OBJECT_0 && r->phase==2 && r->result[0]) {completed=true;result=0;}
                else {
                    LONG phase=InterlockedCompareExchange(&r->phase,3,0);
                    // Completion may race the wait timeout. Preserve its result if already published.
                    if(phase==2 && r->result[0]) {completed=true;result=0;}
                    else result=error(phase==1?"request_outcome_unknown":wait==WAIT_FAILED?"response_wait_failed":"core_response_timeout",
                        code,"response_wait",phase);
                }
            }
        }
    }
    bool released=!hook || UnhookWindowsHookEx(hook)!=FALSE;
    if(dll) FreeLibrary(dll);
    // Report only after removing the request's hook. This does not prove DLL unload.
    if(completed) {
        size_t length=strnlen_s(r->result,sizeof(r->result));
        if(length && length<sizeof(r->result) && r->result[length-1]=='}')
            printf("%.*s,\"transport_hook_released\":%s}\n",(int)length-1,r->result,released?"true":"false");
        else result=error("invalid_bridge_reply",ERROR_INVALID_DATA);
    }
    UnmapViewOfFile(r);CloseHandle(signal);CloseHandle(file);return result;
}
#endif
