#include "protocol.h"
#include <d3d11.h>
#include <dxgi.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <cstdio>
#include <cstring>
using Microsoft::WRL::ComPtr;
using namespace overlay;
static void Check(bool condition, const char* message) { if (!condition) { printf("FAIL: %s\n", message); fflush(stdout); ExitProcess(1); } }
static unsigned gameWheelMessages=0;
static LRESULT CALLBACK TestWndProc(HWND window,UINT message,WPARAM wParam,LPARAM lParam) {
    if(message==WM_MOUSEWHEEL) ++gameWheelMessages;
    return DefWindowProcW(window,message,wParam,lParam);
}

int main(int argc, char** argv) {
    bool exclusive=false,borderless=false,flip=false;
    for(int i=1;i<argc;++i) { exclusive|=strcmp(argv[i],"--exclusive")==0; borderless|=strcmp(argv[i],"--borderless")==0; flip|=strcmp(argv[i],"--flip")==0; }
    wchar_t name[160]{}; uint64_t nonce = GetTickCount64() | 1;
    MappingName(name, GetCurrentProcessId(), GetCurrentProcessId(), nonce);
    HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, DWORD(MappingSize), name);
    Check(mapping != nullptr, "create mapping");
    auto frame = static_cast<Frame*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, MappingSize));
    Check(frame != nullptr, "map frame");
    wchar_t mutexName[180]{}; swprintf_s(mutexName, L"%s.Lock", name);
    HANDLE mutex = CreateMutexW(nullptr, FALSE, mutexName); Check(mutex != nullptr, "create mutex");
    frame->magic = Magic; frame->version = Version; frame->ownerPid = frame->gamePid = GetCurrentProcessId();
    frame->enabled = 1; frame->mayhemEnabled = 1; frame->heartbeat = GetTickCount64();
    strcpy_s(frame->title, "海克斯大乱斗 · 增幅推荐");
    strcpy_s(frame->statusText, "DirectX 11 全屏覆盖层渲染验证");
    strcpy_s(frame->recommendations, "白银 / 黄金 / 棱彩\n双刀流 50.8% · 119,846 场\n连拨击锤 50.6% · 115,327 场");
    strcpy_s(frame->offered, "本次识别\n1. 双刀流\n2. 连拨击锤");
    WNDCLASSW wc{}; wc.lpfnWndProc = TestWndProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"SC.Overlay.Smoke";
    RegisterClassW(&wc);
    HWND window = CreateWindowExW(WS_EX_NOACTIVATE, wc.lpszClassName, L"SC Overlay Render Test", borderless ? WS_POPUP : WS_OVERLAPPEDWINDOW,
        10, 10, 1280, 720, nullptr, nullptr, wc.hInstance, nullptr);
    Check(window != nullptr, "test window"); ShowWindow(window, SW_SHOWNOACTIVATE);
    DXGI_SWAP_CHAIN_DESC desc{}; desc.BufferCount = 2; desc.BufferDesc.Width = 1280; desc.BufferDesc.Height = 720;
    desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.OutputWindow = window; desc.SampleDesc.Count = 1; desc.Windowed = TRUE;
    if(flip) desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
    ComPtr<IDXGISwapChain> chain; ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    Check(SUCCEEDED(D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
        &desc, &chain, &device, nullptr, &context)), "D3D11 hardware device");
    const char computeSource[]="[numthreads(1,1,1)] void main(uint3 id:SV_DispatchThreadID) {}";
    ComPtr<ID3DBlob> code,errors; ComPtr<ID3D11ComputeShader> gameCompute;
    Check(SUCCEEDED(D3DCompile(computeSource,sizeof(computeSource),nullptr,nullptr,nullptr,"main","cs_5_0",0,0,&code,&errors)),"compile game compute shader");
    Check(SUCCEEDED(device->CreateComputeShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&gameCompute)),"game compute shader");
    D3D11_BUFFER_DESC ud{}; ud.ByteWidth=16; ud.Usage=D3D11_USAGE_DEFAULT; ud.BindFlags=D3D11_BIND_UNORDERED_ACCESS;
    ud.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED; ud.StructureByteStride=4;
    ComPtr<ID3D11Buffer> ub; ComPtr<ID3D11UnorderedAccessView> gameUav;
    Check(SUCCEEDED(device->CreateBuffer(&ud,nullptr,&ub)) && SUCCEEDED(device->CreateUnorderedAccessView(ub.Get(),nullptr,&gameUav)),"game UAV");
    if (exclusive) {
        DWORD foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),nullptr);
        bool joined=foregroundThread!=GetCurrentThreadId() && AttachThreadInput(GetCurrentThreadId(),foregroundThread,TRUE);
        ShowWindow(window, SW_SHOWNORMAL); SetForegroundWindow(window);
        if(joined) AttachThreadInput(GetCurrentThreadId(),foregroundThread,FALSE);
        HRESULT full = chain->SetFullscreenState(TRUE, nullptr);
        if (FAILED(full)) { printf("EXCLUSIVE UNAVAILABLE: 0x%08lx\n", full); return 2; }
    }
    HMODULE module = LoadLibraryW(L"sc-game-overlay-test.dll"); Check(module != nullptr, "load test module");
    auto bootstrap = reinterpret_cast<HOOKPROC>(GetProcAddress(module, "OverlayBootstrap"));
    HHOOK hook = bootstrap ? SetWindowsHookExW(WH_GETMESSAGE, bootstrap, module, GetCurrentThreadId()) : nullptr;
    Check(hook != nullptr, "bootstrap Windows message hook");
    Check(PostThreadMessageW(GetCurrentThreadId(), RegisterWindowMessageW(StartMessage), GetCurrentProcessId(), LPARAM(nonce)) != FALSE, "post bootstrap request");
    ULONGLONG installDeadline = GetTickCount64() + 10000;
    while (frame->status == 0 && GetTickCount64() < installDeadline) {
        MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        Sleep(10);
    }
    Check(UnhookWindowsHookEx(hook) != FALSE, "release bootstrap hook");
    Check(frame->status == 1, "hook installed handshake");
    auto syncSnapshot=reinterpret_cast<void(*)()>(GetProcAddress(module,"OverlayTestSync"));
    Check(syncSnapshot!=nullptr,"test snapshot fixture");
    bool savedPreview = flip;
    auto present = [&](bool changed=true) {
        if(changed) ++frame->revision;
        syncSnapshot();
        MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        ComPtr<ID3D11Texture2D> back; ComPtr<ID3D11RenderTargetView> view;
        Check(SUCCEEDED(chain->GetBuffer(0, IID_PPV_ARGS(&back))), "get backbuffer");
        Check(SUCCEEDED(device->CreateRenderTargetView(back.Get(), nullptr, &view)), "render target");
        const float color[]{0.1f, 0.2f, 0.3f, 1.f}; context->ClearRenderTargetView(view.Get(), color);
        ID3D11RenderTargetView* target = view.Get(); ID3D11UnorderedAccessView* uav=gameUav.Get(); UINT preserve=UINT(-1);
        context->OMSetRenderTargetsAndUnorderedAccessViews(1,&target,nullptr,1,1,&uav,&preserve);
        context->CSSetShader(gameCompute.Get(),nullptr,0);
        Check(SUCCEEDED(chain->Present(0, 0)), "present");
        if (!savedPreview && frame->status == 2) {
            D3D11_TEXTURE2D_DESC readDesc{}; back->GetDesc(&readDesc);
            readDesc.Usage = D3D11_USAGE_STAGING; readDesc.BindFlags = readDesc.MiscFlags = 0; readDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            ComPtr<ID3D11Texture2D> readback;
            Check(SUCCEEDED(device->CreateTexture2D(&readDesc, nullptr, &readback)), "preview staging texture");
            context->CopyResource(readback.Get(), back.Get());
            D3D11_MAPPED_SUBRESOURCE data{};
            Check(SUCCEEDED(context->Map(readback.Get(), 0, D3D11_MAP_READ, 0, &data)), "preview readback");
            unsigned changed = 0;
            for (UINT y = 0; y < readDesc.Height; ++y) {
                auto row = static_cast<const unsigned char*>(data.pData) + size_t(y)*data.RowPitch;
                for (UINT x = 0; x < readDesc.Width; ++x) if (row[x*4+1] != 51) ++changed;
            }
            Check(changed > 4000, "overlay visibly changes game pixels");
            FILE* file = nullptr; fopen_s(&file, "smoke-preview.bmp", "wb");
            Check(file != nullptr, "create preview");
            BITMAPFILEHEADER header{}; header.bfType = 0x4d42; header.bfOffBits = sizeof(header)+sizeof(BITMAPINFOHEADER);
            header.bfSize = header.bfOffBits + readDesc.Width*readDesc.Height*4;
            BITMAPINFOHEADER info{}; info.biSize = sizeof(info); info.biWidth = readDesc.Width; info.biHeight = -LONG(readDesc.Height);
            info.biPlanes = 1; info.biBitCount = 32; info.biCompression = BI_RGB;
            fwrite(&header, sizeof(header), 1, file); fwrite(&info, sizeof(info), 1, file);
            for (UINT y = 0; y < readDesc.Height; ++y) {
                auto row = static_cast<const unsigned char*>(data.pData) + size_t(y)*data.RowPitch;
                for (UINT x = 0; x < readDesc.Width; ++x) { unsigned char pixel[]{row[x*4+2],row[x*4+1],row[x*4],255}; fwrite(pixel, 4, 1, file); }
            }
            fclose(file); context->Unmap(readback.Get(), 0); savedPreview = true;
        }
        ComPtr<ID3D11RenderTargetView> restored; context->OMGetRenderTargets(1, &restored, nullptr);
        Check(restored.Get() == target || (flip && !restored), "game output target restored (flip Present may unbind it)");
        ComPtr<ID3D11ComputeShader> restoredCompute; context->CSGetShader(&restoredCompute,nullptr,nullptr);
        Check(restoredCompute.Get()==gameCompute.Get(),"game compute stage restored");
        ComPtr<ID3D11UnorderedAccessView> restoredUav;
        context->OMGetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,1,1,&restoredUav);
        Check(restoredUav.Get()==gameUav.Get(),"game output UAV restored");
        context->OMSetRenderTargets(0, nullptr, nullptr);
    };
    if(exclusive) {
        DWORD thread=GetWindowThreadProcessId(GetForegroundWindow(),nullptr);
        bool joined=thread!=GetCurrentThreadId() && AttachThreadInput(GetCurrentThreadId(),thread,TRUE);
        ShowWindow(window,SW_SHOWNORMAL); SetForegroundWindow(window);
        if(joined) AttachThreadInput(GetCurrentThreadId(),thread,FALSE);
        Check(SUCCEEDED(chain->SetFullscreenState(TRUE,nullptr)),"enter fullscreen after fixture setup");
    }
    frame->captureRequest = 1;
    present();
    Check(frame->status == 2 && frame->renderedFrames == 1, "actual game-frame rendering");
    if (exclusive) {
        BOOL fullscreen = FALSE; chain->GetFullscreenState(&fullscreen, nullptr);
        if(!fullscreen || GetForegroundWindow()!=window) printf("fullscreen diagnostic: state=%d focus=%d\n",fullscreen,GetForegroundWindow()==window);
        Check(fullscreen && GetForegroundWindow() == window, "exclusive fullscreen and game focus preserved");
        puts("PASS: exclusive fullscreen rendering keeps game focus");
        chain->SetFullscreenState(FALSE, nullptr);
    }
    Check(frame->captureResponse==0,"capture defers GPU readback to later frame");
    auto waitCapture=[&](LONG request) {
        ULONGLONG deadline=GetTickCount64()+2500;
        while(frame->captureResponse!=request && GetTickCount64()<deadline) { present(false); Sleep(1); }
        Check(frame->captureResponse==request && frame->captureError==0,"async capture completes");
    };
    waitCapture(1);
    auto beforeDisable=frame->renderedFrames;
    Check(frame->captureResponse == 1 && frame->captureError == 0, "pre-overlay backbuffer capture");
    Check(frame->captureWidth == 853 && frame->captureHeight == 230, "capture region dimensions");
    auto pixels = reinterpret_cast<unsigned char*>(frame) + PixelOffset;
    Check(pixels[0] >= 75 && pixels[0] <= 78 && pixels[1] == 51 && pixels[2] >= 25 && pixels[2] <= 27, "RGBA converted to BGRA");
    // The captured region overlaps the panel. Its final pixel must still be the
    // game background, proving capture precedes overlay rendering.
    size_t last = (size_t(frame->captureWidth) * frame->captureHeight - 1) * 4;
    Check(pixels[last] >= 75 && pixels[last] <= 78 && pixels[last+1] == 51, "overlay excluded from OCR image");
    frame->enabled = 0; present(); Check(frame->renderedFrames == beforeDisable, "disable hides rendering");
    frame->enabled = 1; frame->mayhemEnabled = 1; frame->heartbeat = GetTickCount64() - 6000;
    present(); Check(frame->renderedFrames == beforeDisable, "expired owner heartbeat hides rendering");
    frame->heartbeat = GetTickCount64(); frame->collapsed = 1; present();
    Check(frame->renderedFrames == beforeDisable+1, "collapsed panel renders");
    Check(frame->panelWidth == 48 && frame->panelHeight == 48 && frame->hitRegionCount == 1, "collapsed hit area is only 48x48");
    // Exercise actual window messages, without Alt or a desktop overlay window.
    auto region = reinterpret_cast<BOOL(*)(unsigned,RECT*)>(GetProcAddress(module,"OverlayTestRegion"));
    auto setPointer = reinterpret_cast<void(*)(LONG,LONG,BOOL)>(GetProcAddress(module,"OverlayTestPointer"));
    Check(setPointer!=nullptr,"test pointer fixture");
    RECT bubble{}; Check(region && region(0,&bubble),"get test hit region");
    RECT clientBounds{}; GetClientRect(window,&clientBounds);
    Check(abs(bubble.right-clientBounds.right)<=1,"collapsed entry docks to right edge");
    HWND previousForeground=GetForegroundWindow(); POINT previousPointer{}; GetCursorPos(&previousPointer);
    DWORD foregroundThread=GetWindowThreadProcessId(previousForeground,nullptr);
    bool joined=foregroundThread!=GetCurrentThreadId() && AttachThreadInput(GetCurrentThreadId(),foregroundThread,TRUE);
    ShowWindow(window,SW_SHOWNORMAL); SetForegroundWindow(window);
    if(joined) AttachThreadInput(GetCurrentThreadId(),foregroundThread,FALSE);
    // Use a deterministic client pointer so real user mouse movements cannot
    // redirect a test click. Window-message filtering and ImGui input remain real.
    setPointer((bubble.left+bubble.right)/2,(bubble.top+bubble.bottom)/2,TRUE);
    Check(!(GetAsyncKeyState(VK_MENU)&0x8000),"test does not hold Alt");
    present();
    SendMessageW(window,WM_LBUTTONDOWN,MK_LBUTTON,0); present();
    SendMessageW(window,WM_LBUTTONUP,0,0); present();
    if(frame->command!=2 || GetForegroundWindow()!=window) {
        POINT actual{}; GetCursorPos(&actual); ScreenToClient(window,&actual);
        RECT nowClient{}; GetClientRect(window,&nowClient); RECT nowBubble{}; region(0,&nowBubble);
        printf("click diagnostic: command=%ld focus=%d cursor=%ld,%ld region=%ld,%ld,%ld,%ld client=%ld,%ld\n",frame->command,
            GetForegroundWindow()==window,actual.x,actual.y,nowBubble.left,nowBubble.top,nowBubble.right,nowBubble.bottom,nowClient.right,nowClient.bottom); fflush(stdout);
    }
    Check(frame->command==2 && GetForegroundWindow()==window,"click collapsed entry without Alt preserves game focus");
    frame->command=0;
    frame->collapsed=0; present(); present();
    RECT headerPanel{}; Check(region(0,&headerPanel),"get expanded panel hit region");
    const POINT headerClicks[]={{5,5},{45,23},{220,23},{350,23},{45,50}};
    for(const auto& point:headerClicks) {
        setPointer(headerPanel.left+point.x,headerPanel.top+point.y,TRUE);
        present(); SendMessageW(window,WM_LBUTTONDOWN,MK_LBUTTON,0); present();
        SendMessageW(window,WM_LBUTTONUP,0,0); present();
        Check(frame->command==2 && GetForegroundWindow()==window,"entire header collapses without losing game focus");
        frame->command=0;
    }
    setPointer(headerPanel.left+180,headerPanel.top+23,TRUE); present();
    SendMessageW(window,WM_LBUTTONDOWN,MK_LBUTTON,0); present();
    setPointer(headerPanel.left+140,headerPanel.top+53,TRUE); present();
    SendMessageW(window,WM_LBUTTONUP,0,0); present();
    RECT movedPanel{}; Check(region(0,&movedPanel),"get dragged panel hit region");
    Check(frame->command==0 && movedPanel.left<headerPanel.left && movedPanel.top>headerPanel.top,
        "dragging header moves panel without collapsing");
    auto aux=reinterpret_cast<Auxiliary*>(reinterpret_cast<unsigned char*>(frame)+AuxOffset);
    frame->mayhemEnabled=0; aux->flags=2|4|8;
    strcpy_s(aux->coachTitle,"当前建议"); strcpy_s(aux->coachBody,"独立于增幅侧栏显示");
    strcpy_s(aux->skinToast,"已切换皮肤"); strcpy_s(aux->skinTitle,"选择皮肤");
    strcpy_s(aux->skinNames[0],"基础皮肤"); aux->skinCount=1; aux->skinReady=1; aux->skinCanRefresh=1; aux->skinVersion=42;
    present(); present();
    Check(frame->panelWidth==0 && frame->hitRegionCount==1,"other panels render while mayhem is disabled");
    RECT picker{}; Check(region(0,&picker) && picker.right>picker.left,"skin picker owns separate hit area");
    auto skinControl=reinterpret_cast<BOOL(*)(int,int,RECT*)>(GetProcAddress(module,"OverlayTestSkinControl"));
    Check(skinControl!=nullptr,"skin category and list test controls available");
    auto clickSkinControl=[&](int kind,int index) {
        RECT r{}; Check(skinControl(kind,index,&r)!=FALSE,"skin control has hit rectangle");
        setPointer((r.left+r.right)/2,(r.top+r.bottom)/2,TRUE);
        present(); SendMessageW(window,WM_LBUTTONDOWN,MK_LBUTTON,0); present(); SendMessageW(window,WM_LBUTTONUP,0,0); present();
    };
    clickSkinControl(1,0);
    Check(aux->command==1 && aux->argument==0 && aux->commandVersion==42,"skin selection command includes list version");
    aux->command=0; clickSkinControl(0,1);
    Check(aux->command==5 && aux->argument==1 && aux->commandVersion==42,"ward category emits versioned command");
    aux->skinCategory=1; ++aux->skinVersion; present();
    aux->command=0; clickSkinControl(0,2);
    Check(aux->command==5 && aux->argument==2 && aux->commandVersion==43,"minion category emits versioned command");
    aux->skinCategory=2; ++aux->skinVersion; aux->skinCanRefresh=0; aux->command=0; present(); clickSkinControl(0,0);
    Check(aux->command==0,"busy picker does not switch category");
    aux->skinCanRefresh=1; present(); clickSkinControl(0,0);
    Check(aux->command==5 && aux->argument==0,"return to hero category");
    aux->skinCategory=0; ++aux->skinVersion; aux->skinPage=0; aux->skinPageCount=2; aux->command=0; present();
    clickSkinControl(2,1); Check(aux->command==6 && aux->argument==1,"next page command");
    aux->skinPage=1; ++aux->skinVersion; aux->command=0; present();
    clickSkinControl(2,0); Check(aux->command==6 && aux->argument==-1,"previous page command");
    aux->skinPage=0; aux->skinPageCount=1; ++aux->skinVersion; present();
    RECT firstSkin{}; Check(skinControl(1,0,&firstSkin)!=FALSE,"first skin row remains available");
    setPointer((firstSkin.left+firstSkin.right)/2,(firstSkin.top+firstSkin.bottom)/2,TRUE); present();
    auto pendingWheel=reinterpret_cast<LONG(*)()>(GetProcAddress(module,"OverlayTestWheel"));
    auto skinScroll=reinterpret_cast<float(*)()>(GetProcAddress(module,"OverlayTestSkinScroll"));
    auto maskRaw=reinterpret_cast<void(*)(RAWINPUT*,UINT)>(GetProcAddress(module,"OverlayTestRawWheel"));
    auto maskBuffer=reinterpret_cast<void(*)(RAWINPUT*,UINT,UINT)>(GetProcAddress(module,"OverlayTestRawBuffer"));
    Check(pendingWheel && skinScroll && maskRaw && maskBuffer,"wheel test fixtures");
    aux->skinCount=40;
    for(int i=0;i<40;++i) sprintf_s(aux->skinNames[i],"Skin %d",i);
    present(); present();
    const float initialScroll=skinScroll();
    Check(PostMessageW(window,WM_MOUSEWHEEL,MAKEWPARAM(0,WORD(-WHEEL_DELTA)),0)!=FALSE,"post picker wheel");
    MSG wheelMessage{};
    for(int i=0;i<2;++i) {
        Check(PeekMessageW(&wheelMessage,window,WM_MOUSEWHEEL,WM_MOUSEWHEEL,PM_NOREMOVE) && wheelMessage.message==WM_NULL,
            "game cannot inspect picker wheel before dispatch");
        Check(pendingWheel()==0,"peeking does not duplicate wheel input");
    }
    Check(PeekMessageW(&wheelMessage,window,WM_MOUSEWHEEL,WM_MOUSEWHEEL,PM_REMOVE) && wheelMessage.message==WM_NULL,
        "picker wheel removed before game receives it");
    Check(pendingWheel()==-WHEEL_DELTA,"wheel delivered to overlay exactly once");
    DispatchMessageW(&wheelMessage); present(); present();
    Check(gameWheelMessages==0 && skinScroll()>initialScroll,"picker scrolls without game zoom input");
    float downScroll=skinScroll();
    PostMessageW(window,WM_MOUSEWHEEL,MAKEWPARAM(0,WHEEL_DELTA),0); present(); present();
    Check(gameWheelMessages==0 && skinScroll()<downScroll,"upward picker wheel also stays inside overlay");
    RAWINPUT raw{}; raw.header.dwType=RIM_TYPEMOUSE; raw.header.dwSize=sizeof(raw);
    raw.data.mouse.usButtonFlags=RI_MOUSE_WHEEL|RI_MOUSE_LEFT_BUTTON_DOWN;
    raw.data.mouse.usButtonData=WHEEL_DELTA; raw.data.mouse.lLastX=23; raw.data.mouse.lLastY=-17;
    RAWINPUT originalRaw=raw;
    maskRaw(&raw,sizeof(raw));
    Check(raw.data.mouse.usButtonFlags==RI_MOUSE_LEFT_BUTTON_DOWN && raw.data.mouse.usButtonData==0 &&
        raw.data.mouse.lLastX==23 && raw.data.mouse.lLastY==-17,"raw wheel masked while buttons and motion survive");
    raw=originalRaw; maskRaw(&raw,sizeof(RAWINPUTHEADER));
    Check(memcmp(&raw,&originalRaw,sizeof(raw))==0,"truncated raw input untouched");
    raw=originalRaw; raw.data.mouse.usButtonFlags=RI_MOUSE_HWHEEL; originalRaw=raw; maskRaw(&raw,sizeof(raw));
    Check(memcmp(&raw,&originalRaw,sizeof(raw))==0,"horizontal raw wheel untouched");
    alignas(RAWINPUT) unsigned char rawBuffer[sizeof(RAWINPUT)*2]{};
    auto keyboard=reinterpret_cast<RAWINPUT*>(rawBuffer);
    keyboard->header.dwType=RIM_TYPEKEYBOARD;
    keyboard->header.dwSize=UINT(offsetof(RAWINPUT,data)+sizeof(RAWKEYBOARD));
    keyboard->data.keyboard.VKey='A';
    size_t next=(keyboard->header.dwSize+sizeof(ULONG_PTR)-1)&~(sizeof(ULONG_PTR)-1);
    auto mouse=reinterpret_cast<RAWINPUT*>(rawBuffer+next);
    *mouse=raw; mouse->data.mouse.usButtonFlags=RI_MOUSE_WHEEL;
    unsigned char savedKeyboard[sizeof(RAWINPUT)]{}; memcpy(savedKeyboard,keyboard,next);
    maskBuffer(keyboard,2,UINT(next+sizeof(RAWINPUT)));
    Check(memcmp(savedKeyboard,keyboard,next)==0 && mouse->data.mouse.usButtonFlags==0,"buffered raw wheel masked without altering keyboard");
    setPointer(picker.right+20,picker.top+100,TRUE);
    PostMessageW(window,WM_MOUSEWHEEL,MAKEWPARAM(0,WHEEL_DELTA),0);
    Check(PeekMessageW(&wheelMessage,window,WM_MOUSEWHEEL,WM_MOUSEWHEEL,PM_REMOVE) && wheelMessage.message==WM_MOUSEWHEEL,
        "wheel outside picker reaches game queue");
    DispatchMessageW(&wheelMessage);
    raw=originalRaw; raw.data.mouse.usButtonFlags=RI_MOUSE_WHEEL; originalRaw=raw; maskRaw(&raw,sizeof(raw));
    Check(gameWheelMessages==1 && pendingWheel()==0 && memcmp(&raw,&originalRaw,sizeof(raw))==0,
        "game wheel and raw input work outside picker");
    aux->flags=0; present(); present(); setPointer(picker.left+35,picker.top+100,TRUE);
    PostMessageW(window,WM_MOUSEWHEEL,MAKEWPARAM(0,WHEEL_DELTA),0); present();
    raw=originalRaw; maskRaw(&raw,sizeof(raw));
    Check(gameWheelMessages==2 && memcmp(&raw,&originalRaw,sizeof(raw))==0,"closed picker restores game wheel");
    aux->command=0; aux->flags=0; frame->mayhemEnabled=1;
    setPointer(0,0,FALSE); if(previousForeground && previousForeground!=window) SetForegroundWindow(previousForeground);
    Check(SUCCEEDED(chain->ResizeBuffers(2, 1024, 768, DXGI_FORMAT_UNKNOWN, 0)), "resize has no retained backbuffers");
    frame->collapsed=1; present();
    Check(region(0,&bubble) && abs(bubble.right-clientBounds.right)<=1,"collapsed entry stays docked after resolution change");
    frame->collapsed = 0; frame->captureRequest = 2; present();
    waitCapture(2);
    Check(frame->captureResponse == 2 && frame->gameWidth == 1024 && frame->gameHeight == 768, "capture follows resolution change");
    auto creates=frame->targetCreates, builds=frame->uiBuilds, rendered=frame->renderedFrames;
    LARGE_INTEGER started{},finished{},hz{}; QueryPerformanceFrequency(&hz); QueryPerformanceCounter(&started);
    for(int i=0;i<300;++i) present(false);
    QueryPerformanceCounter(&finished);
    Check(frame->targetCreates==creates,"no per-frame render target allocation");
    Check(frame->renderedFrames==rendered+300,"cached UI still draws every game frame");
    Check(frame->uiBuilds-builds<100,"static UI layout reused across frames");
    printf("PERF: 300 cached frames, UI builds=%u, target creates=%u, last hook CPU=%uus, total present avg=%.3fms\n",
        frame->uiBuilds-builds,frame->targetCreates-creates,frame->lastCpuUs,
        double(finished.QuadPart-started.QuadPart)*1000/hz.QuadPart/300);
    frame->enabled = 0;
    printf("PASS: DX11 render, UTF-8 Chinese UI, state restore, capture, disable, heartbeat, 48x48 collapse, mouse without Alt, auxiliary panels, versioned skin selection, wheel queue routing, raw wheel masking, resolution change\n");
    DestroyWindow(window);
    // The production module intentionally stays pinned until process exit.
    UnmapViewOfFile(frame); CloseHandle(mapping); CloseHandle(mutex);
    return 0;
}
