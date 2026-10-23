#include "public.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#define CHECK(x) do { if (!(x)) { printf("FAIL line %d, win32=%lu\n", __LINE__, GetLastError()); return 1; } } while (0)
int main(int argc, char** argv) {
    if (argc == 2 && !strcmp(argv[1], "--protocol-only")) {
        CHECK(sizeof(SKIN_RELAY_COMMAND) == 72 && sizeof(SKIN_RELAY_RESULT) == 24);
        CHECK((SKIN_RELAY_SUBMIT >> 14 & 3) == FILE_WRITE_ACCESS);
        CHECK((SKIN_RELAY_CLAIM >> 14 & 3) == FILE_READ_ACCESS);
        puts("Protocol layout/access checks passed; kernel driver was not loaded."); return 0;
    }
    HANDLE owner = CreateFileW(SKIN_RELAY_PATH, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    CHECK(owner != INVALID_HANDLE_VALUE);
    HANDLE target = CreateFileW(SKIN_RELAY_PATH, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    CHECK(target != INVALID_HANDLE_VALUE);
    DWORD bytes = 0; SKIN_RELAY_CAPABILITIES caps{};
    CHECK(DeviceIoControl(owner, SKIN_RELAY_CAPS, nullptr, 0, &caps, sizeof(caps), &bytes, nullptr));
    CHECK(caps.Version == SKIN_RELAY_VERSION && bytes == sizeof(caps));
    SKIN_RELAY_COMMAND command{sizeof(command), SKIN_RELAY_VERSION, 6, GetCurrentProcessId(), GetTickCount64() | 1, GetTickCount64() + 5000, -1, -1, ""};
    CHECK(!DeviceIoControl(target, SKIN_RELAY_SUBMIT, &command, sizeof(command), nullptr, 0, &bytes, nullptr));
    CHECK(DeviceIoControl(owner, SKIN_RELAY_SUBMIT, &command, sizeof(command), nullptr, 0, &bytes, nullptr));
    SKIN_RELAY_KEY key{command.Nonce}; SKIN_RELAY_COMMAND claimed{};
    CHECK(DeviceIoControl(target, SKIN_RELAY_CLAIM, &key, sizeof(key), &claimed, sizeof(claimed), &bytes, nullptr));
    CHECK(claimed.Nonce == command.Nonce && claimed.ProcessId == GetCurrentProcessId());
    CHECK(!DeviceIoControl(target, SKIN_RELAY_CLAIM, &key, sizeof(key), &claimed, sizeof(claimed), &bytes, nullptr));
    BYTE packet[sizeof(SKIN_RELAY_RESULT) + 64]{};
    auto header = (SKIN_RELAY_RESULT*)packet;
    const char json[] = "{\"ok\":true}";
    *header = {sizeof(*header), SKIN_RELAY_VERSION, SKIN_RELAY_DONE, sizeof(json) - 1, key.Nonce};
    memcpy(packet + sizeof(*header), json, sizeof(json) - 1);
    CHECK(DeviceIoControl(target, SKIN_RELAY_COMPLETE, packet, sizeof(*header) + sizeof(json) - 1, nullptr, 0, &bytes, nullptr));
    CHECK(DeviceIoControl(owner, SKIN_RELAY_FETCH, &key, sizeof(key), packet, sizeof(packet), &bytes, nullptr));
    CHECK(header->Phase == SKIN_RELAY_DONE && header->ResultBytes == sizeof(json) - 1);
    CHECK(!memcmp(packet + sizeof(*header), json, sizeof(json) - 1));
    CloseHandle(owner);
    CHECK(!DeviceIoControl(target, SKIN_RELAY_CLAIM, &key, sizeof(key), &claimed, sizeof(claimed), &bytes, nullptr));
    CloseHandle(target);
    puts("Live driver relay checks passed against this test process; no game connected."); return 0;
}
