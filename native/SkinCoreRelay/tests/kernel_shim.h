// User-mode test shim for exercising the actual driver's dispatch logic.
// This does not emulate Windows ACL/IRP/lifetime guarantees or replace VM tests.
#pragma once
#define WIN32_NO_STATUS
#include <windows.h>
#include <winternl.h>
#undef WIN32_NO_STATUS
#include <ntstatus.h>
#include <stdlib.h>
#include <string.h>
typedef LONG NTSTATUS;
typedef struct _SIM_PROCESS { ULONG Id; LONG References; } SIM_PROCESS, *PEPROCESS;
typedef void* WDFWAITLOCK;
typedef void* WDFDEVICE;
typedef void* WDFDRIVER;
typedef struct _SIM_FILE { WDFDEVICE Device; } SIM_FILE, *WDFFILEOBJECT;
typedef struct _SIM_REQUEST {
    WDFFILEOBJECT File;
    PEPROCESS Caller;
    void* Buffer;
    size_t InputLength, OutputLength;
    NTSTATUS Status;
    ULONG_PTR Information;
} SIM_REQUEST, *WDFREQUEST;
typedef WDFDEVICE WDFQUEUE;
#define WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(type, name) static type* name(WDFDEVICE device) { return (type*)device; }
#define POOL_FLAG_NON_PAGED 1
static SIM_PROCESS Processes[] = {{100, 0}, {200, 0}, {300, 0}};
static ULONGLONG SimTime = 1;
static LONG Allocations = 0;
static void InitializeListHead(PLIST_ENTRY head) { head->Flink = head->Blink = head; }
static void InsertTailList(PLIST_ENTRY head, PLIST_ENTRY item) { item->Blink = head->Blink; item->Flink = head; head->Blink->Flink = item; head->Blink = item; }
static void RemoveEntryList(PLIST_ENTRY item) { item->Blink->Flink = item->Flink; item->Flink->Blink = item->Blink; }
static void ObReferenceObject(PEPROCESS process) { process->References++; }
static void ObDereferenceObject(PEPROCESS process) { process->References--; }
static void* ExAllocatePool2(ULONG flags, size_t size, ULONG tag) { void* memory; (void)flags; (void)tag; memory = calloc(1, size); if (memory) Allocations++; return memory; }
static void ExFreePoolWithTag(void* memory, ULONG tag) { (void)tag; free(memory); Allocations--; }
static ULONGLONG KeQueryInterruptTime(void) { return SimTime; }
static NTSTATUS PsLookupProcessByProcessId(HANDLE id, PEPROCESS* output) {
    size_t n; for (n = 0; n < 3; n++) if (Processes[n].Id == HandleToULong(id)) { *output = &Processes[n]; ObReferenceObject(*output); return STATUS_SUCCESS; }
    return STATUS_NOT_FOUND;
}
static void WdfWaitLockAcquire(WDFWAITLOCK lock, void* timeout) { (void)lock; (void)timeout; }
static void WdfWaitLockRelease(WDFWAITLOCK lock) { (void)lock; }
static WDFDEVICE WdfFileObjectGetDevice(WDFFILEOBJECT file) { return file->Device; }
static PUNICODE_STRING WdfFileObjectGetFileName(WDFFILEOBJECT file) { static UNICODE_STRING name; (void)file; return &name; }
static WDFDEVICE WdfIoQueueGetDevice(WDFQUEUE queue) { return queue; }
static WDFFILEOBJECT WdfRequestGetFileObject(WDFREQUEST request) { return request->File; }
static WDFREQUEST WdfRequestWdmGetIrp(WDFREQUEST request) { return request; }
static PEPROCESS IoGetRequestorProcess(WDFREQUEST request) { return request->Caller; }
static NTSTATUS WdfRequestRetrieveInputBuffer(WDFREQUEST request, size_t length, void** buffer, void* actual) {
    (void)actual; if (length > request->InputLength) return STATUS_BUFFER_TOO_SMALL; *buffer = request->Buffer; return STATUS_SUCCESS;
}
static NTSTATUS WdfRequestRetrieveOutputBuffer(WDFREQUEST request, size_t length, void** buffer, void* actual) {
    (void)actual; if (length > request->OutputLength) return STATUS_BUFFER_TOO_SMALL; *buffer = request->Buffer; return STATUS_SUCCESS;
}
static void WdfRequestComplete(WDFREQUEST request, NTSTATUS status) { request->Status = status; }
static void WdfRequestCompleteWithInformation(WDFREQUEST request, NTSTATUS status, ULONG_PTR information) { request->Status = status; request->Information = information; }
