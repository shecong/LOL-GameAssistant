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
    WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"SC.Overlay.Smoke";
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
    auto aux=reinterpret_cast<Auxiliary*>(reinterpret_cast<unsigned char*>(frame)+AuxOffset);
    frame->mayhemEnabled=0; aux->flags=2|4|8;
    strcpy_s(aux->coachTitle,"当前建议"); strcpy_s(aux->coachBody,"独立于增幅侧栏显示");
    strcpy_s(aux->skinToast,"已切换皮肤"); strcpy_s(aux->skinTitle,"选择皮肤");
    strcpy_s(aux->skinNames[0],"基础皮肤"); aux->skinCount=1; aux->skinReady=1; aux->skinCanRefresh=1; aux->skinVersion=42;
    present(); present();
    Check(frame->panelWidth==0 && frame->hitRegionCount==1,"other panels render while mayhem is disabled");
    RECT picker{}; Check(region(0,&picker) && picker.right>picker.left,"skin picker owns separate hit area");
    setPointer(picker.left+35,picker.top+100,TRUE);
    present(); SendMessageW(window,WM_LBUTTONDOWN,MK_LBUTTON,0); present(); SendMessageW(window,WM_LBUTTONUP,0,0); present();
    Check(aux->command==1 && aux->argument==0 && aux->commandVersion==42,"skin selection command includes list version");
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
    printf("PASS: DX11 render, UTF-8 Chinese UI, state restore, capture, disable, heartbeat, 48x48 collapse, mouse without Alt, auxiliary panels, versioned skin selection, resolution change\n");
    DestroyWindow(window);
    // The production module intentionally stays pinned until process exit.
    UnmapViewOfFile(frame); CloseHandle(mapping); CloseHandle(mutex);
    return 0;
}
