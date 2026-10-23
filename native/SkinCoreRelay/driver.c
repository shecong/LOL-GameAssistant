// Bounded message relay only. This driver never accesses another process's memory
// and never loads a module or executes a caller-provided address.
#ifdef SKIN_RELAY_TEST
#include "tests/kernel_shim.h"
#else
#include <ntifs.h>
#pragma warning(push)
#pragma warning(disable:4324) // WDF ABI explicitly pads aligned request structures.
#include <wdf.h>
#pragma warning(pop)
#include <wdmsec.h>
#endif
#include "public.h"

#define RELAY_TAG 'rKcS'
typedef struct _RECORD {
    LIST_ENTRY Link;
    WDFFILEOBJECT OwnerFile;
    PEPROCESS Owner, Target;
    ULONGLONG Expires;
    SKIN_RELAY_COMMAND Command;
    ULONG Phase, ResultBytes;
    CHAR Result[SKIN_RELAY_RESULT_MAX];
} RECORD;
typedef struct _DEVICE_CONTEXT { WDFWAITLOCK Lock; LIST_ENTRY Records; ULONG Count; } DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, Context)
#ifndef SKIN_RELAY_TEST
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_UNLOAD RelayUnload;
EVT_WDF_DEVICE_FILE_CREATE RelayCreate;
EVT_WDF_FILE_CLEANUP RelayCleanup;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL RelayIoctl;
#endif

static void DestroyRecord(RECORD* record) {
    ObDereferenceObject(record->Owner); ObDereferenceObject(record->Target);
    ExFreePoolWithTag(record, RELAY_TAG);
}
void RelayUnload(WDFDRIVER driver) { UNREFERENCED_PARAMETER(driver); }
void RelayCreate(WDFDEVICE device, WDFREQUEST request, WDFFILEOBJECT file) {
    UNREFERENCED_PARAMETER(device);
    WdfRequestComplete(request, WdfFileObjectGetFileName(file)->Length ? STATUS_OBJECT_NAME_INVALID : STATUS_SUCCESS);
}
void RelayCleanup(WDFFILEOBJECT file) {
    DEVICE_CONTEXT* context = Context(WdfFileObjectGetDevice(file));
    PLIST_ENTRY item, next;
    WdfWaitLockAcquire(context->Lock, NULL);
    for (item = context->Records.Flink; item != &context->Records; item = next) {
        RECORD* record = CONTAINING_RECORD(item, RECORD, Link); next = item->Flink;
        if (record->OwnerFile == file) { RemoveEntryList(item); context->Count--; DestroyRecord(record); }
    }
    WdfWaitLockRelease(context->Lock);
}

void RelayIoctl(WDFQUEUE queue, WDFREQUEST request, size_t outputLength, size_t inputLength, ULONG code) {
    DEVICE_CONTEXT* context = Context(WdfIoQueueGetDevice(queue));
    WDFFILEOBJECT file = WdfRequestGetFileObject(request);
    PEPROCESS caller = IoGetRequestorProcess(WdfRequestWdmGetIrp(request));
    void *input = NULL, *output = NULL;
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    ULONG_PTR information = 0;
    RECORD* record = NULL;
    PLIST_ENTRY item;
    if (!file || !caller) { WdfRequestComplete(request, STATUS_ACCESS_DENIED); return; }
    if (inputLength) {
        status = WdfRequestRetrieveInputBuffer(request, inputLength, &input, NULL);
        if (!NT_SUCCESS(status)) { WdfRequestComplete(request, status); return; }
    }
    if (outputLength) {
        status = WdfRequestRetrieveOutputBuffer(request, outputLength, &output, NULL);
        if (!NT_SUCCESS(status)) { WdfRequestComplete(request, status); return; }
    }
    WdfWaitLockAcquire(context->Lock, NULL);
    status = STATUS_INVALID_DEVICE_REQUEST;
    if (code == SKIN_RELAY_CAPS) {
        SKIN_RELAY_CAPABILITIES caps = { sizeof(caps), SKIN_RELAY_VERSION, SKIN_RELAY_RESULT_MAX, SKIN_RELAY_MAX_REQUESTS };
        if (inputLength || outputLength < sizeof(caps)) { status = STATUS_INVALID_BUFFER_SIZE; goto done; }
        RtlCopyMemory(output, &caps, sizeof(caps)); information = sizeof(caps); status = STATUS_SUCCESS; goto done;
    }
    if (code == SKIN_RELAY_SUBMIT) {
        SKIN_RELAY_COMMAND command;
        PEPROCESS target = NULL;
        if (inputLength != sizeof(command) || outputLength) { status = STATUS_INVALID_BUFFER_SIZE; goto done; }
        RtlCopyMemory(&command, input, sizeof(command));
        if (command.Size != sizeof(command) || command.Version != SKIN_RELAY_VERSION || !command.Nonce ||
            command.Command < 6 || command.Command > 17 || !memchr(command.Session, 0, sizeof(command.Session))) {
            status = STATUS_INVALID_PARAMETER; goto done;
        }
        if (context->Count >= SKIN_RELAY_MAX_REQUESTS) { status = STATUS_QUOTA_EXCEEDED; goto done; }
        for (item = context->Records.Flink; item != &context->Records; item = item->Flink) {
            RECORD* existing = CONTAINING_RECORD(item, RECORD, Link);
            if (existing->Command.Nonce == command.Nonce || existing->OwnerFile == file) { status = STATUS_OBJECT_NAME_COLLISION; goto done; }
        }
        status = PsLookupProcessByProcessId(ULongToHandle(command.ProcessId), &target);
        if (!NT_SUCCESS(status)) goto done;
        record = ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(RECORD), RELAY_TAG);
        if (!record) { ObDereferenceObject(target); status = STATUS_INSUFFICIENT_RESOURCES; goto done; }
        ObReferenceObject(caller); record->Owner = caller; record->Target = target;
        record->OwnerFile = file; record->Command = command;
        record->Expires = KeQueryInterruptTime() + 5000ull * 10000ull;
        record->Phase = SKIN_RELAY_QUEUED;
        InsertTailList(&context->Records, &record->Link); context->Count++;
        status = STATUS_SUCCESS; goto done;
    }
    if (code == SKIN_RELAY_CLAIM || code == SKIN_RELAY_FETCH || code == SKIN_RELAY_CANCEL || code == SKIN_RELAY_COMPLETE) {
        ULONGLONG nonce;
        if (code == SKIN_RELAY_COMPLETE) {
            SKIN_RELAY_RESULT header;
            if (inputLength < sizeof(header)) { status = STATUS_INVALID_BUFFER_SIZE; goto done; }
            RtlCopyMemory(&header, input, sizeof(header)); nonce = header.Nonce;
        } else {
            if (inputLength != sizeof(SKIN_RELAY_KEY)) { status = STATUS_INVALID_BUFFER_SIZE; goto done; }
            nonce = ((SKIN_RELAY_KEY*)input)->Nonce;
        }
        for (item = context->Records.Flink; item != &context->Records; item = item->Flink) {
            RECORD* candidate = CONTAINING_RECORD(item, RECORD, Link);
            if (candidate->Command.Nonce == nonce) { record = candidate; break; }
        }
        if (!record) { status = STATUS_NOT_FOUND; goto done; }
        if (code == SKIN_RELAY_CLAIM || code == SKIN_RELAY_COMPLETE) {
            if (caller != record->Target) { status = STATUS_ACCESS_DENIED; goto done; }
        } else if (file != record->OwnerFile || caller != record->Owner) { status = STATUS_ACCESS_DENIED; goto done; }
        if (record->Phase == SKIN_RELAY_QUEUED && KeQueryInterruptTime() > record->Expires) record->Phase = SKIN_RELAY_CANCELLED;
        if (code == SKIN_RELAY_CLAIM) {
            if (record->Phase != SKIN_RELAY_QUEUED) { status = STATUS_INVALID_DEVICE_STATE; goto done; }
            if (outputLength < sizeof(record->Command)) { status = STATUS_BUFFER_TOO_SMALL; goto done; }
            RtlCopyMemory(output, &record->Command, sizeof(record->Command)); information = sizeof(record->Command);
            record->Phase = SKIN_RELAY_RUNNING; status = STATUS_SUCCESS; goto done;
        }
        if (code == SKIN_RELAY_COMPLETE) {
            SKIN_RELAY_RESULT header;
            RtlCopyMemory(&header, input, sizeof(header));
            if (outputLength || header.Size != sizeof(header) || header.Version != SKIN_RELAY_VERSION || header.Phase != SKIN_RELAY_DONE ||
                !header.ResultBytes || header.ResultBytes >= SKIN_RELAY_RESULT_MAX || inputLength != sizeof(header) + header.ResultBytes) {
                status = STATUS_INVALID_BUFFER_SIZE; goto done;
            }
            if (record->Phase != SKIN_RELAY_RUNNING) { status = STATUS_INVALID_DEVICE_STATE; goto done; }
            RtlCopyMemory(record->Result, (CHAR*)input + sizeof(header), header.ResultBytes);
            record->ResultBytes = header.ResultBytes; record->Phase = SKIN_RELAY_DONE;
            status = STATUS_SUCCESS; goto done;
        }
        // METHOD_BUFFERED input/output can alias: nonce has already been copied.
        if (outputLength < sizeof(SKIN_RELAY_RESULT) + record->ResultBytes) { status = STATUS_BUFFER_TOO_SMALL; goto done; }
        if (code == SKIN_RELAY_CANCEL && record->Phase == SKIN_RELAY_QUEUED) record->Phase = SKIN_RELAY_CANCELLED;
        {
            SKIN_RELAY_RESULT header = { sizeof(header), SKIN_RELAY_VERSION, record->Phase, record->ResultBytes, record->Command.Nonce };
            RtlCopyMemory(output, &header, sizeof(header));
            if (record->ResultBytes) RtlCopyMemory((CHAR*)output + sizeof(header), record->Result, record->ResultBytes);
            information = sizeof(header) + record->ResultBytes; status = STATUS_SUCCESS;
        }
    }
done:
    WdfWaitLockRelease(context->Lock);
    WdfRequestCompleteWithInformation(request, status, NT_SUCCESS(status) ? information : 0);
}

#ifndef SKIN_RELAY_TEST
NTSTATUS DriverEntry(PDRIVER_OBJECT object, PUNICODE_STRING registry) {
    WDF_DRIVER_CONFIG config;
    WDFDRIVER driver;
    PWDFDEVICE_INIT init;
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_FILEOBJECT_CONFIG files;
    WDF_IO_QUEUE_CONFIG queue;
    NTSTATUS status;
    // Interactive users can only open READ handles for the target-bound executor.
    // Submission/fetch/cancel require WRITE access, limited to administrators/SYSTEM.
    DECLARE_CONST_UNICODE_STRING(sddl, L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;IU)");
    DECLARE_CONST_UNICODE_STRING(name, L"\\Device\\SkinCoreRelay");
    DECLARE_CONST_UNICODE_STRING(link, L"\\DosDevices\\SkinCoreRelay");
    WDF_DRIVER_CONFIG_INIT(&config, WDF_NO_EVENT_CALLBACK);
    config.DriverInitFlags = WdfDriverInitNonPnpDriver;
    config.EvtDriverUnload = RelayUnload;
    status = WdfDriverCreate(object, registry, WDF_NO_OBJECT_ATTRIBUTES, &config, &driver);
    if (!NT_SUCCESS(status)) return status;
    init = WdfControlDeviceInitAllocate(driver, &sddl);
    if (!init) return STATUS_INSUFFICIENT_RESOURCES;
    status = WdfDeviceInitAssignName(init, &name);
    if (!NT_SUCCESS(status)) { WdfDeviceInitFree(init); return status; }
    WdfDeviceInitSetDeviceType(init, FILE_DEVICE_UNKNOWN);
    WdfDeviceInitSetCharacteristics(init, FILE_DEVICE_SECURE_OPEN, FALSE);
    WDF_FILEOBJECT_CONFIG_INIT(&files, RelayCreate, WDF_NO_EVENT_CALLBACK, RelayCleanup);
    WdfDeviceInitSetFileObjectConfig(init, &files, WDF_NO_OBJECT_ATTRIBUTES);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DEVICE_CONTEXT);
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfDeviceCreate(&init, &attributes, &device);
    if (!NT_SUCCESS(status)) { if (init) WdfDeviceInitFree(init); return status; }
    InitializeListHead(&Context(device)->Records);
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes); attributes.ParentObject = device;
    status = WdfWaitLockCreate(&attributes, &Context(device)->Lock);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceCreateSymbolicLink(device, &link);
    if (!NT_SUCCESS(status)) return status;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue, WdfIoQueueDispatchSequential);
    queue.EvtIoDeviceControl = RelayIoctl;
    status = WdfIoQueueCreate(device, &queue, WDF_NO_OBJECT_ATTRIBUTES, NULL);
    if (!NT_SUCCESS(status)) return status;
    WdfControlFinishInitializing(device);
    return STATUS_SUCCESS;
}
#endif
