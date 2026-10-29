#define BUILD_HOST
#define main unused_host_main
#include "core.cpp"
#undef main
#include <cassert>
#include "runtime_test_support.h"
#include "runtime_profile_test_fixtures.h"
static BYTE* fixtureImage(const RuntimeFixture& fixture) {
    auto image=(BYTE*)VirtualAlloc(nullptr,fixture.size,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);assert(image);
    runtimeTestHeaders(image,fixture.size,fixture.timestamp);
    for(size_t n=0;n<fixture.count;n++)memcpy(image+fixture.blocks[n].rva,fixture.blocks[n].bytes,fixture.blocks[n].size);
    return image;
}
int main(int argc,char** argv) {
    if(argc==2) {
        HANDLE file=CreateFileA(argv[1],GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);assert(file!=INVALID_HANDLE_VALUE);
        LARGE_INTEGER size{};assert(GetFileSizeEx(file,&size) && size.QuadPart>0x1900000 && size.QuadPart<=256*1024*1024);
        auto image=(BYTE*)VirtualAlloc(nullptr,(size_t)size.QuadPart,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);assert(image);
        DWORD bytes=0;assert(ReadFile(file,image,(DWORD)size.QuadPart,&bytes,nullptr) && bytes==size.QuadPart);CloseHandle(file);
        runtimeTestHeaders(image,(DWORD)size.QuadPart,(DWORD)Game_timestamp);RuntimeProfile p{};
        bool resolved=runtimeResolve(image,&p);
        if(!resolved)printf("Snapshot rejected: %s / %s\n",p.error,p.stage);
        assert(resolved);
        printf("Saved executable snapshot resolved independently: player=%llx translate=%llx stack=%llx skin=%llx elapsed=%llu ms id=%s\n",
            (uint64_t)p.player_slot,(uint64_t)p.translate,(uint64_t)p.stack_offset,(uint64_t)p.skin_offset,p.elapsed,p.sha);
        VirtualFree(image,0,MEM_RELEASE);return 0;
    }
    for(const auto& fixture:RuntimeFixtures) {
        auto image=fixtureImage(fixture);RuntimeProfile p{};
        assert(runtimeResolve(image,&p));
        assert(p.timestamp==fixture.timestamp && p.image_size==fixture.size && strlen(p.sha)==64);
        assert(p.player_slot==fixture.player && p.champion_slot==fixture.champions && p.translate==fixture.translate);
        assert(p.update==fixture.update && p.push==fixture.push && p.stack_offset==fixture.stack && p.skin_offset==fixture.skin);
        char first[65];strcpy_s(first,p.sha);assert(runtimeResolve(image,&p) && !strcmp(first,p.sha));
        // A new build identifier must resolve rather than trip a date/size gate.
        auto nt=(IMAGE_NT_HEADERS64*)(image+0x100);nt->FileHeader.TimeDateStamp++;
        assert(runtimeResolve(image,&p) && strcmp(first,p.sha));
        VirtualFree(image,0,MEM_RELEASE);
    }
    const auto& fixture=RuntimeFixtures[1];
    {
        auto image=fixtureImage(fixture);RuntimeProfile p{};
        // Duplicate a real player locator at another address: no selection and
        // no "first hit wins" behavior is permitted.
        memcpy(image+0x10000,fixture.blocks[0].bytes,fixture.blocks[0].size);
        assert(!runtimeResolve(image,&p) && !strcmp(p.error,"profile_signature_ambiguous") && !p.ready);
        assert(!strcmp(p.stage,"locator:player"));VirtualFree(image,0,MEM_RELEASE);
    }
    {
        auto image=fixtureImage(fixture);RuntimeProfile p{};
        image[fixture.blocks[2].rva]^=1;
        assert(!runtimeResolve(image,&p) && !strcmp(p.error,"profile_signature_missing") && !strcmp(p.stage,"locator:update"));
        VirtualFree(image,0,MEM_RELEASE);
    }
    {
        auto image=fixtureImage(fixture);RuntimeProfile p{};
        image[fixture.blocks[7].rva+29]^=1; // Skin decoder XOR opcode/constant region.
        assert(!runtimeResolve(image,&p) && !p.ready);VirtualFree(image,0,MEM_RELEASE);
    }
    {
        auto image=fixtureImage(fixture);RuntimeProfile p{};
        int32_t bad=(int32_t)(0x11000-(fixture.blocks[0].rva+7));memcpy(image+fixture.blocks[0].rva+3,&bad,4);
        assert(!runtimeResolve(image,&p) && !strcmp(p.error,"profile_schema_not_verified"));
        VirtualFree(image,0,MEM_RELEASE);
    }
    {
        auto image=fixtureImage(fixture);RuntimeProfile p{};DWORD old;
        assert(VirtualProtect(image+0x1000,4096,PAGE_NOACCESS,&old));
        assert(!runtimeResolve(image,&p) && !strcmp(p.error,"profile_code_not_ready"));
        VirtualFree(image,0,MEM_RELEASE);
    }
    {
        auto image=fixtureImage(fixture);assert(runtimeResolve(image,&runtimeGame));
        assert(runtimeResolveObjects(image));
        assert(!runtimeResolveCamera(image,false)); // Camera absent in this fixture.
        assert(runtimeResolve(image,&runtimeGame) && runtimeGame.ready); // Hero remains usable.
        VirtualFree(image,0,MEM_RELEASE);runtimeGame=RuntimeProfile{};
    }
    puts("Runtime resolver: both saved builds, synthetic relocated functions/globals/layouts, changed build ID, ambiguity, missing code, changed decoder, invalid targets, unreadable pages and feature isolation passed.");
}
