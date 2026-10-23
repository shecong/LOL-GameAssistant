#define SKIN_RELAY_TEST
#include "../driver.c"
#include <stdio.h>
#define CHECK(x) do { if (!(x)) { printf("FAIL dispatch line %d\n", __LINE__); exit(1); } } while (0)
static BYTE Buffer[SKIN_RELAY_RESULT_MAX + sizeof(SKIN_RELAY_RESULT) + 16];
static SIM_REQUEST Send(DEVICE_CONTEXT* context, SIM_FILE* file, int actor, ULONG code, const void* data, size_t input, size_t output) {
    SIM_REQUEST request;
    CHECK(input <= sizeof(Buffer) && output <= sizeof(Buffer));
    memset(Buffer, 0xcc, sizeof(Buffer)); if (input) memcpy(Buffer, data, input);
    request = (SIM_REQUEST){file, &Processes[actor], Buffer, input, output, STATUS_UNSUCCESSFUL, 0};
    RelayIoctl(context, &request, output, input, code);
    CHECK(request.Information <= output);
    if (!NT_SUCCESS(request.Status)) CHECK(request.Information == 0);
    return request;
}
int main(void) {
    DEVICE_CONTEXT context = {0};
    SIM_FILE owner = {&context}, target = {&context}, other = {&context};
    SKIN_RELAY_COMMAND command = {sizeof(command), SKIN_RELAY_VERSION, 8, 200, 12345, 1000, 0, -1, "0123456789abcdef"};
    SKIN_RELAY_KEY key = {command.Nonce};
    SIM_REQUEST result;
    BYTE packet[sizeof(SKIN_RELAY_RESULT) + 16] = {0};
    SKIN_RELAY_RESULT completion = {sizeof(completion), SKIN_RELAY_VERSION, SKIN_RELAY_DONE, 2, key.Nonce};
    InitializeListHead(&context.Records);
    CHECK(Send(&context, &owner, 0, SKIN_RELAY_CAPS, NULL, 0, 1).Status == STATUS_INVALID_BUFFER_SIZE);
    CHECK(NT_SUCCESS(Send(&context, &owner, 0, SKIN_RELAY_CAPS, NULL, 0, sizeof(SKIN_RELAY_CAPABILITIES)).Status));
    CHECK(((SKIN_RELAY_CAPABILITIES*)Buffer)->Version == SKIN_RELAY_VERSION);
    CHECK(Send(&context, &owner, 0, SKIN_RELAY_SUBMIT, &command, sizeof(command) - 1, 0).Status == STATUS_INVALID_BUFFER_SIZE);
    command.Version++;
    CHECK(Send(&context, &owner, 0, SKIN_RELAY_SUBMIT, &command, sizeof(command), 0).Status == STATUS_INVALID_PARAMETER);
    command.Version--;
    CHECK(NT_SUCCESS(Send(&context, &owner, 0, SKIN_RELAY_SUBMIT, &command, sizeof(command), 0).Status));
    CHECK(context.Count == 1 && Allocations == 1);
    CHECK(Send(&context, &owner, 0, SKIN_RELAY_SUBMIT, &command, sizeof(command), 0).Status == STATUS_OBJECT_NAME_COLLISION);
    CHECK(Send(&context, &other, 0, SKIN_RELAY_FETCH, &key, sizeof(key), sizeof(Buffer)).Status == STATUS_ACCESS_DENIED);
    CHECK(Send(&context, &owner, 2, SKIN_RELAY_FETCH, &key, sizeof(key), sizeof(Buffer)).Status == STATUS_ACCESS_DENIED);
    CHECK(Send(&context, &target, 2, SKIN_RELAY_CLAIM, &key, sizeof(key), sizeof(command)).Status == STATUS_ACCESS_DENIED);
    CHECK(Send(&context, &target, 1, SKIN_RELAY_CLAIM, &key, sizeof(key), 1).Status == STATUS_BUFFER_TOO_SMALL);
    CHECK(NT_SUCCESS(Send(&context, &target, 1, SKIN_RELAY_CLAIM, &key, sizeof(key), sizeof(command)).Status));
    CHECK(((SKIN_RELAY_COMMAND*)Buffer)->Nonce == key.Nonce);
    CHECK(Send(&context, &target, 1, SKIN_RELAY_CLAIM, &key, sizeof(key), sizeof(command)).Status == STATUS_INVALID_DEVICE_STATE);
    CHECK(NT_SUCCESS(Send(&context, &owner, 0, SKIN_RELAY_CANCEL, &key, sizeof(key), sizeof(Buffer)).Status));
    CHECK(((SKIN_RELAY_RESULT*)Buffer)->Phase == SKIN_RELAY_RUNNING);
    memcpy(packet, &completion, sizeof(completion)); memcpy(packet + sizeof(completion), "{}", 2);
    CHECK(Send(&context, &target, 2, SKIN_RELAY_COMPLETE, packet, sizeof(completion) + 2, 0).Status == STATUS_ACCESS_DENIED);
    CHECK(Send(&context, &target, 1, SKIN_RELAY_COMPLETE, packet, sizeof(completion) + 1, 0).Status == STATUS_INVALID_BUFFER_SIZE);
    CHECK(NT_SUCCESS(Send(&context, &target, 1, SKIN_RELAY_COMPLETE, packet, sizeof(completion) + 2, 0).Status));
    CHECK(Send(&context, &target, 1, SKIN_RELAY_COMPLETE, packet, sizeof(completion) + 2, 0).Status == STATUS_INVALID_DEVICE_STATE);
    result = Send(&context, &owner, 0, SKIN_RELAY_FETCH, &key, sizeof(key), sizeof(Buffer));
    CHECK(NT_SUCCESS(result.Status) && result.Information == sizeof(completion) + 2);
    CHECK(((SKIN_RELAY_RESULT*)Buffer)->Phase == SKIN_RELAY_DONE && !memcmp(Buffer + sizeof(completion), "{}", 2));
    RelayCleanup(&owner); CHECK(context.Count == 0 && Allocations == 0);
    CHECK(Send(&context, &target, 1, SKIN_RELAY_CLAIM, &key, sizeof(key), sizeof(command)).Status == STATUS_NOT_FOUND);
    CHECK(NT_SUCCESS(Send(&context, &owner, 0, SKIN_RELAY_SUBMIT, &command, sizeof(command), 0).Status));
    CHECK(NT_SUCCESS(Send(&context, &owner, 0, SKIN_RELAY_CANCEL, &key, sizeof(key), sizeof(Buffer)).Status));
    CHECK(((SKIN_RELAY_RESULT*)Buffer)->Phase == SKIN_RELAY_CANCELLED);
    CHECK(Send(&context, &target, 1, SKIN_RELAY_CLAIM, &key, sizeof(key), sizeof(command)).Status == STATUS_INVALID_DEVICE_STATE);
    RelayCleanup(&owner);
    CHECK(NT_SUCCESS(Send(&context, &owner, 0, SKIN_RELAY_SUBMIT, &command, sizeof(command), 0).Status));
    SimTime += 5001ull * 10000ull;
    CHECK(Send(&context, &target, 1, SKIN_RELAY_CLAIM, &key, sizeof(key), sizeof(command)).Status == STATUS_INVALID_DEVICE_STATE);
    RelayCleanup(&owner);
    {
        SIM_FILE files[5]; int n;
        for (n = 0; n < 5; n++) {
            files[n].Device = &context; command.Nonce++;
            result = Send(&context, &files[n], 0, SKIN_RELAY_SUBMIT, &command, sizeof(command), 0);
            CHECK(n < 4 ? NT_SUCCESS(result.Status) : result.Status == STATUS_QUOTA_EXCEEDED);
        }
        for (n = 0; n < 5; n++) RelayCleanup(&files[n]);
    }
    CHECK(context.Count == 0 && Allocations == 0);
    CHECK(Processes[0].References == 0 && Processes[1].References == 0 && Processes[2].References == 0);
    puts("Driver dispatch checks passed: ownership, target identity, replay, cancellation, expiry, quota, cleanup. Windows kernel runtime not tested.");
    return 0;
}
