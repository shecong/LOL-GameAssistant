#pragma once
#include <windows.h>
#include <cstdint>
#include <cstddef>
#include <cwchar>

namespace overlay {
constexpr uint32_t Magic = 0x53434f56, Version = 2;
constexpr size_t PixelOffset = 65536, PixelCapacity = 32 * 1024 * 1024;
constexpr size_t AuxOffset = PixelOffset + PixelCapacity, BattlePixelOffset = AuxOffset + 65536;
constexpr size_t BattlePixelCapacity = 1600 * 1000 * 4;
constexpr size_t MappingSize = BattlePixelOffset + BattlePixelCapacity;
constexpr wchar_t StartMessage[] = L"SC.MayhemOverlay.Start.v2";
#pragma pack(push, 1)
struct Frame {
    uint32_t magic, version;
    volatile LONG enabled, status; // 0 waiting, 1 installed, 2 rendered, negative = failure
    uint32_t ownerPid, gamePid;
    uint64_t heartbeat;
    volatile LONG command;
    uint32_t rarity, collapsed, scanning;
    volatile LONG captureRequest, captureResponse;
    uint32_t captureWidth, captureHeight, gameWidth, gameHeight;
    int32_t captureError;
    uint32_t renderedFrames;
    uint32_t mayhemEnabled, panelWidth, panelHeight, hitRegionCount;
    uint32_t revision, uiBuilds, targetCreates, lastCpuUs, maxCpuUs;
    uint8_t reserved[12];
    char title[256], statusText[2048], recommendations[32768], offered[16384];
};
struct Auxiliary {
    uint32_t flags, battleVersion, battleWidth, battleHeight;
    uint64_t coachDeadline, skinDeadline;
    int32_t coachPosition, coachOffsetX, coachOffsetY;
    int32_t skinReady, skinCanRefresh, skinSelected, skinCount, skinVersion;
    volatile LONG command, argument, commandVersion;
    uint8_t reserved[52];
    char coachTitle[256], coachBody[2048], coachDetail[1024], skinToast[1024];
    char skinTitle[256], skinStatus[2048], skinNames[256][192];
};
#pragma pack(pop)
static_assert(offsetof(Frame, title) == 128);
static_assert(sizeof(Frame) == 51584);
static_assert(offsetof(Auxiliary, coachTitle) == 128);
static_assert(offsetof(Auxiliary, skinNames) == 6784);
static_assert(sizeof(Auxiliary) == 55936);
inline void MappingName(wchar_t (&name)[160], DWORD game, DWORD owner, uint64_t nonce) {
    swprintf_s(name, L"Local\\SC.MayhemOverlay.v2.%lu.%lu.%016llx", game, owner, nonce);
}
inline bool Valid(const Frame& frame, DWORD game, DWORD owner) {
    return frame.magic == Magic && frame.version == Version && frame.gamePid == game && frame.ownerPid == owner;
}
inline bool Live(const Frame& frame) {
    uint64_t now = GetTickCount64();
    return frame.enabled == 1 && frame.heartbeat <= now && now - frame.heartbeat < 5000;
}
}
