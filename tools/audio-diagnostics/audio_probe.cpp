#include <windows.h>
#include <mmdeviceapi.h>
#include <audiopolicy.h>
#include <endpointvolume.h>
#include <functiondiscoverykeys_devpkey.h>
#include <wrl/client.h>
#include <cstdio>
using Microsoft::WRL::ComPtr;
int main() {
 CoInitializeEx(nullptr,COINIT_MULTITHREADED);
 ComPtr<IMMDeviceEnumerator> en;
 HRESULT hr=CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&en));
 if(FAILED(hr)){printf("enumerator error %lx\n",hr);return 1;}
 DWORD fg=0; GetWindowThreadProcessId(GetForegroundWindow(),&fg);printf("foreground_pid=%lu\n",fg);
 ComPtr<IMMDeviceCollection> devices; en->EnumAudioEndpoints(eAll,DEVICE_STATE_ACTIVE,&devices);
 UINT count=0;devices->GetCount(&count);
 for(UINT i=0;i<count;i++) {
  ComPtr<IMMDevice> dev; devices->Item(i,&dev); LPWSTR id=nullptr;dev->GetId(&id);
  ComPtr<IPropertyStore> props; dev->OpenPropertyStore(STGM_READ,&props);PROPVARIANT name;PropVariantInit(&name);props->GetValue(PKEY_Device_FriendlyName,&name);
  wprintf(L"endpoint=%s id=%s\n",name.vt==VT_LPWSTR?name.pwszVal:L"?",id);CoTaskMemFree(id);PropVariantClear(&name);
  ComPtr<IAudioEndpointVolume> ev;
  if(SUCCEEDED(dev->Activate(__uuidof(IAudioEndpointVolume),CLSCTX_ALL,nullptr,&ev))) {float vol=0;BOOL mute=0;ev->GetMasterVolumeLevelScalar(&vol);ev->GetMute(&mute);printf(" endpoint_volume=%.3f mute=%d\n",vol,mute);}
  ComPtr<IAudioSessionManager2> mgr;if(FAILED(dev->Activate(__uuidof(IAudioSessionManager2),CLSCTX_ALL,nullptr,&mgr)))continue;
  ComPtr<IAudioSessionEnumerator> sessions;mgr->GetSessionEnumerator(&sessions);int n=0;sessions->GetCount(&n);
  for(int j=0;j<n;j++) {
   ComPtr<IAudioSessionControl> ctl;sessions->GetSession(j,&ctl);
   ComPtr<IAudioSessionControl2> ctl2;ctl.As(&ctl2);DWORD pid=0;ctl2->GetProcessId(&pid);
   ComPtr<ISimpleAudioVolume> vol;ctl.As(&vol);float level=0;BOOL mute=0;vol->GetMasterVolume(&level);vol->GetMute(&mute);AudioSessionState state;ctl->GetState(&state);
   wchar_t path[1024]=L"";DWORD size=1024;HANDLE proc=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);if(proc){QueryFullProcessImageNameW(proc,0,path,&size);CloseHandle(proc);}
   printf(" session pid=%lu volume=%.3f mute=%d state=%d\n",pid,level,mute,state);wprintf(L" process=%s\n",path);
  }
 }
 CoUninitialize();
}

