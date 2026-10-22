#include <windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdarg>
#include <cmath>
#include <algorithm>
#include "../../native/LeagueSkinCore/native/game_profile.h"

constexpr DWORD Magic=0x43445250;
constexpr UINT Message=WM_APP+0x619;
struct Request {
    DWORD magic,pid;
    uint64_t nonce,deadline;
    volatile LONG phase;
    int operation,offset,length,depth,path[4];
    char result[1024*1024];
};
static void names(DWORD pid,uint64_t nonce,wchar_t* map,wchar_t* event) {
    swprintf_s(map,128,L"Local\\CooldownReadonlyProbe.%lu.%016llx",pid,nonce);
    swprintf_s(event,128,L"Local\\CooldownReadonlyProbe.Done.%lu.%016llx",pid,nonce);
}
static bool readable(const void* address,size_t size) {
    if(!address || !size || size>0x10000)return false;
    uintptr_t at=(uintptr_t)address,end=at+size;if(end<at)return false;
    while(at<end){MEMORY_BASIC_INFORMATION info{};
        if(!VirtualQuery((void*)at,&info,sizeof(info)) || info.State!=MEM_COMMIT ||
            (info.Protect&(PAGE_NOACCESS|PAGE_GUARD)))return false;
        uintptr_t next=(uintptr_t)info.BaseAddress+info.RegionSize;if(next<=at)return false;at=next;
    }return true;
}
struct Writer { char* text;size_t used,capacity;bool failed; };
static void append(Writer* w,const char* format,...) {
    if(w->failed)return;va_list args;va_start(args,format);
    int n=vsnprintf(w->text+w->used,w->capacity-w->used,format,args);va_end(args);
    if(n<0 || size_t(n)>=w->capacity-w->used){w->failed=true;return;}w->used+=n;
}
static void hex(Writer* w,BYTE* p,size_t size) {
    append(w,"\"");for(size_t i=0;i<size;i++)append(w,"%02x",p[i]);append(w,"\"");
}
static void execute(Request* r) {
    BYTE* base=(BYTE*)GetModuleHandleW(nullptr);
    auto dos=(IMAGE_DOS_HEADER*)base;
    auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    if(nt->FileHeader.TimeDateStamp!=Game_timestamp || nt->OptionalHeader.SizeOfImage!=Game_image_size || !gameProfileMatches(base)) {
        strcpy_s(r->result,"{\"ok\":false,\"error\":\"game_profile_mismatch\"}");return;
    }
    BYTE* player=*(BYTE**)(base+Game_player_slot);
    if(!readable(player,0x5000)){strcpy_s(r->result,"{\"ok\":false,\"error\":\"player_unreadable\"}");return;}
    Writer w{r->result,0,sizeof(r->result),false};
    if(r->operation==2) {
        const BYTE clockCode[]={0x0f,0xb6,0x51,0x08,0x0f,0x57,0xc0,0x48,0x8b,0x01,0x48,0xc1,0xe2,0x07};
        if(memcmp(base+0xffce20,clockCode,sizeof(clockCode))){strcpy_s(r->result,"{\"ok\":false,\"error\":\"clock_fingerprint_mismatch\"}");return;}
        BYTE* manager=*(BYTE**)(base+0x1c7beb0);
        if(!readable(manager,0x18)){strcpy_s(r->result,"{\"ok\":false,\"error\":\"clock_manager_unreadable\"}");return;}
        BYTE* timer=*(BYTE**)(manager+0x10);
        if(!readable(timer,0x10)){strcpy_s(r->result,"{\"ok\":false,\"error\":\"timer_unreadable\"}");return;}
        BYTE* clock=*(BYTE**)timer;unsigned index=timer[8];
        if(index>32 || !readable(clock,0x38)){strcpy_s(r->result,"{\"ok\":false,\"error\":\"clock_unreadable\"}");return;}
        BYTE* ticks=*(BYTE**)(clock+0x30);
        if(!readable(ticks+index*128,8)){strcpy_s(r->result,"{\"ok\":false,\"error\":\"ticks_unreadable\"}");return;}
        float gameTime=float(double(*(int64_t*)(ticks+index*128)) * *(double*)(base+0x1d3bd08));
        if(!std::isfinite(gameTime) || gameTime<0 || gameTime>86400){strcpy_s(r->result,"{\"ok\":false,\"error\":\"clock_value_invalid\"}");return;}
        append(&w,"{\"ok\":true,\"read_only\":true,\"pid\":%lu,\"base\":%llu,\"player\":%llu,\"game_time\":%.6f,\"slots\":[",GetCurrentProcessId(),(uint64_t)base,(uint64_t)player,gameTime);
        for(int n=0;n<6;n++) {
            if(n)append(&w,",");BYTE* slot=*(BYTE**)(player+0x3388+n*8);
            if(!readable(slot,0x150)){append(&w,"{\"index\":%d,\"valid\":false}",n);continue;}
            BYTE* info=*(BYTE**)(slot+0x138);BYTE* data=readable(info,0x80)?*(BYTE**)(info+0x78):nullptr;
            char spell[65]{};if(readable(data,0x50)){for(int j=0;j<64;j++){char ch=*(char*)(data+0x10+j);if(!ch)break;if(ch<32 || ch>126){spell[0]=0;break;}spell[j]=ch;}}
            float end=*(float*)(slot+0x38),secondary=*(float*)(slot+0x74),duration=*(float*)(slot+0x80);
            append(&w,"{\"index\":%d,\"stored_index\":%d,\"rank\":%d,\"spell\":\"%s\",\"address\":%llu,\"cooldown_end\":%.6f,\"secondary_end\":%.6f,\"duration_candidate\":%.6f,\"remaining_primary\":%.6f}",n,*(int*)(slot+0x100),*(int*)(slot+0x28),spell,(uint64_t)slot,end,secondary,duration,(std::max)(0.0f,end-gameTime));
        }append(&w,"]}");return;
    }
    append(&w,"{\"ok\":true,\"read_only\":true,\"pid\":%lu,\"base\":%llu,\"player\":%llu,\"timestamp\":%llu,\"operation\":%d,\"player_hex\":",GetCurrentProcessId(),(uint64_t)base,(uint64_t)player,(uint64_t)Game_timestamp,r->operation);
    hex(&w,player,0x5000);
    append(&w,",\"children\":[");bool first=true;int count=0;
    if(r->operation==1) {
        BYTE* node=player;
        for(int n=0;n<r->depth;n++) {
            if(r->path[n]<0 || r->path[n]>=0x5000 || r->path[n]%8 || !readable(node+r->path[n],8)) {
                strcpy_s(r->result,"{\"ok\":false,\"error\":\"chain_unreadable\"}");return;
            }
            node=*(BYTE**)(node+r->path[n]);
        }
        if(!readable(node,0x400)){strcpy_s(r->result,"{\"ok\":false,\"error\":\"target_unreadable\"}");return;}
        append(&w,"{\"offset\":%d,\"address\":%llu,\"hex\":",r->path[0],(uint64_t)node);hex(&w,node,0x400);append(&w,"}]}");return;
    }
    int begin=0,end=0x5000;
    if(begin<0 || end>0x5000 || begin%8){strcpy_s(r->result,"{\"ok\":false,\"error\":\"invalid_offset\"}");return;}
    for(int off=begin;off<end;off+=8) {
        BYTE* child=*(BYTE**)(player+off);
        if((uintptr_t)child<0x10000 || (uintptr_t)child>0x7fffffffffff || (uintptr_t)child%8 ||
            ((uintptr_t)child>=(uintptr_t)base && (uintptr_t)child<(uintptr_t)base+Game_image_size) ||
            !readable(child,0x400))continue;
        if(count++>=160 || GetTickCount64()>r->deadline-250)break;
        if(!first)append(&w,",");first=false;
        append(&w,"{\"offset\":%d,\"address\":%llu,\"hex\":",off,(uint64_t)child);hex(&w,child,0x400);append(&w,"}");
    }
    append(&w,"]}");if(w.failed)strcpy_s(r->result,"{\"ok\":false,\"error\":\"response_full\"}");
}
#ifndef BUILD_HOST
extern "C" __declspec(dllexport) LRESULT CALLBACK CooldownProbeHook(int code,WPARAM removed,LPARAM data) {
    if(code>=0 && removed==PM_REMOVE){MSG* msg=(MSG*)data;
        if(msg->message==Message && msg->wParam==Magic){
            wchar_t mapName[128],eventName[128];uint64_t nonce=(uint64_t)msg->lParam;names(GetCurrentProcessId(),nonce,mapName,eventName);
            HANDLE map=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mapName),event=OpenEventW(EVENT_MODIFY_STATE,FALSE,eventName);
            if(map && event){auto r=(Request*)MapViewOfFile(map,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request));
                if(r){if(r->magic==Magic && r->pid==GetCurrentProcessId() && r->nonce==nonce &&
                    GetTickCount64()<r->deadline && InterlockedCompareExchange(&r->phase,1,0)==0){
                    __try { execute(r); } __except(EXCEPTION_EXECUTE_HANDLER){strcpy_s(r->result,"{\"ok\":false,\"error\":\"read_exception\"}");}
                    InterlockedExchange(&r->phase,2);SetEvent(event);
                }UnmapViewOfFile(r);}
            }if(map)CloseHandle(map);if(event)CloseHandle(event);msg->message=WM_NULL;msg->wParam=msg->lParam=0;
        }
    }return CallNextHookEx(nullptr,code,removed,data);
}
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}
#else
static int error(const char* stage,DWORD code){printf("{\"ok\":false,\"stage\":\"%s\",\"win32\":%lu}\n",stage,code);return 1;}
int main(int argc,char** argv){
    if(argc<2 || (strcmp(argv[1],"snapshot") && strcmp(argv[1],"timers")))return error("invalid_command",87);
    HWND window=FindWindowW(nullptr,L"League of Legends (TM) Client");if(!window)return error("game_window_missing",1168);
    DWORD pid=0,thread=GetWindowThreadProcessId(window,&pid);uint64_t nonce=0;
    if(BCryptGenRandom(nullptr,(PUCHAR)&nonce,8,BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0)return error("nonce",13);
    wchar_t mapName[128],eventName[128];names(pid,nonce,mapName,eventName);
    HANDLE map=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Request),mapName);
    if(!map)return error("mapping",GetLastError());
    if(GetLastError()==ERROR_ALREADY_EXISTS){CloseHandle(map);return error("collision",183);}
    HANDLE event=CreateEventW(nullptr,TRUE,FALSE,eventName);auto r=(Request*)MapViewOfFile(map,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request));
    if(!event || !r){if(event)CloseHandle(event);CloseHandle(map);return error("channel",GetLastError());}
    r->magic=Magic;r->pid=pid;r->nonce=nonce;r->deadline=GetTickCount64()+3000;r->operation=0;
    if(!strcmp(argv[1],"timers")){if(argc!=2)return error("invalid_arguments",87);r->operation=2;}
    if(argc>6)return error("invalid_depth",87);
    if(argc>=3){r->operation=1;r->depth=argc-2;
        for(int n=0;n<r->depth;n++){char* end=nullptr;long offset=strtol(argv[n+2],&end,0);if(end==argv[n+2] || *end || offset<0 || offset>=0x5000 || offset%8)return error("invalid_offset",87);r->path[n]=(int)offset;}
    }
    wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);wchar_t* slash=wcsrchr(path,L'\\');
    if(!slash)return error("own_path",13);wcscpy_s(slash+1,32768-(slash+1-path),L"cooldown-readonly-probe.dll");
    HMODULE dll=LoadLibraryW(path);HHOOK hook=nullptr;int code=1;bool done=false;
    if(!dll)code=error("load_own_probe",GetLastError());else{
        auto callback=(HOOKPROC)GetProcAddress(dll,"CooldownProbeHook");hook=callback?SetWindowsHookExW(WH_GETMESSAGE,callback,dll,thread):nullptr;
        if(!hook)code=error("standard_windows_hook_rejected",GetLastError());else{
            if(PostThreadMessageW(thread,Message,Magic,(LPARAM)nonce) && WaitForSingleObject(event,3500)==WAIT_OBJECT_0 && r->phase==2){done=true;code=0;}
            else code=error("probe_not_completed",1460);
        }
    }
    bool released=!hook || UnhookWindowsHookEx(hook)!=FALSE;if(dll)FreeLibrary(dll);
    if(done){size_t len=strnlen_s(r->result,sizeof(r->result));if(len && r->result[len-1]=='}')printf("%.*s,\"transport_hook_released\":%s}\n",(int)len-1,r->result,released?"true":"false");else code=error("invalid_reply",13);}
    UnmapViewOfFile(r);CloseHandle(event);CloseHandle(map);return code;
}
#endif
