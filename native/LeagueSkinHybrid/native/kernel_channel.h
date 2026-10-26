#pragma once
static bool relayCaps(HANDLE device) {
    SKIN_RELAY_CAPABILITIES caps{}; DWORD bytes = 0;
    return DeviceIoControl(device, SKIN_RELAY_CAPS, nullptr, 0, &caps, sizeof(caps), &bytes, nullptr) &&
        bytes == sizeof(caps) && caps.Size == sizeof(caps) && caps.Version == SKIN_RELAY_VERSION &&
        caps.MaxResultBytes == SKIN_RELAY_RESULT_MAX && caps.MaxRequests == SKIN_RELAY_MAX_REQUESTS;
}
#ifndef BUILD_HOST
extern "C" __declspec(dllexport) LRESULT CALLBACK KernelHookProc(int code, WPARAM removed, LPARAM data) {
    if (code >= 0 && removed == PM_REMOVE) {
        MSG* message = (MSG*)data;
        if (message->message == KernelMessage && message->wParam == Magic) {
            SKIN_RELAY_KEY key{ (ULONGLONG)message->lParam };
            message->message = WM_NULL; message->wParam = 0; message->lParam = 0;
            HANDLE device = CreateFileW(SKIN_RELAY_PATH, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
            if (device != INVALID_HANDLE_VALUE) {
                SKIN_RELAY_COMMAND command{}; DWORD bytes = 0;
                if (relayCaps(device) && DeviceIoControl(device, SKIN_RELAY_CLAIM, &key, sizeof(key), &command, sizeof(command), &bytes, nullptr) &&
                    bytes == sizeof(command) && command.Size == sizeof(command) && command.Version == SKIN_RELAY_VERSION &&
                    command.ProcessId == GetCurrentProcessId() && command.Nonce == key.Nonce) {
                    // Heap allocation avoids putting the 1MB reply on the game thread's stack.
                    auto request = (Request*)calloc(1, sizeof(Request));
                    auto response = (BYTE*)calloc(1, sizeof(SKIN_RELAY_RESULT) + SKIN_RELAY_RESULT_MAX);
                    if (request && response) {
                        request->magic = Magic; request->version = Protocol; request->command = command.Command;
                        request->pid = command.ProcessId; request->nonce = command.Nonce; request->deadline = command.Deadline;
                        request->index = command.Index; request->gear = command.Gear;
                        memcpy(request->session, command.Session, sizeof(request->session));
                        if (claim(request, GetCurrentProcessId(), key.Nonce)) {
                            finishRequest(request);
                            size_t length = strnlen_s(request->result, sizeof(request->result));
                            if (length && length < SKIN_RELAY_RESULT_MAX) {
                                auto header = (SKIN_RELAY_RESULT*)response;
                                header->Size = sizeof(*header); header->Version = SKIN_RELAY_VERSION;
                                header->Phase = SKIN_RELAY_DONE; header->Nonce = key.Nonce; header->ResultBytes = (ULONG)length;
                                memcpy(response + sizeof(*header), request->result, length);
                                DeviceIoControl(device, SKIN_RELAY_COMPLETE, response, (DWORD)(sizeof(*header) + length), nullptr, 0, &bytes, nullptr);
                            }
                        }
                    }
                    free(response); free(request);
                }
                CloseHandle(device);
            }
        }
    }
    return CallNextHookEx(nullptr, code, removed, data);
}
#else
static bool relayReceive(HANDLE device, DWORD operation, ULONGLONG nonce, BYTE* response, DWORD capacity) {
    SKIN_RELAY_KEY key{nonce}; DWORD bytes = 0;
    if (!DeviceIoControl(device, operation, &key, sizeof(key), response, capacity, &bytes, nullptr) || bytes < sizeof(SKIN_RELAY_RESULT)) return false;
    auto header = (SKIN_RELAY_RESULT*)response;
    return header->Size == sizeof(*header) && header->Version == SKIN_RELAY_VERSION && header->Nonce == nonce &&
        header->Phase <= SKIN_RELAY_CANCELLED && header->ResultBytes < SKIN_RELAY_RESULT_MAX &&
        bytes == sizeof(*header) + header->ResultBytes && (header->Phase == SKIN_RELAY_DONE || header->ResultBytes == 0);
}
static int kernelHost(const SKIN_RELAY_COMMAND& command, DWORD thread, HWND window) {
    HANDLE device = CreateFileW(SKIN_RELAY_PATH, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (device == INVALID_HANDLE_VALUE) return error("kernel_driver_unavailable", GetLastError());
    if (!relayCaps(device)) { CloseHandle(device); return error("kernel_protocol_mismatch", ERROR_REVISION_MISMATCH); }
    auto response = (BYTE*)calloc(1, sizeof(SKIN_RELAY_RESULT) + SKIN_RELAY_RESULT_MAX);
    if (!response) { CloseHandle(device); return error("allocation_failed", ERROR_NOT_ENOUGH_MEMORY); }
    DWORD bytes = 0;
    if (!DeviceIoControl(device, SKIN_RELAY_SUBMIT, (void*)&command, sizeof(command), nullptr, 0, &bytes, nullptr)) {
        DWORD code = GetLastError(); free(response); CloseHandle(device); return error("kernel_submit_failed", code);
    }
    wchar_t path[32768]; GetModuleFileNameW(nullptr, path, 32768); wchar_t* slash = wcsrchr(path, L'\\');
    HMODULE dll = nullptr; HHOOK hook = nullptr; int result = 1; bool completed = false;
    if (slash) { wcscpy_s(slash + 1, 32768 - (slash + 1 - path), L"league-skin-core.dll"); dll = LoadLibraryW(path); }
    if (!dll) result = error("load_own_bridge_failed", GetLastError(), "bridge_load");
    else {
        auto callback = (HOOKPROC)GetProcAddress(dll, "KernelHookProc");
        hook = callback ? SetWindowsHookExW(WH_GETMESSAGE, callback, dll, thread) : nullptr;
        if (!hook) result = error("standard_windows_hook_rejected", GetLastError(), "hook_install");
        else if (!postGameRequest(window, command.ProcessId, thread, KernelMessage, Magic, (LPARAM)command.Nonce)) result = error("message_post_failed", GetLastError(), "message_post");
        else {
            const DWORD capacity = sizeof(SKIN_RELAY_RESULT) + SKIN_RELAY_RESULT_MAX;
            ULONGLONG until = GetTickCount64() + 5500;
            bool valid = true;
            while (GetTickCount64() <= until) {
                if (!relayReceive(device, SKIN_RELAY_FETCH, command.Nonce, response, capacity)) { valid = false; break; }
                auto header = (SKIN_RELAY_RESULT*)response;
                if (header->Phase == SKIN_RELAY_DONE) { completed = true; break; }
                if (header->Phase == SKIN_RELAY_CANCELLED) break;
                Sleep(10);
            }
            if (!completed) {
                valid = relayReceive(device, SKIN_RELAY_CANCEL, command.Nonce, response, capacity) && valid;
                auto header = (SKIN_RELAY_RESULT*)response;
                if (valid && header->Phase == SKIN_RELAY_DONE) completed = true;
                else result = error(!valid || header->Phase == SKIN_RELAY_RUNNING ? "request_outcome_unknown" : "core_response_timeout",
                    ERROR_TIMEOUT, "response_wait", valid ? (LONG)header->Phase : -1);
            }
        }
    }
    bool released = !hook || UnhookWindowsHookEx(hook) != FALSE;
    if (dll) FreeLibrary(dll);
    if (completed) {
        auto header = (SKIN_RELAY_RESULT*)response; const char* json = (char*)response + sizeof(*header);
        size_t length = header->ResultBytes;
        if (length && json[length - 1] == '}' && !memchr(json, 0, length)) {
            printf("%.*s,\"backend\":\"kernel_relay\",\"transport_hook_released\":%s}\n", (int)length - 1, json, released ? "true" : "false"); result = 0;
        } else result = error("request_outcome_unknown", ERROR_INVALID_DATA);
    }
    // Closing the owner file revokes the record. It cannot cancel an action already executing.
    free(response); CloseHandle(device); return result;
}
#endif
