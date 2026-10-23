#pragma once
#ifdef _KERNEL_MODE
#include <ntddk.h>
#else
#include <windows.h>
#include <winioctl.h>
#endif

#define SKIN_RELAY_VERSION 1u
#define SKIN_RELAY_RESULT_MAX (1024u * 1024u)
#define SKIN_RELAY_MAX_REQUESTS 4u
#define SKIN_RELAY_PATH L"\\\\.\\SkinCoreRelay"
#define SKIN_RELAY_CAPS CTL_CODE(FILE_DEVICE_UNKNOWN, 0x900, METHOD_BUFFERED, FILE_READ_ACCESS)
#define SKIN_RELAY_SUBMIT CTL_CODE(FILE_DEVICE_UNKNOWN, 0x901, METHOD_BUFFERED, FILE_WRITE_ACCESS)
#define SKIN_RELAY_CLAIM CTL_CODE(FILE_DEVICE_UNKNOWN, 0x902, METHOD_BUFFERED, FILE_READ_ACCESS)
#define SKIN_RELAY_COMPLETE CTL_CODE(FILE_DEVICE_UNKNOWN, 0x903, METHOD_BUFFERED, FILE_READ_ACCESS)
#define SKIN_RELAY_FETCH CTL_CODE(FILE_DEVICE_UNKNOWN, 0x904, METHOD_BUFFERED, FILE_WRITE_ACCESS)
#define SKIN_RELAY_CANCEL CTL_CODE(FILE_DEVICE_UNKNOWN, 0x905, METHOD_BUFFERED, FILE_WRITE_ACCESS)

typedef struct _SKIN_RELAY_CAPABILITIES {
    ULONG Size, Version, MaxResultBytes, MaxRequests;
} SKIN_RELAY_CAPABILITIES;
typedef struct _SKIN_RELAY_KEY { ULONGLONG Nonce; } SKIN_RELAY_KEY;
typedef struct _SKIN_RELAY_COMMAND {
    ULONG Size, Version, Command, ProcessId;
    ULONGLONG Nonce, Deadline;
    LONG Index, Gear;
    CHAR Session[32];
} SKIN_RELAY_COMMAND;
// Payload immediately follows this header. No pointers cross the boundary.
typedef struct _SKIN_RELAY_RESULT {
    ULONG Size, Version, Phase, ResultBytes;
    ULONGLONG Nonce;
} SKIN_RELAY_RESULT;
#define SKIN_RELAY_QUEUED 0u
#define SKIN_RELAY_RUNNING 1u
#define SKIN_RELAY_DONE 2u
#define SKIN_RELAY_CANCELLED 3u
#ifdef __cplusplus
static_assert(sizeof(SKIN_RELAY_COMMAND) == 72);
static_assert(sizeof(SKIN_RELAY_RESULT) == 24);
#endif
