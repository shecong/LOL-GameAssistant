#pragma once
// The GUI keeps this anonymous mapping alive between short-lived host requests.
// Only scalar RVAs/rule indexes are shared, never callable process pointers.
constexpr DWORD RuntimeCacheMagic=0x52504331,RuntimeCacheVersion=1;
constexpr size_t RuntimeCacheCapacity=16384;
constexpr wchar_t RuntimeCacheName[]=L"Local\\LeagueSkinCore.Auto.ProfileCache.v1";
#pragma pack(push,1)
struct RuntimeCachedMatch {uint64_t rva;int32_t pattern;};
struct RuntimeCacheData {
    DWORD pid,timestamp,size;uint64_t created;char rules[65];RuntimeCachedMatch matches[RuntimeRuleCount];
};
#pragma pack(pop)
struct RuntimeCacheRow {volatile LONG phase;RuntimeCacheData data;uint64_t checksum;};
struct RuntimeCacheMap {volatile LONG magic,version;RuntimeCacheRow rows[8];};
static_assert(sizeof(RuntimeCacheMap)<RuntimeCacheCapacity);
static bool runtimeCacheIdentity(const RuntimeImage& image,RuntimeCacheData* data) {
    FILETIME created,exited,kernel,user;
    if(!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user))return false;
    data->pid=GetCurrentProcessId();data->timestamp=image.timestamp;data->size=image.size;
    memcpy(&data->created,&created,8);strcpy_s(data->rules,RuntimeRulesSha);return true;
}
static bool runtimeCacheSame(const RuntimeCacheData& left,const RuntimeCacheData& right) {
    return left.pid==right.pid && left.created==right.created && left.timestamp==right.timestamp && left.size==right.size &&
        !memcmp(left.rules,right.rules,sizeof(left.rules));
}
static RuntimeCacheMap* runtimeCacheOpen(HANDLE* handle) {
    *handle=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,(DWORD)RuntimeCacheCapacity,RuntimeCacheName);
    if(!*handle)return nullptr;
    auto map=(RuntimeCacheMap*)MapViewOfFile(*handle,FILE_MAP_ALL_ACCESS,0,0,RuntimeCacheCapacity);
    if(!map){CloseHandle(*handle);*handle=nullptr;return nullptr;}
    if(InterlockedCompareExchange(&map->magic,RuntimeCacheMagic,0)==0)InterlockedExchange(&map->version,RuntimeCacheVersion);
    if(map->magic!=RuntimeCacheMagic || map->version!=RuntimeCacheVersion){UnmapViewOfFile(map);CloseHandle(*handle);*handle=nullptr;return nullptr;}
    return map;
}
static bool runtimeCacheLoad(BYTE* base,RuntimeProfile* profile) {
    RuntimeImage image{};RuntimeCacheData identity{};
    if(!runtimeImage(base,&image) || !runtimeCacheIdentity(image,&identity))return false;
    HANDLE file=nullptr;auto map=runtimeCacheOpen(&file);if(!map)return false;
    bool found=false;RuntimeCacheData saved{};
    for(auto& row:map->rows) {
        if(InterlockedCompareExchange(&row.phase,2,1)!=1)continue;
        memcpy(&saved,&row.data,sizeof(saved));uint64_t checksum=row.checksum;InterlockedExchange(&row.phase,1);
        if(checksum!=digest(0xcbf29ce484222325ULL,&saved,sizeof(saved)) || !runtimeCacheSame(saved,identity))continue;
        bool valid=true;
        for(unsigned n=0;n<RuntimeRuleCount;n++) {
            int index=saved.matches[n].pattern;
            if(index<0){if(n<9)valid=false;continue;}
            if(index>=(int)(sizeof(RuntimePatterns)/sizeof(RuntimePatterns[0])) || RuntimePatterns[index].key!=n ||
                !runtimePatternAt(image,RuntimePatterns[index],(uintptr_t)saved.matches[n].rva)){valid=false;break;}
        }
        if(!valid)continue;
        *profile=RuntimeProfile{};profile->base=base;profile->timestamp=image.timestamp;profile->image_size=image.size;
        for(unsigned n=0;n<RuntimeRuleCount;n++)profile->matches[n]={(uintptr_t)saved.matches[n].rva,saved.matches[n].pattern};
        found=true;break;
    }
    UnmapViewOfFile(map);CloseHandle(file);return found;
}
static void runtimeCacheSave(const RuntimeProfile& profile) {
    if(!profile.ready)return;RuntimeImage image{};RuntimeCacheData saved{};
    if(!runtimeImage(profile.base,&image) || !runtimeCacheIdentity(image,&saved))return;
    for(unsigned n=0;n<RuntimeRuleCount;n++)saved.matches[n]={(uint64_t)profile.matches[n].rva,profile.matches[n].pattern};
    HANDLE file=nullptr;auto map=runtimeCacheOpen(&file);if(!map)return;
    RuntimeCacheRow* selected=nullptr;
    for(auto& row:map->rows) {
        if(InterlockedCompareExchange(&row.phase,2,1)!=1)continue;
        if(runtimeCacheSame(row.data,saved)){selected=&row;break;}
        InterlockedExchange(&row.phase,1);
    }
    if(!selected)for(auto& row:map->rows)if(InterlockedCompareExchange(&row.phase,2,0)==0){selected=&row;break;}
    // Reuse a slot if the bounded cache is full; readers never wait for writers.
    if(!selected)for(auto& row:map->rows)if(InterlockedCompareExchange(&row.phase,2,1)==1){selected=&row;break;}
    if(selected) {
        memcpy(&selected->data,&saved,sizeof(saved));selected->checksum=digest(0xcbf29ce484222325ULL,&saved,sizeof(saved));
        InterlockedExchange(&selected->phase,1);
    }
    UnmapViewOfFile(map);CloseHandle(file);
}
