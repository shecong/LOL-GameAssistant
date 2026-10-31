#include "protocol.h"
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <atomic>
#include <memory>
#include <mutex>
#include <algorithm>
#include <cstring>
#include <cfloat>
#include <vector>
#include <string>
#include <sstream>
#include "MinHook.h"
#include "imgui.h"
#include "imgui_impl_dx11.h"
using Microsoft::WRL::ComPtr;
using namespace overlay;

namespace {
struct Session {
    HANDLE mapping = nullptr, mutex = nullptr;
    Frame* frame = nullptr;
    LONG pendingCommand = 0; // Render-thread queue; shared command slot is mutex-protected.
    LONG pendingAux = 0, pendingArgument = 0, pendingVersion = 0;
    Frame cached{};
    Auxiliary auxiliary{};
    uint32_t textureVersion = 0;
    ComPtr<ID3D11ShaderResourceView> battleTexture;
    uint32_t jungleTextureVersion = 0;
    ComPtr<ID3D11ShaderResourceView> jungleTexture;
    ComPtr<ID3D11Texture2D> captureTexture;
    LONG capturePending=0;
    ULONGLONG captureStarted=0, nextSnapshot=0;
    UINT captureGameWidth=0,captureGameHeight=0;
    ~Session() {
        if (frame) UnmapViewOfFile(frame);
        if (mutex) CloseHandle(mutex);
        if (mapping) CloseHandle(mapping);
    }
};
struct Launch { DWORD owner; uint64_t nonce; };
std::mutex sessionGate, renderGate;
std::shared_ptr<Session> session;
std::atomic<int> installState{0};
std::atomic<LONG> overlayLeft{0}, overlayTop{0}, overlayRight{0}, overlayBottom{0}, wheel{0};
std::atomic<LONG> cameraWheel{0};
std::atomic<bool> cameraReset{false};
std::atomic<bool> cameraListening{false};
std::atomic<bool> acceptsMouse{false};
std::atomic<WNDPROC> previousProc{nullptr};
std::atomic<HWND> gameWindow{nullptr};
HHOOK wheelQueueHook=nullptr;
using RawInputData=UINT(WINAPI*)(HRAWINPUT,UINT,LPVOID,PUINT,UINT);
using RawInputBuffer=UINT(WINAPI*)(PRAWINPUT,PUINT,UINT);
RawInputData originalRawInputData=nullptr;
RawInputBuffer originalRawInputBuffer=nullptr;
std::atomic<unsigned> consumedButtons{0};
std::atomic<ULONGLONG> lastRender{0};
struct HitRegion { std::atomic<LONG> left{0}, top{0}, right{0}, bottom{0}; };
HitRegion regions[2];
std::atomic<unsigned> regionCount{0};
std::mutex inputGate;
std::vector<std::pair<int, bool>> mouseEvents;
using Present = HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
Present originalPresent = nullptr;
ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext> context;
ImGuiContext* gui = nullptr;
LARGE_INTEGER frequency{}, lastTick{};
using ResizeBuffers=HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT);
ResizeBuffers originalResize=nullptr;
IDXGISwapChain* cachedChain=nullptr;
ComPtr<ID3D11RenderTargetView> cachedTarget;
D3D11_TEXTURE2D_DESC cachedBuffer{};
bool drawReady=false;
ULONGLONG nextUi=0;
uint32_t builtRevision=0;
POINT builtPointer{};
bool builtInteractive=false, builtHovered=false;
void DropTarget() { cachedTarget.Reset(); cachedChain=nullptr; drawReady=false; }
HRESULT STDMETHODCALLTYPE OnResize(IDXGISwapChain* chain,UINT count,UINT width,UINT height,DXGI_FORMAT format,UINT flags) {
    std::lock_guard lock(renderGate);
    if(cachedChain==chain) DropTarget();
    return originalResize(chain,count,width,height,format,flags);
}
HRESULT ForwardPresent(IDXGISwapChain* chain,UINT sync,UINT flags,std::unique_lock<std::mutex>& lock) {
    if(lock.owns_lock()) lock.unlock();
    return originalPresent(chain,sync,flags);
}

#ifdef SC_OVERLAY_TEST
std::atomic<LONG> testPointerX{0}, testPointerY{0};
std::atomic<bool> testPointerEnabled{false};
std::atomic<float> testSkinScroll{0};
RECT testSkinCategories[3]{}, testSkinFirstEntry{}, testSkinPages[2]{}, testCameraToggle{};
#endif
POINT ClientPointer(HWND window) {
#ifdef SC_OVERLAY_TEST
    if (testPointerEnabled) return POINT{testPointerX,testPointerY};
#endif
    POINT p{}; GetCursorPos(&p); ScreenToClient(window,&p); return p;
}

bool PointerOverOverlay() {
    if (!acceptsMouse || GetTickCount64() - lastRender > 500 || GetForegroundWindow() != gameWindow) return false;
    POINT p=ClientPointer(gameWindow);
    for (unsigned i = 0; i < regionCount && i < 2; ++i)
        if (p.x >= regions[i].left && p.x < regions[i].right && p.y >= regions[i].top && p.y < regions[i].bottom) return true;
    return false;
}

// Games may inspect MSG before DispatchMessage, or poll raw input without
// dispatching WM_INPUT. WndProc alone cannot prevent those paths from zooming.
int ScaleCameraWheel(int delta,int& remainder,bool consume=true) {
    int total=delta*3+remainder,scaled=total/10;
    if(consume) remainder=total%10;
    return scaled;
}
LRESULT CALLBACK WheelQueueProc(int code,WPARAM removed,LPARAM data) {
    static thread_local int remainder=0;
    if(!cameraListening || GetForegroundWindow()!=gameWindow || PointerOverOverlay()) remainder=0;
    if(code==HC_ACTION && data) {
        auto message=reinterpret_cast<MSG*>(data);
        if(message->hwnd==gameWindow && message->message==WM_MOUSEWHEEL && PointerOverOverlay()) {
            if(removed==PM_REMOVE) wheel.fetch_add(GET_WHEEL_DELTA_WPARAM(message->wParam));
            message->message=WM_NULL; message->wParam=0; message->lParam=0;
        }
        else if(message->hwnd==gameWindow && message->message==WM_MOUSEWHEEL &&
            cameraListening && GetForegroundWindow()==gameWindow) {
            int delta=GET_WHEEL_DELTA_WPARAM(message->wParam);
            if(removed==PM_REMOVE) cameraWheel.fetch_add(delta);
            message->wParam=MAKEWPARAM(GET_KEYSTATE_WPARAM(message->wParam),static_cast<WORD>(ScaleCameraWheel(delta,remainder,removed==PM_REMOVE)));
        }
        else if(removed==PM_REMOVE && message->hwnd==gameWindow &&
            (message->message==WM_MBUTTONDOWN || message->message==WM_MBUTTONDBLCLK) &&
            cameraListening && GetForegroundWindow()==gameWindow && !PointerOverOverlay())
            cameraReset=true;
    }
    return CallNextHookEx(nullptr,code,removed,data);
}
void MaskOverlayRawWheel(RAWINPUT* input,size_t bytes) {
    static thread_local int remainder=0;
    if(!input || bytes<offsetof(RAWINPUT,data)+sizeof(RAWMOUSE) || input->header.dwType!=RIM_TYPEMOUSE) return;
    auto& mouse=input->data.mouse;
    if(!cameraListening || GetForegroundWindow()!=gameWindow || PointerOverOverlay()) remainder=0;
    if(!PointerOverOverlay()) {
        if(cameraListening && GetForegroundWindow()==gameWindow && (mouse.usButtonFlags&RI_MOUSE_WHEEL))
            mouse.usButtonData=static_cast<USHORT>(ScaleCameraWheel(static_cast<SHORT>(mouse.usButtonData),remainder));
        return;
    }
    if(mouse.usButtonFlags & RI_MOUSE_WHEEL) {
        mouse.usButtonFlags &= ~RI_MOUSE_WHEEL;
        if(!(mouse.usButtonFlags & RI_MOUSE_HWHEEL)) mouse.usButtonData=0;
    }
}
void MaskOverlayRawBuffer(RAWINPUT* input,UINT count,UINT capacity) {
    auto cursor=reinterpret_cast<unsigned char*>(input);
    size_t remaining=capacity;
    for(UINT i=0;cursor && i<count && remaining>=sizeof(RAWINPUTHEADER);++i) {
        auto block=reinterpret_cast<RAWINPUT*>(cursor);
        if(block->header.dwSize<sizeof(RAWINPUTHEADER) || block->header.dwSize>remaining) break;
        MaskOverlayRawWheel(block,block->header.dwSize);
        size_t stride=(size_t(block->header.dwSize)+sizeof(ULONG_PTR)-1)&~(sizeof(ULONG_PTR)-1);
        if(stride>remaining) break;
        cursor+=stride; remaining-=stride;
    }
}
UINT WINAPI OnRawInputData(HRAWINPUT handle,UINT command,LPVOID data,PUINT size,UINT headerSize) {
    UINT result=originalRawInputData(handle,command,data,size,headerSize);
    if(result!=UINT(-1) && command==RID_INPUT && data && headerSize==sizeof(RAWINPUTHEADER))
        MaskOverlayRawWheel(static_cast<RAWINPUT*>(data),result);
    return result;
}
UINT WINAPI OnRawInputBuffer(PRAWINPUT data,PUINT size,UINT headerSize) {
    UINT capacity=data && size ? *size : 0;
    UINT count=originalRawInputBuffer(data,size,headerSize);
    if(count!=UINT(-1) && data && headerSize==sizeof(RAWINPUTHEADER)) MaskOverlayRawBuffer(data,count,capacity);
    return count;
}
LRESULT CALLBACK GameWndProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    // Only clicks begun inside interactive panels belong to the overlay.
    unsigned button = message == WM_LBUTTONUP || message == WM_LBUTTONDOWN ? 1 :
        message == WM_RBUTTONUP || message == WM_RBUTTONDOWN ? 2 :
        message == WM_MBUTTONUP || message == WM_MBUTTONDOWN ? 4 : 0;
    bool up = message == WM_LBUTTONUP || message == WM_RBUTTONUP || message == WM_MBUTTONUP;
    int index = button == 1 ? 0 : button == 2 ? 1 : 2;
    if (up && (consumedButtons.fetch_and(~button) & button)) {
        std::lock_guard lock(inputGate); mouseEvents.emplace_back(index, false); return 0;
    }
    if (message == WM_KILLFOCUS) {
        consumedButtons = 0; std::lock_guard lock(inputGate);
        for (int i=0; i<3; ++i) mouseEvents.emplace_back(i, false);
    }
    if (PointerOverOverlay() || (consumedButtons != 0 && GetForegroundWindow() == gameWindow)) {
        // Do not swallow a button-up that belongs to a game click begun outside
        // the panel; otherwise the game can remain stuck with a pressed button.
        if (up) {
            auto previous = previousProc.load();
            return previous ? CallWindowProcW(previous, window, message, wParam, lParam) : DefWindowProcW(window, message, wParam, lParam);
        }
        if (button) { consumedButtons.fetch_or(button); std::lock_guard lock(inputGate); mouseEvents.emplace_back(index, true); }
        if (message == WM_INPUT) {
            RAWINPUTHEADER header{}; UINT size = sizeof(header);
            if (GetRawInputData(reinterpret_cast<HRAWINPUT>(lParam), RID_HEADER, &header, &size, sizeof(header)) != UINT(-1) && header.dwType == RIM_TYPEMOUSE)
                return DefWindowProcW(window, message, wParam, lParam); // Release foreground raw-input storage.
        }
        if (message == WM_MOUSEWHEEL) { wheel.fetch_add(GET_WHEEL_DELTA_WPARAM(wParam)); return 0; }
        if (message >= WM_MOUSEFIRST && message <= WM_MOUSELAST) return 0;
    }
    auto previous = previousProc.load();
    return previous ? CallWindowProcW(previous, window, message, wParam, lParam) : DefWindowProcW(window, message, wParam, lParam);
}

HRESULT Capture(IDXGISwapChain* chain, Session& target, LONG request) {
    Frame* output=target.frame;
    if(target.capturePending==request) {
        D3D11_MAPPED_SUBRESOURCE mapped{};
        HRESULT result=context->Map(target.captureTexture.Get(),0,D3D11_MAP_READ,D3D11_MAP_FLAG_DO_NOT_WAIT,&mapped);
        if(result==DXGI_ERROR_WAS_STILL_DRAWING && GetTickCount64()-target.captureStarted<2000) return E_PENDING;
        target.capturePending=0;
        if(FAILED(result)) return result;
        D3D11_TEXTURE2D_DESC desc{}; target.captureTexture->GetDesc(&desc);
        bool bgra=desc.Format==DXGI_FORMAT_B8G8R8A8_UNORM || desc.Format==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
        auto pixels=reinterpret_cast<uint8_t*>(output)+PixelOffset;
        for(UINT y=0;y<desc.Height;++y) {
            auto source=static_cast<const uint8_t*>(mapped.pData)+size_t(y)*mapped.RowPitch;
            auto row=pixels+size_t(y)*desc.Width*4;
            if(bgra) memcpy(row,source,size_t(desc.Width)*4);
            else for(UINT x=0;x<desc.Width;++x) {
                row[x*4]=source[x*4+2]; row[x*4+1]=source[x*4+1]; row[x*4+2]=source[x*4]; row[x*4+3]=255;
            }
        }
        context->Unmap(target.captureTexture.Get(),0);
        output->captureWidth=desc.Width; output->captureHeight=desc.Height;
        output->gameWidth=target.captureGameWidth; output->gameHeight=target.captureGameHeight;
        return S_OK;
    }
    target.capturePending=0; output->captureWidth=output->captureHeight=0;
    ComPtr<ID3D11Texture2D> back;
    HRESULT result = chain->GetBuffer(0, IID_PPV_ARGS(&back));
    if (FAILED(result)) return result;
    D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
    UINT width = desc.Width * 2 / 3, height = desc.Height * 32 / 100;
    if (width == 0 || height == 0 || uint64_t(width) * height * 4 > PixelCapacity) return E_INVALIDARG;
    bool rgba = desc.Format == DXGI_FORMAT_R8G8B8A8_UNORM || desc.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    bool bgra = desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM || desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
    if (!rgba && !bgra) return DXGI_ERROR_UNSUPPORTED; // Never interpret HDR/10-bit pixels as BGRA.
    ComPtr<ID3D11Texture2D> resolved;
    if (desc.SampleDesc.Count > 1) {
        D3D11_TEXTURE2D_DESC resolveDesc = desc;
        resolveDesc.SampleDesc = {1, 0}; resolveDesc.Usage = D3D11_USAGE_DEFAULT;
        resolveDesc.BindFlags = resolveDesc.CPUAccessFlags = resolveDesc.MiscFlags = 0;
        result = device->CreateTexture2D(&resolveDesc, nullptr, &resolved);
        if (FAILED(result)) return result;
        context->ResolveSubresource(resolved.Get(), 0, back.Get(), 0, desc.Format);
    }
    D3D11_TEXTURE2D_DESC stagingDesc{};
    stagingDesc.Width = width; stagingDesc.Height = height;
    stagingDesc.MipLevels = stagingDesc.ArraySize = 1; stagingDesc.Format = desc.Format;
    stagingDesc.SampleDesc.Count = 1; stagingDesc.Usage = D3D11_USAGE_STAGING;
    stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    D3D11_TEXTURE2D_DESC existing{}; ComPtr<ID3D11Device> previousDevice;
    if(target.captureTexture) { target.captureTexture->GetDesc(&existing); target.captureTexture->GetDevice(&previousDevice); }
    if(existing.Width!=width || existing.Height!=height || existing.Format!=desc.Format || previousDevice.Get()!=device.Get()) {
        target.captureTexture.Reset();
        result=device->CreateTexture2D(&stagingDesc,nullptr,&target.captureTexture);
        if(FAILED(result)) return result;
    }
    D3D11_BOX box{desc.Width/6,desc.Height*28/100,0,desc.Width/6+width,desc.Height*28/100+height,1};
    context->CopySubresourceRegion(target.captureTexture.Get(),0,0,0,0,resolved ? resolved.Get() : back.Get(),0,&box);
    target.capturePending=request; target.captureStarted=GetTickCount64();
    target.captureGameWidth=desc.Width; target.captureGameHeight=desc.Height;
    return E_PENDING;
}

bool Initialize(IDXGISwapChain* chain, HWND window) {
    ComPtr<ID3D11Device> next;
    if (FAILED(chain->GetDevice(IID_PPV_ARGS(&next)))) return false;
    if (device.Get() == next.Get() && gui && gameWindow == window) return true;
    if (gui) { ImGui_ImplDX11_Shutdown(); ImGui::DestroyContext(gui); gui = nullptr; }
    DropTarget();
    device = next; device->GetImmediateContext(&context);
    gui = ImGui::CreateContext();
    auto& io = ImGui::GetIO(); io.IniFilename = io.LogFilename = nullptr;
    wchar_t windows[MAX_PATH]{}; GetWindowsDirectoryW(windows, MAX_PATH);
    wchar_t fontPath[MAX_PATH]{}; swprintf_s(fontPath, L"%s\\Fonts\\msyh.ttc", windows);
    char utf8Path[MAX_PATH * 3]{};
    WideCharToMultiByte(CP_UTF8, 0, fontPath, -1, utf8Path, sizeof(utf8Path), nullptr, nullptr);
    if (GetFileAttributesW(fontPath) == INVALID_FILE_ATTRIBUTES ||
        !io.Fonts->AddFontFromFileTTF(utf8Path, 17.f, nullptr, io.Fonts->GetGlyphRangesChineseFull())) {
        ImGui::DestroyContext(gui); gui = nullptr; return false;
    }
    if (!ImGui_ImplDX11_Init(device.Get(), context.Get())) {
        ImGui::DestroyContext(gui); gui = nullptr; return false;
    }
    ImGui::StyleColorsDark();
    ImGui::GetStyle().WindowRounding = 8;
    if (gameWindow != window) {
        HMODULE module=nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&WheelQueueProc),&module);
        HHOOK nextQueueHook=SetWindowsHookExW(WH_GETMESSAGE,WheelQueueProc,module,GetWindowThreadProcessId(window,nullptr));
        if(!nextQueueHook) return false;
        if (gameWindow && reinterpret_cast<WNDPROC>(GetWindowLongPtrW(gameWindow, GWLP_WNDPROC)) == GameWndProc)
            SetWindowLongPtrW(gameWindow, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(previousProc.load()));
        previousProc = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window, GWLP_WNDPROC));
        SetLastError(0);
        LONG_PTR previous = SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(GameWndProc));
        if (!previous && GetLastError()) { UnhookWindowsHookEx(nextQueueHook); return false; }
        if(wheelQueueHook) UnhookWindowsHookEx(wheelQueueHook);
        wheelQueueHook=nextQueueHook;
        previousProc = reinterpret_cast<WNDPROC>(previous);
        gameWindow = window;
    }
    QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&lastTick);
    return true;
}

#include "panels.h"
#include "pipeline.h"

HRESULT STDMETHODCALLTYPE OnPresent(IDXGISwapChain* chain, UINT sync, UINT flags) {
    if (flags & DXGI_PRESENT_TEST) return originalPresent(chain,sync,flags);
    std::unique_lock renderLock(renderGate, std::try_to_lock);
    if (!renderLock.owns_lock()) return ForwardPresent(chain,sync,flags,renderLock);
    std::shared_ptr<Session> current;
    { std::unique_lock lock(sessionGate, std::try_to_lock); if (lock.owns_lock()) current = session; }
    if(!current) return ForwardPresent(chain,sync,flags,renderLock);
    LARGE_INTEGER cpuStarted{}; QueryPerformanceCounter(&cpuStarted);
    if(GetTickCount64()>=current->nextSnapshot) {
        DWORD acquired=WaitForSingleObject(current->mutex,0);
        if(acquired==WAIT_OBJECT_0 || acquired==WAIT_ABANDONED) {
            if(current->cached.magic==0 || current->cached.revision!=current->frame->revision) {
                memcpy(&current->cached,current->frame,sizeof(Frame));
                memcpy(&current->auxiliary,reinterpret_cast<uint8_t*>(current->frame)+AuxOffset,sizeof(Auxiliary));
            }
            current->cached.enabled=current->frame->enabled;
            current->cached.heartbeat=current->frame->heartbeat;
            current->cached.status=current->frame->status;
            ReleaseMutex(current->mutex); current->nextSnapshot=GetTickCount64()+16;
        }
    }
    Frame& snapshot=current->cached; Auxiliary& auxiliary=current->auxiliary;
    if(!Live(snapshot) || snapshot.status<0) { cameraListening=false; cameraWheel=0; cameraReset=false; acceptsMouse=false; drawReady=false; return ForwardPresent(chain,sync,flags,renderLock); }
    cameraListening=(auxiliary.flags & 16)!=0;
    if(!cameraListening) { cameraWheel=0; cameraReset=false; }
    if(cameraListening && (cameraWheel.load()!=0 || cameraReset.load())) {
        DWORD acquired=WaitForSingleObject(current->mutex,0);
        if(acquired==WAIT_OBJECT_0 || acquired==WAIT_ABANDONED) {
            auto output=reinterpret_cast<Auxiliary*>(reinterpret_cast<uint8_t*>(current->frame)+AuxOffset);
            if(output->flags & 16) {
                if(cameraReset.exchange(false)) { output->cameraReset=1; output->cameraWheel=0; cameraWheel=0; }
                else output->cameraWheel+=cameraWheel.exchange(0);
            }
            else { cameraWheel=0; cameraReset=false; }
            ReleaseMutex(current->mutex);
        }
    }
    DXGI_SWAP_CHAIN_DESC desc{}; DWORD pid = 0;
    if (FAILED(chain->GetDesc(&desc)) || !IsWindowVisible(desc.OutputWindow) || IsIconic(desc.OutputWindow))
        return ForwardPresent(chain,sync,flags,renderLock);
    GetWindowThreadProcessId(desc.OutputWindow, &pid);
    if (pid != GetCurrentProcessId()) return ForwardPresent(chain,sync,flags,renderLock);
    auto previousContext = ImGui::GetCurrentContext();
    if (gui) ImGui::SetCurrentContext(gui);
    if (!Initialize(chain, desc.OutputWindow)) {
        InterlockedExchange(&current->frame->status, -4);
        ImGui::SetCurrentContext(previousContext); return ForwardPresent(chain,sync,flags,renderLock);
    }
    ImGui::SetCurrentContext(gui);
    // Capture before drawing the panel, so OCR never reads its own recommendations.
    LONG request = current->frame->captureRequest;
    if (request > 0 && request != current->frame->captureResponse) {
        HRESULT result=Capture(chain,*current,request);
        if(result!=E_PENDING) { current->frame->captureError=result; InterlockedExchange(&current->frame->captureResponse,request); }
    }
    if(cachedChain!=chain || !cachedTarget) {
        DropTarget();
        ComPtr<ID3D11Texture2D> back;
        if(FAILED(chain->GetBuffer(0,IID_PPV_ARGS(&back))) || FAILED(device->CreateRenderTargetView(back.Get(),nullptr,&cachedTarget))) {
            ImGui::SetCurrentContext(previousContext); return ForwardPresent(chain,sync,flags,renderLock);
        }
        back->GetDesc(&cachedBuffer); cachedChain=chain; ++current->frame->targetCreates;
    }
    const auto& bufferDesc=cachedBuffer;
    RECT client{}; GetClientRect(desc.OutputWindow, &client);
    auto& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(float(bufferDesc.Width), float(bufferDesc.Height));
    LARGE_INTEGER now{}; QueryPerformanceCounter(&now);
    io.DeltaTime = (std::max)(0.00001f, float(double(now.QuadPart-lastTick.QuadPart) / frequency.QuadPart)); 
    bool interactive = GetForegroundWindow() == gameWindow;
    POINT point=ClientPointer(gameWindow);
    float scaleX = io.DisplaySize.x / (std::max)(1L, client.right);
    float scaleY = io.DisplaySize.y / (std::max)(1L, client.bottom);
    bool hovered=PointerOverOverlay();
    std::unique_lock inputLock(inputGate,std::try_to_lock);
    bool input=inputLock.owns_lock() && !mouseEvents.empty();
    bool pointerChanged=(hovered || builtHovered || consumedButtons!=0) && (point.x!=builtPointer.x || point.y!=builtPointer.y);
    bool rebuild=!drawReady || GetTickCount64()>=nextUi || builtRevision!=snapshot.revision ||
        interactive!=builtInteractive || hovered!=builtHovered || pointerChanged || input || wheel!=0;
    if(rebuild) {
        io.AddMousePosEvent(interactive ? point.x*scaleX : -FLT_MAX,interactive ? point.y*scaleY : -FLT_MAX);
        if(inputLock.owns_lock()) { for(auto [button,down]:mouseEvents) io.AddMouseButtonEvent(button,down && interactive); mouseEvents.clear(); }
        int scroll=wheel.exchange(0); io.AddMouseWheelEvent(0,interactive ? scroll/float(WHEEL_DELTA) : 0);
        io.MouseDrawCursor=interactive && (hovered || consumedButtons!=0);
    }
    if(inputLock.owns_lock()) inputLock.unlock();
    if(rebuild) {
        lastTick=now;
        ImGui_ImplDX11_NewFrame(); ImGui::NewFrame();
        DrawPanels(*current,snapshot,auxiliary,scaleX,scaleY); ImGui::Render();
        drawReady=true; nextUi=GetTickCount64()+33; builtRevision=snapshot.revision;
        builtPointer=point; builtHovered=hovered; builtInteractive=interactive;
        ++current->frame->uiBuilds;
    }
    {
        ExtraPipelineState state(context.Get());
        ID3D11RenderTargetView* view=cachedTarget.Get(); context->OMSetRenderTargets(1,&view,nullptr);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
    }
    if (current->pendingCommand) {
        DWORD acquired = WaitForSingleObject(current->mutex, 0);
        if (acquired == WAIT_OBJECT_0 || acquired == WAIT_ABANDONED) {
            if (InterlockedCompareExchange(&current->frame->command, current->pendingCommand, 0) == 0)
                current->pendingCommand = 0;
            ReleaseMutex(current->mutex);
        }
    }
    if (current->pendingAux) {
        DWORD acquired = WaitForSingleObject(current->mutex, 0);
        if (acquired == WAIT_OBJECT_0 || acquired == WAIT_ABANDONED) {
            auto output = reinterpret_cast<Auxiliary*>(reinterpret_cast<uint8_t*>(current->frame) + AuxOffset);
            if (output->command == 0) {
                output->argument = current->pendingArgument; output->commandVersion = current->pendingVersion;
                InterlockedExchange(&output->command, current->pendingAux); current->pendingAux = 0;
            }
            ReleaseMutex(current->mutex);
        }
    }
    InterlockedExchange(&current->frame->status, 2); InterlockedIncrement(reinterpret_cast<volatile LONG*>(&current->frame->renderedFrames));
    ImGui::SetCurrentContext(previousContext);
    LARGE_INTEGER finished{}; QueryPerformanceCounter(&finished);
    uint32_t elapsed=uint32_t((finished.QuadPart-cpuStarted.QuadPart)*1000000/frequency.QuadPart);
    current->frame->lastCpuUs=elapsed; current->frame->maxCpuUs=(std::max)(current->frame->maxCpuUs,elapsed);
    return ForwardPresent(chain,sync,flags,renderLock);
}

int Install() {
    WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"SC.Overlay.Probe";
    RegisterClassW(&wc);
    HWND probe = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, wc.lpszClassName, L"", WS_POPUP, 0, 0, 16, 16, nullptr, nullptr, wc.hInstance, nullptr);
    if (!probe) return -1;
    DXGI_SWAP_CHAIN_DESC desc{}; desc.BufferCount = 1; desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.OutputWindow = probe; desc.SampleDesc.Count = 1; desc.Windowed = TRUE;
    ComPtr<IDXGISwapChain> chain; ComPtr<ID3D11Device> probeDevice;
    HRESULT result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
        &desc, &chain, &probeDevice, nullptr, nullptr);
    if (FAILED(result)) { DestroyWindow(probe); return -2; }
    auto table=*reinterpret_cast<void***>(chain.Get());
    void* present=table[8]; void* resize=table[13];
    MH_STATUS status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) { DestroyWindow(probe); return -3; }
    HMODULE user32=GetModuleHandleW(L"user32.dll");
    void* rawData=reinterpret_cast<void*>(GetProcAddress(user32,"GetRawInputData"));
    void* rawBuffer=reinterpret_cast<void*>(GetProcAddress(user32,"GetRawInputBuffer"));
    struct Hook { void* target; void* callback; void** original; };
    Hook hooks[]{
        {present,reinterpret_cast<void*>(&OnPresent),reinterpret_cast<void**>(&originalPresent)},
        {resize,reinterpret_cast<void*>(&OnResize),reinterpret_cast<void**>(&originalResize)},
        {rawData,reinterpret_cast<void*>(&OnRawInputData),reinterpret_cast<void**>(&originalRawInputData)},
        {rawBuffer,reinterpret_cast<void*>(&OnRawInputBuffer),reinterpret_cast<void**>(&originalRawInputBuffer)}
    };
    for(auto& hook:hooks) {
        status=MH_CreateHook(hook.target,hook.callback,hook.original);
        if(status!=MH_OK) break;
    }
    if(status==MH_OK) for(auto& hook:hooks) { status=MH_QueueEnableHook(hook.target); if(status!=MH_OK) break; }
    if(status==MH_OK) status=MH_ApplyQueued();
    if(status!=MH_OK) for(auto& hook:hooks) MH_RemoveHook(hook.target);
    chain.Reset(); probeDevice.Reset(); DestroyWindow(probe); UnregisterClassW(wc.lpszClassName, wc.hInstance);
    return status == MH_OK ? 1 : -3;
}

DWORD WINAPI Attach(void* argument) {
    std::unique_ptr<Launch> launch(static_cast<Launch*>(argument));
    wchar_t image[MAX_PATH]{}; GetModuleFileNameW(nullptr, image, MAX_PATH);
    const wchar_t* name = wcsrchr(image, L'\\'); name = name ? name+1 : image;
#ifndef SC_OVERLAY_TEST
    if (_wcsicmp(name, L"League of Legends.exe")) return 1;
#endif
    wchar_t mapName[160]{}; MappingName(mapName, GetCurrentProcessId(), launch->owner, launch->nonce);
    auto next = std::make_shared<Session>();
    next->mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, mapName);
    wchar_t mutexName[180]{}; swprintf_s(mutexName, L"%s.Lock", mapName);
    next->mutex = OpenMutexW(SYNCHRONIZE | MUTEX_MODIFY_STATE, FALSE, mutexName);
    if (!next->mapping || !next->mutex) return 2;
    next->frame = static_cast<Frame*>(MapViewOfFile(next->mapping, FILE_MAP_ALL_ACCESS, 0, 0, MappingSize));
    if (!next->frame || !Valid(*next->frame, GetCurrentProcessId(), launch->owner)) return 3;
    // Pin before returning to the launcher: hooks must never reference an unloaded DLL.
    // Disabling uses enabled=0 + heartbeat; code remains dormant until game exit.
    HMODULE pinned = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&Attach), &pinned);
    std::lock_guard lock(sessionGate);
    if (session && Live(*session->frame) && session->frame->ownerPid != launch->owner) return 4;
    session = next;
    int installed = installState;
    if (installed == 0) { installed = Install(); installState = installed; }
    InterlockedExchange(&next->frame->status, installed);
    return installed > 0 ? 0 : 5;
}
}

#ifdef SC_OVERLAY_TEST
extern "C" __declspec(dllexport) void OverlayTestSync() {
    std::lock_guard lock(renderGate); if(session) session->nextSnapshot=0;
}
extern "C" __declspec(dllexport) void OverlayTestPointer(LONG x,LONG y,BOOL enabled) {
    testPointerX=x; testPointerY=y; testPointerEnabled=enabled!=FALSE;
}
extern "C" __declspec(dllexport) BOOL OverlayTestRegion(unsigned index, RECT* output) {
    if (!output || index >= regionCount || index >= 2) return FALSE;
    *output = RECT{regions[index].left,regions[index].top,regions[index].right,regions[index].bottom}; return TRUE;
}
extern "C" __declspec(dllexport) LONG OverlayTestWheel() { return wheel.load(); }
extern "C" __declspec(dllexport) float OverlayTestSkinScroll() { return testSkinScroll.load(); }
extern "C" __declspec(dllexport) BOOL OverlayTestSkinControl(int kind,int index,RECT* rect) {
    if(!rect) return FALSE;
    if(kind==0 && index>=0 && index<3) *rect=testSkinCategories[index];
    else if(kind==1 && index==0) *rect=testSkinFirstEntry;
    else if(kind==2 && index>=0 && index<2) *rect=testSkinPages[index];
    else if(kind==3 && index==0) *rect=testCameraToggle;
    else return FALSE;
    return rect->right>rect->left && rect->bottom>rect->top;
}
extern "C" __declspec(dllexport) void OverlayTestRawWheel(RAWINPUT* input,UINT bytes) { MaskOverlayRawWheel(input,bytes); }
extern "C" __declspec(dllexport) void OverlayTestRawBuffer(RAWINPUT* input,UINT count,UINT bytes) { MaskOverlayRawBuffer(input,count,bytes); }
#endif

extern "C" __declspec(dllexport) LRESULT CALLBACK OverlayBootstrap(int code, WPARAM removed, LPARAM data) {
    if (code >= 0 && removed == PM_REMOVE) {
        auto message = reinterpret_cast<MSG*>(data);
        if (message->message == RegisterWindowMessageW(StartMessage)) {
            auto launch = new Launch{static_cast<DWORD>(message->wParam), static_cast<uint64_t>(message->lParam)};
            HANDLE worker = CreateThread(nullptr, 0, Attach, launch, 0, nullptr);
            if (worker) CloseHandle(worker); else delete launch;
            message->message = WM_NULL;
        }
    }
    return CallNextHookEx(nullptr, code, removed, data);
}
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
