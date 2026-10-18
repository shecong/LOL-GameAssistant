#include "protocol.h"
#include <cstdio>
#include <cwchar>
#include <cstdlib>

struct Search { DWORD pid; HWND window; };
static BOOL CALLBACK FindWindow(HWND window, LPARAM data) {
    auto search = reinterpret_cast<Search*>(data); DWORD pid = 0;
    GetWindowThreadProcessId(window, &pid);
    if (pid == search->pid && IsWindowVisible(window) && !GetWindow(window, GW_OWNER)) {
        search->window = window; return FALSE;
    }
    return TRUE;
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 4) return 2;
    wchar_t *end = nullptr;
    DWORD game = wcstoul(argv[1], &end, 10); if (!game || *end) return 2;
    DWORD owner = wcstoul(argv[2], &end, 10); if (!owner || *end) return 2;
    uint64_t nonce = _wcstoui64(argv[3], &end, 16); if (!nonce || *end) return 2;
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, game);
    wchar_t image[32768]{}; DWORD size = 32768;
    if (!process || !QueryFullProcessImageNameW(process, 0, image, &size)) {
        wprintf(L"target_query_failed:%lu\n", GetLastError()); if (process) CloseHandle(process); return 3;
    }
    CloseHandle(process);
    const wchar_t* name = wcsrchr(image, L'\\'); name = name ? name+1 : image;
    if (_wcsicmp(name, L"League of Legends.exe")) { puts("target_mismatch"); return 4; }
    wchar_t mappingName[160]{}; overlay::MappingName(mappingName, game, owner, nonce);
    HANDLE mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, mappingName);
    auto frame = mapping ? static_cast<const overlay::Frame*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, sizeof(overlay::Frame))) : nullptr;
    if (!frame || !overlay::Valid(*frame, game, owner)) { puts("protocol_mismatch"); if (frame) UnmapViewOfFile(frame); if (mapping) CloseHandle(mapping); return 5; }
    Search search{game, nullptr}; EnumWindows(FindWindow, reinterpret_cast<LPARAM>(&search));
    DWORD thread = search.window ? GetWindowThreadProcessId(search.window, nullptr) : 0;
    wchar_t dll[32768]{}; GetModuleFileNameW(nullptr, dll, 32768);
    auto slash = wcsrchr(dll, L'\\'); if (!slash) return 6;
    wcscpy_s(slash+1, size_t(32768-(slash+1-dll)), L"sc-game-overlay.dll");
    HMODULE module = LoadLibraryExW(dll, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto callback = module ? reinterpret_cast<HOOKPROC>(GetProcAddress(module, "OverlayBootstrap")) : nullptr;
    HHOOK hook = thread && callback ? SetWindowsHookExW(WH_GETMESSAGE, callback, module, thread) : nullptr;
    int result = 0;
    if (!hook) { printf("overlay_load_rejected:%lu\n", GetLastError()); result = 7; }
    else {
        if (!PostThreadMessageW(thread, RegisterWindowMessageW(overlay::StartMessage), owner, static_cast<LPARAM>(nonce))) result = 8;
        ULONGLONG deadline = GetTickCount64() + 8000;
        while (!result && frame->status == 0 && GetTickCount64() < deadline) Sleep(50);
        if (!result && frame->status <= 0) { printf("overlay_install_failed:%ld\n", frame->status); result = 9; }
        UnhookWindowsHookEx(hook);
    }
    if (module) FreeLibrary(module);
    UnmapViewOfFile(frame); CloseHandle(mapping);
    return result;
}
