#pragma once
// Host-only discovery: verify the executable before considering its top-level windows.
struct GameWindowCandidate {
    HWND window;DWORD pid,thread;LONG area,width,height;int priority;
    bool visible,hasOwner,rectRead;DWORD rectError;const char* reason;
    wchar_t title[160],className[96];
};
struct GameWindowDiscovery {
    DWORD pids[32]{};int processes=0,queryErrors=0,candidates=0,observed=0;DWORD win32=0;
    bool overflow=false;GameWindowCandidate windows[16]{},selected{};const char* state="not_checked";
};
static GameWindowDiscovery windowDiscovery;
static bool postGameRequest(HWND window,DWORD pid,DWORD thread,UINT message,WPARAM magic,LPARAM nonce) {
    DWORD currentPid=0;DWORD currentThread=GetWindowThreadProcessId(window,&currentPid);
    if(!currentThread || currentPid!=pid || currentThread!=thread) {
        SetLastError(ERROR_INVALID_WINDOW_HANDLE);return false;
    }
    return PostMessageW(window,message,magic,nonce)!=FALSE;
}
static bool gameExecutable(const wchar_t* path) {
    const wchar_t* name=wcsrchr(path,L'\\');return !_wcsicmp(name?name+1:path,L"League of Legends.exe");
}
static BOOL CALLBACK collectGameWindow(HWND window,LPARAM context) {
    auto info=(GameWindowDiscovery*)context;DWORD pid=0;DWORD thread=GetWindowThreadProcessId(window,&pid);
    bool belongs=false;for(int i=0;i<info->processes;i++)if(info->pids[i]==pid)belongs=true;
    if(!belongs)return TRUE;
    if(info->observed>=16) {info->overflow=true;return TRUE;}
    auto& item=info->windows[info->observed++];item.window=window;item.pid=pid;item.thread=thread;
    GetWindowTextW(window,item.title,160);GetClassNameW(window,item.className,96);
    RECT rect{};item.rectRead=GetClientRect(window,&rect)!=FALSE;
    if(!item.rectRead)item.rectError=GetLastError();
    item.width=rect.right-rect.left;item.height=rect.bottom-rect.top;
    item.visible=IsWindowVisible(window)!=FALSE;item.hasOwner=GetWindow(window,GW_OWNER)!=nullptr;
    bool recognized=!_wcsicmp(item.className,L"RiotWindowClass");
    // Alt-tab/fullscreen transitions may hide or shrink the actual game window.
    // A verified game process + known game class remains a valid communication target.
    item.reason=!thread?"thread_missing":recognized?"recognized_game_class":!item.visible?"hidden":
        item.hasOwner?"owned_window":!item.rectRead?"client_rect_failed":
        item.width<64 || item.height<64?"client_too_small":"visible_main_window";
    if(!thread || (!recognized && (!item.visible || item.hasOwner || !item.rectRead || item.width<64 || item.height<64)))return TRUE;
    item.priority=recognized?2:1;
    int64_t area=(int64_t)item.width*item.height;
    item.area=area<=0?1:area>LONG_MAX?LONG_MAX:(LONG)area;info->candidates++;
    return TRUE;
}
static bool selectGameWindow(GameWindowDiscovery& info) {
    GameWindowCandidate* best=nullptr;bool ambiguous=false;DWORD targetPid=0;
    for(int i=0;i<info.observed;i++) {
        auto& item=info.windows[i];if(item.area<=0)continue;
        if(targetPid && targetPid!=item.pid) {info.state="ambiguous_game_processes";return false;}
        targetPid=item.pid;
        if(!best || item.priority>best->priority || (item.priority==best->priority && item.area>best->area)) {best=&item;ambiguous=false;}
        else if(item.priority==best->priority && item.area==best->area)ambiguous=true;
    }
    if(info.overflow || ambiguous) {info.state="ambiguous_game_windows";return false;}
    if(!best) {info.state=info.processes?"no_usable_game_window":"no_verified_game_process";return false;}
    info.selected=*best;info.state="selected_by_process";return true;
}
static bool discoverGameWindow() {
    windowDiscovery={};auto& info=windowDiscovery;
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if(snapshot==INVALID_HANDLE_VALUE) {info.state="process_snapshot_failed";info.win32=GetLastError();return false;}
    PROCESSENTRY32W entry{};entry.dwSize=sizeof(entry);
    BOOL more=Process32FirstW(snapshot,&entry);
    if(!more) {info.state="process_enumeration_failed";info.win32=GetLastError();CloseHandle(snapshot);return false;}
    while(more) {
        if(gameExecutable(entry.szExeFile)) {
            HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,entry.th32ProcessID);
            if(!process) {info.queryErrors++;info.win32=GetLastError();}
            else {
                wchar_t path[32768];DWORD size=32768;
                if(!QueryFullProcessImageNameW(process,0,path,&size)) {info.queryErrors++;info.win32=GetLastError();}
                else if(gameExecutable(path)) {
                    if(info.processes<32)info.pids[info.processes++]=entry.th32ProcessID;else info.overflow=true;
                }
                CloseHandle(process);
            }
        }
        more=Process32NextW(snapshot,&entry);
    }
    DWORD enumerationError=GetLastError();CloseHandle(snapshot);
    if(enumerationError!=ERROR_NO_MORE_FILES) {info.state="process_enumeration_failed";info.win32=enumerationError;return false;}
    if(!EnumWindows(collectGameWindow,(LPARAM)&info)) {info.state="window_enumeration_failed";info.win32=GetLastError();return false;}
    return selectGameWindow(info);
}
static void windowText(Writer* writer,const wchar_t* value) {
    char utf8[640]{};WideCharToMultiByte(CP_UTF8,0,value,-1,utf8,sizeof(utf8),nullptr,nullptr);quoted(writer,utf8);
}
static void printWindowDiscovery() {
    if(!strcmp(windowDiscovery.state,"not_checked"))return;
    char json[16384];Writer writer{json,0,sizeof(json),false};auto& info=windowDiscovery;
    append(&writer,"{\"state\":\"%s\",\"process_count\":%d,\"query_errors\":%d,\"win32\":%lu,\"candidate_count\":%d,\"selected_title\":",info.state,info.processes,info.queryErrors,info.win32,info.candidates);
    windowText(&writer,info.selected.title);append(&writer,",\"selected_class\":");windowText(&writer,info.selected.className);
    append(&writer,",\"windows\":[");
    for(int i=0;i<info.observed && i<8;i++) {
        auto& item=info.windows[i];if(i)append(&writer,",");
        append(&writer,"{\"pid\":%lu,\"thread_id\":%lu,\"eligible\":%s,\"visible\":%s,\"has_owner\":%s,\"client_width\":%ld,\"client_height\":%ld,\"rect_win32\":%lu,\"reason\":\"%s\",\"title\":",
            item.pid,item.thread,item.area>0?"true":"false",item.visible?"true":"false",item.hasOwner?"true":"false",
            item.width,item.height,item.rectError,item.reason?item.reason:"unknown");
        windowText(&writer,item.title);append(&writer,",\"class_name\":");windowText(&writer,item.className);append(&writer,"}");
    }
    append(&writer,"]}");if(!writer.failed)printf(",\"window_discovery\":%s",json);
}
