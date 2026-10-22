// Recovered original menu: DLL RVA 0x5d4ac..0x5d5ae, UTF-8 labels 0xa7c40/0xa7c50.
#pragma once
#include <cmath>
constexpr uintptr_t CameraSlot=0x1c85220;
constexpr unsigned char CameraLookup[]={0x48,0x8b,0x05,0xe8,0x67,0x92,0x01,0x48,0x85,0xc0,0x75,0x5a,0x48,0x8b,0x1d,0x04,0x68,0x92,0x01};
constexpr unsigned char CameraDefault[]={0xc7,0x41,0x20,0x00,0x00,0x16,0x44,0xc7,0x41,0x24,0x00,0x00,0x7a,0x44};
static bool cameraDistance(float value) { return std::isfinite(value) && value>=600.f && value<=1700.f; }
static int cameraBits(float value) { int bits;memcpy(&bits,&value,4);return bits; }
static float cameraFloat(int bits) { float value;memcpy(&value,&bits,4);return value; }
static bool cameraWritable(void* field,size_t bytes=4) {
    MEMORY_BASIC_INFORMATION info{};
    if((uintptr_t)field%4 || !VirtualQuery(field,&info,sizeof(info)) || info.State!=MEM_COMMIT || (info.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return false;
    DWORD mode=info.Protect&0xff;
    return (mode==PAGE_READWRITE || mode==PAGE_WRITECOPY || mode==PAGE_EXECUTE_READWRITE || mode==PAGE_EXECUTE_WRITECOPY) &&
        (uintptr_t)field+bytes<=(uintptr_t)info.BaseAddress+info.RegionSize;
}
// Saved game code: camera manager -> +0x18 controller; +0x3c0 current zoom,
// +0x3c4 target zoom. Constructor initializes both from config +0x28.
constexpr uintptr_t CameraManagerSlot=0x1c7c020;
constexpr unsigned char CameraViewLookup[]={0x48,0x8b,0x0d,0x83,0x11,0x62,0x01,0x48,0x8d,0x54,0x24,0x20,0xf3,0x0f,0x10,0x48,0x28,0xf3,0x0f,0x11,0x4c,0x24,0x20,0x48,0x8b,0x41,0x18,0xf3,0x0f,0x10,0x88,0xc0,0x03,0x00,0x00};
constexpr unsigned char CameraViewInit[]={0x48,0x8b,0x8b,0x78,0x04,0x00,0x00,0x48,0x89,0x8b,0xb0,0x03,0x00,0x00,0x8b,0x41,0x28,0x89,0x83,0xc0,0x03,0x00,0x00,0x8b,0x41,0x28,0x89,0x83,0xc4,0x03,0x00,0x00};
struct CameraViewPair {float current,target;};
static_assert(sizeof(CameraViewPair)==8);
static bool validCameraView(float value) {return std::isfinite(value) && value>=100.f && value<=10000.f;}
static bool cameraMove(CameraViewPair* fields,CameraViewPair expected,float selected) {
    if((uintptr_t)fields%8 || !validCameraView(selected) || !validCameraView(expected.current) || !validCameraView(expected.target) || !cameraWritable(fields,8))return false;
    LONG64 before,after;CameraViewPair values{selected,selected};memcpy(&before,&expected,8);memcpy(&after,&values,8);
    return InterlockedCompareExchange64((volatile LONG64*)fields,after,before)==before;
}
static bool cameraReplace(float* field,float expected,float selected) {
    if(!cameraDistance(expected) || !cameraDistance(selected) || !cameraWritable(field))return false;
    return InterlockedCompareExchange((volatile LONG*)field,cameraBits(selected),cameraBits(expected))==cameraBits(expected);
}
static void independentCamera(Request* r,Writer* w,IndependentContext* c,bool* invoked) {
    if(memcmp(c->base+0x35ea31,CameraLookup,sizeof(CameraLookup)) ||
       memcmp(c->base+0x346dfa,CameraDefault,sizeof(CameraDefault))) {fail(r,"camera_profile_not_supported");return;}
    BYTE* config=*(BYTE**)(c->base+CameraSlot);
    if(!objectReadable(config,0xa0) || !cameraWritable(config+0x20)) {fail(r,"camera_unavailable");return;}
    float* field=(float*)(config+0x20);float value=*field;
    if(!cameraDistance(value)){fail(r,"camera_state_not_supported");return;}
    float defaultView=*(float*)(config+0x28);
    if(!validCameraView(defaultView)){fail(r,"camera_default_view_not_verified");return;}
    if(r->command==17) {
        if(strcmp(r->session,c->session)){fail(r,"stale_session");return;}
        if(!cameraDistance(cameraFloat(r->index)) && cameraFloat(r->index)!=defaultView){fail(r,"invalid_camera_distance");return;}
        if(memcmp(c->base+0x65ae96,CameraViewLookup,sizeof(CameraViewLookup)) || memcmp(c->base+0x98a028,CameraViewInit,sizeof(CameraViewInit))) {fail(r,"camera_view_profile_not_supported");return;}
        BYTE* manager=*(BYTE**)(c->base+CameraManagerSlot);
        if(!objectReadable(manager,0x20)){fail(r,"camera_view_unavailable");return;}
        BYTE* camera=*(BYTE**)(manager+0x18);
        if(!objectReadable(camera,0x480) || *(BYTE**)(camera+0x478)!=config || *(BYTE**)(camera+0x3b0)!=config || camera[0x3e5]) {fail(r,"camera_view_unavailable");return;}
        auto fields=(CameraViewPair*)(camera+0x3c0);CameraViewPair expected;memcpy(&expected,fields,8);
        if(!cameraMove(fields,expected,cameraFloat(r->index))){fail(r,"camera_view_state_changed");return;}
        *invoked=true;CameraViewPair actual;memcpy(&actual,fields,8);
        uint64_t identity=digest(modelHash(c->session),&config,sizeof(config)),viewIdentity=digest(modelHash(c->session),&camera,sizeof(camera));
        append(w,"{\"ok\":true,\"independent\":true,\"original_required\":false,\"session\":\"%s\",\"reference_sha256\":\"%s\",\"request_id\":\"%016llx\",\"camera_id\":\"%016llx\",\"camera_default_view_distance\":%.9g,\"camera_distance\":%.9g,\"camera_view_id\":\"%016llx\",\"camera_view_distance\":%.9g,\"camera_view_target\":%.9g,\"invoked\":true,\"state_verified\":%s}",c->session,GameProfileSha,r->nonce,identity,(double)defaultView,(double)value,viewIdentity,(double)actual.current,(double)actual.target,(actual.current==cameraFloat(r->index) && actual.target==actual.current)?"true":"false");
        return;
    }
    if(r->command==16) {
        if(strcmp(r->session,c->session)){fail(r,"stale_session");return;}
        float selected=cameraFloat(r->index),expected=cameraFloat(r->gear);
        if(!cameraDistance(selected) || !cameraDistance(expected)){fail(r,"invalid_camera_distance");return;}
        if(!cameraReplace(field,expected,selected)){fail(r,"camera_state_changed");return;}
        *invoked=true;value=*field;
    }
    uint64_t identity=digest(modelHash(c->session),&config,sizeof(config));
    append(w,"{\"ok\":true,\"independent\":true,\"original_required\":false,\"session\":\"%s\",\"reference_sha256\":\"%s\",\"request_id\":\"%016llx\",\"camera_id\":\"%016llx\",\"camera_default_view_distance\":%.9g,\"camera_distance\":%.9g,\"invoked\":%s,\"state_verified\":%s}",
        c->session,GameProfileSha,r->nonce,identity,(double)defaultView,(double)value,*invoked?"true":"false",(r->command==15 || value==cameraFloat(r->index))?"true":"false");
}
