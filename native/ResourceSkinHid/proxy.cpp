#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <cstdio>

static HMODULE selfModule;
static HMODULE systemHid;
static HMODULE backend;
static INIT_ONCE initialized = INIT_ONCE_STATIC_INIT;

static std::wstring Directory() {
    wchar_t path[32768]{};
    DWORD count = GetModuleFileNameW(selfModule, path, 32768);
    std::wstring value(path, count);
    return value.substr(0, value.find_last_of(L"\\/"));
}

static void Log(const char* message, DWORD error = 0) {
    auto path = Directory() + L"\\Logs\\sc-resource-loader.log";
    HANDLE file = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    char line[256]{};
    int length = sprintf_s(line, "pid=%lu tid=%lu %s error=%lu\r\n", GetCurrentProcessId(), GetCurrentThreadId(), message, error);
    DWORD written{};
    WriteFile(file, line, static_cast<DWORD>(length), &written, nullptr);
    CloseHandle(file);
}

static BOOL CALLBACK Initialize(PINIT_ONCE, PVOID, PVOID*) {
    Log("hid_api_first_call");
    auto directory = Directory();
    systemHid = LoadLibraryW((directory + L"\\sc_resource_hid_system.dll").c_str());
    if (!systemHid) { Log("system_hid_load_failed", GetLastError()); return TRUE; }
    Log("system_hid_loaded");
    wchar_t processPath[32768]{};
    GetModuleFileNameW(nullptr, processPath, 32768);
    std::wstring process(processPath);
    process = process.substr(process.find_last_of(L"\\/") + 1);
    if (_wcsicmp(process.c_str(), L"League of Legends.exe") != 0) {
        Log("non_game_process_system_api_only");
        return TRUE;
    }
    // Load on the requesting game thread, outside DllMain, and retain the handle
    // for the lifetime of the process. The original WAD backend remains separate.
    backend = LoadLibraryW((directory + L"\\sc_resource_backend.dll").c_str());
    Log(backend ? "wad_backend_loaded" : "wad_backend_load_failed", backend ? 0 : GetLastError());
    return TRUE;
}

#include "exports.inc"

extern "C" void* ResolveHid(unsigned index) {
    InitOnceExecuteOnce(&initialized, Initialize, nullptr, nullptr);
    if (!systemHid || index >= sizeof(exportNames) / sizeof(exportNames[0])) return nullptr;
    void* address = reinterpret_cast<void*>(GetProcAddress(systemHid, exportNames[index]));
    if (!address) Log("system_hid_export_missing", GetLastError());
    return address;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) selfModule = instance;
    return TRUE;
}
