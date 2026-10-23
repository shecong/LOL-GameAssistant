// Reuse the existing game executor; no game function runs in kernel mode.
#ifdef BUILD_HOST
#define main classic_host_main
#endif
#include "../../LeagueSkinCore/native/core.cpp"
#ifdef BUILD_HOST
#undef main
#endif
#include "../../SkinCoreRelay/public.h"
constexpr UINT KernelMessage = WM_APP + 0x533;
#include "kernel_channel.h"
#ifdef BUILD_HOST
int main(int argc, char** argv) {
    if (argc == 2 && !strcmp(argv[1], "driver-status")) {
        HANDLE device = CreateFileW(SKIN_RELAY_PATH, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (device == INVALID_HANDLE_VALUE) return error("kernel_driver_unavailable", GetLastError());
        bool ready = relayCaps(device); CloseHandle(device);
        if (!ready) return error("kernel_protocol_mismatch", ERROR_REVISION_MISMATCH);
        puts("{\"ok\":true,\"independent\":true,\"original_required\":false,\"backend\":\"kernel_relay\",\"driver_ready\":true}"); return 0;
    }
    if (argc == 3 && !strcmp(argv[1], "module-state")) {
        int pid = 0; if (!integer(argv[2], 1, 2147483647, &pid)) return error("invalid_process", ERROR_INVALID_PARAMETER);
        return moduleState((DWORD)pid);
    }
    DWORD command=99;int index=-1,gear=-1;const char* session="";
    if(argc==2 && !strcmp(argv[1],"status")) command=6;
    if(argc==2 && !strcmp(argv[1],"catalog")) command=7;
    if(argc==2 && !strcmp(argv[1],"inventory")) command=10;
    if(argc==2 && !strcmp(argv[1],"object-probe")) command=11;
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
    if(command==8 || command==9 || command==13 || command==14 || command==16 || command==17) {
        if(strlen(session)!=16) return error("invalid_session",ERROR_INVALID_PARAMETER);
        for(const char* p=session;*p;p++) if(!((*p>='0'&&*p<='9')||(*p>='a'&&*p<='f'))) return error("invalid_session",ERROR_INVALID_PARAMETER);
    }
    HWND window=FindWindowW(nullptr,L"League of Legends (TM) Client");if(!window) return error("game_window_not_found",ERROR_NOT_FOUND);
    DWORD pid=0;DWORD thread=GetWindowThreadProcessId(window,&pid);uint64_t nonce=0;
    if(BCryptGenRandom(nullptr,(PUCHAR)&nonce,sizeof(nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0) return error("nonce_failed",ERROR_INVALID_DATA);
    SKIN_RELAY_COMMAND request{};
    request.Size = sizeof(request); request.Version = SKIN_RELAY_VERSION; request.Command = command; request.ProcessId = pid;
    request.Nonce = nonce; request.Deadline = GetTickCount64() + 5000; request.Index = index; request.Gear = gear;
    strcpy_s(request.Session, session);
    return kernelHost(request, thread);
}
#endif
